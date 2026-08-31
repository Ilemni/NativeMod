using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SharpPdb.Windows;
using SharpPdb.Windows.GSI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public abstract class CsUdt(CsGen gen, TypeIndex index, TagRecord record)
  : CsType(gen, index) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public virtual TagRecord Record { get; } = record;

  public override string? Namespace {
    get => Parent is null ? field : Parent.Namespace;
    set;
  }

  public override string CppName { get; } = record.Name.String;

  public override string FullyQualifiedName => _fullyQualifiedName ??= FullyQualify();

  public override string GlobalQualifiedName =>
    _globalQualifiedName ??= "global::" + Gen.Namespace + '.' + FullyQualifiedName;

  private string? _fullyQualifiedName;
  private string? _globalQualifiedName;


  public CsStructure? Parent { get; private set; }
  public NestedTypeRecord? ThisAsNested { get; private set; }
  public bool IsForwardReference => Record.IsForwardReference;

  public void SetParent(CsStructure parent, NestedTypeRecord? record) {
    Parent = parent;
    ThisAsNested = record;

    // Invalidate names
    SelfName = null;
    FullName = null;
    _fullyQualifiedName = null;
    _globalQualifiedName = null;
  }

  protected override string CreateFullName() =>
    Parent is null || ReferenceEquals(this, Parent) ? SelfName : $"{Parent.FullName}.{SelfName}";

  protected override string CreateSelfName() {
    string name = Record.Name.String;

    HandleCompilerGeneratedNames(ref name, TypeIndex);

    string str = ThisAsNested?.Name.String ?? name;
    if (ThisAsNested is null) {
      if ((Record.Options & ClassOptions.Scoped) != 0) {
        return SanitizeName(str);
      }

      int idx = name.LastIndexOf("::", StringComparison.Ordinal);
      if (idx == -1) {
        return SanitizeName(str);
      }


      int preGenericScope = str.IndexOf('<');
      if (preGenericScope != -1) {
        idx = str.AsSpan()[..preGenericScope].LastIndexOf("::", StringComparison.Ordinal);
      }

      if (idx != -1) {
        str = name[(idx + 2)..];
      }

      Namespace = idx != -1
        ? name[..idx]
          .Replace("::", ".")
          .Replace("`anonymous-namespace'", "_")
          .Replace("`anonymous namespace'", "_")
          .KeywordToVerbatim(true)
        : null;

      return SanitizeName(str);
    }

    if (!string.IsNullOrWhiteSpace(ThisAsNested.Name.String)) {
      return SanitizeName(str);
    }

    string parentName = Parent!.Record.Name.String;
    if (!name.StartsWith(parentName)) {
      throw new InvalidOperationException($"Nested type {name} does not start with parent type {parentName}");
    }

    if (name.Length <= parentName.Length + 2) {
      throw new InvalidOperationException($"Nested type {name} is too short to contain parent type {parentName}");
    }

    var subStrSpan = name.AsSpan()[(parentName.Length + 2)..];
    string result = subStrSpan.ToString();
    return SanitizeName(result);

    string SanitizeName(string value) {
      return TryFromUnnamedTag(value, out string sanitized) ? sanitized : value.SanitizeName();
    }

    static void HandleCompilerGeneratedNames(ref string recordName, TypeIndex typeIndex) {
      string? typeNum = null;
      const string lambdaAt = "`lambda at";
      const char lambdaAtEnd = '\'';
      int badIndex = recordName.IndexOf(lambdaAt, StringComparison.Ordinal);
      while (badIndex != -1) {
        int endIndex = recordName.IndexOf(lambdaAtEnd, badIndex);
        if (endIndex != -1) {
          recordName =
            recordName[..badIndex] +
            "lambda_" + (typeNum ??= typeIndex.ArrayIndex.ToString()) +
            recordName[(endIndex + 1)..];
        }

        badIndex = recordName.IndexOf(lambdaAt, StringComparison.Ordinal);
      }

      const string unnamedStructAt = "`unnamed struct at";
      const char unnamedStructEnd = '\'';
      badIndex = recordName.IndexOf(unnamedStructAt, StringComparison.Ordinal);
      while (badIndex != -1) {
        int endIndex = recordName.IndexOf(unnamedStructEnd, badIndex);
        if (endIndex != -1) {
          recordName =
            recordName[..badIndex] +
            "unnamed_struct_" + (typeNum ??= typeIndex.ArrayIndex.ToString()) +
            recordName[(endIndex + 1)..];
        }

        badIndex = recordName.IndexOf(unnamedStructAt, StringComparison.Ordinal);
      }
    }
  }

  private bool TryFromUnnamedTag(string name, out string result) {
    if (name is "<unnamed-tag>" or "<unnamed-type>") {
      ref int i = ref this is CsStruct
        ? ref Gen.UnnamedStructs
        : ref this is CsUnion
          ? ref Gen.UnnamedUnions
          : ref Gen.UnnamedEnums;

      string type = this switch {
        CsStruct => "unnamed_struct_",
        CsUnion => "unnamed_union_",
        CsEnum => "unnamed_enum_",
        _ => throw new NotSupportedException($"Unsupported UDT type: {GetType().Name}")
      };

      result = type + ++i;
      return true;
    }

    if (name.StartsWith("<unnamed-type-")) {
      //type is named after field, i.e. "<unnamed-type-myFieldName>", extract myFieldName
      int start = "<unnamed-type-".Length;
      int end = name.IndexOf('>', start);
      if (end > start) {
        var fieldName = name.AsSpan()[start..end];
        // Uppercase the first letter of the field name, and sanitize it to be a valid C# identifier
        Span<char> nameSpan = stackalloc char[fieldName.Length];
        fieldName.CopyTo(nameSpan);
        if (nameSpan.Length > 0) {
          nameSpan[0] = char.ToUpper(nameSpan[0]);
        }

        result = nameSpan.ToString().SanitizeName();
        return true;
      }
    }

    result = string.Empty;
    return false;
  }
}

public abstract class CsStructure : CsUdt {
  protected CsStructure(CsGen gen, TypeIndex index, TagRecord record) : base(gen, index, record) {
    AllFields = record.FieldList.TryAs(PdbFile, out FieldListRecord? r) ? r.Fields : [];
    VfPtr = AllFields.Count == 0 ? null : AllFields.OfType<VirtualFunctionPointerRecord>().FirstOrDefault();

    if (record is ClassRecord classRecord &&
        classRecord.VirtualTableShape.TryAs(PdbFile, out VirtualFunctionTableShapeRecord? vft)) {
      VfTable = vft;
    }
  }

  public readonly IReadOnlyList<TypeRecord> AllFields;
  public readonly VirtualFunctionPointerRecord? VfPtr;
  public readonly VirtualFunctionTableShapeRecord? VfTable;

  public ulong VfAddress => _vfAddress ??= Gen.VTableAddresses.TryGetValue(Record.Name.String, out ulong a) ? a : 0;

  private ulong? _vfAddress;

  // Always lazy load fields/props with CsType members
  public readonly HashSet<CsStructure> DerivedTypes = [];
  public CsBaseClass[] BaseClasses => field ??= GetBaseClasses();
  public HashSet<CsUdt> NestedClasses => field ??= GetNestedTypes();

  public CsInstanceField[] InstanceFields => field ??= GetInstanceFields();
  public CsMethod[] AllMethods => field ??= GetMethods();

  public CsMethod[] NonVirtualMethods => field ??= AllMethods
    .Where(m => !m.IsVirtual)
    .OrderByDescending(m => m.IsDefined)
    .Distinct()
    .ToArray();

  public CsMethod[] DefinedMethods => field ??= AllMethods
    .Where(m => m.IsDefined)
    .ToArray();

  public CsMethod[] VirtualMethods => field ??= GetVirtualMethods();
  public CsStaticField[] StaticFields => field ??= GetStaticFields();

  public bool IsUnsafe => InstanceFields.Any(f => f.FieldType is CsPointerType or CsSimplePointerType);

  private CsBaseClass[] GetBaseClasses() {
    int count = AllFields.OfType<BaseClassRecord>().Count();
    var result = new CsBaseClass[count];
    foreach ((int i, BaseClassRecord baseClass) in AllFields.OfType<BaseClassRecord>().Index()) {
      result[i] = new CsBaseClass(Gen, baseClass);
      result[i].BaseType.DerivedTypes.Add(this);
    }

    return result;
  }

  private HashSet<CsUdt> GetNestedTypes() {
    return [
      .. AllFields.OfType<NestedTypeRecord>()
        .Where(n => !n.Type.IsSimple)
        .Select(n => Gen.Types[n.Type.ArrayIndex])
        .OfType<CsUdt>()
        .Where(c => c.Parent?.Record.UniqueName.String == Record.UniqueName.String)
    ];
  }

  private CsInstanceField[] GetInstanceFields() {
    return AllFields
      .OfType<DataMemberRecord>()
      .Select(f => f.Type.TryAs(PdbFile, out BitFieldRecord? bitField)
        ? new CsBitField(this, bitField, f)
        : new CsInstanceField(this, f))
      .ToArray();
  }

  private CsMethod[] GetMethods() {
    int singleAndOverloadCount =
      AllFields.OfType<OneMethodRecord>().Count() +
      AllFields.OfType<OverloadedMethodRecord>().Sum(o => o.OverloadsCount);
    if (singleAndOverloadCount == 0) {
      return [];
    }

    var result = new CsMethod[singleAndOverloadCount];

    int i = -1;
    foreach (TypeRecord typeRecord in AllFields) {
      switch (typeRecord) {
        case OneMethodRecord oneMethod:
          result[++i] = new CsMethod(this, oneMethod);
          break;
        case OverloadedMethodRecord overloadedMethod:
          int overloadId = 0;
          var methods = overloadedMethod.MethodList.As<MethodOverloadListRecord>(PdbFile).Methods;
          foreach (OneMethodRecord overload in methods) {
            result[++i] = new CsMethod(this, overload, overloadedMethod.Name.String, ++overloadId);
          }

          break;
      }
    }

    return result;
  }

  private CsMethod[] GetVirtualMethods() {
    if (VfTable is null) {
      return [];
    }

    var result = new CsMethod?[VfTable.Slots.Length];
    foreach (CsMethod method in AllMethods) {
      int slot = method.VfSlot;
      if (slot >= 0) {
        result[slot] = method;
      }
    }

    if (!result.Any(m => m is null)) {
      return result!;
    }

    if (BaseClasses.FirstOrDefault()?.BaseType.VirtualMethods is not { } baseMethods) {
      throw new InvalidDataException(
        $"Virtual function table for {Record.Name.String} has null entries and no base class to inherit from.");
    }

    if (baseMethods.Length is 0 && result.All(m => m is null)) {
      return [];
    }

    bool anyNull = false;
    bool anyNotNull = false;
    foreach ((int i, CsMethod method) in baseMethods.Index()) {
      result[i] ??= method;
      anyNull |= result[i] is null;
      anyNotNull |= result[i] is not null;
    }

    return (anyNull, anyNotNull) switch {
      (false, true) => (CsMethod[])result!,
      (true, false) => [],
      _ => Throw(this, result)
    };

    [DoesNotReturn]
    static CsMethod[] Throw(CsStructure csStruct, CsMethod?[] methods) {
      var missingIndices = methods
        .Select((m, idx) => m is null ? idx : -1)
        .Where(idx => idx != -1);
      string missing = string.Join(", ", missingIndices);
      string name = csStruct.Record.Name.String;
      throw new InvalidDataException($"Could not fully populate virtual function table for {name}. " +
        $"Missing methods at offsets: {missing}");
    }
  }

  private CsStaticField[] GetStaticFields() {
    int numStaticFields = AllFields.OfType<StaticDataMemberRecord>().Count();
    if (numStaticFields == 0) {
      return [];
    }

    var fields = new CsStaticField[numStaticFields];
    foreach ((int i, StaticDataMemberRecord sMember) in AllFields.OfType<StaticDataMemberRecord>().Index()) {
      FindStaticField(this, sMember, out fields[i]);
    }

    return fields;

    static void FindStaticField(CsStructure csStruct, StaticDataMemberRecord sMember, out CsStaticField field) {
      // Check if static field is constant
      string fullName = csStruct.Record.Name.String + "::" + sMember.Name;
      GlobalsStream globals = csStruct.PdbFile.GlobalsStream;
      ConstantSymbol? constant = null;
      ThreadLocalDataSymbol? threadLocalData = null;
      DataSymbol? data = null;

      if (globals.HashBuckets != null) {
        uint hash = HashTable.HashStringV1(fullName);
        GlobalsStreamHashBucket bucket = globals.HashBuckets[hash % (uint)globals.HashBuckets.Length];

        for (int j = bucket.Start; j < bucket.End; j++) {
          SymbolRecord record = globals.Symbols[j];

          if (record is ConstantSymbol c && c.Name.String == fullName) {
            constant = c;
            break;
          }

          if (record is ThreadLocalDataSymbol tls && tls.Name.String == fullName) {
            threadLocalData = tls;
            break;
          }

          if (record is DataSymbol ds && ds.Name.String == fullName) {
            data = ds;
            break;
          }
        }
      }
      else {
        data = globals.Data.FirstOrDefault(d => d.Name.String == fullName);
        if (data == null) {
          constant = globals.Constants.FirstOrDefault(c => c.Name.String == fullName);
          if (constant == null)
            threadLocalData =
              globals.ThreadLocalData.FirstOrDefault(t => t.Name.String == fullName);
        }
      }

      // Create correct static field type
      if (constant != null)
        field = new CsConstantField(csStruct, sMember, constant);
      else if (threadLocalData != null)
        field = new CsThreadLocalStorageField(csStruct, sMember, threadLocalData);
      else if (data != null)
        field = new CsRegularStaticField(csStruct, sMember, data);
      else
        field = new CsStaticField(csStruct, sMember);
    }
  }
}

public sealed class CsStruct(CsGen gen, TypeIndex index, ClassRecord record) : CsStructure(gen, index, record) {
  public override ClassRecord Record => (ClassRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() => Record.Name.String;

  protected override bool EqualsCore(CsType? other) {
    return other is CsStruct otherStruct && TypeIndex == otherStruct.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsUnion(CsGen gen, TypeIndex index, UnionRecord record)
  : CsStructure(gen, index, record) {
  public override UnionRecord Record => (UnionRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() => Record.Name.String;

  protected override bool EqualsCore(CsType? other) {
    return other is CsUnion otherUnion && TypeIndex == otherUnion.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}
