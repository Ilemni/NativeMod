using System.Runtime.InteropServices;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;
using Writer = System.CodeDom.Compiler.IndentedTextWriter;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed partial class CsGen : LangGen {
  private static readonly Dictionary<Guid, CsGen> Instances = [];
  private static readonly Dictionary<Guid, CsGen> DebugInstances = [];

  public override CsType?[] Types { get; }

  public bool WriteHooks { get; set; }
  private readonly Dictionary<TypeIndex, CsType> _csSimpleTypes = [];
  private readonly Dictionary<TypeIndex, CsUdt> _csUdts = [];

  private CsGen(PdbFileReader pdb, string namespaceName, string bindsPath, string nativeModPath) : base(pdb,
    namespaceName, bindsPath, nativeModPath) {
    Types = new CsType[pdb.PdbFile.TpiStream.TypeRecordCount];
    Writers = new CsWriters(this);
    Log.Step("Fixing symbols");
    Pdb.FixNulls();
    Records = Pdb.TpiStream.GetTypeRecords();
    WriteHooks = true;
  }

  /// For debug/dissect purposes only
  private CsGen(PdbFileReader pdb) : base(pdb, null!, null!, null!) {
    Types = new CsType[pdb.PdbFile.TpiStream.TypeRecordCount];
    Log.Step("Fixing symbols");
    Pdb.FixNulls();
    Records = Pdb.TpiStream.GetTypeRecords();
  }

  protected override CsWriters Writers { get; }

  public static CsGen GetGen(PdbFile pdb) {
    if (!Instances.TryGetValue(pdb.InfoStream.Header.Guid, out CsGen? result) &&
        !DebugInstances.TryGetValue(pdb.InfoStream.Header.Guid, out result)) {
      throw new ArgumentException(
        "No CsSet instance found for specified PDB file. Ensure that CsSet.Create() has been called for this PDB file.");
    }

    return result;
  }

  public static CsGen CreateGen(PdbFileReader pdb, string namespaceName, string bindsPath, string nativeModPath) {
    CsGen gen = new(pdb, namespaceName, bindsPath, nativeModPath);
    Instances[pdb.PdbFile.InfoStream.Header.Guid] = gen;
    return gen;
  }

  public static CsGen CreateDebugGen(PdbFileReader pdb) {
    CsGen gen = new(pdb);
    DebugInstances[pdb.PdbFile.InfoStream.Header.Guid] = gen;
    return gen;
  }

  public override CsType GetOrCreate(TypeIndex index) {
    if (TryGetOrCreate(index) is { } result) {
      return result;
    }

    string recordName = index.TryAsRecord(Pdb)?.GetType().Name ?? "null";
    throw new InvalidOperationException($"Failed to create CsType for TypeIndex {index} with record {recordName}");
  }

  public T GetOrCreate<T>(TypeIndex index) where T : CsType {
    CsType result = GetOrCreate(index);
    if (result is not T typedResult) {
      throw new InvalidCastException(
        $"Expected type {typeof(T).Name} but got {result.GetType().Name} for TypeIndex {index}");
    }

    return typedResult;
  }

  public override CsType? TryGetOrCreate(TypeIndex index) {
    if (index.IsSimple) {
      return GetOrCreateSimple(index);
    }

    if (Types[index.ArrayIndex] is { } existing) {
      return existing;
    }

    CsType? result = Records[index.ArrayIndex] switch {
      ModifierRecord modifierRecord => new CsModifiedType(this, index, modifierRecord),
      PointerRecord pointerRecord => new CsPointerType(this, index, pointerRecord),
      ArrayRecord arrayRecord => new CsArray(this, index, arrayRecord),
      ClassRecord classRecord => new CsStruct(this, index, classRecord),
      UnionRecord unionRecord => new CsUnion(this, index, unionRecord),
      EnumRecord enumRecord => new CsEnum(this, index, enumRecord),
      MemberFunctionRecord mFuncRecord => new CsMemberFunctionType(this, index, mFuncRecord),
      ProcedureRecord procRecord => new CsFunctionType(this, index, procRecord),
      VirtualFunctionTableShapeRecord vftRecord => new CsVft(this, index, vftRecord),
      _ => null
    };
    Types[index.ArrayIndex] = result;
    return result;
  }

  private CsType GetOrCreateSimple(TypeIndex index) {
    if (_csSimpleTypes.TryGetValue(index, out CsType? existingSimple)) {
      return existingSimple;
    }

    CsType result = index.SimpleMode == SimpleTypeMode.Direct
      ? new CsSimpleType(this, index)
      : new CsSimplePointerType(this, index);
    _csSimpleTypes[index] = result;
    return result;
  }

  public override string UnDecorateSymbolName(string name) => CsNameUndecorator.UnDecorateSymbolName(name);

  public override void PreProcess() {
    base.PreProcess();
    PopulateUdts();

    SymbolStream pdbStream = Pdb.PdbSymbolStream;
    for (int i = 0; i < pdbStream.References.Count; i++) {
      _ = pdbStream[i];
    }

    Dictionary<SymbolRecordKind, SymbolRecord[]> symbolRecordsByKind = [];
    foreach (ushort i in (ushort[])Enum.GetValuesAsUnderlyingType<SymbolRecordKind>()) {
      var a = pdbStream[(SymbolRecordKind)i];
      if (a.Length > 0) {
        symbolRecordsByKind[(SymbolRecordKind)i] = a;
      }
    }

    Log.Step("Setting parents for all nested types");
    var nestedIter = Types.OfType<CsStructure>().Distinct()
      .Where(p => p.Record.Options.HasFlag(ClassOptions.ContainsNestedClass))
      .Select(p => (parent: p, p.Record.FieldList.As<FieldListRecord>(Pdb).Fields.OfType<NestedTypeRecord>()));

    foreach (var iter in nestedIter) {
      foreach (NestedTypeRecord nested in iter.Item2) {
        string parentName = iter.parent.Record.Name.String;
        if (_csUdts.TryGetValue(nested.Type, out CsUdt? nestedCs)) {
          string nestedName = nestedCs.Record.Name.String;

          bool isChild = nestedName != parentName && nestedName.StartsWith(parentName);
          if (isChild) {
            nestedCs.SetParent(iter.parent, nested);
            // if (nestedCs.IsForwardReference) {
            //   Log.Warn($"Forward reference set as child. Parent: {iter.parent.FullName}, Child: {nestedCs.SelfName}");
            // }
          }
        }
      }
    }

    // Force loading of lazy-loaded props
    // If anything throws, we'll know before letting the program do IO.
    foreach (CsType csType in Types.OfType<CsType>()) {
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

          foreach (CsMethod m in csStruct.AllMethods) {
            _ = m.MemberFunction.ParameterTypes;
          }

          break;
        }
        case CsArray csArray:
          _ = csArray.InnerElement;
          break;
      }
    }

    Log.Step("Merging namespaces into types with the same name");
    var potentialParents = Types.OfType<CsStructure>().Distinct().OrderBy(p => p.IsForwardReference).ToArray();
    var potentialChildren = Types.OfType<CsUdt>().Distinct()
      .Where(c => c.Parent is null && c.Namespace is not null)
      .OrderBy(s => s.Namespace)
      .ToArray();
    foreach (CsUdt potentialChild in potentialChildren) {
      if (potentialChild.Namespace is null) {
        continue;
      }

      foreach (CsStructure potentialParent in potentialParents) {
        if (ReferenceEquals(potentialParent, potentialChild)) {
          continue;
        }

        if (potentialChild.Namespace! == potentialParent.FullyQualifiedName) {
          potentialChild.SetParent(potentialParent, null);
          potentialParent.NestedClasses.Add(potentialChild);
          break;
        }
      }
    }
  }

  private void PopulateUdts() {
    foreach ((int i, CsUdt udt) in Types.Index().Where(r => r.Item is CsUdt).Select(r => (r.Index, (CsUdt)r.Item!))) {
      _csUdts.TryAdd(udt.TypeIndex, udt);
      if (i != udt.TypeIndex.ArrayIndex) {
        _csUdts.TryAdd(TypeIndex.FromArrayIndex(i), udt);
      }
    }
  }

  public override void WriteAll() {
    WriteGlobalFields();
    WriteGlobalFunctions();
    WriteInlineArrays();
    Writers.CreateModuleFile();
    WriteNativeModHookClasses();
    PopulateUdts();

    Log.Step("Writing all other types");
    HashSet<TypeIndex> created = [];
    HashSet<string> duplicateNames = [];
    Dictionary<string, Dictionary<string, CsStructure>> addedClassesByNamespace = [];
    Dictionary<string, Dictionary<string, CsEnum>> addedEnumsByNamespace = [];

    Dictionary<string, HashSet<CsUdt>> duplicates = [];
    foreach (CsUdt udt in _csUdts.Values) {
      var h = CollectionsMarshal.GetValueRefOrAddDefault(duplicates, udt.GlobalQualifiedName, out bool _) ??= [];
      h.Add(udt);
    }

    foreach (CsUdt udt in _csUdts.Values.Where(u => u.Parent is null)
               .DistinctBy(u => u.TypeIndex)
               .OrderBy(u => u.IsForwardReference)
               .ThenBy(u => u.GlobalQualifiedName)) {
      if (!created.Add(udt.TypeIndex) || !duplicateNames.Add(udt.GlobalQualifiedName)) {
        // Was a forward reference that has a body defined in the pdb; both entries are identical
        // Log.Warn(string.IsNullOrEmpty(udt.Namespace)
        //   ? $"Duplicate class name \"{udt.FullName}\"."
        //   : $"Duplicate class name \"{udt.FullName}\" in namespace \"{udt.Namespace}\".");
        continue;
      }

      switch (udt) {
        case CsEnum csEnum: {
          WriteIfNotDuplicate(csEnum, addedEnumsByNamespace, WriteEnumType);
          break;
        }
        case CsStructure csStruct: {
          WriteIfNotDuplicate(csStruct, addedClassesByNamespace, WriteStruct);
          if (csStruct.DefinedMethods.Length != 0) {
            if (WriteHooks) {
              Writer writer = Writers.CreateHookWriter(csStruct.SelfName, csStruct.Namespace, out bool mustDispose);
              WriteClassHooks(csStruct, writer);
              if (mustDispose) {
                writer.Flush();
                writer.Dispose();
              }
            }
          }

          break;
        }
      }
    }
  }

  private void WriteIfNotDuplicate<T>(T udt, Dictionary<string, Dictionary<string, T>> dict,
    Action<T, Writer> writeAction) where T : CsUdt {
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
        Log.Warn($"Duplicate class name \"{udt.FullName}\" in namespace \"{udt.Namespace}\".");
        return;
      }
    }

    Writer writer = Writers.GetMatching(udt);
    writeAction(udt, writer);
    writer.Flush();
  }
}
