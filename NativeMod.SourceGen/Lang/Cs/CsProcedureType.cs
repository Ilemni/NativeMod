using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public abstract class CsProcedureType : CsType {
  protected CsProcedureType(CsGen gen, TypeIndex index, TypeIndex argRecord) : base(gen, index) {
    ParameterTypes = argRecord.As<ArgumentListRecord>(gen.Pdb).Arguments.Select(p => Gen.GetOrCreate(p)).ToArray();
  }

  public CallingConvention CallingConvention { get; protected init; }
  public abstract CsType ReturnType { get; }

  public abstract CsType ThisType { get; }
  public virtual CsStructure? ClassType => null;

  public string DelegateType => field ??= CreateDelegateName();
  public override string CppName => DelegateType;

  protected override string CreateSelfName() => DelegateType;

  /// <summary>
  /// Indicates whether the C++ function has a real return value.
  /// That is <see cref="HasReturnType"/> is <see langword="true"/>,
  /// and <see cref="NeedsReturnBuffer"/> is <see langword="false"/>.
  /// </summary>
  public bool HasRealReturn => HasReturnType && !NeedsReturnBuffer;

  /// <summary>
  /// Indicates whether the C++ function has a non-void return type.
  /// </summary>
  public bool HasReturnType => ReturnType is not CsSimpleType { SelfName: "void" };

  /// <summary>
  /// Indicates whether the C++ function passes a hidden "return buffer" pointer as the very first argument.
  /// <para /> If <see langword="true"/>, the function return void,
  /// and a return buffer argument is injected into the first spot in the argument listed.
  /// <br />For example:
  /// <code>Vector3 AddTwoVectors(Vector3 first, Vector3 second);</code>
  /// turns into
  /// <code>void AddTwoVectors(Vector3* retBuffer, Vector3 first, Vector3 second);</code>
  /// </summary>
  public bool NeedsReturnBuffer => TypeNeedsReturnBuffer(ReturnType);

  public static bool TypeNeedsReturnBuffer(CsType csType) =>
    csType is CsStructure csStruct && (csStruct.Size > 8 || csStruct.VfAddress != 0);

  [MemberNotNullWhen(false, nameof(ThisType))]
  public bool IsStatic => ThisType is null || ThisType.TypeIndex.IsNoneType;

  public readonly CsType[] ParameterTypes;

  public bool IsVariadicFunction => ParameterTypes.Any(p => p.IsVariadic);

  public bool HasAnyVariadic {
    get {
      if (_checkedHasAnyVariadic) {
        return field;
      }

      _checkedHasAnyVariadic = true;
      foreach (CsType param in ParameterTypes) {
        if (param.IsVariadic) {
          return field = true;
        }

        if (param is not CsPointerType and not CsArray) {
          continue;
        }

        CsType inner = (param as CsPointerType)?.InnerElement ?? ((CsArray)param).InnerElement;
        if (inner is { IsVariadic: true } ||
            inner is CsProcedureType p && (p.IsVariadicFunction || p.HasAnyVariadic)) {
          return field = true;
        }
      }

      return false;
    }
  }

  private bool _checkedHasAnyVariadic;

  public bool IsUnsafe => !IsStatic ||
    ParameterTypes.Any(p => p is CsPointerType or CsSimplePointerType) ||
    ReturnType is CsPointerType or CsSimplePointerType;

  private string CreateDelegateName() {
    // Should return something like "delegate* unmanaged[Stdcall]<int, int, int>"
    using StringWriter writer = new(new StringBuilder());
    string conv = CallingConvention switch {
      CallingConvention.NearC => "Cdecl",
      CallingConvention.NearStdCall => "Stdcall",
      CallingConvention.NearFast => "Fastcall",
      CallingConvention.ThisCall => "Thiscall",
      _ => "Cdecl"
    };

    writer.WriteMany("delegate* unmanaged[", conv, "]<");

    bool needsComma = false;
    if (NeedsReturnBuffer) {
      writer.WriteCommaIfNeeded(ref needsComma);
      writer.Write(ReturnType.GlobalQualifiedName);
      writer.Write('*');
      needsComma = true;
    }

    writer.WriteIf(ThisType.GlobalQualifiedName, !IsStatic, ref needsComma);
    writer.WriteCppParameterTypes(ParameterTypes, ref needsComma);
    writer.WriteCommaIfNeeded(ref needsComma);
    writer.Write(!NeedsReturnBuffer ? ReturnType.GlobalQualifiedName : "void");
    writer.Write('>');
    return writer.ToString();
  }
}

public sealed class CsFunctionType : CsProcedureType {
  public CsFunctionType(CsGen gen, TypeIndex index, ProcedureRecord record)
    : base(gen, index, record.ArgumentList) {
    CallingConvention = record.CallingConvention;

    ThisType = Gen.GetOrCreate(TypeIndex.None);
    ReturnType = gen.GetOrCreate(record.ReturnType);
  }

  public override CsType ThisType { get; }
  public override CsType ReturnType { get; }

  protected override bool EqualsCore(CsType? other) {
    return other is CsFunctionType otherFunc && (
      ReferenceEquals(this, other) ||
      ReturnType.Equals(otherFunc.ReturnType) &&
      ParameterTypes.SequenceEqual(otherFunc.ParameterTypes));
  }

  public override int GetHashCode() {
    HashCode hash = new();
    hash.Add(ReturnType);
    foreach (CsType arg in ParameterTypes) {
      hash.Add(arg);
    }

    return hash.ToHashCode();
  }
}

public sealed class CsMemberFunctionType : CsProcedureType {
  public CsMemberFunctionType(CsGen gen, TypeIndex index, MemberFunctionRecord record)
    : base(gen, index, record.ArgumentList) {
    CallingConvention = record.CallingConvention;

    ThisType = gen.GetOrCreate(record.ThisType);
    ClassType = gen.GetOrCreate<CsStructure>(record.ClassType);
    ReturnType = gen.GetOrCreate(record.ReturnType);
  }

  public override CsType ThisType { get; }
  public override CsStructure ClassType { get; }
  public override CsType ReturnType { get; }

  protected override bool EqualsCore(CsType? other) {
    return other is CsMemberFunctionType otherFunc && (
      ReferenceEquals(this, other) ||
      ThisType.Equals(otherFunc.ThisType) &&
      ReturnType.Equals(otherFunc.ReturnType) &&
      ParameterTypes.SequenceEqual(otherFunc.ParameterTypes));
  }

  public override int GetHashCode() {
    HashCode hash = new();
    hash.Add(ThisType);
    hash.Add(ReturnType);
    foreach (CsType param in ParameterTypes) {
      hash.Add(param);
    }

    return hash.ToHashCode();
  }
}

public sealed class CsMethod : CsType {
#if DEBUG
  public static readonly List<CsMethod> HasProcSyms = [];
  public static readonly List<CsMethod> MissingProcSyms = [];
#endif
  public CsMethod(CsStructure container, OneMethodRecord record, string? overloadedName = null, int overloadId = 0)
    : base(container.Gen, record.Type) {
    Record = record;
    OverloadId = overloadId;
    MethodRecord = record.Type.As<MemberFunctionRecord>(PdbFile);
    VfSlot = record.VFTableOffset >= 0 ? record.VFTableOffset / 8 : -1;

    CppName = record.Name.String ?? overloadedName!;
    string? operatorName = Operators.GetValueOrDefault(CppName);
    if (operatorName is not null) {
      OperatorName = CppName;
      Name = operatorName;
    }
    else {
      OperatorName = operatorName;
      string replaceCtorDtor = MethodRecord.Options.HasFlag(FunctionOptions.Constructor)
        ? "Ctor"
        : CppName.StartsWith('~')
          ? "Dtor"
          : CppName;

      Name = replaceCtorDtor.SanitizeName(true, true);
    }

    CleanName = (OverloadId > 0 ? $"{Name}_{OverloadId}" : Name).SanitizeName(true, true);
    DelegateFieldName = CleanName.KeywordToVerbatim();

    string className = container.Record.Name.String;
    ProcedureInfo = Gen.ProcCache.TryGetValue((className, CppName, Record.Type), out ProcedureInfo pInfo)
      ? pInfo
      : null;


    if (ProcedureInfo.HasValue) {
#if DEBUG
      HasProcSyms.Add(this);
#endif
      Parameters = pInfo.Args;
      Address = ProcedureInfo.Value.Address;
    }
    else {
      string[] args = Enumerable.Range(0, MethodRecord.ParameterCount).Select(i => $"arg{i + 1}").ToArray();
      Parameters = Gen.GetOrCreate<CsMemberFunctionType>(Record.Type).ParameterTypes
        .Zip(args, (type, name) => (type, name)).ToArray();
      Address = 0;
#if DEBUG
      MethodKind methodKind = Record.Attributes.MethodKind;
      if (!methodKind.HasFlag(MethodKind.PureVirtual) &&
          !methodKind.HasFlag(MethodKind.PureIntroducingVirtual)) {
        MissingProcSyms.Add(this);
      }
#endif
    }
  }

  /// Original C++ name of the method as found in the source code or metadata.
  /// <br /> This is mainly used to display the original name in documentation.
  public override string CppName { get; }
  /// Sanitized C# name of the method. May potentially be an <c>operator</c>.
  /// <br /> This is to be used for the method name, e.g. <c>public void {Name}(...)</c>
  public readonly string Name;

  public readonly string? OperatorName;
  /// De-duplicated version of <see cref="Name"/>, with <c>operator</c>s renamed to OperatorAssign, etc.
  /// <br /> This is to be used in documentation that points to <see cref="DelegateFieldName"/>
  public readonly string CleanName;
  /// Same as <see cref="CleanName"/> but with any necessary <c>@</c> prefix for C# keywords.
  /// <br /> This is to be used for the delegate field name, e.g. <c>public static readonly delegate* ... {DelegateFieldName};</c>
  /// <br /> This can also be used in all other cases where duplicate names are not allowed, such as hook class names.
  public readonly string DelegateFieldName;

  public readonly OneMethodRecord Record;
  public readonly MemberFunctionRecord MethodRecord;
  public readonly ProcedureInfo? ProcedureInfo;
  public readonly int OverloadId;
  public readonly int VfSlot;
  public bool IsVirtual => VfSlot != -1;
  public readonly uint Address;
  /// <summary> Indicates whether the method has a known address in the binary. </summary>
  public bool IsDefined => Address != 0;

  public CsMemberFunctionType MemberFunction => field ??= Gen.GetOrCreate<CsMemberFunctionType>(Record.Type);

  public (CsType type, string name)[] Parameters { get; }

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  private string? _toStringValue;

  protected override string CreateSelfName() => MemberFunction.SelfName;

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

    string ret = MemberFunction.ReturnType.CppName;
    string args = string.Join(", ", MemberFunction.ParameterTypes.Select(p => p.CppName));

    string vfOffset = VfSlot != -1 ? $" (vfSlot: {VfSlot})" : string.Empty;

    string address = IsDefined
      ? $" (Address: 0x{Address:X})"
      : "";

    return $"{access}{@sealed}{virt}{ret} {CppName}({args}){vfOffset}{address}";
  }

  protected override bool EqualsCore(CsType? other) {
    if (ReferenceEquals(this, other)) return true;
    if (other is not CsMethod otherMethod) return false;
    if (TypeIndex == other.TypeIndex) return true;

    return MemberFunction.Equals(otherMethod.MemberFunction) &&
      Name.Equals(otherMethod.Name);
  }

  public override int GetHashCode() => HashCode.Combine(MemberFunction, Name);

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
