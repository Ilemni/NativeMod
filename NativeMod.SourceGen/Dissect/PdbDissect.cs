using System.CodeDom.Compiler;
using System.Runtime.InteropServices;
using NativeMod.SourceGen.Lang.Cs;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.DebugSubsections;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;

namespace NativeMod.SourceGen.Dissect;

internal static class PdbDissect {
  public static void DissectPdb() {
    const string pdbPath = "mio.pdb";
    string pdbName = Path.GetFileNameWithoutExtension(pdbPath);
    string output = $"output/dissect/{pdbName}_";
    Directory.CreateDirectory("output/dissect");

    Log.Step($"Dissecting {pdbPath}");
    using PdbFileReader pdbReader = new(pdbPath);
    PdbFile pdb = pdbReader.PdbFile;
    CsGen gen = CsGen.CreateDebugGen(pdbReader);
    pdb.FixNulls();

    Log.Step("Writing TNode_d.txt");
    WriteTNodeD(output + "TNode_d.txt", pdbReader, gen);

    return;
    // Log.Step($"Writing functions.txt");
    // using (StreamWriter debugWriter = new(output + "functions.txt")) {
    //   HashSet<string> funcNames = [];
    //   const Flags flags =
    //     Flags.NoAllocationLanguage | Flags.NoAccessSpecifiers | Flags.NoLeadingUnderscores |
    //     Flags.NoMicrosoftKeywords | Flags.NoComplexType;
    //   foreach (PdbPublicSymbol? sym in pdbReader.PublicSymbols.OrderBy(s => s.RelativeVirtualAddress)) {
    //     string csName = Lang.Cs.CsNameUndecorator.UnDecorateSymbolName(sym.Name, flags);
    //     if (!csName.Contains('(')) {
    //       // Not a function
    //       continue;
    //     }
    //
    //     int argStartIndex = csName.IndexOf('(');
    //     if (!csName.AsSpan(0, argStartIndex).Contains('.')) {
    //       // Not a member function
    //       continue;
    //     }
    //
    //     if (csName.Contains("<lambda")) {
    //       // Don't care about lambdas
    //       continue;
    //     }
    //
    //     if (csName.Contains("`RTTI")) {
    //       // Don't care about this
    //       continue;
    //     }
    //
    //     debugWriter.Write("  RVA: 0x");
    //     debugWriter.Write($"{sym.RelativeVirtualAddress:X}");
    //     debugWriter.Write("   | C#: ");
    //     debugWriter.Write(csName);
    //
    //     if (!funcNames.Add(csName)) {
    //       // Already seen this function name
    //       debugWriter.Write(" // Duplicate function name");
    //     }
    //
    //     debugWriter.WriteLine();
    //   }
    // }

    // ProcedureHelper.Load(pdbReader);

    // Everything below this point is for debugging and analysis of the PDB file

    // Log.Step("Writing functions2.txt");
    // using (StreamWriter funcWriter = new(output + "functions2.txt")) {
    //   // var syms = pdbReader.PublicSymbols
    //   //   .Select(s => (s.RelativeVirtualAddress, s.GetUndecoratedName()))
    //   //   .OrderBy(s => s.RelativeVirtualAddress)
    //   //   .ToArray();
    //
    //   var funcs2 = pdbReader.Functions
    //     .Where(f => f.FunctionType.TypeIndex.TryAs<MemberFunctionRecord>(pdb, out _))
    //     .Select(f => (f.RelativeVirtualAddress,
    //       $"\"{f.Name}\" ({f.FunctionType.Name}) ({AsStr(pdb, f.FunctionType.TypeIndex)})"))
    //     .OrderBy(f => f.RelativeVirtualAddress)
    //     .ToArray();
    //
    //   string AsStr(PdbFile p, TypeIndex t) {
    //     if (t.IsSimple) {
    //       return t.SimpleKind is SimpleTypeKind.None ? "none" : Lang.Cs.CsSimpleType.ToCsName(t);
    //     }
    //
    //     TypeRecord? type = t.TryAsRecord(p);
    //     return type is null ? $"<null type for {t}>" : type.ToString(pdb);
    //   }
    //
    //   foreach ((ulong RelativeVirtualAddress, string Name) f in /*syms.Union(*/
    //            funcs2 /*)*/.OrderBy(f => f.RelativeVirtualAddress)) {
    //     funcWriter.WriteLine($"{f.RelativeVirtualAddress:X8} | {f.Name}");
    //   }
    // }

    // Log.Step("Fetching TPI records");
    // var tpiRecords = pdb.TpiStream.GetTypeRecords();

    Log.Step("Fetching IPI records");
    var ipiRecords = pdb.IpiStream.GetTypeRecords();

    Log.Step("Writing globals.txt");
    WriteGlobals(output + "globals.txt", pdb);

    Log.Step("Writing locals.txt");
    WriteLocals(output + "locals.txt", pdb);

    // Log.Step("Writing statics.txt");
    // WriteStatics(output + "statics.txt", pdbReader);


    // Log.Step("Writing tpi.txt");
    // using (IndentedTextWriter writer = new(new StreamWriter(output + "tpi.txt"))) {
    //   foreach (TypeRecord typeRecord in tpiRecords) {
    //     writer.Write(typeRecord.Kind);
    //     writer.Write(" | ");
    //     writer.WriteRecord(typeRecord, pdb);
    //     writer.WriteLine();
    //   }
    // }

    Log.Step("Writing ipi.txt");
    using (IndentedTextWriter writer = new(new StreamWriter(output + "ipi.txt"))) {
      foreach (TypeRecord typeRecord in ipiRecords) {
        writer.Write(typeRecord.Kind);
        writer.Write(" | ");
        writer.WriteRecord(typeRecord, pdb);
        writer.WriteLine();
      }
    }

    Log.Step("Writing ipi_src.txt");
    WriteIpiSrc(output + "ipi_src.txt", ipiRecords, pdb);

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

    // Log.Step("Writing pdb headers");
    // WritePdbHeaders(pdbName, pdb);

    // Debug list of types that exist in the PDB, to get an idea of what we're working with and identify any unhandled types

    Dictionary<(TypeLeafKind Kind, string), int> tpiTypes = [];
    Dictionary<(TypeLeafKind Kind, string), int> ipiTypes = [];
    // foreach (TypeRecord typeRecord in tpiRecords) {
    //   IncrementCount(tpiTypes, typeRecord, typeRecord.Kind);
    // }

    // foreach (TypeRecord typeRecord in ipiRecords) {
    //   IncrementCount(ipiTypes, typeRecord, typeRecord.Kind);
    // }

    // These are her just to debug inspect into
    var orderedTpiTypes = tpiTypes.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();
    var orderedIpiTypes = ipiTypes.OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();

    var orderedGlobalTypes = GetGlobalSymbolTypeCounts(pdb).OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();
    var orderedModuleTypes = GetModuleSymbolTypeCounts(pdb).OrderBy(kv => kv.Key.Kind).ThenBy(kv => kv.Value).ToArray();


    // Log.Step("Writing args.txt");
    // using (IndentedTextWriter writer = new(new StreamWriter(output + "args.txt"))) {
    //   var argDict = BuildArgumentDictionary(pdb);
    //   foreach ((ProcedureSymbol key, var value) in argDict.OrderBy(kvp => kvp.Key.FunctionType.Index)) {
    //     if (value.Length > 0) {
    //       writer.WriteSymLine(key);
    //     }
    //   }
    // }

    // WriteCppHeader(output + ".h", pdb, tpiRecords);
    // Log.Step("Writing template_names.txt");
    // WriteTemplateNames(output + "template_names.txt", tpiRecords);

    Log.Step("Done.");
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

  private static void WriteTNodeD(string fileName, PdbFileReader pdb, CsGen gen) {
    using StreamWriter streamWriter = new(fileName);
    IndentedTextWriter writer = new(streamWriter);

    Dictionary<string, ProcedureSymbol> smallestProcedures = [];

    foreach (DbiModuleDescriptor module in pdb.PdbFile.DbiStream.Modules
               .Where(m => m.LocalSymbolStream is not null)) {
      var procedures = module.LocalSymbolStream.AsEnumerable()
        .OfType<ProcedureSymbol>()
        .Where(p => p.Children.OfType<InlineSiteSymbol>().Any())
        .Select(p => (p, i: p.Children.OfType<InlineSiteSymbol>()
          .Select(i => (i, m: i.Inlinee.TryAsRecord(pdb.PdbFile.IpiStream)))
          .Where(t => t.m is MemberFunctionIdRecord)
          .Select(t => (t.i, name: ((MemberFunctionIdRecord)t.m!).Name.String,
            m: gen.GetOrCreate<CsMemberFunctionType>(((MemberFunctionIdRecord)t.m!).FunctionType)))
          .Where(t => t.m.ClassType.SelfName.StartsWith("TNode") && t.name != "TNode")
          .ToArray()))
        .Where(t => t.i.Length > 0)
        .OrderBy(t => t.p.CodeSize)
        .ToArray();
      if (procedures.Length == 0) {
        continue;
      }

      writer.WriteMany("Module: ", module.ModuleName.String);
      using (writer.BracedScope()) {
        foreach ((ProcedureSymbol p, var inlines) in procedures) {
          writer.WriteMany(p.Name.String, " | 0x", p.Offset.ToString("X8"));
          writer.WriteMany(" | Size: ", p.CodeSize.ToString());
          using (writer.BracedScope()) {
            foreach ((InlineSiteSymbol i, string name, CsMemberFunctionType m) in inlines) {
              string className = ((CsUdt)m.ClassType).Record.Name.String;
              if (!smallestProcedures.TryGetValue(className, out ProcedureSymbol? smallestProcedure) || p.CodeSize < smallestProcedure.CodeSize) {
                smallestProcedures[className] = p;
              }
              writer.WriteMany(p.Name.String, " | ");
              writer.Write(i.End.ToString("X8"));
              writer.Write(" | ");
              writer.Write(className);
              writer.Write('.');
              writer.Write(name);
              writer.Write('(');
              writer.WriteParameterTypes(m.ParameterTypes, static t => t.FullyQualifiedName);
              writer.Write(") -> ");
              writer.Write(m.ReturnType.FullyQualifiedName);
              writer.WriteLine();
            }
          }
        }
      }
    }

    writer.WriteLine();
    foreach ((string className, ProcedureSymbol smallestProcedure) in smallestProcedures.OrderBy(kv => kv.Value.CodeSize)) {
      writer.WriteMany("Smallest procedure for class ", className, ": ", smallestProcedure.Name.String);
      writer.Write(" | 0x");
      writer.WriteLine(smallestProcedure.CodeSize.ToString("X"));
    }
  }

  private static void WriteIpiSrc(string ipiSrcTxt, TypeRecord[] ipiRecords, PdbFile pdb) {
    using IndentedTextWriter writer = new(new StreamWriter(ipiSrcTxt));
    DbiModuleList moduleList = pdb.DbiStream.Modules;
    var namesDict = pdb.InfoStream.NamesMap.Dictionary;
    foreach (UdtModuleSourceLineRecord modSrc in ipiRecords.OfType<UdtModuleSourceLineRecord>()
               // .OrderBy(m => m.SourceFile.Index)
               .OrderBy(m => m.Module)
               .ThenBy(m => m.SourceFile.Index)
               .ThenBy(m => m.LineNumber)
            ) {
      if (modSrc.UDT.TryAsRecord(pdb) is not TagRecord { IsForwardReference: false } udt) {
        continue;
      }

      int moduleId = modSrc.Module - 1;
      DbiModuleDescriptor module = moduleList[moduleId];
      string sourceFile = namesDict[modSrc.SourceFile.Index];

      writer.Write(module.ModuleName.String);
      writer.Write(" | ");
      writer.Write(sourceFile);
      writer.Write(" | ");
      writer.WriteRecord(udt, pdb);
      writer.WriteLine();
    }

    writer.Flush();
  }

  private static void WritePdbHeaders(string pdbName, PdbFile pdb) {
    using StreamWriter writer = new(pdbName + "_pdbTonicHeaders.txt");
    const string key = "tonic";
    var tonicHeaderFiles = pdb.DbiStream.Modules
      .SelectMany(m => m.Files)
      .Where(s => s.Contains(key))
      .Distinct()
      .Order();
    foreach (string file in tonicHeaderFiles) {
      int i = file.IndexOf(key, StringComparison.OrdinalIgnoreCase);
      writer.WriteLine(file.AsSpan(i));
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
    using (StreamWriter symbolsWriter = new(outputName)) {
      IndentedTextWriter writer = new(symbolsWriter);
      foreach (DbiModuleDescriptor module in pdb.DbiStream.Modules) {
        if (module.LocalSymbolStream is not { } mSymbols) {
          continue;
        }

        writer.Indent = 0;
        writer.WriteMany("Module: ", Path.GetFileName(module.ModuleName.String));
        using var _ = writer.BracedScope();

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
              writer.InnerWriter.Write($"{symbolRecord.SymbolStreamIndex} ".PadLeft(6));
              switch (symbolRecord) {
                case BlockSymbol block:
                  writer.Write("{ ");
                  writer.WriteLine(block.Name.String);
                  break;
                case InlineSiteSymbol inline:
                  writer.WriteSymLine(inline);
                  break;
                case EndSymbol { Kind: SymbolRecordKind.S_END }:
                  writer.WriteLine('}');
                  break;
                case EndSymbol { Kind: SymbolRecordKind.S_INLINESITE_END }:
                  writer.WriteLine("}\t/* Inline Site End */");
                  break;
                default:
                  writer.WriteSymLine(symbolRecord);
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
      }
    }

    using (StreamWriter symbolsWriter = new(outputName.Replace(".txt", "_procedures.txt"))) {
      IndentedTextWriter writer = new(symbolsWriter);
      foreach (DbiModuleDescriptor module in pdb.DbiStream.Modules) {
        if (module.LocalSymbolStream is not { } mSymbols) {
          continue;
        }

        writer.Indent = 0;
        writer.WriteMany("Module: ", Path.GetFileName(module.ModuleName.String));
        using var _ = writer.BracedScope();

        foreach (ProcedureSymbol proc in mSymbols.AsEnumerable().OfType<ProcedureSymbol>()) {
          writer.Write("ProcSym ");
          writer.WriteSym(proc);
          WriteNested(writer, proc.Children);
        }
      }
    }

    static void WriteNested(IndentedTextWriter writer, SymbolRecord[] children) {
      if (children.Length == 0) {
        writer.WriteLine();
        return;
      }

      using var _ = writer.BracedScope();
      foreach (LocalSymbol local in children.OfType<LocalSymbol>()) {
        writer.WriteSymLine(local);
      }

      foreach (InlineSiteSymbol inline in children.OfType<InlineSiteSymbol>()) {
        writer.WriteSym(inline);
        WriteNested(writer, inline.Children);
      }
    }
  }

  private static void WriteGlobals(string outputName, PdbFile pdb) {
    ArrayCache<SymbolRecord> globalSymbols = pdb.GlobalsStream.Symbols;
    for (int i = 0; i < pdb.GlobalsStream.HashRecords.Length; i++) {
      _ = globalSymbols[i];
    }

    using (StreamWriter streamWriter = new(outputName)) {
      IndentedTextWriter writer = new(streamWriter);
      foreach (SymbolRecord symbol in globalSymbols) {
        writer.WriteSymLine(symbol);
      }
    }

    using (StreamWriter streamWriter = new(outputName.Replace(".txt", "_procedures.txt"))) {
      IndentedTextWriter writer = new(streamWriter);
      foreach ((ProcedureReferenceSymbol prs, ProcedureSymbol ps) in MapReferences(pdb)
                 .OrderBy(kv => kv.prs.Name.String)
              ) {
        TypeRecord? record = ps.FunctionType.TryAsRecord(pdb);
        if (record is not ProcedureRecord and not MemberFunctionRecord) {
          writer.WriteMany("0x", ps.Offset.ToString("X8"), " | ");
          writer.Write(prs.Name.String);
          writer.Write(" NULL FUNCTION");
          writer.WriteLine();
          continue;
        }

        TypeIndex returnType = (record as ProcedureRecord)?.ReturnType ?? ((MemberFunctionRecord)record).ReturnType;

        writer.WriteMany("0x", ps.Offset.ToString("X8"), " | ");
        writer.Write(prs.Name.String);
        writer.Write("(");
        writer.WriteParameterTypesAndNames(ps.GetNamedArgs());
        writer.Write(") -> ");
        writer.Write(returnType.ToString(pdb));
        writer.WriteLine();
      }
    }
  }

  private static List<(ProcedureReferenceSymbol prs, ProcedureSymbol proc)> MapReferences(PdbFile pdb) {
    List<(ProcedureReferenceSymbol, ProcedureSymbol)> results = [];

    if (pdb.DbiStream is null) {
      throw new InvalidOperationException("PDB file is missing a valid DBI stream or Section Map.");
    }

    DbiModuleList modules = pdb.DbiStream.Modules;

    var syms = pdb.GlobalsStream.Symbols.OfType<ProcedureReferenceSymbol>();
    foreach (ProcedureReferenceSymbol refSym in syms) {
      results.Add((refSym, refSym.GetProcedureSymbol(modules)));
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
      string typeName = data.Type.TypeIndex.IsSimple
        ? Lang.Cs.CsSimpleType.ToCsName(data.Type.TypeIndex)
        : data.Type.Name;

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

        typeName = eType.TypeIndex.IsSimple ? Lang.Cs.CsSimpleType.ToCsName(eType.TypeIndex) : eType.Name;

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
