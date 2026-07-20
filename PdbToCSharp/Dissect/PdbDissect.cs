using System.CodeDom.Compiler;
using System.Runtime.InteropServices;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.DebugSubsections;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;
using Flags = PdbToCSharp.CsNameUndecorator.Flags;

namespace PdbToCSharp.Dissect;

internal static class PdbDissect {
  public static void DissectPdb() {
    const string pdbPath = "mio.pdb";
    string pdbName = Path.GetFileNameWithoutExtension(pdbPath);
    string output = $"output/{pdbName}_";
    Directory.CreateDirectory("output");

    using PdbFileReader pdbReader = new(pdbPath);
    using (StreamWriter debugWriter = new(output + "functions.txt")) {
      HashSet<string> funcNames = [];
      const Flags flags = Flags.NoAllocationLanguage | Flags.NoAccessSpecifiers | Flags.NoLeadingUnderscores |
        Flags.NoMicrosoftKeywords | Flags.NoComplexType;
      foreach (PdbPublicSymbol? sym in pdbReader.PublicSymbols.OrderBy(s => s.RelativeVirtualAddress)) {
        string csName = CsNameUndecorator.UnDecorateSymbolName(sym.Name, flags);
        if (!csName.Contains('(')) {
          // Not a function
          continue;
        }

        int argStartIndex = csName.IndexOf('(');
        if (!csName.AsSpan(0, argStartIndex).Contains('.')) {
          // Not a member function
          continue;
        }

        if (csName.Contains("<lambda")) {
          // Don't care about lambdas
          continue;
        }

        if (csName.Contains("`RTTI")) {
          // Don't care about this
          continue;
        }

        debugWriter.Write("  RVA: 0x");
        debugWriter.Write($"{sym.RelativeVirtualAddress:X}");
        debugWriter.Write("   | C#: ");
        debugWriter.Write(csName);

        if (!funcNames.Add(csName)) {
          // Already seen this function name
          debugWriter.Write(" // Duplicate function name");
        }

        debugWriter.WriteLine();
      }
    }

    PdbFile pdb = pdbReader.PdbFile;
    ProcedureHelper.ReplaceNullSymbols(pdb);
    // ProcedureHelper.Load(pdbReader);

    // Everything below this point is for debugging and analysis of the PDB file

    using (StreamWriter funcWriter = new(output + "functions2.txt")) {
      // var syms = pdbReader.PublicSymbols
      //   .Select(s => (s.RelativeVirtualAddress, s.GetUndecoratedName()))
      //   .OrderBy(s => s.RelativeVirtualAddress)
      //   .ToArray();

      var funcs2 = pdbReader.Functions
        .Where(f => pdb.TryGetRecord(f.FunctionType.TypeIndex) is MemberFunctionRecord)
        .Select(f => (f.RelativeVirtualAddress,
          $"\"{f.Name}\" ({f.FunctionType.Name}) ({AsStr(pdb, f.FunctionType.TypeIndex)})"))
        .OrderBy(f => f.RelativeVirtualAddress)
        .ToArray();

      string AsStr(PdbFile p, TypeIndex t) {
        if (t.IsSimple) {
          return t.SimpleKind is SimpleTypeKind.None ? "none" : Types.CsSimpleType.ToCsName(t);
        }

        TypeRecord? type = p.TryGetRecord(t);
        return type is null ? $"<null type for {t}>" : type.ToString(pdb);
      }

      foreach ((ulong RelativeVirtualAddress, string Name) f in /*syms.Union(*/
               funcs2 /*)*/.OrderBy(f => f.RelativeVirtualAddress)) {
        funcWriter.WriteLine($"{f.RelativeVirtualAddress:X8} | {f.Name}");
      }
    }

    WriteGlobals(output + "globals.txt", pdb);
    WriteLocals(output + "locals.txt", pdb);

    WriteStatics(output + "statics.txt", pdbReader);

    var tpiRecords = pdb.TpiStream.GetTypeRecords();
    var ipiRecords = pdb.IpiStream.GetTypeRecords();
    string replacementText = Environment.NewLine + "\n    ";
    using (StreamWriter debugWriter = new(output + "tpi.txt")) {
      foreach (TypeRecord typeRecord in tpiRecords) {
        debugWriter.WriteLine(
          typeRecord.Kind + " | " +
          typeRecord.ToString(pdb).ReplaceLineEndings(replacementText));
      }
    }

    using (StreamWriter debugWriter = new(output + "ipi.txt")) {
      foreach (TypeRecord typeRecord in ipiRecords) {
        debugWriter.WriteLine(
          typeRecord.Kind + " | " +
          typeRecord.ToString(pdb).ReplaceLineEndings(replacementText));
      }
    }

    // These below variables are for inspecting into via debug.
    var funcs = pdbReader.Functions.Where(f => AllowedName(f.Name)).ToArray();
    var udts = pdbReader.UserDefinedTypes
      .OfType<PdbClassType>()
      .Where(udt => AllowedName(udt.Name))
      .OrderBy(udt => udt.Name)
      .ToArray();

    var a = Enum.GetValues<DebugSubsectionKind>();
    Dictionary<DebugSubsectionKind, List<DebugSubsection>> debugSubsections = [];
    foreach (DebugSubsectionStream m in pdb.DbiStream.Modules
               .Where(m => m.LocalSymbolStream is not null)
               .Select(m => m.DebugSubsectionStream)) {
      foreach (DebugSubsectionKind debugSubsectionKind in a) {
        if (m[debugSubsectionKind] is { Length: > 0 } arr) {
          if (!debugSubsections.TryGetValue(debugSubsectionKind, out var list)) {
            list = [];
            debugSubsections[debugSubsectionKind] = list;
          }

          list.AddRange(arr);
        }
      }
    }

    WritePdbHeaders(pdbName, pdb);

    // Debug list of types that exist in the PDB, to get an idea of what we're working with and identify any unhandled types

    Dictionary<(TypeLeafKind Kind, string), int> tpiTypes = [];
    Dictionary<(TypeLeafKind Kind, string), int> ipiTypes = [];
    foreach (TypeRecord typeRecord in tpiRecords) {
      IncrementCount(tpiTypes, typeRecord, typeRecord.Kind);
    }

    foreach (TypeRecord typeRecord in ipiRecords) {
      IncrementCount(ipiTypes, typeRecord, typeRecord.Kind);
    }

    // These are her just to debug inspect into
    var orderedTpiTypes = tpiTypes.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();
    var orderedIpiTypes = ipiTypes.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();

    var orderedGlobalTypes = GetGlobalSymbolTypeCounts(pdb).OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();
    var orderedModuleTypes = GetModuleSymbolTypeCounts(pdb).OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();


    var argDict = BuildArgumentDictionary(pdb);
    using (StreamWriter testWriter = new(output + "args.txt")) {
      foreach ((ProcedureSymbol key, var value) in argDict.OrderBy(kvp => kvp.Key.FunctionType.Index)) {
        if (value.Length > 0) {
          testWriter.WriteLine(key.ToString(pdb));
        }
      }
    }

    // WriteCppHeader(output + ".h", pdb, tpiRecords);
    WriteTemplateNames(output + "template_names.txt", tpiRecords);

    return;

    static bool AllowedName(string str) => !(
      str.ContainsAny(['<', '~', '`']) || // Skip template instantiations, destructors, and compiler-generated names
      str.StartsWith("std::") || // Standard library
      str.StartsWith("Concurrency::") || // C++ Concurrency Runtime
      str.StartsWith("crashpad::") || // Crashpad
      str.Contains("D3D12", StringComparison.OrdinalIgnoreCase) || // Direct3D 12
      str.StartsWith("__") || // Compiler-generated templates
      str.Contains("<unnamed") || // Unnamed types
      str.Contains("`anonymous") || // Anonymous types
      str.Contains("<lambda") || // Lambdas
      str.Contains("IDXGI", StringComparison.OrdinalIgnoreCase) || // DXGI
      str.StartsWith("Im") || // ImGui
      str.Contains("hb_") || // Harfbuzz
      str.StartsWith("OT::") || // Harfbuzz
      str.Contains("PlayFab", StringComparison.OrdinalIgnoreCase) // PlayFab SDK
    );
  }

  private static void WritePdbHeaders(string pdbName, PdbFile pdb) {
    using (StreamWriter testWriter = new(pdbName + "_pdbHeaders.txt")) {
      var gameHeaderFiles = pdb.DbiStream.Modules
        .SelectMany(m => m.Files)
        .Where(s => s.Contains("tonic"))
        .Distinct()
        .Order();
      // .OrderByDescending(s => s.EndsWith('h'))
      // .ThenBy(s => s, StringComparer.OrdinalIgnoreCase);
      foreach (string gameHeaderFile in gameHeaderFiles) {
        var substr = gameHeaderFile.IndexOf("tonic", StringComparison.OrdinalIgnoreCase) is var index and not -1
          ? gameHeaderFile.AsSpan(index)
          : gameHeaderFile.AsSpan();
        testWriter.WriteLine(substr);
      }
    }
  }

  private static Dictionary<(SymbolRecordKind Kind, string), int> GetModuleSymbolTypeCounts(PdbFile pdb) {
    Dictionary<(SymbolRecordKind Kind, string), int> moduleTypes = [];
    foreach (SymbolRecord sym in pdb.DbiStream.Modules
               .Where(m => m.LocalSymbolStream is not null)
               .SelectMany(m => m.LocalSymbolStream.AsEnumerable())) {
      IncrementCount<SymbolRecord, SymbolRecordKind, NullSymbol>(moduleTypes, sym, sym.Kind, sym.Children);
    }

    return moduleTypes;
  }

  private static Dictionary<(SymbolRecordKind Kind, string), int> GetGlobalSymbolTypeCounts(PdbFile pdb) {
    Dictionary<(SymbolRecordKind Kind, string), int> globalTypes = [];
    var globalSymbols = pdb.GlobalsStream.Symbols;
    for (int i = 0; i < pdb.GlobalsStream.HashRecords.Length; i++) {
      if (globalSymbols[i] is { } sym) {
        IncrementCount<SymbolRecord, SymbolRecordKind, NullSymbol>(globalTypes, sym, sym.Kind, sym.Children);
      }
    }

    return globalTypes;
  }

  /// This basically does `dict[key]++`, where `key` may also indicate having children and null children
  private static void IncrementCount<T, TKind, TNull>(Dictionary<(TKind, string), int> dict, T val, TKind kind,
    T[] arr) {
    if (val is null) {
      return;
    }

    dict.Increment((kind, val.GetType().Name));
    if (arr is { Length: > 0 }) {
      dict.Increment((kind, "!! HAS_CHILDREN"));
      if (arr.Any(a => a is TNull or null)) {
        dict.Increment((kind, "!! HAS_CHILDREN THAT CAN BE NULL"));
      }
    }
  }

  /// Basically does `dict[key]++`
  private static void IncrementCount<T, TKind>(Dictionary<(TKind, string), int> dict, T val, TKind kind) {
    if (val is not null) {
      dict.Increment((kind, val.GetType().Name));
    }
  }

  private static void WriteTemplateNames(string outputName, TypeRecord[] records) {
    using StreamWriter templateNamesWriter = new(outputName);
    Dictionary<string, List<string>> uniqueTemplateNames = [];
    foreach (TagRecord t in records
               .OfType<TagRecord>()
               .OrderBy(t => t.Name.String)) {
      if (t.IsForwardReference) {
        continue;
      }

      string str = t.Name.String;
      if (str.StartsWith("std::") || // Standard library
          str.StartsWith("Concurrency::") || // C++ Concurrency Runtime
          str.Contains("D3D12", StringComparison.OrdinalIgnoreCase) || // Direct3D 12
          str.StartsWith("__") || // Compiler-generated templates
          str.Contains("<unnamed") || // Unnamed types
          str.Contains("`anonymous") || // Anonymous types
          str.Contains("<lambda") || // Lambdas
          str.Contains("IDXGI", StringComparison.OrdinalIgnoreCase) || // DXGI
          str.StartsWith("Im") || // ImGui
          str.Contains("hb_") || // Harfbuzz
          str.StartsWith("OT::") || // Harfbuzz
          str.Contains("PlayFab", StringComparison.OrdinalIgnoreCase) // PlayFab SDK
         ) {
        continue;
      }

      if (str.IndexOf('<') is not (var index and not -1)) {
        continue;
      }

      string first = str[..index];
      string second = str[(index + 1)..^1];
      if (second.Contains("lambda at")) {
        second = "<lambda>";
      }

      ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(uniqueTemplateNames, first, out bool exists);
      if (!exists) {
        list = [];
      }

      list!.Add(second);
    }

    foreach ((string name, var types) in uniqueTemplateNames
               //.OrderBy(kv => kv.Key)
               .OrderByDescending(kv => kv.Value.Count)
            ) {
      const int valueLength = 40;
      templateNamesWriter.Write($"{name,32}: [{types.Count,4}]");
      foreach (string type in types) {
        var subspan = type.AsSpan()[..Math.Min(valueLength - 2, type.Length)];
        bool isLongString = type.Length > valueLength - 2;
        if (isLongString) {
          subspan = subspan[..^3];
        }

        templateNamesWriter.Write(isLongString
          ? $" <{subspan,valueLength - 5}...> | "
          : $"{"<" + subspan.ToString(),valueLength}> | ");
      }

      templateNamesWriter.WriteLine();
    }
  }

  private static void WriteLocals(string outputName, PdbFile pdb) {
    using StreamWriter symbolsWriter = new(outputName);
    IndentedTextWriter writer = new(symbolsWriter);
    foreach (DbiModuleDescriptor module in pdb.DbiStream.Modules) {
      if (module.LocalSymbolStream is not { } mSymbols) {
        continue;
      }

      writer.Indent = 0;
      writer.WriteLine($"Module: {Path.GetFileName(module.ModuleName.String)}");
      writer.Indent++;

      int inlineDepth = 0;
      foreach (SymbolRecord symbolRecord in mSymbols.AsEnumerable()) {
        // This will sometimes throw due to some apparent parsing error
        try {
          if (symbolRecord
              is DefRangeRegisterSymbol
              or DefRangeRegisterRelativeSymbol
              or DefRangeSubfieldRegisterSymbol
              or DefRangeFramePointerRelativeSymbol
              or DefRangeFramePointerRelativeFullScopeSymbol
              // or LocalSymbol { Flags: LocalVariableFlags.IsOptimizedOut }
             ) {
            continue;
          }

          if (symbolRecord is EndSymbol) {
            writer.Indent--;
          }


          if (inlineDepth == 0) {
            writer.InnerWriter.Write($"[{symbolRecord.SymbolStreamIndex}] ");
            switch (symbolRecord) {
              case BlockSymbol block:
                writer.Write("{ ");
                writer.WriteLine(block.Name.String);
                break;
              case InlineSiteSymbol inline:
                writer.WriteLine(inline.ToString(pdb));
                break;
              case EndSymbol { Kind: SymbolRecordKind.S_END }:
                writer.WriteLine('}');
                break;
              case EndSymbol { Kind: SymbolRecordKind.S_INLINESITE_END }:
                writer.WriteLine("}\t/* Inline Site End */");
                break;
              default:
                writer.WriteLine(symbolRecord.ToString(pdb));
                break;
            }
          }

          if (symbolRecord is InlineSiteSymbol or ProcedureSymbol or BlockSymbol) {
            writer.Indent++;
          }

          if (symbolRecord is InlineSiteSymbol) {
            inlineDepth++;
          }
          else if (symbolRecord is EndSymbol { Kind: SymbolRecordKind.S_INLINESITE_END }) {
            inlineDepth--;
          }
        }
        catch (Exception ex) {
          writer.WriteLine(
            $"{{ Error writing symbol {symbolRecord.Kind} in module {module.ModuleName.String}: {ex.Message} }}");
        }
      }

      foreach (ProcedureSymbol proc in mSymbols.AsEnumerable().OfType<ProcedureSymbol>()) {
        break;

        string procName = proc.Name.String;
        if (procName.Contains('<')
            || procName.StartsWith("std::", StringComparison.Ordinal)
            || procName.Contains('~')
           ) {
          continue;
        }

        TypeRecord? funcRecord = pdb.TryGetRecord(proc.FunctionType);
        bool isStatic;
        int paramsLeft;
        switch (funcRecord) {
          case ProcedureRecord procRecord:
            paramsLeft = procRecord.ParameterCount;
            writer.Write("\t/* PROC */ ");

            isStatic = proc.Kind is SymbolRecordKind.S_GPROC32;
            if (isStatic) {
              writer.Write("static ");
            }

            writer.Write(procRecord.ReturnType.ToString(pdb));
            writer.Write(' ');
            writer.Write(procName);
            break;
          case MemberFunctionRecord mFunc: {
            paramsLeft = mFunc.ParameterCount;
            writer.Write("\t/* MEMPROC */ ");
            bool isConstructor = mFunc.Options.HasFlag(FunctionOptions.Constructor);
            isStatic = mFunc.ThisType is
              { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void };
            if (isStatic) {
              writer.Write("static ");
            }

            if (isConstructor) {
              writer.Write("/* Ctor */ ");
              // Return value is Void for constructors, but writing the class type is more informative
              writer.Write(mFunc.ClassType.ToString(pdb));
            }
            else {
              writer.Write(mFunc.ReturnType.ToString(pdb));
            }

            writer.Write(' ');
            string className = mFunc.ClassType.ToString(pdb);
            writer.Write(className);
            if (!isConstructor) {
              writer.Write("::");
              writer.Write(procName.AsSpan()[(className.Length + 2)..]);
            }

            break;
          }
          default:
            continue;
        }

        writer.Write(" | ");
        writer.Write($"Size: {proc.CodeSize}, Offset: {proc.Offset}");

        writer.Write(" | Named Args: (");
        foreach (LocalSymbol local in proc.Children.OfType<LocalSymbol>()) {
          if (local.Name.String == "this" || !local.Flags.HasFlag(LocalVariableFlags.IsParam)) {
            continue;
          }

          if (paramsLeft == -1) {
            writer.Write("/* More local symbols that shouldn't be matched to a param */");
          }

          writer.Write(local.Type.ToString(pdb));
          writer.Write(' ');
          writer.Write(local.Name.String);
          if (--paramsLeft > 0) {
            writer.Write(", ");
          }
        }

        if (paramsLeft > 0) {
          writer.Write("/* Missing " + paramsLeft + " parameters */");
        }

        writer.WriteLine(");");
      }
    }
  }

  private static void WriteGlobals(string outputName, PdbFile pdb) {
    ArrayCache<SymbolRecord> globalSymbols = pdb.GlobalsStream.Symbols;
    for (int i = 0; i < pdb.GlobalsStream.HashRecords.Length; i++) {
      _ = pdb.GlobalsStream.Symbols[i];
    }

    // using StreamWriter globalsWriter = new(outputName);
    // foreach (SymbolRecord symbol in globalSymbols) {
    //   break;
    //   switch (symbol) {
    //     case ConstantSymbol constant:
    //       globalsWriter.WriteLine(
    //         $"                  Constant {constant.Name.String}: {constant.TypeIndex.ToString(pdb)} = {constant.Value}");
    //       break;
    //     case ProcedureReferenceSymbol procRef:
    //       globalsWriter.WriteLine(
    //         $"{procRef.Offset:X8}:{procRef.Module:X6} | " +
    //         $"Procedure Name=\"{procRef.Name.String}\", Module={procRef.Module}");
    //       break;
    //     case DataSymbol data:
    //       globalsWriter.WriteLine($"{data.Offset:X8}:{data.Segment:X6} | " +
    //         $"Data Name=\"{data.Name.String}\", Type={data.Type.ToString(pdb)}");
    //       break;
    //     case ThreadLocalDataSymbol threadLocalData:
    //       globalsWriter.WriteLine($"{threadLocalData.Offset:X8}:{threadLocalData.Segment:X6} | " +
    //         $"Thread Local Data Name=\"{threadLocalData.Name.String}\", Type={threadLocalData.Type.ToString(pdb)}");
    //       break;
    //     case UdtSymbol udt:
    //       globalsWriter.WriteLine(
    //         $"                  UDT Name=\"{udt.Name.String}\",Type={udt.Type.ToString(pdb)}");
    //       break;
    //     default:
    //       globalsWriter.WriteLine($"                  Unknown Symbol Kind={symbol.Kind}, Type={symbol.GetType().Name}");
    //       break;
    //   }
    // }

    string? previousName = null;
    bool prevNameWritten = false;
    using StreamWriter procWriter = new(outputName.Replace(".txt", "_procedures.txt"));
    foreach (((ProcedureReferenceSymbol prs, ProcedureSymbol ps), uint rva) in GetProcedureReferenceRvas(pdb)
               .OrderBy(kv => kv.Key.Item1.Name.String)
            ) {
      TypeRecord? record = pdb.TryGetRecord(ps.FunctionType);
      if (ps.FunctionType.Index == 0) {
        continue;
      }
      ProcedureRecord? asProc = record as ProcedureRecord;
      MemberFunctionRecord? asMfunc = record as MemberFunctionRecord;
      if (asProc is null && asMfunc is null) {
        continue;
      }

      ArgumentListRecord argList = (asProc?.ArgumentList ?? asMfunc!.ArgumentList).As<ArgumentListRecord>(pdb);
      TypeIndex returnType = asProc?.ReturnType ?? asMfunc!.ReturnType;

      string args = string.Join(", ", argList.Arguments.Select(a => a.ToString(pdb)));
      string name = $"{returnType.ToString(pdb),32} {prs.Name.String}({args})";
      if (name == previousName) {
        if (!prevNameWritten) {
          procWriter.WriteLine($"{previousName} = {rva}");
          prevNameWritten = true;
        }
        procWriter.WriteLine($"{name} = {rva}");
        continue;
      }
      previousName = name;
      prevNameWritten = false;
    }
  }

  public static Dictionary<(ProcedureReferenceSymbol, ProcedureSymbol), uint> GetProcedureReferenceRvas(PdbFile pdb) {
    Dictionary<(ProcedureReferenceSymbol, ProcedureSymbol), uint> results = [];

    if (pdb.DbiStream is null) {
      throw new InvalidOperationException("PDB file is missing a valid DBI stream or Section Map.");
    }

    var modules = pdb.DbiStream.Modules;
    var sectionMap = pdb.DbiStream.SectionMap;

    var syms = pdb.GlobalsStream.Symbols;
    for (int i = 0; i < syms.Count; i++) {
      if (syms[i] is not ProcedureReferenceSymbol refSym) {
        continue;
      }

      // Modules use 1-based indexing
      int moduleIndex = refSym.Module - 1;
      if (moduleIndex < 0 || moduleIndex >= modules.Count ||
          modules[moduleIndex]?.LocalSymbolStream is not { } localSymStream) {
        continue;
      }

      // 2. Look up the underlying ProcedureSymbol inside the module's local symbol stream
      localSymStream.TryGetSymbolRecordByOffset(refSym.Offset, out SymbolRecord? localSym);
      if (localSym is not ProcedureSymbol procSym) {
        continue;
      }

      // Segments use 1-based indexing
      int sectionIndex = procSym.Segment - 1;
      if (sectionIndex >= 0 && sectionIndex < sectionMap.Length) {
        SectionMapEntry sectionHeader = sectionMap[sectionIndex];

        // 4. Calculate RVA using the underlying procedure's actual memory segment
        uint rva = sectionHeader.Offset + procSym.Offset;
        results.Add((refSym, procSym), rva);
      }
    }

    return results;
  }

  private static void WriteStatics(string outputName, PdbFileReader pdbReader) {
    using StreamWriter w = new(outputName);
    HashSet<(string, string)> uniques = [];
    foreach ((PdbGlobalVariable data, ulong rva) in pdbReader.GlobalVariables
               .Select(gv => (gv, gv.RelativeVirtualAddress))
               .OrderBy(pair => pair.RelativeVirtualAddress)) {
      string dataName = data.Name;
      string typeName = data.Type.TypeIndex.IsSimple ? Types.CsSimpleType.ToCsName(data.Type.TypeIndex) : data.Type.Name;

      if (rva == 0 ||
          typeName.StartsWith('*') ||
          typeName.StartsWith('_') ||
          !uniques.Add((dataName, typeName))) {
        continue;
      }

      if (typeName.ContainsAny(['$', '@']) || dataName.ContainsAny(['$', '@'])) {
        continue;
      }

      w.Write("    ");
      if (data.Type is PdbArrayType arr) {
        PdbType eType = arr.ElementType;
        if (eType is not PdbSimpleType and not PdbClassType ||
            typeName.ContainsAny(['<', ':', '`']) ||
            dataName.ContainsAny(['<', ':', '`'])) {
          w.Write("// ");
        }

        typeName = eType.TypeIndex.IsSimple ? Types.CsSimpleType.ToCsName(eType.TypeIndex) : eType.Name;

        w.Write("public static unsafe Span<");
        w.Write(typeName);
        w.Write("> ");
        w.Write(dataName);
        w.Write(" => new((void*)(mioMemoryAddress + 0x");
        w.Write(rva.ToString("X"));
        w.Write("), ");
        w.Write(arr.Count);
        w.WriteLine(");");
        continue;
      }

      if (typeName.ContainsAny(['<', ':', '`']) ||
          dataName.ContainsAny(['<', ':', '`'])) {
        w.Write("// ");
      }

      w.Write("public static unsafe ref ");
      w.Write(typeName);
      w.Write(' ');
      w.Write(dataName);
      w.Write(" => ref *(");
      w.Write(typeName);
      w.Write("*)(mioMemoryAddress + 0x");
      w.Write(rva.ToString("X"));
      w.WriteLine(");");
    }
  }

  internal static TypeRecord[] GetTypeRecords(this TpiStream tpi) {
    var records = new TypeRecord[tpi.TypeRecordCount];
    uint count = (uint)tpi.TypeRecordCount + 4096U;
    for (uint i = 0; i < tpi.TypeRecordCount; i++) {
      uint index = i + 4096U;
      try {
        records[i] = tpi[new TypeIndex(index)];
      }
      catch (Exception ex) {
        Console.WriteLine(
          $"{ex.GetType().Name} thrown while reading type record at index {index}/{count}: {ex.Message}");
        records[i] = new NullRecord();
      }
    }

    return records;
  }

  /// Probably obsolete in favor of ProcedureHelper stuff
  private static Dictionary<ProcedureSymbol, (TypeIndex, StringReference)[]> BuildArgumentDictionary(PdbFile pdb) {
    return pdb.DbiStream.Modules
      .Where(m => m.LocalSymbolStream is not null)
      .SelectMany(m => m.LocalSymbolStream.AsEnumerable())
      .OfType<ProcedureSymbol>()
      .Where(p => p.FunctionType is not { IsNoneType: true })
      .GroupBy(p => (p, p.FunctionType))
      .ToDictionary(
        g => g.Key.p,
        p => p.Last().Children
          .OfType<LocalSymbol>()
          .Where(l => l.Name.String != "this" && l.Flags.HasFlag(LocalVariableFlags.IsParam))
          .Select(l => (l.Type, l.Name))
          .ToArray());
  }
}
