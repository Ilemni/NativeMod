using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PdbToCSharp.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PdbToCSharp;

public partial class SourceGen {
  // Attributes
  private static readonly AttributeListSyntax CompGenAttribute =
    AttributeList(
      SingletonSeparatedList(Attribute(IdentifierName("GeneratedCode(\"PdbToCSharp\", \"0.1.0.0\")"))));

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
  private static readonly SyntaxTokenList StructKws = PubUnsafe /*.Add(PartialKw)*/;

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

  private static StructDeclarationSyntax CreateInlineArraySyntax(CsArray arr, string arrayName,
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
      .WithLeadingTrivia(Comment($"/// Inline array type: {arr.FullName}"));
    return csArray;
  }

  private static StructDeclarationSyntax CreateStructSyntax(CsStructure udtType) {
    StructDeclarationSyntax csClass = StructDeclaration(udtType.SelfName)
      .WithAttributeLists(ClassAttribute(udtType.Size))
      .WithModifiers(StructKws)
      .WithLeadingTrivia(Comment(
        $"/// {(udtType is CsStruct ? "struct" : "union")} type: {udtType.Record.Name} ({udtType.TypeIndex})"));
    return csClass;
  }

  private static EnumDeclarationSyntax CreateEnumSyntax(CsEnum enumType) {
    return EnumDeclaration(enumType.SelfName)
      .WithAttributeLists([CompGenAttribute])
      .WithModifiers(Pub)
      .WithLeadingTrivia(Comment($"/// Enum type: {enumType.Record.Name} ({enumType.Record.UniqueName})"));
  }

  private static EnumMemberDeclarationSyntax CreateEnumMemberSyntax(CsEnumField enumValue) {
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
              _ => throw new UnreachableException(
                $"Unexpected enum value type: {enumValue.Value.GetType().Name} for {enumValue.Name}")
            }
          )
      ));
    return enumMember;
  }

  private static FieldDeclarationSyntax CreateConstFieldSyntax(CsConstantField constant, string fieldTypeName,
    string value) {
    return FieldDeclaration(
        VariableDeclaration(IdentifierName(fieldTypeName))
          .WithVariables(
            SingletonSeparatedList(
              VariableDeclarator(constant.Name.EscapeField())
                .WithInitializer(
                  EqualsValueClause(
                    IdentifierName(constant.Constant.TypeIndex.IsSimple ? value : $"({fieldTypeName}){value}")
                  )))))
      .WithModifiers(PubConst);
  }

  private static PropertyDeclarationSyntax CreateStaticField(CsRegularStaticField regularStaticField,
    string fieldTypeName) {
    return PropertyDeclaration(IdentifierName(fieldTypeName), Identifier(regularStaticField.Name.EscapeField()))
      .WithModifiers(PubStatic)
      .WithExpressionBody(
        ArrowExpressionClause(
          IdentifierName(
            $"*(({fieldTypeName}*)(mioMemoryAddress + {regularStaticField.RelativeVirtualAddress}))")))
      .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
  }

  private static FieldDeclarationSyntax CreateInstanceFieldSyntax(CsInstanceField field) {
    AttributeListSyntax fieldOffsetAttribute = CreateFieldOffsetAttribute((int)field.Offset);
    return FieldDeclaration(
        VariableDeclaration(IdentifierName(field.FieldType.FullName))
          .AddVariables(VariableDeclarator(field.Name.SanitizeName().EscapeField())))
      .AddAttributeLists(fieldOffsetAttribute)
      .WithModifiers(field.Record.Attributes.Access == MemberAccess.Public ? Pub : Private)
      .WithLeadingTrivia(Comment(
        /*field is PdbTypeBitField bf
          ? $"/// BitField: {bf.Type.Name} (TypeIndex: {bf.Type.TypeIndex}) (Pos:{bf.Offset} Off:{bf.BitOffset} Size:{bf.BitSize})"
          :*/
        $"/// Type: {field.FieldType.FullName} (TypeIndex: {field.Type}) {(field.FieldType is CsUdt { Record.IsForwardReference: true } ? "(Forward Reference)" : "")}"
      ));
  }

  private static FieldDeclarationSyntax CreateBaseTypeFieldSyntax(CsBaseClass baseClass, int? i) {
    AttributeListSyntax fieldOffsetAttribute = CreateFieldOffsetAttribute((int)baseClass.Record.Offset);
    FieldDeclarationSyntax field = FieldDeclaration(
        VariableDeclaration(IdentifierName(baseClass.BaseClass.FullName))
          .AddVariables(VariableDeclarator(i is not null ? $"Base{i + 1}" : "Base")))
      .WithAttributeLists([fieldOffsetAttribute])
      .WithModifiers(Pub);

    return field;
  }

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

  // Have this here as a reference for now, idk if there's any inconsistencies
  private static readonly Dictionary<SimpleTypeKind, string> SimpleTypeNames = new() {
    [SimpleTypeKind.Void] = "void",
    [SimpleTypeKind.NotTranslated] = "<not translated>",
    [SimpleTypeKind.HResult] = nameof(CppHResult), // HRESULT
    [SimpleTypeKind.SignedCharacter] = nameof(CppSignedChar), // signed char
    [SimpleTypeKind.UnsignedCharacter] = nameof(CppUnsignedChar), // unsigned char
    [SimpleTypeKind.NarrowCharacter] = nameof(CppChar), // char
    [SimpleTypeKind.WideCharacter] = nameof(CppWideChar), // wchar_t
    [SimpleTypeKind.Character16] = nameof(CppChar16), // char16_t
    [SimpleTypeKind.Character32] = nameof(CppChar32), // char32_t
    [SimpleTypeKind.SByte] = nameof(CppInt8), // __int8
    [SimpleTypeKind.Byte] = nameof(CppUInt8), // unsigned __int8
    [SimpleTypeKind.Int16Short] = nameof(CppInt16Short), // short
    [SimpleTypeKind.UInt16Short] = nameof(CppUInt16Short), // unsigned short
    [SimpleTypeKind.Int16] = nameof(CppInt16), // __int16
    [SimpleTypeKind.UInt16] = nameof(CppUInt16), // unsigned __int16
    [SimpleTypeKind.Int32Long] = nameof(CppInt32Long), // long
    [SimpleTypeKind.UInt32Long] = nameof(CppUInt32Long), // unsigned long
    [SimpleTypeKind.Int32] = nameof(CppInt32), // int
    [SimpleTypeKind.UInt32] = nameof(CppUInt32), // unsigned
    [SimpleTypeKind.Int64Quad] = nameof(CppInt64Quad), // __int64
    [SimpleTypeKind.UInt64Quad] = nameof(CppUInt64Quad), // unsigned __int64
    [SimpleTypeKind.Int64] = nameof(CppInt64), // __int64
    [SimpleTypeKind.UInt64] = nameof(CppUInt64), // unsigned __int64
    [SimpleTypeKind.Int128Oct] = nameof(CppUInt128), // unsigned __int128
    [SimpleTypeKind.UInt128Oct] = nameof(CppUInt128Oct), // unsigned __int128
    [SimpleTypeKind.Int128] = nameof(CppInt128Oct), // __int128
    [SimpleTypeKind.UInt128] = nameof(CppUInt128Oct), // unsigned __int128
    [SimpleTypeKind.Float16] = "global::System.Single", // __half
    [SimpleTypeKind.Float32] = "float", // float
    [SimpleTypeKind.Float32PartialPrecision] = nameof(CppFloat32PartialPrecision), // float
    [SimpleTypeKind.Float48] = "float48", // __float48
    [SimpleTypeKind.Float64] = "double", // double
    [SimpleTypeKind.Float80] = "float80", // long double
    [SimpleTypeKind.Float128] = "float128", // __float128
    [SimpleTypeKind.Complex32] = "complex32", // _Complex float
    [SimpleTypeKind.Complex64] = "complex64", // _Complex double
    [SimpleTypeKind.Complex80] = "complex80", // _Complex long double
    [SimpleTypeKind.Complex128] = "global::System.Numerics.Complex", // _Complex __float128
    [SimpleTypeKind.Boolean8] = "bool", // bool
    [SimpleTypeKind.Boolean16] = nameof(CppBoolean16), // __bool16
    [SimpleTypeKind.Boolean32] = nameof(CppBoolean32), // __bool32
    [SimpleTypeKind.Boolean64] = nameof(CppBoolean64), // __bool64
  };
}
