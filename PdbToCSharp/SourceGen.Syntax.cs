using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PdbToCSharp;

public partial class SourceGen {
  // Attributes
  private static readonly AttributeListSyntax CompGenAttribute =
    AttributeList(
      SingletonSeparatedList(Attribute(IdentifierName("CompilerGenerated"))));

  private static SyntaxList<AttributeListSyntax> ClassAttribute(ulong structSize) =>
    List<AttributeListSyntax>(
    [
      CompGenAttribute,
      CreateStructLayoutAttribute(structSize)
    ]);

  private static AttributeListSyntax CreateFieldOffsetAttribute(int offset) {
    return
      AttributeList(SingletonSeparatedList(
        Attribute(IdentifierName("FieldOffset"))
          .WithArgumentList(
            AttributeArgumentList(
              SingletonSeparatedList(
                AttributeArgument(
                  LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    Literal(offset))))))));
  }

  private static AttributeListSyntax CreateStructLayoutAttribute(ulong size) {
    return AttributeList(SingletonSeparatedList(Attribute(IdentifierName("StructLayout"))
      .WithArgumentList(
        AttributeArgumentList(SeparatedList<AttributeArgumentSyntax>(
          new SyntaxNodeOrToken[] {
            AttributeArgument(
              MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, IdentifierName("LayoutKind"),
                IdentifierName("Explicit"))),
            Token(SyntaxKind.CommaToken),
            AttributeArgument(
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)size)))
              .WithNameEquals(NameEquals(IdentifierName("Size")))
          })))));
  }

  // Keywords and Modifiers
  private static readonly SyntaxToken PubKw = Token(SyntaxKind.PublicKeyword);
  private static readonly SyntaxToken StaticKw = Token(SyntaxKind.StaticKeyword);
  private static readonly SyntaxToken PartialKw = Token(SyntaxKind.PartialKeyword);
  private static readonly SyntaxTokenList Pub = [PubKw];
  private static readonly SyntaxTokenList Private = [Token(SyntaxKind.PrivateKeyword)];
  private static readonly SyntaxTokenList PubStatic = [PubKw, StaticKw];
  private static readonly SyntaxTokenList PubConst = [PubKw, Token(SyntaxKind.ConstKeyword)];
  private static readonly SyntaxTokenList PubUnsafe = [PubKw, Token(SyntaxKind.UnsafeKeyword)];
  private static readonly SyntaxTokenList StructKws = PubUnsafe/*.Add(PartialKw)*/;

  // Creating Syntax Nodes
  internal static FileScopedNamespaceDeclarationSyntax CreateNamespaceSyntax(string namespaceName) {
    return FileScopedNamespaceDeclaration(IdentifierName(namespaceName))
      .WithUsings(List([
        UsingDirective(IdentifierName("System.Runtime.CompilerServices")),
        UsingDirective(IdentifierName("System.Runtime.InteropServices")),
        UsingDirective(IdentifierName("MioModLoader.ModLoader")).WithStaticKeyword(StaticKw)
      ]))
      .WithNamespaceKeyword(
        Token(
          TriviaList(
            Comment("// ReSharper disable InvalidXmlDocComment"),
            Comment("// ReSharper disable RedundantUnsafeContext"),
            Comment("// ReSharper disable InconsistentNaming"),
            Comment("// ReSharper disable UnusedType.Global")),
          SyntaxKind.NamespaceKeyword,
          TriviaList()));
  }

  private static StructDeclarationSyntax CreateInlineArraySyntax(PdbArrayType arr, string arrayName,
    string elementName, ulong count) {
    const string fieldName = "element0";

    StructDeclarationSyntax csArray = StructDeclaration(arrayName)
      .WithAttributeLists(List<AttributeListSyntax>([
        CompGenAttribute,
        AttributeList(SingletonSeparatedList(Attribute(IdentifierName($"InlineArray({count})"))))
      ]))
      .WithModifiers(PubUnsafe)
      .WithMembers(SingletonList<MemberDeclarationSyntax>(
        FieldDeclaration(
            VariableDeclaration(IdentifierName(elementName))
              .WithVariables(SingletonSeparatedList(VariableDeclarator(fieldName))))
          .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
      ))
      .WithLeadingTrivia(Comment($"/// Inline array type: {arr.Name}"));
    return csArray;
  }

  private static StructDeclarationSyntax CreateStructSyntax(PdbUserDefinedType udtType, string name) {
    StructDeclarationSyntax csClass = StructDeclaration(name)
      .WithAttributeLists(ClassAttribute(udtType.Size))
      .WithModifiers(StructKws)
      .WithLeadingTrivia(Comment(
        $"/// {(udtType is PdbClassType ? "struct" : "union")} type: {udtType.Name} ({udtType.TypeIndex})"));
    return csClass;
  }

  private static EnumDeclarationSyntax CreateEnumSyntax(PdbEnumType enumType, string name) {
    return EnumDeclaration(name)
      .WithAttributeLists([CompGenAttribute])
      .WithModifiers(Pub)
      .WithLeadingTrivia(Comment($"/// Enum type: {enumType.Name} ({enumType.UniqueName})"));
  }

  private static EnumMemberDeclarationSyntax CreateEnumMemberSyntax(PdbEnumeratorValue enumValue) {
    EnumMemberDeclarationSyntax enumMember = EnumMemberDeclaration(enumValue.Name)
      .WithEqualsValue(EqualsValueClause(
        enumValue.Value is bool b
          ? LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)
          : LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            enumValue.Value switch {
              int v => Literal(v),
              uint v => v > int.MaxValue ? Literal(v) : Literal((int)v),
              short v => Literal(v),
              ushort v => Literal(v),
              sbyte v => Literal(v),
              byte v => Literal(v),
              long v => v > int.MaxValue ? Literal(v) : Literal((int)v),
              ulong v => Literal(v),
              float v => Literal(v),
              double v => Literal(v),
              _ => throw new UnreachableException($"Unexpected enum value type: {enumValue.Value.GetType().Name} for {enumValue.Name}")
            }
          )
      ));
    return enumMember;
  }

  private static FieldDeclarationSyntax CreateConstFieldSyntax(PdbTypeConstant constant, string fieldTypeName, string value) {
    return FieldDeclaration(
        VariableDeclaration(IdentifierName(fieldTypeName))
          .WithVariables(
            SingletonSeparatedList(
              VariableDeclarator(constant.Name.EscapeField())
                .WithInitializer(
                  EqualsValueClause(
                    IdentifierName(constant.Type.TypeIndex.IsSimple ? value : $"({fieldTypeName}){value}")
                  )))))
      .WithModifiers(PubConst);
  }

  private static PropertyDeclarationSyntax CreateStaticField(PdbTypeRegularStaticField regularStaticField, string fieldTypeName) {
    return PropertyDeclaration(IdentifierName(fieldTypeName), Identifier(regularStaticField.Name.EscapeField()))
      .WithModifiers(PubStatic)
      .WithExpressionBody(
        ArrowExpressionClause(
          IdentifierName(
            $"*(({fieldTypeName}*)(mioMemoryAddress + {regularStaticField.RelativeVirtualAddress}))")))
      .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
  }

  private static FieldDeclarationSyntax CreateInstanceFieldSyntax(PdbTypeField field, string fieldName) {
    AttributeListSyntax fieldOffsetAttribute = CreateFieldOffsetAttribute((int)field.Offset);
    return FieldDeclaration(
        VariableDeclaration(IdentifierName(fieldName))
          .AddVariables(VariableDeclarator(field.Name.SanitizeName().EscapeField())))
      .AddAttributeLists(fieldOffsetAttribute)
      .WithModifiers(field.Access == MemberAccess.Public ? Pub : Private)
      .WithLeadingTrivia(Comment(
        field is PdbTypeBitField bf
          ? $"/// BitField: {bf.Type.Name} (TypeIndex: {bf.Type.TypeIndex}) (Pos:{bf.Offset} Off:{bf.BitOffset} Size:{bf.BitSize})"
          : $"/// Type: {field.Type.Name} (TypeIndex: {field.Type.TypeIndex}) {(field.Type is PdbUserDefinedType { TagRecord.IsForwardReference: true } ? "(Forward Reference)" : "")}"
      ));
  }

  private static FieldDeclarationSyntax CreateBaseTypeFieldSyntax(PdbTypeBaseClass baseClass, string name, int? i) {
    AttributeListSyntax fieldOffsetAttribute = CreateFieldOffsetAttribute((int)baseClass.Offset);
    FieldDeclarationSyntax field = FieldDeclaration(
        VariableDeclaration(IdentifierName(name))
          .AddVariables(VariableDeclarator(i is not null ? $"Base{i + 1}" : "Base")))
      .WithAttributeLists([fieldOffsetAttribute])
      .WithModifiers(Pub);

    return field;
  }

  public static readonly Dictionary<TypeIndex, string> BuiltinTypeNames = new() {
    [new TypeIndex(0u)] = "<no type>", // <no type> (None | Direct)
    [new TypeIndex(3)] = "void", // void (Void | Direct)
    [new TypeIndex(8)] = "uint", // HRESULT (HResult | Direct)
    [new TypeIndex(16)] = "sbyte", // signed char (SignedCharacter | Direct)
    [new TypeIndex(17)] = "short", // short (Int16Short | Direct)
    [new TypeIndex(18)] = "int", // long (Int32Long | Direct)
    [new TypeIndex(19)] = "long", // __int64 (Int64Quad | Direct)
    [new TypeIndex(32)] = "ushort", // unsigned char (UnsignedCharacter | Direct)
    [new TypeIndex(33)] = "ushort", // unsigned short (UInt16Short | Direct)
    [new TypeIndex(34)] = "uint", // unsigned long (UInt32Long | Direct)
    [new TypeIndex(35)] = "ulong", // unsigned __int64 (UInt64Quad | Direct)
    [new TypeIndex(48)] = "bool", // bool (Boolean8 | Direct)
    [new TypeIndex(49)] = "ushort", // __bool16 (Boolean16 | Direct)
    [new TypeIndex(50)] = "uint", // __bool32 (Boolean32 | Direct)
    [new TypeIndex(51)] = "ulong", // __bool64 (Boolean64 | Direct)
    [new TypeIndex(64)] = "float", // float (Float32 | Direct)
    [new TypeIndex(65)] = "double", // double (Float64 | Direct)
    // [new TypeIndex(66)] = "long double", // long double (Float80 | Direct)
    // [new TypeIndex(67)] = "__float128", // __float128 (Float128 | Direct)
    // [new TypeIndex(68)] = "__float48", // __float48 (Float48 | Direct)
    [new TypeIndex(69)] = "float", // float (Float32PartialPrecision | Direct)
    [new TypeIndex(70)] = "single", // __half (Float16 | Direct)
    // [new TypeIndex(80)] = "_Complex float", // _Complex float (Complex32 | Direct)
    // [new TypeIndex(81)] = "_Complex double", // _Complex double (Complex64 | Direct)
    // [new TypeIndex(82)] = "_Complex long double", // _Complex long double (Complex80 | Direct)
    // [new TypeIndex(83)] = "_Complex __float128", // _Complex __float128 (Complex128 | Direct)
    [new TypeIndex(104)] = "sbyte", // __int8 (SByte | Direct)
    [new TypeIndex(105)] = "byte", // unsigned __int8 (Byte | Direct)
    [new TypeIndex(112)] = "byte", // char (NarrowCharacter | Direct)
    [new TypeIndex(113)] = "char", // wchar_t (WideCharacter | Direct)
    [new TypeIndex(114)] = "short", // __int16 (Int16 | Direct)
    [new TypeIndex(115)] = "ushort", // unsigned __int16 (UInt16 | Direct)
    [new TypeIndex(116)] = "int", // int (Int32 | Direct)
    [new TypeIndex(117)] = "uint", // unsigned (UInt32 | Direct)
    [new TypeIndex(118)] = "long", // __int64 (Int64 | Direct)
    [new TypeIndex(119)] = "ulong", // unsigned __int64 (UInt64 | Direct)
    // [new TypeIndex(120)] = "__int128", // __int128 (Int128 | Direct)
    // [new TypeIndex(121)] = "unsigned __int128", // unsigned __int128 (UInt128 | Direct)
    [new TypeIndex(122)] = "char", // char16_t (Character16 | Direct)
    [new TypeIndex(123)] = "uint", // char32_t (Character32 | Direct)

    [new TypeIndex(1539)] = "void*", // void* (Void | NearPointer64)
    [new TypeIndex(1544)] = "uint*", // HRESULT* (HResult | NearPointer64)
    [new TypeIndex(1552)] = "sbyte*", // signed char* (SignedCharacter | NearPointer64)
    [new TypeIndex(1553)] = "short*", // short* (Int16Short | NearPointer64)
    [new TypeIndex(1554)] = "int*", // long* (Int32Long | NearPointer64)
    [new TypeIndex(1555)] = "long*", // __int64* (Int64Quad | NearPointer64)
    [new TypeIndex(1568)] = "ushort*", // unsigned char* (UnsignedCharacter | NearPointer64)
    [new TypeIndex(1569)] = "ushort*", // unsigned short* (UInt16Short | NearPointer64)
    [new TypeIndex(1570)] = "uint*", // unsigned long* (UInt32Long | NearPointer64)
    [new TypeIndex(1571)] = "ulong*", // unsigned __int64* (UInt64Quad | NearPointer64)
    [new TypeIndex(1584)] = "bool*", // bool* (Boolean8 | NearPointer64)
    [new TypeIndex(1585)] = "ushort*", // __bool16* (Boolean16 | NearPointer64)
    [new TypeIndex(1586)] = "uint*", // __bool32* (Boolean32 | NearPointer64)
    [new TypeIndex(1587)] = "ulong*", // __bool64* (Boolean64 | NearPointer64)
    [new TypeIndex(1600)] = "float*", // float* (Float32 | NearPointer64)
    [new TypeIndex(1601)] = "double*", // double* (Float64 | NearPointer64)
    // [new TypeIndex(1602)] = "long double*", // long double* (Float80 | NearPointer64)
    // [new TypeIndex(1603)] = "__float128*", // __float128* (Float128 | NearPointer64)
    // [new TypeIndex(1604)] = "__float48*", // __float48* (Float48 | NearPointer64)
    [new TypeIndex(1605)] = "float*", // float* (Float32PartialPrecision | NearPointer64)
    [new TypeIndex(1606)] = "single*", // __half* (Float16 | NearPointer64)
    // [new TypeIndex(1616)] = "_Complex float*", // _Complex float* (Complex32 | NearPointer64)
    // [new TypeIndex(1617)] = "_Complex double*", // _Complex double* (Complex64 | NearPointer64)
    // [new TypeIndex(1618)] = "_Complex long double*", // _Complex long double* (Complex80 | NearPointer64)
    // [new TypeIndex(1619)] = "_Complex __float128*", // _Complex __float128* (Complex128 | NearPointer64)
    [new TypeIndex(1640)] = "sbyte*", // __int8* (SByte | NearPointer64)
    [new TypeIndex(1641)] = "byte*", // unsigned __int8* (Byte | NearPointer64)
    [new TypeIndex(1648)] = "byte*", // char* (NarrowCharacter | NearPointer64)
    [new TypeIndex(1649)] = "char*", // wchar_t* (WideCharacter | NearPointer64)
    [new TypeIndex(1650)] = "short*", // __int16* (Int16 | NearPointer64)
    [new TypeIndex(1651)] = "ushort*", // unsigned __int16* (UInt16 | NearPointer64)
    [new TypeIndex(1652)] = "int*", // int* (Int32 | NearPointer64)
    [new TypeIndex(1653)] = "uint*", // unsigned* (UInt32 | NearPointer64)
    [new TypeIndex(1654)] = "long*", // __int64* (Int64 | NearPointer64)
    [new TypeIndex(1655)] = "ulong*", // unsigned __int64* (UInt64 | NearPointer64)
    // [new TypeIndex(1656)] = "__int128*", // __int128* (Int128 | NearPointer64)
    // [new TypeIndex(1657)] = "unsigned __int128*", // unsigned __int128* (UInt128 | NearPointer64)
    [new TypeIndex(1658)] = "char*", // char16_t* (Character16 | NearPointer64)
    [new TypeIndex(1659)] = "uint*", // char32_t* (Character32 | NearPointer64)
  };
}
