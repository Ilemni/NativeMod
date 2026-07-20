using System.CodeDom.Compiler;
using System.Runtime.InteropServices;
using PdbToCSharp.Dissect;
using PdbToCSharp.Types;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public sealed partial class SourceGen : IDisposable {
  public SourceGen(string pdbPath, string namespaceName, string outputPath) {
    Namespace = namespaceName.KeywordToVerbatim();
    Pdb = new PdbFileReader(pdbPath);
    _writers = new CsWriters(this, outputPath, namespaceName);
    CsTypes = new CsType[Pdb.PdbFile.TpiStream.TypeRecordCount];

    MemoryAddressFieldName = namespaceName.Replace(".", "") + "MemoryAddress";
    FunctionAddressFieldName = namespaceName.Replace(".", "") + "FunctionAddress";
  }

  /// Root namespace for all generated code.
  public readonly string Namespace;

  /// String for how generated code will reference the memory address of the module.
  public readonly string MemoryAddressFieldName;

  public readonly string FunctionAddressFieldName;

  internal int UnnamedStructs;
  internal int UnnamedUnions;
  internal int UnnamedEnums;

  internal readonly Dictionary<TypeIndex, CsType> CsSimpleTypes = [];
  internal readonly CsType?[] CsTypes;
  internal readonly Dictionary<TypeIndex, CsUdt> CsUdts = [];

  // Debug inspect to see which CsTypes are not mapped to records
  private IEnumerable<(CsType? Cs, TypeRecord Pdb)> CsPdbTypePairs =>
    Records.Select((record, i) => (CsTypes[i], record));

  // Debug inspect to see which CsTypes do get mapped to records
  private IEnumerable<(CsType Cs, TypeRecord Pdb)> CsNotNullPairs =>
    CsPdbTypePairs.Where(p => p.Cs is not null).Cast<(CsType Cs, TypeRecord Pdb)>();


  internal readonly PdbFileReader Pdb;
  private PdbFile PdbFile => Pdb.PdbFile;
  private readonly CsWriters _writers;

  internal TypeRecord[] Records = null!;
  private (TagRecord tag, TypeIndex index)[] _tagRecords = null!;

  public readonly Dictionary<(string Class, string Method, TypeIndex MFuncType), ProcedureInfo> ProcCache = [];
  public readonly Dictionary<(string Method, TypeIndex MFuncType), ProcedureInfo> GProcCache = [];
  public readonly Dictionary<string, ulong> VTableAddresses = [];

  public void PdbToCSharp() {
    Log.Step("Processing PDB");
    PreProcess();
    Process();
    Log.Step("Done.");
  }

  private void PreProcess() {
    // Lets us get argument names for the methods, which are not available in the TPI stream.
    Log.Step("Fixing symbols");
    ProcedureHelper.ReplaceNullSymbols(PdbFile);
    Log.Step("Loading procedure info");
    ProcessProcedureInfo();
    Log.Step("Loading VTables");
    ProcessVTables();

    Records = PdbFile.TpiStream.GetTypeRecords();

    Log.Step("Collecting tag records");
    _tagRecords = Records
      .Index()
      .Where(r => r.Item is TagRecord tag && AllowedName(tag.Name.String))
      .Select(r => ((TagRecord)r.Item, TypeIndex.FromArrayIndex(r.Index)))
      .ToArray();

    Log.Step("Creating non-forward reference types");
    foreach ((TagRecord _, TypeIndex index) in _tagRecords.Where(r => !r.tag.IsForwardReference)) {
      CsType.GetOrCreate(this, index);
    }

    Log.Step("Resolving forward references");
    // replace below parallel.foreach with regular foreach
    foreach ((int index, (TagRecord tag, TypeIndex i)) in _tagRecords.Index().Where(r => r.Item.tag.IsForwardReference)) {
      CsTypes[i.ArrayIndex] = ResolveForwardReference(tag, index, out TypeIndex rIndex)
        ? CsTypes[rIndex.ArrayIndex]!
        : CsType.GetOrCreate(this, i);
    }

    // Cannot use Paralle.ForEach to assign to dictionary
    foreach ((int i, CsUdt udt) in CsTypes.Index().Where(r => r.Item is CsUdt).Select(r => (r.Index, (CsUdt)r.Item!))) {
      CsUdts[udt.TypeIndex] = udt;
      if (i != udt.TypeIndex.ArrayIndex) {
        CsUdts[TypeIndex.FromArrayIndex(i)] = udt;
      }
    }

    Log.Step("Setting parents for all nested types");
    var nestedIter = CsUdts.Values.OfType<CsStructure>()
      .Where(p => p.Record.Options.HasFlag(ClassOptions.ContainsNestedClass))
      .Select(p => (parent: p, p.Record.GetFields(Pdb).OfType<NestedTypeRecord>()));

    foreach (var iter in nestedIter) {
      foreach (NestedTypeRecord nested in iter.Item2) {
        if (CsUdts.TryGetValue(nested.Type, out CsUdt? nestedCs) && nestedCs.Record.IsNested) {
          nestedCs.SetParent(iter.parent, nested);
        }
      }
    }

    // Force loading of lazy-loaded props
    // If anything throws, we'll know before letting the program do IO.
    foreach (CsType? csType in CsTypes) {
      if (csType is null) {
        continue;
      }
      _ = csType.FullName;

      switch (csType) {
        case CsStructure csStruct: {
          _ = csStruct.BaseClasses;
          _ = csStruct.NestedClasses;
          foreach (CsInstanceField f in csStruct.InstanceFields) {
            _ = f.FieldType;
          }

          foreach (CsStaticField f in csStruct.StaticFields) {
            _ = f.FieldType;
          }

          foreach (CsInstanceMethod m in csStruct.InstanceMethods) {
            _ = m.ParameterTypes;
          }

          break;
        }
        case CsArray csArray:
          _ = csArray.InnerElement;
          break;
      }
    }

    Log.Step("Merging namespaces into types with the same name");
    var rootTypes = CsUdts.Values.Where(c => c.Parent is null).ToArray();
    foreach (CsUdt potentialChild in rootTypes.Where(c => c.Namespace is not null)) {
      foreach (CsStructure potentialParent in rootTypes.OfType<CsStructure>()) {
        if (ReferenceEquals(potentialParent, potentialChild)) {
          continue;
        }

        if (potentialChild.Namespace! == potentialParent.FullName) {
          potentialChild.SetParent(potentialParent, null);
          potentialParent.NestedClasses.Add(potentialChild);
          break;
        }
      }
    }
  }

  private void Process() {
    WriteInlineArrays();
    WriteGlobals();
    _writers.CreateModuleFile();

    Log.Step("Writing all other types... ");
    HashSet<TypeIndex> created = [];
    HashSet<string> duplicateNames = [];
    Dictionary<string, Dictionary<string, CsUdt>> addedClassesByNamespace = [];
    Dictionary<string, Dictionary<string, CsEnum>> addedEnumsByNamespace = [];

    Dictionary<string, HashSet<CsUdt>> duplicates = [];
    foreach (CsUdt udt in CsUdts.Values) {
      var h = CollectionsMarshal.GetValueRefOrAddDefault(duplicates, udt.FullyQualifiedName, out bool _) ??= [];
      h.Add(udt);
    }

    var dups = duplicates.Where(v => v.Value.Count > 1).OrderBy(v => v.Key).ToArray();

    // var ims = CsUdts
    //   .Select(s => (s.Value,
    //     (s.Value as CsStructure)?.InstanceMethods
    //     .Where(m => m.MethodRecord.Options.HasFlag(FunctionOptions.Constructor)).ToArray()))
    //   .Where(t => t.Item2 is { Length: > 0 })
    //   .ToArray();

    foreach (CsUdt udt in CsUdts.Values.Where(u => u.Parent is null)
               .DistinctBy(u => u.TypeIndex)
               .OrderBy(u => u.Record.IsForwardReference)
               .ThenBy(u => u.FullyQualifiedName)) {
      if (!created.Add(udt.TypeIndex) || !duplicateNames.Add(udt.FullyQualifiedName)) {
        // Was a forward reference that has a body defined in the pdb; both entries are identical
        continue;
      }

      switch (udt) {
        case CsEnum csEnum: {
          if (CheckDuplicateName(csEnum, addedEnumsByNamespace)) {
            IndentedTextWriter writer = _writers.GetMatching(csEnum);
            WriteEnumType(csEnum, writer);
          }

          break;
        }
        case CsStructure csStructure: {
          if (CheckDuplicateName(csStructure, addedClassesByNamespace)) {
            IndentedTextWriter writer = _writers.GetMatching(csStructure);
            WriteStruct(csStructure, writer);
          }

          break;
        }
      }
    }

    return;

    static bool CheckDuplicateName<T>(T udt, Dictionary<string, Dictionary<string, T>> dict) where T : CsUdt {
      string fullName = udt.Namespace is { } ns
        ? ns + '.' + udt.FullName
        : udt.FullName;
      if (!dict.TryGetValue(fullName, out var nsDict)) {
        nsDict = [];
        dict[fullName] = nsDict;
      }
      else {
        if (!nsDict.TryAdd(fullName, udt)) {
          // TODO: find a way to do proper de-duplicating
          // Log.Warn($"Duplicate class name \"{udt.FullName}\" in namespace \"{udt.Namespace}\".");
          return false;
        }
      }

      return true;
    }
  }

  private void WriteGlobals() {
    if (Pdb.GlobalVariables.Length <= 0) {
      return;
    }

    Log.Step("Writing globals");
    IndentedTextWriter globalsWriter = _writers.GlobalsWriter;
    globalsWriter.Write("public struct Globals {");
    globalsWriter.Indent++;
    HashSet<string> fields = [];
    foreach (PdbGlobalVariable globalVar in Pdb.GlobalVariables.OrderBy(g => g.RelativeVirtualAddress)) {
      TypeIndex type = globalVar.Type.TypeIndex;
      CsType? csType = type.IsSimple ? CsSimpleTypes[type] : CsTypes[type.ArrayIndex];
      if (csType is null || !fields.Add(globalVar.Name)) {
        continue;
      }

      globalsWriter.Write("public static unsafe ref ");
      globalsWriter.Write(csType.FullyQualifiedName);
      globalsWriter.Write(' ');
      globalsWriter.Write(globalVar.Name.SanitizeName(true, true));
      globalsWriter.Write(" => ref *(");
      globalsWriter.Write(csType.FullyQualifiedName);
      globalsWriter.Write("*)(");
      globalsWriter.Write(MemoryAddressFieldName);
      globalsWriter.Write(" + 0x");
      globalsWriter.Write($"{globalVar.RelativeVirtualAddress:X}");
      globalsWriter.WriteLine(");");
    }

    globalsWriter.Indent--;
    globalsWriter.Write('}');
    globalsWriter.Flush();
  }

  private void ProcessProcedureInfo() {
    DbiModuleList modules = PdbFile.DbiStream.Modules;
    HashSet<string> argNames = [];
    foreach (DbiModuleDescriptor module in modules) {
      if (module.LocalSymbolStream == null) continue;

      foreach (SymbolRecord symbol in module.LocalSymbolStream.AsEnumerable()) {
        if (symbol is not ProcedureSymbol procSym) {
          continue;
        }

        (TypeIndex, string)[] args = procSym.Children.OfType<LocalSymbol>().Where(l =>
            l.Flags.HasFlag(LocalVariableFlags.IsParam) &&
            l.Name.String != "this")
          .Select(l => (l.Type, l.Name.String))
          .ToArray();

        // Sanitize and deduplicate argument names, as they may be missing or invalid
        argNames.Clear();
        foreach ((int i, (TypeIndex _, string argName)) in args.Index()) {
          ref string name = ref args[i].Item2;
          if (string.IsNullOrWhiteSpace(argName)) {
            name = $"arg{i + 1}";
            continue;
          }

          if (!argNames.Add(argName)) {
            name = $"{argName}_{i + 1}";
          }

          name = name.SanitizeName(true, true).KeywordToVerbatim();
        }

        TypeRecord? functionRecord = PdbFile.TryGetRecord(procSym.FunctionType);
        if (functionRecord is ProcedureRecord gProc) {
          (string, TypeIndex) gKey = (procSym.Name.String, procSym.FunctionType);
          ProcedureInfo gProcInfo = new(procSym, args, args.Length == gProc.ParameterCount) {
            Rva = PdbFile.FindRelativeVirtualAddress(procSym.Segment, procSym.Offset)
          };
          GProcCache.TryAdd(gKey, gProcInfo);
          continue;
        }

        if (functionRecord is not MemberFunctionRecord mProc) {
          continue;
        }

        string fullyQualifiedName = procSym.Name.String;

        // Extract method name and parent class name
        int lastColon = fullyQualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        if (lastColon == -1) continue; // Skip free/global functions

        string methodName = fullyQualifiedName[(lastColon + 2)..];
        string parentClassName = fullyQualifiedName[..lastColon];

        (string, string, TypeIndex) mKey = (parentClassName, methodName, procSym.FunctionType);

        ProcedureInfo mProcInfo = new(procSym, args, args.Length == mProc.ParameterCount) {
          Rva = PdbFile.FindRelativeVirtualAddress(procSym.Segment, procSym.Offset)
        };

        // Handle potential duplicate entries from templates or incremental linking safely
        ProcCache.TryAdd(mKey, mProcInfo);
      }
    }
  }

  private void ProcessVTables() {
    foreach (Public32Symbol? s in PdbFile.PublicsStream.PublicSymbols) {
      string name = s.Name.String;
      if (name.StartsWith("??_7")) {
        string undecoratedName = CsNameUndecorator.UnDecorateSymbolName(name);
        int indexOf = undecoratedName.IndexOf(".`vftable'", StringComparison.Ordinal);
        if (indexOf != -1) {
          name = undecoratedName[..indexOf];
          VTableAddresses[name] = PdbFile.FindRelativeVirtualAddress(s.Segment, s.Offset);
        }
      }
    }
  }

  private bool ResolveForwardReference(TagRecord tag, int start, out TypeIndex index) {
    // Try resolving forward first
    (TagRecord? resolved, index) = _tagRecords
      .Skip(start)
      .FirstOrDefault(r => !r.tag.IsForwardReference &&
        r.tag.Name.String == tag.Name.String &&
        r.tag.UniqueName.String == tag.UniqueName.String);

    if (resolved is null) {
      // Much less common, some types may resolve backwards
      (resolved, index) = _tagRecords
        .Take(start)
        .LastOrDefault(r => !r.tag.IsForwardReference &&
          r.tag.Name.String == tag.Name.String &&
          r.tag.UniqueName.String == tag.UniqueName.String);
    }

    return resolved is not null;
  }

  private static bool AllowedName(string name) {
    return !name.Contains("unnamed struct at") &&
      !name.Contains("`lambda at") && !name.Contains("<lambda_");
  }

  public void Dispose() {
    Pdb.Dispose();
    _writers.Dispose();
  }
}
