using System.Diagnostics;
using System.Runtime.CompilerServices;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.GSI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Types;

public abstract class CsType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers) {
  public readonly TypeIndex TypeIndex = index;
  public readonly ModifierOptions Modifiers = modifiers;

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal SourceGen SourceGen { get; } = sourceGen;

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal PdbFileReader Pdb => SourceGen.Pdb;

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal PdbFile PdbFile => SourceGen.Pdb.PdbFile;

  public abstract ulong Size { get; }

  public string SelfName => field ??= ValidateName(CreateSelfName(), true);
  public string FullName => field ??= ValidateName(CreateFullName(), false);

  protected abstract string CreateSelfName();
  protected virtual string CreateFullName() => SelfName;

  public sealed override int GetHashCode() => (int)TypeIndex.Index;

  private string ValidateName(string name, bool isSelfName) {
    if (this is CsFunctionType or CsPointerType or CsArray) {
      // Functions, pointers, and arrays should pass by default. Any inner arguments should throw.
      return name;
    }

    if (string.IsNullOrWhiteSpace(name)) {
      throw new ArgumentException("Name cannot be null or whitespace.");
    }

    // Check if all characters are A-z, 0-9, or _
    bool hasPtr = false;
    foreach ((int i, char c) in name.Index()) {
      if (c == '@' && (i == 0 || name[i - 1] == '.')) {
        // Allow @ at the start of the name, but not elsewhere
        continue;
      }

      if (!char.IsLetterOrDigit(c) && c is not '_' and not '.') {
        if (this is CsPointerType && c == '*') {
          // Allow * in pointer types
          hasPtr = true;
          continue;
        }

        // TODO: Remove this check once properly implement array names
        if (this is CsArray) {
          continue;
        }

        throw new ArgumentException($"Name {name} contains invalid character: {c}");
      }

      if (hasPtr) {
        throw new ArgumentException($"Name {name} cannot contain '*' before other characters");
      }
    }

    if (isSelfName) {
      if (name.Contains('.')) {
        throw new ArgumentException($"Self name {name} cannot contain '.'");
      }
    }
    else {
      // For fully qualified names, cannot start or end with a '.',
      if (name.StartsWith('.') || name.EndsWith('.')) {
        throw new ArgumentException($"Full name {name} cannot start or end with a '.'");
      }

      // Types must not start with a number, including after a '.'. Cannot contain consecutive '.' characters.
      var parts = name.AsSpan().Split('.');
      foreach (Range part in parts) {
        if (char.IsDigit(name[part.Start.Value])) {
          throw new ArgumentException($"Full name {name} cannot start with a number");
        }

        if (part.End.Value == part.Start.Value) {
          throw new ArgumentException($"Full name {name} cannot contain consecutive '.' characters");
        }
      }
    }


    return name;
  }

  public static CsType GetOrCreate(SourceGen sourceGen, TypeIndex index,
    ModifierOptions modifiers = ModifierOptions.None) {
    CsType result;
    if (index.IsSimple) {
      if (sourceGen.CsSimpleTypes.TryGetValue(index, out CsType? existingSimple)) {
        return existingSimple;
      }

      result = index.SimpleMode != SimpleTypeMode.Direct
        ? new CsSimplePointerType(sourceGen, index, modifiers)
        : new CsSimpleType(sourceGen, index, modifiers);
      sourceGen.CsSimpleTypes[index] = result;
      return result;
    }

    if (sourceGen.CsTypes[index.ArrayIndex] is { } existing) {
      return existing;
    }

    TypeRecord typeRecord = sourceGen.Records[index.ArrayIndex];
    result = typeRecord switch {
      ModifierRecord modifierRecord => GetOrCreate(sourceGen, modifierRecord.ModifiedType,
        modifiers | modifierRecord.Modifiers),
      PointerRecord pointerRecord => new CsPointerType(pointerRecord, sourceGen, index, modifiers),
      ArrayRecord arrayRecord => new CsArray(arrayRecord, index, sourceGen, modifiers),
      ClassRecord classRecord => new CsStruct(classRecord, index, sourceGen, modifiers),
      UnionRecord unionRecord => new CsUnion(unionRecord, index, sourceGen, modifiers),
      EnumRecord enumRecord => new CsEnum(enumRecord, index, sourceGen, modifiers),
      ProcedureRecord procRecord => new CsFunctionType(procRecord, index, sourceGen, modifiers),
      _ => throw new NotSupportedException($"Unsupported UDT type: {typeRecord.GetType().Name}")
    };
    sourceGen.CsTypes[index.ArrayIndex] = result;
    return result;
  }

  public override string ToString() => FullName;
}

public sealed class CsSimpleType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public override string ToString() => $"{FullName} ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => SourceGen.ToCsName(TypeIndex);

  public override ulong Size {
    get {
      return _size ??= GetSize(null!, TypeIndex);

      [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetSize")]
      static extern ulong GetSize(PdbSimpleType sim, TypeIndex index);
    }
  }

  private ulong? _size;
}

public sealed class CsSimplePointerType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public override string ToString() => $"{FullName}* ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => SourceGen.ToCsName(TypeIndex);

  public override ulong Size {
    get {
      return _size ??= GetSize(null!, TypeIndex);

      [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetPointerSize")]
      static extern ulong GetSize(PdbSimplePointerType sim, TypeIndex index);
    }
  }

  private ulong? _size;
}

public sealed class CsPointerType : CsType {
  public CsPointerType(PointerRecord pointer, SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers) : base(sourceGen, index, modifiers) {
    PointerRecord = pointer;
    if (!pointer.Mode.HasFlag(PointerMode.PointerToMemberFunction)) {
      ElementType = GetOrCreate(sourceGen, pointer.ReferentType, modifiers);
      return;
    }

    CsStructure container = (CsStructure)SourceGen.CsUdts[pointer.MemberInfo.ContainingType];
    ElementType = container.InstanceMethods.First(m => m.TypeIndex == pointer.ReferentType);
  }

  public readonly PointerRecord PointerRecord;
  public readonly CsType ElementType;

  public override string ToString() => $"pointer to {ElementType.FullName}";
  protected override string CreateSelfName() => $"{ElementType.SelfName}*";

  public override ulong Size => PointerRecord.Size != 0
    ? PointerRecord.Size
    : PointerRecord.PointerKind == PointerKind.Near64
      ? 8U
      : 4U;
}

public abstract class CsUdt(TypeIndex index, SourceGen sourceGen, TagRecord record, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public virtual TagRecord Record { get; } = record;

  public string? Namespace {
    get => field ??= Parent?.Namespace;
    private set;
  }

  public CsUdt? Parent { get; private set; }
  public NestedTypeRecord? NestedTypeRecord { get; private set; }

  public void SetParent(CsUdt udt, NestedTypeRecord record) {
    Parent = udt;
    NestedTypeRecord = record;
  }

  protected override string CreateFullName() => Parent is null ? SelfName : $"{Parent.FullName}.{SelfName}";

  protected override string CreateSelfName() {
    string recordName = Record.Name.String;
    string str = NestedTypeRecord?.Name.String ?? recordName;
    if (NestedTypeRecord is null) {
      if ((Record.Options & ClassOptions.Scoped) != 0) {
        return SanitizeName(str);
      }

      int idx = recordName.LastIndexOf("::", StringComparison.Ordinal);
      if (idx == -1) {
        return SanitizeName(str);
      }


      int preGenericScope = str.IndexOf('<');
      if (preGenericScope != -1) {
        idx = str.AsSpan()[..preGenericScope].LastIndexOf("::", StringComparison.Ordinal);
      }

      str = recordName[(idx + 2)..];
      Namespace = idx != -1
        ? recordName[..idx]
          .Replace("::", ".")
          .Replace("`anonymous-namespace'", "_")
          .Replace("`anonymous namespace'", "_")
        : null;

      return SanitizeName(str);
    }

    if (!string.IsNullOrWhiteSpace(NestedTypeRecord.Name.String)) {
      return SanitizeName(str);
    }

    string parentName = Parent!.Record.Name.String;
    if (!recordName.StartsWith(parentName)) {
      throw new InvalidOperationException(
        $"Nested type {recordName} does not start with parent type {parentName}");
    }

    if (recordName.Length <= parentName.Length + 2) {
      throw new InvalidOperationException(
        $"Nested type {recordName} is too short to contain parent type {parentName}");
    }

    var subStrSpan = recordName.AsSpan()[(parentName.Length + 2)..];
    string name = subStrSpan.ToString();
    return SanitizeName(name);

    string SanitizeName(string value) {
      return TryFromUnnamedTag(value, out string result) ? result : value.SanitizeName();
    }
  }

  protected bool TryFromUnnamedTag(string name, out string result) {
    if (name is "<unnamed-tag>" or "<unnamed-type>") {
      ref int i = ref this is CsStruct
        ? ref SourceGen.UnnamedStructs
        : ref this is CsUnion
          ? ref SourceGen.UnnamedUnions
          : ref SourceGen.UnnamedEnums;

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

public abstract class CsStructure(TagRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsUdt(index, sourceGen, record, modifiers) {
  public VirtualFunctionPointerRecord? VfPtr =>
    AllFields.Count == 0 ? null : AllFields[0] as VirtualFunctionPointerRecord;

  public VirtualFunctionTableShapeRecord? VfTable => field ??= GetVfTable();
  public IReadOnlyList<TypeRecord> AllFields => field ??= GetAllFields();
  public CsBaseClass[] BaseClasses => field ??= GetBaseClasses();
  public CsStructure[] NestedClasses => field ??= GetNestedClasses();

  public CsInstanceField[] InstanceFields => field ??= GetInstanceFields();
  public CsInstanceMethod[] InstanceMethods => field ??= GetInstanceMethods();
  public CsStaticField[] StaticFields => field ??= GetStaticFields();

  private CsBaseClass[] GetBaseClasses() {
    int count = AllFields.OfType<BaseClassRecord>().Count();
    var result = new CsBaseClass[count];
    foreach ((int i, BaseClassRecord baseClass) in AllFields.OfType<BaseClassRecord>().Index()) {
      result[i] = new CsBaseClass(this, baseClass);
    }

    return result;
  }

  private CsStructure[] GetNestedClasses() {
    return AllFields.OfType<NestedTypeRecord>()
      .Where(n => !n.Type.IsSimple)
      .Select(n => SourceGen.CsTypes[n.Type.ArrayIndex])
      .OfType<CsStructure>()
      .Where(c => c.Parent == this)
      .ToArray();
  }

  private IReadOnlyList<TypeRecord> GetAllFields() {
    return PdbFile.TryGetRecord<FieldListRecord>(Record.FieldList)?.Fields ?? [];
  }

  private CsInstanceField[] GetInstanceFields() {
    return AllFields
      .OfType<DataMemberRecord>()
      .Select(f => PdbFile.TryGetRecord<BitFieldRecord>(f.Type) is { } bitField
        ? new CsBitField(this, bitField, f)
        : new CsInstanceField(this, f))
      .ToArray();
  }

  private CsInstanceMethod[] GetInstanceMethods() {
    int singleAndOverloadCount = AllFields.OfType<OneMethodRecord>().Count();
    singleAndOverloadCount += AllFields.OfType<OverloadedMethodRecord>().Sum(o => o.OverloadsCount);
    if (singleAndOverloadCount == 0) {
      return [];
    }

    var result = new CsInstanceMethod[singleAndOverloadCount];

    int i = 0;
    foreach (TypeRecord typeRecord in AllFields) {
      switch (typeRecord) {
        case OneMethodRecord oneMethod:
          result[i++] = new CsInstanceMethod(this, oneMethod);
          break;
        case OverloadedMethodRecord overloadedMethod:
          foreach (OneMethodRecord overload in overloadedMethod.MethodList
                     .As<MethodOverloadListRecord>(PdbFile).Methods) {
            result[i++] = new CsInstanceMethod(this, overload, overloadedMethod.Name.String);
          }

          break;
      }
    }

    return result;
  }

  private CsStaticField[] GetStaticFields() {
    var numStaticFields = AllFields.OfType<StaticDataMemberRecord>().Count();
    if (numStaticFields == 0) {
      return [];
    }

    var fields = new CsStaticField[numStaticFields];
    foreach ((int i, StaticDataMemberRecord sMember) in AllFields.OfType<StaticDataMemberRecord>().Index()) {
      // Check if static field is constant
      string fullName = Record.Name.String + "::" + sMember.Name;
      GlobalsStream globals = PdbFile.GlobalsStream;
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
        fields[i] = new CsConstantField(this, sMember, constant);
      else if (threadLocalData != null)
        fields[i] = new CsThreadLocalStorageField(this, sMember, threadLocalData);
      else if (data != null)
        fields[i] = new CsRegularStaticField(this, sMember, data);
      else
        fields[i] = new CsStaticField(this, sMember);
    }

    return fields;
  }

  private VirtualFunctionTableShapeRecord? GetVfTable() {
    PointerRecord? pointer = VfPtr is null ? null : PdbFile.GetRecord<PointerRecord>(VfPtr.Type);
    if (pointer is null) {
      return null;
    }

    return PdbFile.GetRecord<VirtualFunctionTableShapeRecord>(pointer.ReferentType);
  }
}

public sealed class CsStruct(ClassRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsStructure(record, index, sourceGen, modifiers) {
  public override ClassRecord Record => (ClassRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() => NestedTypeRecord is null
    ? $"struct {FullName}"
    : $"struct {FullName} ({SelfName})";
}

public sealed class CsUnion(UnionRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsStructure(record, index, sourceGen, modifiers) {
  public override UnionRecord Record => (UnionRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() =>
    NestedTypeRecord is null ? $"union {FullName}" : $"union {FullName} ({SelfName})";
}

public sealed class CsEnum(EnumRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsUdt(index, sourceGen, record, modifiers) {
  public override EnumRecord Record => (EnumRecord)base.Record;
  public CsType Underlying => field ??= GetOrCreate(SourceGen, Record.UnderlyingType);
  public override string ToString() => NestedTypeRecord is null ? $"enum {FullName}" : $"enum {FullName} ({SelfName})";

  public override ulong Size => Underlying.Size;

  [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
  public CsEnumField[] Values => Record.MemberCount > 0
    ? PdbFile
      .GetRecord<FieldListRecord>(Record.FieldList).Fields
      .OfType<EnumeratorRecord>()
      .Select(e => new CsEnumField(e)).ToArray()
    : [];
}

public sealed class CsEnumField(EnumeratorRecord record) {
  public readonly EnumeratorRecord Record = record;

  public string Name => Record.Name.String;
  public object Value => Record.Value;

  public override string ToString() => $"{Name} = {Value}";
}

public sealed class CsArray(ArrayRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public readonly ArrayRecord Record = record;
  public CsType ElementType => field ??= GetOrCreate(SourceGen, Record.ElementType, Modifiers);
  public ulong Count => ElementType.Size != 0 ? Record.Size / ElementType.Size : 0;

  public override ulong Size => Record.Size;

  public override string ToString() => $"array of {ElementType} [{Count}]";

  protected override string CreateSelfName() {
    return $"{ElementType.SelfName}[{Count}]";
  }
}

public class CsInstanceField(CsStructure container, DataMemberRecord record) {
  public readonly CsStructure Container = container;
  public readonly DataMemberRecord Record = record;

  public string Name => Record.Name.String;
  public virtual TypeIndex Type => Record.Type;
  public TypeRecord TypeRecord => field ??= Container.PdbFile.GetRecord(Type);
  public uint Offset => (uint)Record.FieldOffset;

  public CsType FieldType => CsType.GetOrCreate(Container.SourceGen, Type);

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}{FieldType.FullName} {Name} (offset: 0x{Offset:X})";
  }
}

public sealed class CsBitField(CsStructure container, BitFieldRecord bitRecord, DataMemberRecord record)
  : CsInstanceField(container, record) {
  public readonly BitFieldRecord BitFieldRecord = bitRecord;
  public override TypeIndex Type => BitFieldRecord.Type;

  public uint BitSize => BitFieldRecord.BitSize;
  public uint BitOffset => BitFieldRecord.BitOffset;

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}{FieldType.FullName} {Name} : {BitSize} (offset: 0x{Offset:X}, bit offset: {BitOffset})";
  }
}

public class CsStaticField(CsStructure container, StaticDataMemberRecord record) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public readonly CsStructure Container = container;

  public readonly StaticDataMemberRecord Record = record;

  public string Name => Record.Name.String;
  public TypeIndex Type => Record.Type;

  public CsType FieldType {
    get {
      try {
        return CsType.GetOrCreate(Container.SourceGen, Type);
      }
      catch {
        return null!;
      }
    }
  }

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}static {FieldType.FullName} {Name}";
  }
}

public sealed class CsConstantField(CsStructure container, StaticDataMemberRecord record, ConstantSymbol constant)
  : CsStaticField(container, record) {
  public readonly ConstantSymbol Constant = constant;

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}static const {FieldType.FullName} {Name} = {Constant.Value}";
  }
}

public sealed class CsThreadLocalStorageField(CsStructure container, StaticDataMemberRecord record, ThreadLocalDataSymbol threadLocalData)
  : CsStaticField(container, record) {
  public readonly ThreadLocalDataSymbol ThreadLocalData = threadLocalData;

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}static thread_local {FieldType.FullName} {Name}";
  }
}

public sealed class CsRegularStaticField : CsStaticField {
  public CsRegularStaticField(CsStructure container, StaticDataMemberRecord record, DataSymbol data) : base(container, record) {
    Data = data;
    RelativeVirtualAddress = container.PdbFile.FindRelativeVirtualAddress(data.Segment, data.Offset);
  }

  public readonly DataSymbol Data;
  public readonly ulong RelativeVirtualAddress;


  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}static {FieldType.FullName} {Name} (RVA: 0x{RelativeVirtualAddress:X})";
  }
}

public sealed class CsInstanceMethod : CsType {
#if DEBUG
  public static readonly List<string> HasFuncNames = [];
  public static readonly List<string> MissingFuncName = [];
#endif
  public CsInstanceMethod(CsStructure container, OneMethodRecord record, string? overloadedName = null) : base(container.SourceGen, record.Type, ModifierOptions.None) {
    Container = container;
    Record = record;
    Name = record.Name.String ?? overloadedName!;
    MethodRecord = Container.PdbFile.GetRecord<MemberFunctionRecord>(record.Type);
    CallingConvention = MethodRecord.CallingConvention;
    IsStatic = MethodRecord.ThisType is
      { IsSimple: true, SimpleMode: SimpleTypeMode.Direct, SimpleKind: SimpleTypeKind.Void };
    // TODO:
    string lookupName = container.Record.Name.String + "::" + Name;
    ProcedureInfo = ProcedureHelper.MemberNames.TryGetValue((record.Type, lookupName), out ProcedureInfo pInfo)
      ? pInfo
      : null;

    if (ProcedureInfo.HasValue) {
      HasFuncNames.Add(lookupName);
      Args = pInfo.GoodSize ? pInfo.Args.Select(a => a.Name).ToArray() : [];
    }
    else {
      Args = [];
      MethodKind methodKind = Record.Attributes.MethodKind;
      if (!methodKind.HasFlag(MethodKind.PureVirtual) &&
          !methodKind.HasFlag(MethodKind.PureIntroducingVirtual)) {
        MissingFuncName.Add(lookupName);
      }
    }

    RelativeVirtualAddress = ProcedureInfo?.Rva;
  }

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public readonly CsStructure Container;

  public readonly string Name;
  public readonly OneMethodRecord Record;
  public readonly MemberFunctionRecord MethodRecord;
  public readonly CallingConvention CallingConvention;
  public readonly bool IsStatic;
  public readonly ProcedureInfo? ProcedureInfo;
  public readonly ulong? RelativeVirtualAddress;
  public readonly string[] Args;

  public override ulong Size => 0;

  public CsType ReturnType => field ??= GetOrCreate(Container.SourceGen, MethodRecord.ReturnType);

  public CsType[] ParameterTypes => field ??= MethodRecord.ArgumentList.As<ArgumentListRecord>(Container.PdbFile)
    .Arguments.Select(p => GetOrCreate(Container.SourceGen, p)).ToArray();

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  private string? _toStringValue;

  protected override string CreateSelfName() => "TODO_INSTANCE_METHOD_NAME";

  public override string ToString() => _toStringValue ??= GenerateToString();

  private string GenerateToString() {
    string @sealed = (Record.Attributes.Flags & MethodOptions.Sealed) != 0 ? "sealed " : string.Empty;

    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    string virt = Record.Attributes.MethodKind switch {
      MethodKind.Vanilla => string.Empty,
      MethodKind.Friend => string.Empty,
      MethodKind.Static => "static ",
      MethodKind.IntroducingVirtual => "virtual ",
      MethodKind.Virtual => "override ",
      MethodKind.PureIntroducingVirtual => "abstract ",
      MethodKind.PureVirtual => "abstract override ",
      _ => string.Empty
    };

    string ret = ReturnType.FullName;
    string args = Args.Length != ParameterTypes.Length
      ? string.Join(", ", ParameterTypes.Select(p => p.FullName))
      : string.Join(", ", ParameterTypes.Zip(Args, (type, name) => $"{type.FullName} {name}"));

    string vfOffset = Record.VFTableOffset != -1 ? $" (vfOffset: {Record.VFTableOffset})" : string.Empty;

    string rva = RelativeVirtualAddress.HasValue ? $" (RVA: 0x{RelativeVirtualAddress.Value:X})" : string.Empty;

    return $"{access}{@sealed}{virt}{ret} {Name}({args}){vfOffset}{rva}";
  }
}

public sealed class CsFunctionType(
  ProcedureRecord record,
  TypeIndex index,
  SourceGen sourceGen,
  ModifierOptions modifiers) : CsType(sourceGen, index, modifiers) {
  public readonly ProcedureRecord Record = record;
  public CsType[] Arguments => field ??= GetArguments();

  public CsType ReturnType => field ??= GetOrCreate(SourceGen, Record.ReturnType);
  public override ulong Size => 0;

  protected override string CreateSelfName() {
    // Should return something like "delegate* unmanaged[Stdcall]<int, int, int>"
    const string del = "delegate* unmanaged";
    string conv = Record.CallingConvention switch {
      CallingConvention.ThisCall => "[Thiscall]",
      CallingConvention.NearFast or CallingConvention.FarFast => "[Fastcall]",
      CallingConvention.NearStdCall or CallingConvention.FarStdCall => "[Stdcall]",
      CallingConvention.NearC or CallingConvention.FarC => "[Cdecl]",
      _ => throw new NotSupportedException("Unsupported calling convention: " + Record.CallingConvention)
    };

    string returnType = ReturnType.FullName;
    if (Arguments.Length == 0) {
      return $"{del}{conv}<{returnType}>";
    }

    string args = string.Join(", ", Arguments.Select(a => a.FullName));
    return $"{del}{conv}<{args}, {returnType}>";
  }

  private CsType[] GetArguments() {
    return Record.ArgumentList.As<ArgumentListRecord>(PdbFile).Arguments.Select(a => GetOrCreate(SourceGen, a))
      .ToArray();
  }

  public override string ToString() => SelfName;
}

public sealed class CsBaseClass(CsStructure container, BaseClassRecord record) {
  public readonly CsStructure Container = container;
  public readonly BaseClassRecord Record = record;

  public CsStructure BaseClass => field ??= Container.SourceGen.CsTypes[Record.Type.ArrayIndex] as CsStructure ??
    throw new InvalidOperationException(
      $"Base class type {Record.Type} is not a structure");

  public override string ToString() => $"base class {BaseClass.FullName} (offset: 0x{Record.Offset:X})";
}
