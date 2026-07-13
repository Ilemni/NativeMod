using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PdbToCSharp.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PdbToCSharp;

public partial class SourceGen {

  // Use as reference for creating ctor, dtor, and partially the method body
  private static BaseMethodDeclarationSyntax? CreateMethodDeclaration(CsInstanceMethod method, string? name = null) {
    name ??= method.Name;
    MemberFunctionRecord funcRecord = method.MethodRecord;
    bool isConstructor = funcRecord.Options.HasFlag(FunctionOptions.Constructor);
    var args = funcRecord.ArgumentList.As<ArgumentListRecord>(method.PdbFile).Arguments;
    bool hasProc = method.ProcedureInfo is not null;
    ProcedureInfo pInfo = method.ProcedureInfo.GetValueOrDefault();

    // Create parameters list
    List<ParameterSyntax> parameterSyntaxes = [];
    foreach ((int i, CsType argType) in method.ParameterTypes.Index()) {
      string arg = pInfo.GoodSize ? method.Args[i] : $"arg{i + 1}";
      parameterSyntaxes.Add(
        Parameter(Identifier(arg)).WithType(IdentifierName(argType.FullName))
      );
    }

    BaseMethodDeclarationSyntax methodDeclaration;
    if (isConstructor) {
      return null;
      // TODO: Create constructor

      // Constructor with parameter list
      methodDeclaration =
        ConstructorDeclaration(name)
          .WithParameterList(ParameterList(SeparatedList(parameterSyntaxes)))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
    else if (name.Contains('~')) {
      return null;
      // TODO: Maybe create destructor

      // This is a destructor
      methodDeclaration =
        DestructorDeclaration(Identifier(name[1..]))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
    else {
      methodDeclaration =
        // TODO: do NOT use typeIndex.ToString
        MethodDeclaration(IdentifierName(funcRecord.ReturnType.ToString(method.PdbFile).SanitizeName()), name)
          .WithParameterList(ParameterList(SeparatedList(parameterSyntaxes)))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

      // Static method
      if (funcRecord.ThisType is { IsSimple: true, SimpleKind: SimpleTypeKind.Void }) {
        // methodDeclaration = methodDeclaration
        //   .AddModifiers(StaticKw);
      }
    }

    // TODO: do NOT use typeIndex.ToString, use CsType.ToString
    string typeParams = string.Join(", ", args.Select(a => a.ToString(method.PdbFile).Sanitize()));
    var delegateParams = parameterSyntaxes.Select(p => Argument(IdentifierName(p.Identifier.Text)));
    if (hasProc) {
      string delegateBody =
        $"((delegate* unmanaged<{typeParams}>)(mioMemoryAddress + {method.RelativeVirtualAddress}))";
      methodDeclaration = methodDeclaration
        .WithExpressionBody(ArrowExpressionClause(
          InvocationExpression(IdentifierName(delegateBody))
            .WithArgumentList(ArgumentList(SeparatedList(delegateParams)))
        ));
    }
    else {
      return null;

      // TODO: Should we implement this? Perhaps it should be something like:
      //  => (mioMemoryAddress + (ThisClass.Addresses.ThisMethod ?? throw NIE ))
      //  in case a mod adds an implementation for the missing method

      // emit throw new NotImplementedException();
      methodDeclaration = methodDeclaration
        .WithExpressionBody(ArrowExpressionClause(
            InvocationExpression(
                MemberAccessExpression(
                  SyntaxKind.SimpleMemberAccessExpression,
                  IdentifierName("throw"),
                  IdentifierName("new NotImplementedException")))
              .WithArgumentList(ArgumentList())
          )
        );
    }


    return methodDeclaration;
  }

  // TODO: move this to another file
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
