using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.GSI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Types;

public abstract class CsType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers) : IEquatable<CsType> {
  public readonly TypeIndex TypeIndex = index;
  public readonly ModifierOptions Modifiers = modifiers;

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal SourceGen SourceGen { get; } = sourceGen;

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal PdbFile PdbFile => SourceGen.Pdb.PdbFile;

  public abstract ulong Size { get; }

  public string SelfName => field ??= ValidateName(CreateSelfName(), true);

  [AllowNull]
  public string FullName {
    get => field ??= ValidateName(CreateFullName(), false);
    protected set;
  }

  public virtual string FullyQualifiedName => SelfName;

  public virtual string? Namespace { get; set; }

  protected abstract string CreateSelfName();
  protected virtual string CreateFullName() => SelfName;

  private string ValidateName(string name, bool isSelfName) {
    if (this is CsFunctionType or CsPointerType or CsSimplePointerType or CsArray) {
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

  protected string QualifyWithGlobal() {
    string fullName = FullName;
    string result = "global::" +
      SourceGen.Namespace + '.' +
      (Namespace is { } ns ? ns + '.' : "") +
      fullName;
    return result;
  }

  public override string ToString() => FullName;

  public sealed override bool Equals(object? obj) => Equals(obj as CsType);

  public abstract bool Equals(CsType? other);

  public abstract override int GetHashCode();
}

public sealed class CsSimpleType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public override string ToString() => $"{FullName} ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => ToCsName(TypeIndex);

  // TODO: add global:: if there is a namespace (System.Half, etc)
  public override string FullyQualifiedName => SelfName;

  public override ulong Size {
    get {
      return _size ??= GetSize(null!, TypeIndex);

      [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetSize")]
      static extern ulong GetSize(PdbSimpleType sim, TypeIndex index);
    }
  }

  private ulong? _size;


  public static string ToCsName(TypeIndex index) => index.SimpleKind switch {
    SimpleTypeKind.None => "__arglist",
    SimpleTypeKind.Void => "void",
    SimpleTypeKind.NotTranslated => throw new NotSupportedException("NotTranslated type kind is not supported"),
    SimpleTypeKind.HResult => nameof(CppHResult),
    SimpleTypeKind.SignedCharacter => nameof(CppSignedChar),
    SimpleTypeKind.UnsignedCharacter => nameof(CppUnsignedChar),
    SimpleTypeKind.NarrowCharacter => nameof(CppChar),
    SimpleTypeKind.WideCharacter => nameof(CppWideChar),
    SimpleTypeKind.Character16 => nameof(CppChar16),
    SimpleTypeKind.Character32 => nameof(CppChar32),
    SimpleTypeKind.SByte => nameof(CppInt8),
    SimpleTypeKind.Byte => nameof(CppUInt8),
    SimpleTypeKind.Int16Short => nameof(CppInt16Short),
    SimpleTypeKind.UInt16Short => nameof(CppUInt16Short),
    SimpleTypeKind.Int16 => nameof(CppInt16),
    SimpleTypeKind.UInt16 => nameof(CppUInt16),
    SimpleTypeKind.Int32Long => nameof(CppInt32Long),
    SimpleTypeKind.UInt32Long => nameof(CppUInt32Long),
    SimpleTypeKind.Int32 => nameof(CppInt32),
    SimpleTypeKind.UInt32 => nameof(CppUInt32),
    SimpleTypeKind.Int64Quad => nameof(CppInt64Quad),
    SimpleTypeKind.UInt64Quad => nameof(CppUInt64Quad),
    SimpleTypeKind.Int64 => nameof(CppInt64),
    SimpleTypeKind.UInt64 => nameof(CppUInt64),
    SimpleTypeKind.Int128Oct => nameof(CppInt128Oct),
    SimpleTypeKind.UInt128Oct => nameof(CppUInt128Oct),
    SimpleTypeKind.UInt128 => nameof(CppUInt128),
    SimpleTypeKind.Int128 => nameof(CppInt128),
    SimpleTypeKind.Float16 => "global::System.Single",
    SimpleTypeKind.Float32 => "float",
    SimpleTypeKind.Float32PartialPrecision => nameof(CppFloat32PartialPrecision),
    SimpleTypeKind.Float48 => throw new NotSupportedException("Float48 type kind is not supported"),
    SimpleTypeKind.Float64 => "double",
    SimpleTypeKind.Float80 => throw new NotSupportedException("Float80 type kind is not supported"),
    SimpleTypeKind.Float128 => throw new NotSupportedException("Float128 type kind is not supported"),
    SimpleTypeKind.Complex32 => throw new NotSupportedException("Complex32 type kind is not supported"),
    SimpleTypeKind.Complex64 => throw new NotSupportedException("Complex64 type kind is not supported"),
    SimpleTypeKind.Complex80 => throw new NotSupportedException("Complex80 type kind is not supported"),
    SimpleTypeKind.Complex128 => "global::System.Numerics.Complex",
    SimpleTypeKind.Boolean8 => "bool",
    SimpleTypeKind.Boolean16 => nameof(CppBoolean16),
    SimpleTypeKind.Boolean32 => nameof(CppBoolean32),
    SimpleTypeKind.Boolean64 => nameof(CppBoolean64),
    SimpleTypeKind.Complex16 => throw new NotSupportedException("Complex16 type kind is not supported"),
    SimpleTypeKind.Complex32PartialPrecision => throw new NotSupportedException(
      "Complex32PartialPrecision type kind is not supported"),
    SimpleTypeKind.Complex48 => throw new NotSupportedException("Complex48 type kind is not supported"),
    SimpleTypeKind.Boolean128 => nameof(CppBoolean128),
    _ => throw new ArgumentOutOfRangeException(nameof(index))
  };

  public override bool Equals(CsType? other) {
    return other is CsSimpleType otherSimple && TypeIndex == otherSimple.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsSimplePointerType(SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public override string ToString() => $"{FullName}* ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => CsSimpleType.ToCsName(TypeIndex) + '*';

  public override ulong Size {
    get {
      return _size ??= GetSize(null!, TypeIndex);

      [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetPointerSize")]
      static extern ulong GetSize(PdbSimplePointerType sim, TypeIndex index);
    }
  }

  private ulong? _size;

  public override bool Equals(CsType? other) {
    return other is CsSimplePointerType otherPointer && TypeIndex == otherPointer.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsPointerType : CsType {
  public CsPointerType(PointerRecord pointer, SourceGen sourceGen, TypeIndex index, ModifierOptions modifiers) : base(
    sourceGen, index, modifiers) {
    PointerRecord = pointer;
    if (!pointer.Mode.HasFlag(PointerMode.PointerToMemberFunction)) {
      ElementType = GetOrCreate(sourceGen, pointer.ReferentType, modifiers);
      return;
    }

    CsStructure container = (CsStructure)SourceGen.CsUdts[pointer.MemberInfo.ContainingType];
    ElementType = container.InstanceMethods.First(m => m.TypeIndex == PointerRecord.ReferentType);
  }

  public readonly PointerRecord PointerRecord;
  public readonly CsType ElementType;

  public CsType InnerElement {
    get {
      CsType current = ElementType;
      while (current is CsPointerType pointer) {
        current = pointer.ElementType;
      }

      return current;
    }
  }

  public int Depth {
    get {
      int depth = 1;
      CsType current = ElementType;
      while (current is CsPointerType pointer) {
        depth++;
        current = pointer.ElementType;
      }

      return depth;
    }
  }

  public override string? Namespace => ElementType.Namespace;
  public override string FullyQualifiedName => ElementType.FullyQualifiedName + "*";

  public override string ToString() => $"Pointer to {ElementType.FullName} ({ElementType.TypeIndex}) Depth: {Depth}";
  protected override string CreateSelfName() => $"{ElementType.SelfName}";

  public override ulong Size => PointerRecord.Size != 0
    ? PointerRecord.Size
    : PointerRecord.PointerKind == PointerKind.Near64
      ? 8U
      : 4U;


  public override bool Equals(CsType? other) {
    return other is CsPointerType otherPointer &&
      Depth == otherPointer.Depth &&
      InnerElement.Equals(otherPointer.InnerElement);
  }

  public override int GetHashCode() => HashCode.Combine(Depth, InnerElement);
}

public abstract class CsUdt(TypeIndex index, SourceGen sourceGen, TagRecord record, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public virtual TagRecord Record { get; } = record;

  public override string? Namespace {
    get => Parent is null ? field : Parent.Namespace;
    set;
  }

  public override string FullyQualifiedName => _fullyQualifiedName ??= QualifyWithGlobal();
  private string? _fullyQualifiedName;

  public CsStructure? Parent { get; private set; }
  public NestedTypeRecord? ThisAsNested { get; private set; }

  public void SetParent(CsStructure parent, NestedTypeRecord? record) {
    Parent = parent;
    // Template types will always have their "T" named "Type".
    // We want to maintain the original name.
    if (!parent.Record.Name.String.Contains('<') || record?.Name.String != "Type") {
      ThisAsNested = record;
    }

    // Invalidate names
    FullName = null;
    _fullyQualifiedName = null;
  }

  protected override string CreateFullName() => Parent is null ? SelfName : $"{Parent.FullName}.{SelfName}";

  protected override string CreateSelfName() {
    string recordName = Record.Name.String;
    string str = ThisAsNested?.Name.String ?? recordName;
    if (ThisAsNested is null) {
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

      if (idx != -1) {
        str = recordName[(idx + 2)..];
      }

      Namespace = idx != -1
        ? recordName[..idx]
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

public abstract class CsStructure : CsUdt {
  protected CsStructure(TagRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers) : base(index,
    sourceGen, record, modifiers) {
    AllFields = PdbFile.TryGetRecord<FieldListRecord>(record.FieldList)?.Fields ?? [];
    VfPtr = AllFields.Count == 0 ? null : AllFields.OfType<VirtualFunctionPointerRecord>().FirstOrDefault();
    if (VfPtr is { } vfPtr) {
      PointerRecord pointer = PdbFile.GetRecord<PointerRecord>(vfPtr.Type);
      VfTable = PdbFile.GetRecord<VirtualFunctionTableShapeRecord>(pointer.ReferentType);
    }
  }

  public readonly IReadOnlyList<TypeRecord> AllFields;
  public readonly VirtualFunctionPointerRecord? VfPtr;
  public readonly VirtualFunctionTableShapeRecord? VfTable;

  public ulong VfAddress =>
    _vfAddress ??= SourceGen.VTableAddresses.TryGetValue(FullName, out ulong address) ? address : 0;

  private ulong? _vfAddress;

  // Always lazy load fields/props with CsType members
  public readonly List<CsStructure> DerivedTypes = [];
  public CsBaseClass[] BaseClasses => field ??= GetBaseClasses();
  public HashSet<CsUdt> NestedClasses => field ??= GetNestedTypes();

  public CsInstanceField[] InstanceFields => field ??= GetInstanceFields();
  public CsInstanceMethod[] InstanceMethods => field ??= GetInstanceMethods();
  public CsStaticField[] StaticFields => field ??= GetStaticFields();

  public bool IsUnsafe => InstanceFields.Any(f => f.FieldType is CsPointerType or CsSimplePointerType);

  private CsBaseClass[] GetBaseClasses() {
    int count = AllFields.OfType<BaseClassRecord>().Count();
    var result = new CsBaseClass[count];
    foreach ((int i, BaseClassRecord baseClass) in AllFields.OfType<BaseClassRecord>().Index()) {
      result[i] = new CsBaseClass(this, baseClass);
      result[i].BaseType.DerivedTypes.Add(this);
    }

    return result;
  }

  private HashSet<CsUdt> GetNestedTypes() {
    return AllFields.OfType<NestedTypeRecord>()
      .Where(n => !n.Type.IsSimple)
      .Select(n => SourceGen.CsTypes[n.Type.ArrayIndex])
      .OfType<CsUdt>()
      .Where(c => c.Parent?.Record.UniqueName.String == Record.UniqueName.String)
      .ToHashSet();
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
          int overloadId = 0;
          foreach (OneMethodRecord overload in overloadedMethod.MethodList
                     .As<MethodOverloadListRecord>(PdbFile).Methods) {
            result[i++] = new CsInstanceMethod(this, overload, overloadedMethod.Name.String, ++overloadId);
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

  public VirtualFunctionTableShapeRecord? FindVfTable(out CsStructure? holder) {
    if (VfTable is not null) {
      holder = this;
      return VfTable;
    }

    CsStructure? current = this;
    while (current is not null) {
      if (current.VfTable is not null) {
        holder = current;
        return current.VfTable;
      }

      current = current.BaseClasses.FirstOrDefault()?.BaseType;
    }

    holder = null;
    return null;
  }
}

public sealed class CsStruct(ClassRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsStructure(record, index, sourceGen, modifiers) {
  public override ClassRecord Record => (ClassRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() => Parent is null
    ? $"struct {FullName}"
    : $"struct {FullName} ({SelfName})";

  public override bool Equals(CsType? other) {
    return other is CsStruct otherStruct && TypeIndex == otherStruct.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsUnion(UnionRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsStructure(record, index, sourceGen, modifiers) {
  public override UnionRecord Record => (UnionRecord)base.Record;

  public override ulong Size => Record.Size;

  public override string ToString() => Parent is null
    ? $"union {FullName}"
    : $"union {FullName} ({SelfName})";

  public override bool Equals(CsType? other) {
    return other is CsUnion otherUnion && TypeIndex == otherUnion.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsEnum : CsUdt {
  public CsEnum(EnumRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers) : base(index,
    sourceGen, record, modifiers) {
    Values = Record.MemberCount > 0
      ? PdbFile
        .GetRecord<FieldListRecord>(Record.FieldList).Fields
        .OfType<EnumeratorRecord>()
        .Select(e => new CsEnumField(e)).ToArray()
      : [];
  }

  public override EnumRecord Record => (EnumRecord)base.Record;
  public CsType Underlying => field ??= GetOrCreate(SourceGen, Record.UnderlyingType);

  public override string ToString() => Parent is null
    ? $"enum {FullName}"
    : $"enum {FullName} ({SelfName})";

  public override ulong Size => Underlying.Size;

  [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
  public readonly CsEnumField[] Values;

  public override bool Equals(CsType? other) {
    return other is CsEnum otherEnum && TypeIndex == otherEnum.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsEnumField(EnumeratorRecord record) {
  public readonly string Name = record.Name.String;
  public readonly object Value = record.Value;

  public override string ToString() => $"{Name} = {Value}";
}

public sealed class CsArray(ArrayRecord record, TypeIndex index, SourceGen sourceGen, ModifierOptions modifiers)
  : CsType(sourceGen, index, modifiers) {
  public readonly ArrayRecord Record = record;
  public CsType ElementType => field ??= GetOrCreate(SourceGen, Record.ElementType, Modifiers);
  public ulong Count => ElementType.Size != 0 ? Record.Size / ElementType.Size : 0;

  public override ulong Size => Record.Size;

  public override string FullyQualifiedName => field ??= QualifyWithGlobal();

  public CsType InnerElement {
    get {
      CsType current = ElementType;
      while (current is CsArray array) {
        current = array.ElementType;
      }

      return current;
    }
  }

  public override string ToString() => $"Array of {ElementType} [{Count}]";

  protected override string CreateSelfName() {
    const string start = "InlineArray_";
    string end = "";
    CsType rootElement = this;
    while (rootElement is CsArray a) {
      rootElement = a.ElementType;
      end += '_' + ((int)a.Count).ToString();
    }

    string innerName = rootElement.Namespace is null
      ? rootElement.FullName
      : rootElement.Namespace + '.' + rootElement.FullName;

    string elementName = innerName.SanitizeName(true, true);
    string result = start + elementName + end;
    return result;
  }

  public override bool Equals(CsType? other) {
    return other is CsArray otherArray &&
      Count == otherArray.Count &&
      ElementType.Equals(otherArray.ElementType);
  }

  public override int GetHashCode() => HashCode.Combine(Count, InnerElement);
}

public class CsInstanceField(CsStructure container, DataMemberRecord record) {
  public readonly CsStructure Container = container;
  public readonly DataMemberRecord Record = record;

  public string Name => Record.Name.String;
  public virtual TypeIndex Type => Record.Type;
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

  public virtual string Name => Record.Name.String;
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

public sealed class CsConstantField : CsStaticField {
  public CsConstantField(CsStructure container, StaticDataMemberRecord record, ConstantSymbol symbol) : base(container,
    record) {
    Symbol = symbol;

    string symName = symbol.Name.String;
    int index = symName.LastIndexOf("::", StringComparison.Ordinal);
    Name = (index != -1 ? symName[(index + 2)..] : symName).KeywordToVerbatim();
  }

  public readonly ConstantSymbol Symbol;

  public override string Name { get; }
  public object Value => Symbol.Value;

  public override string ToString() {
    string access = Record.Attributes.Access switch {
      MemberAccess.Public => "public ",
      MemberAccess.Protected => "protected ",
      MemberAccess.Private => "private ",
      _ => string.Empty
    };

    return $"{access}static const {FieldType.FullName} {Name} = {Symbol.Value}";
  }
}

public sealed class CsThreadLocalStorageField(
  CsStructure container,
  StaticDataMemberRecord record,
  ThreadLocalDataSymbol threadLocalData)
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

public sealed class CsRegularStaticField(CsStructure container, StaticDataMemberRecord record, DataSymbol data)
  : CsStaticField(container, record) {
  public readonly DataSymbol Data = data;

  public readonly ulong RelativeVirtualAddress =
    container.PdbFile.FindRelativeVirtualAddress(data.Segment, data.Offset);


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
  public static readonly List<CsInstanceMethod> HasFuncNames = [];
  public static readonly List<CsInstanceMethod> MissingFuncName = [];
#endif
  public CsInstanceMethod(CsStructure container, OneMethodRecord record, string? overloadedName = null,
    int overloadId = 0) : base(container.SourceGen, record.Type, ModifierOptions.None) {
    Container = container;
    Record = record;
    OverloadId = overloadId;
    MethodRecord = Container.PdbFile.GetRecord<MemberFunctionRecord>(record.Type);
    Name = MethodRecord.Options.HasFlag(FunctionOptions.Constructor)
      ? "Ctor"
      : record.Name.String ?? overloadedName!;
    if (Name.StartsWith('~')) {
      Name = "Dtor";
    }

    string delegateName = Name;
    DelegateFieldName = (OverloadId > 0 ? $"{delegateName}_{OverloadId}" : delegateName)
      .SanitizeName(true, true)
      .KeywordToVerbatim();

    CallingConvention = MethodRecord.CallingConvention;
    IsStatic = MethodRecord.ThisType is
      { IsSimple: true, SimpleMode: SimpleTypeMode.Direct, SimpleKind: SimpleTypeKind.Void };
    // TODO:
    string className = container.Record.Name.String;

    ProcedureInfo = SourceGen.ProcCache.TryGetValue((className, Name, Record.Type), out ProcedureInfo pInfo)
      ? pInfo
      : null;

    if (ProcedureInfo.HasValue) {
      HasFuncNames.Add(this);
      Args = pInfo.GoodSize
        ? pInfo.Args.Select(a => a.Name).ToArray()
        : Enumerable.Range(0, MethodRecord.ParameterCount).Select(i => $"arg{i + 1}").ToArray();
    }
    else {
      Args = Enumerable.Range(0, MethodRecord.ParameterCount).Select(i => $"arg{i + 1}").ToArray();
      MethodKind methodKind = Record.Attributes.MethodKind;
      if (!methodKind.HasFlag(MethodKind.PureVirtual) &&
          !methodKind.HasFlag(MethodKind.PureIntroducingVirtual)) {
        MissingFuncName.Add(this);
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
  public readonly int OverloadId;
  public readonly string DelegateFieldName;

  public override ulong Size => 0;

  public CsType ReturnType => field ??= GetOrCreate(Container.SourceGen, MethodRecord.ReturnType);
  public bool HasReturnType => ReturnType is not CsSimpleType { SelfName: "void" };

  public CsType[] ParameterTypes => field ??= MethodRecord.ArgumentList.As<ArgumentListRecord>(Container.PdbFile)
    .Arguments.Select(p => GetOrCreate(Container.SourceGen, p)).ToArray();

  public (CsType type, string name)[] Parameters =>
    field ??= ParameterTypes.Zip(Args, (type, name) => (type, name)).ToArray();

  public bool IsUnsafe => ParameterTypes.Any(p => p is CsPointerType or CsSimplePointerType) ||
    ReturnType is CsPointerType or CsSimplePointerType;

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

    string rva = RelativeVirtualAddress.HasValue
      ? $" (RVA: 0x{RelativeVirtualAddress.Value:X})"
      : " (RVA: unknown)";

    return $"{access}{@sealed}{virt}{ret} {Name}({args}){vfOffset}{rva}";
  }

  public override bool Equals(CsType? other) {
    return other is CsInstanceMethod otherMethod && Equals(otherMethod);
  }

  public bool Equals(CsInstanceMethod? other) {
    if (ReferenceEquals(this, other)) return true;
    if (other is null) return false;
    if (TypeIndex == other.TypeIndex) return true;

    return
      Container.Equals(other.Container) &&
      Name == other.Name &&
      ParameterTypes.SequenceEqual(other.ParameterTypes);
  }

  public override int GetHashCode() {
    HashCode hash = new();
    hash.Add(Container);
    hash.Add(Name);
    foreach (CsType param in ParameterTypes) {
      hash.Add(param);
    }

    return hash.ToHashCode();
  }

  public static readonly Dictionary<string, string> Operators = new() {
    // Assignment
    ["operator="] = "OperatorAssign",
    ["operator+="] = "OperatorAddAssign",
    ["operator-="] = "OperatorSubtractAssign",
    ["operator*="] = "OperatorMultiplyAssign",
    ["operator/="] = "OperatorDivideAssign",
    ["operator%="] = "OperatorModuloAssign",
    ["operator&="] = "OperatorBitwiseAndAssign",
    ["operator|="] = "OperatorBitwiseOrAssign",
    ["operator^="] = "OperatorBitwiseXorAssign",
    ["operator<<="] = "OperatorLeftShiftAssign",
    ["operator>>="] = "OperatorRightShiftAssign",

    // Arithmetic
    ["operator+"] = "OperatorAdd",
    ["operator-"] = "OperatorSubtract",
    ["operator*"] = "OperatorMultiply",
    ["operator/"] = "OperatorDivide",
    ["operator%"] = "OperatorModulo",

    // Increment/Decrement
    ["operator++"] = "OperatorIncrement",
    ["operator--"] = "OperatorDecrement",

    // Comparison
    ["operator=="] = "OperatorEquals",
    ["operator!="] = "OperatorNotEquals",
    ["operator<"] = "OperatorLessThan",
    ["operator>"] = "OperatorGreaterThan",
    ["operator<="] = "OperatorLessThanOrEqual",
    ["operator>="] = "OperatorGreaterThanOrEqual",
    ["operator<=>"] = "OperatorSpaceship",

    // Logical
    ["operator!"] = "OperatorLogicalNot",
    ["operator&&"] = "OperatorLogicalAnd",
    ["operator||"] = "OperatorLogicalOr",

    // Bitwise
    ["operator~"] = "OperatorBitwiseNot",
    ["operator&"] = "OperatorBitwiseAnd",
    ["operator|"] = "OperatorBitwiseOr",
    ["operator^"] = "OperatorBitwiseXor",
    ["operator<<"] = "OperatorLeftShift",
    ["operator>>"] = "OperatorRightShift",

    // Member and Pointer Access
    ["operator[]"] = "OperatorIndex",
    // ["operator*"] = "OperatorDereference", // This seems to actually be "operator MyType *"
    ["operator->"] = "OperatorMemberAccess",
    ["operator->*"] = "OperatorMemberPointerAccess",

    // Function Call and Comma
    ["operator()"] = "OperatorFunctionCall",
    ["operator,"] = "OperatorComma",

    // Memory Management
    ["operator new"] = "OperatorNew",
    ["operator new[]"] = "OperatorNewArray",
    ["operator delete"] = "OperatorDelete",
    ["operator delete[]"] = "OperatorDeleteArray",

    // User-Defined Literals
    ["operator\"\""] = "OperatorLiteral"
  };
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

  public override bool Equals(CsType? other) {
    return other is CsFunctionType otherFunc &&
      ReturnType.Equals(otherFunc.ReturnType) &&
      Arguments.SequenceEqual(otherFunc.Arguments);
  }

  public override int GetHashCode() {
    HashCode hash = new();
    hash.Add(ReturnType);
    foreach (CsType arg in Arguments) {
      hash.Add(arg);
    }

    return hash.ToHashCode();
  }
}

public sealed class CsBaseClass(CsStructure container, BaseClassRecord record) {
  public readonly CsStructure Container = container;
  public readonly BaseClassRecord Record = record;

  public CsStructure BaseType => field ??= Container.SourceGen.CsTypes[Record.Type.ArrayIndex] as CsStructure ??
    throw new InvalidOperationException(
      $"Base class type {Record.Type} is not a structure");

  public override string ToString() => $"base class {BaseType.FullName} (offset: 0x{Record.Offset:X})";
}
