using System.Runtime.CompilerServices;
using SharpPdb.Native.Types;
using SharpPdb.Windows;

namespace PdbToCSharp.Lang.Cs;

public sealed class CsSimpleType : CsType {
  public CsSimpleType(CsGen gen, TypeIndex index) : base(gen, index) {
    Size = GetSize(null!, TypeIndex);
    CppName = ToCppName(TypeIndex);

    Marshaller = index switch {
      { SimpleMode: not SimpleTypeMode.Direct } => null,
      { SimpleKind: SimpleTypeKind.Boolean8 } => new CsMarshaller {
        CppType = "byte",
        WriteFromCpp = WriteBoolFromCpp,
        WriteToCpp = static (writer, arg) => writer.WriteMany(arg, " ? (byte)1 : (byte)0")
      },
      { SimpleKind: SimpleTypeKind.Boolean16 } => new CsMarshaller {
        CppType = "ushort",
        WriteFromCpp = WriteBoolFromCpp,
        WriteToCpp = static (writer, arg) => writer.WriteMany(arg, " ? (ushort)1 : (ushort)0")
      },
      { SimpleKind: SimpleTypeKind.Boolean32 } => new CsMarshaller {
        CppType = "uint",
        WriteFromCpp = WriteBoolFromCpp,
        WriteToCpp = static (writer, arg) => writer.WriteMany(arg, " ? (uint)1 : (uint)0")
      },
      { SimpleKind: SimpleTypeKind.Boolean64 } => new CsMarshaller {
        CppType = "ulong",
        WriteFromCpp = WriteBoolFromCpp,
        WriteToCpp = static (writer, arg) => writer.WriteMany(arg, " ? (ulong)1 : (ulong)0")
      },
      _ => null
    };
    return;

    static void WriteBoolFromCpp(TextWriter writer, string arg) => writer.WriteMany(arg, " > 0");

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetSize")]
    static extern ulong GetSize(PdbSimpleType sim, TypeIndex index);
  }

  public override CsMarshaller? Marshaller { get; }

  public override ulong Size { get; }

  // TODO: add global:: if there is a namespace (System.Half, etc)
  public override string FullyQualifiedName => SelfName;
  public override string GlobalQualifiedName => SelfName;

  public override string CppName { get; }

  public override string ToString() => $"{FullName} ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => ToCsName(TypeIndex);

  public static string ToCsName(TypeIndex index) => index.SimpleKind switch {
    SimpleTypeKind.None => "__arglist",
    SimpleTypeKind.Void => "void",
    SimpleTypeKind.NotTranslated => throw new NotSupportedException("NotTranslated type kind is not supported"),
    SimpleTypeKind.HResult => nameof(CppHResult),
    SimpleTypeKind.SignedCharacter => nameof(signed_char),
    SimpleTypeKind.UnsignedCharacter => nameof(unsigned_char),
    SimpleTypeKind.NarrowCharacter => nameof(_char),
    SimpleTypeKind.WideCharacter => nameof(wchar_t),
    SimpleTypeKind.Character16 => nameof(char16_t),
    SimpleTypeKind.Character32 => nameof(char32_t),
    SimpleTypeKind.SByte => "sbyte",
    SimpleTypeKind.Byte => "byte",
    SimpleTypeKind.Int16Short => "short",
    SimpleTypeKind.UInt16Short => "ushort",
    SimpleTypeKind.Int16 => "short",
    SimpleTypeKind.UInt16 => "ushort",
    SimpleTypeKind.Int32Long => "int",
    SimpleTypeKind.UInt32Long => "uint",
    SimpleTypeKind.Int32 => "int",
    SimpleTypeKind.UInt32 => "uint",
    SimpleTypeKind.Int64Quad => "long",
    SimpleTypeKind.UInt64Quad => "ulong",
    SimpleTypeKind.Int64 => "long",
    SimpleTypeKind.UInt64 => "ulong",
    SimpleTypeKind.Int128Oct => nameof(__int128),
    SimpleTypeKind.UInt128Oct => nameof(unsigned___int128),
    SimpleTypeKind.UInt128 => nameof(uint128_t),
    SimpleTypeKind.Int128 => nameof(int128_t),
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
    SimpleTypeKind.Boolean16 => "bool",
    SimpleTypeKind.Boolean32 => "bool",
    SimpleTypeKind.Boolean64 => "bool",
    SimpleTypeKind.Complex16 => throw new NotSupportedException("Complex16 type kind is not supported"),
    SimpleTypeKind.Complex32PartialPrecision => throw new NotSupportedException(
      "Complex32PartialPrecision type kind is not supported"),
    SimpleTypeKind.Complex48 => throw new NotSupportedException("Complex48 type kind is not supported"),
    SimpleTypeKind.Boolean128 => "bool",
    _ => throw new ArgumentOutOfRangeException(nameof(index))
  };

  public static string ToCppName(TypeIndex index) => index.SimpleKind switch {
    SimpleTypeKind.None => "__arglist",
    SimpleTypeKind.Void => "void",
    SimpleTypeKind.NotTranslated => throw new NotSupportedException("NotTranslated type kind is not supported"),
    SimpleTypeKind.HResult => "HRESULT",
    SimpleTypeKind.SignedCharacter => "signed char",
    SimpleTypeKind.UnsignedCharacter => "unsigned char",
    SimpleTypeKind.NarrowCharacter => "char",
    SimpleTypeKind.WideCharacter => "wchar_t",
    SimpleTypeKind.Character16 => "char16_t",
    SimpleTypeKind.Character32 => "char32_t",
    SimpleTypeKind.SByte => "int8_t",
    SimpleTypeKind.Byte => "uint8_t",
    SimpleTypeKind.Int16Short => "short",
    SimpleTypeKind.UInt16Short => "unsigned short",
    SimpleTypeKind.Int16 => "int16_t",
    SimpleTypeKind.UInt16 => "uint16_t",
    SimpleTypeKind.Int32Long => "long",
    SimpleTypeKind.UInt32Long => "unsigned long",
    SimpleTypeKind.Int32 => "int",
    SimpleTypeKind.UInt32 => "unsigned int",
    SimpleTypeKind.Int64Quad => "long long",
    SimpleTypeKind.UInt64Quad => "unsigned long long",
    SimpleTypeKind.Int64 => "int64_t",
    SimpleTypeKind.UInt64 => "uint64_t",
    SimpleTypeKind.Int128Oct => "__int128",
    SimpleTypeKind.UInt128Oct => "unsigned __int128",
    SimpleTypeKind.UInt128 => "uint128_t",
    SimpleTypeKind.Int128 => "int128_t",
    SimpleTypeKind.Float16 => "_Float16",
    SimpleTypeKind.Float32 => "float",
    SimpleTypeKind.Float32PartialPrecision => nameof(CppFloat32PartialPrecision),
    SimpleTypeKind.Float48 => throw new NotSupportedException("Float48 type kind is not supported"),
    SimpleTypeKind.Float64 => "double",
    SimpleTypeKind.Float80 => "long double",
    SimpleTypeKind.Float128 => "__float128",
    SimpleTypeKind.Complex32 => "std::complex<_Float16>",
    SimpleTypeKind.Complex64 => "std::complex<float>",
    SimpleTypeKind.Complex80 => throw new NotSupportedException("Complex80 type kind is not supported"),
    SimpleTypeKind.Complex128 => "std::complex<double>",
    SimpleTypeKind.Boolean8 => "bool",
    SimpleTypeKind.Boolean16 => "bool",
    SimpleTypeKind.Boolean32 => "BOOL",
    SimpleTypeKind.Boolean64 => "bool",
    SimpleTypeKind.Complex16 => throw new NotSupportedException("Complex16 type kind is not supported"),
    SimpleTypeKind.Complex32PartialPrecision => throw new NotSupportedException(
      "Complex32PartialPrecision type kind is not supported"),
    SimpleTypeKind.Complex48 => throw new NotSupportedException("Complex48 type kind is not supported"),
    SimpleTypeKind.Boolean128 => "bool",
    _ => throw new ArgumentOutOfRangeException(nameof(index))
  };

  protected override bool EqualsCore(CsType? other) {
    return other is CsSimpleType otherSimple &&
      (TypeIndex == otherSimple.TypeIndex || SelfName == otherSimple.SelfName);
  }

  // ReSharper disable once NonReadonlyMemberInGetHashCode - For simple types, this is effectively immutable
  public override int GetHashCode() => SelfName.GetHashCode();
}

public sealed class CsSimplePointerType : CsType {
  public CsSimplePointerType(CsGen gen, TypeIndex index) : base(gen, index) {
    Size = GetSize(null!, TypeIndex);
    ElementType = Gen.GetOrCreate<CsSimpleType>(new TypeIndex(index.SimpleKind));

    CppName = ElementType.CppName + '*';
    return;

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetPointerSize")]
    static extern ulong GetSize(PdbSimplePointerType sim, TypeIndex index);
  }

  public readonly CsSimpleType ElementType;
  public override string CppName { get; }

  public override ulong Size { get; }

  public override string ToString() => $"{SelfName} ({TypeIndex.SimpleKind})";
  protected override string CreateSelfName() => CsSimpleType.ToCsName(TypeIndex) + '*';

  protected override bool EqualsCore(CsType? other) {
    if (ReferenceEquals(this, other)) return true;

    if (other is CsPointerType type && type.ElementType.Unwrap() is CsSimpleType s) {
      return s.Equals(ElementType);
    }

    return other is CsSimplePointerType otherPtr && (TypeIndex == otherPtr.TypeIndex || SelfName == otherPtr.SelfName);
  }

  // ReSharper disable once NonReadonlyMemberInGetHashCode - For simple types, this is effectively immutable
  public override int GetHashCode() => SelfName.GetHashCode();
}
