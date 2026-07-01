using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using MethodKind = SharpPdb.Windows.TypeRecords.MethodKind;

namespace PdbToCSharp;

public static class SourceGen {
  private static readonly AttributeSyntax CompGenAttribute =
    Attribute(IdentifierName("CompilerGenerated"));

  private static readonly AttributeSyntax StructLayoutAttributeSyntax =
    Attribute(IdentifierName("StructLayout(LayoutKind.Explicit)"));


  private static SyntaxList<AttributeListSyntax> ClassAttribute(ulong structSize) => List<AttributeListSyntax>(
  [
    AttributeList(SingletonSeparatedList(CompGenAttribute)),
    AttributeList(SingletonSeparatedList(CreateStructLayoutAttribute(structSize)))
  ]);


  private static AttributeSyntax CreateFieldOffsetAttribute(int offset) {
    return
      Attribute(IdentifierName("FieldOffset"))
        .WithArgumentList(
          AttributeArgumentList(
            SingletonSeparatedList(
              AttributeArgument(
                LiteralExpression(
                  SyntaxKind.NumericLiteralExpression,
                  Literal(offset))))));
  }

  private static AttributeSyntax CreateStructLayoutAttribute(ulong size) {
    return Attribute(IdentifierName("StructLayout"))
      .WithArgumentList(
        AttributeArgumentList(
          SeparatedList<AttributeArgumentSyntax>(
            new SyntaxNodeOrToken[] {
              AttributeArgument(
                MemberAccessExpression(
                  SyntaxKind.SimpleMemberAccessExpression, IdentifierName("LayoutKind"), IdentifierName("Explicit"))),
              Token(SyntaxKind.CommaToken),
              AttributeArgument(
                  LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal((int)size)))
                .WithNameEquals(NameEquals(IdentifierName("Size")))
            }
          )
        )
      );
  }

  private static readonly SyntaxTokenList PublicKeywordMod =
    [Token(SyntaxKind.PublicKeyword)];

  private static readonly SyntaxTokenList TypeKeywordMod =
    [Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.UnsafeKeyword)];


  public static void PdbToCSharp(string path, string namespaceName) {
    PdbFileReader managedPdb = new(path);
    PdbFile pdb = managedPdb.PdbFile;
    ProcedureHelper.Load(pdb);
    var argNames = ProcedureHelper.Names;
    int badArgCount = argNames.Values.Count(v => !v.GoodSize);

    FileScopedNamespaceDeclarationSyntax classNs = FileScopedNamespaceDeclaration(IdentifierName(namespaceName))
        .WithUsings(List([
          UsingDirective(QualifiedName(QualifiedName(IdentifierName("System"), IdentifierName("Runtime")),
            IdentifierName("CompilerServices"))),
          UsingDirective(QualifiedName(QualifiedName(IdentifierName("System"), IdentifierName("Runtime")),
            IdentifierName("InteropServices"))),
          UsingDirective(IdentifierName("ModLoader")).WithStaticKeyword(Token(SyntaxKind.StaticKeyword))
        ]))
        .WithNamespaceKeyword(
          Token(
            TriviaList(
              Comment("// ReSharper disable InvalidXmlDocComment"),
              Comment("// ReSharper disable RedundantUnsafeContext"),
              Comment("// ReSharper disable InconsistentNaming"),
              Comment("// ReSharper disable UnusedType.Global")),
            SyntaxKind.NamespaceKeyword,
            TriviaList()))
      ;

    // ReSharper disable SuggestVarOrType_SimpleTypes
    var enumNs = classNs;
    var unionNs = classNs;
    var templateNs = classNs;
    var templateUnionNs = classNs;
    var stdNs = classNs; // std:: classes
    var imNs = classNs; // ImGui classes
    var hbNs = classNs; // HarfBuzz classes
    var d3dNs = classNs; // Direct3D classes
    var pfNs = classNs; // PlayFab classes
    var fmodNs = classNs; // FMOD classes
    var jsonNs = classNs; // JSON classes
    var internalNs = classNs; // Internal classes
    var cgNs = classNs; // Compiler generated classes
    // ReSharper restore SuggestVarOrType_SimpleTypes

    var tpiRecords = pdb.TpiStream.GetTypeRecords();
    Dictionary<string, int> namespaces = [];

    foreach (TagRecord tagRecord in tpiRecords.OfType<TagRecord>().Where(r => !r.IsForwardReference)) {
      if (tagRecord is EnumRecord enumRecord) {
        enumNs = enumNs.AddMember(CreateType(enumRecord, pdb));
        continue;
      }

      string name = tagRecord.Name.String;
      if (!name.Contains('<') && name.Contains("::")) {
        string toAdd = name[..name.LastIndexOf("::", StringComparison.Ordinal)];
        namespaces.Increment(toAdd);
      }
      bool isClass = tagRecord is ClassRecord;

      if (name.StartsWith('_')) {
        CreateTypeForNs(tagRecord, pdb, ref internalNs);
      }
      else if (name.StartsWith('$')) {
        CreateTypeForNs(tagRecord, pdb, ref cgNs);
      }
      else if (name.Contains('<')) {
        if (isClass) {
          CreateTypeForNs(tagRecord, pdb, ref templateNs);
        }
        else {
          CreateTypeForNs(tagRecord, pdb, ref templateUnionNs);
        }
      }
      else if (MatchName(name, "std::")) {
        CreateTypeForNs(tagRecord, pdb, ref stdNs);
      }
      else if (MatchName(name, "ImGui::") || name.StartsWith("Im") && name.Length > 2 && char.IsUpper(name[2])) {
        CreateTypeForNs(tagRecord, pdb, ref imNs);
      }
      else if (MatchName(name, "hb_")) {
        CreateTypeForNs(tagRecord, pdb, ref hbNs);
      }
      else if (MatchName(name, "DXGI", "D3D", "D2D")) {
        CreateTypeForNs(tagRecord, pdb, ref d3dNs);
      }
      else if (MatchName(name, "PlayFab")) {
        CreateTypeForNs(tagRecord, pdb, ref pfNs);
      }
      else if (MatchName(name, "FMOD")) {
        CreateTypeForNs(tagRecord, pdb, ref fmodNs);
      }
      else if (MatchName(name, "Json")) {
        CreateTypeForNs(tagRecord, pdb, ref jsonNs);
      }
      else if (isClass) {
        CreateTypeForNs(tagRecord, pdb, ref classNs);
      }
      else if (tagRecord is UnionRecord) {
        CreateTypeForNs(tagRecord, pdb, ref unionNs);
      }
    }

    // Write the generated C# code to a file
    string pdbName = Path.GetFileNameWithoutExtension(path);
    Console.WriteLine("Writing generated C# code...");

    // classNs.WriteToFile($"output/{pdbName}_generated_classes.cs");
    // enumNs.WriteToFile($"output/{pdbName}_generated_enums.cs");
    // unionNs.WriteToFile($"output/{pdbName}_generated_unions.cs");
    // templateNs.WriteToFile($"output/{pdbName}_generated_template_classes.cs");
    // templateUnionNs.WriteToFile($"output/{pdbName}_generated_template_union.cs");
    // stdNs.WriteToFile($"output/{pdbName}_generated_std_classes.cs");
    // imNs.WriteToFile($"output/{pdbName}_generated_imgui_classes.cs");
    // hbNs.WriteToFile($"output/{pdbName}_generated_hb_classes.cs");
    // d3dNs.WriteToFile($"output/{pdbName}_generated_d3d_classes.cs");
    // pfNs.WriteToFile($"output/{pdbName}_generated_playfab_classes.cs");
    // fmodNs.WriteToFile($"output/{pdbName}_generated_fmod_classes.cs");
    // jsonNs.WriteToFile($"output/{pdbName}_generated_json_classes.cs");
    // internalNs.WriteToFile($"output/{pdbName}_generated_internal_classes.cs");
    // cgNs.WriteToFile($"output/{pdbName}_generated_cpp_generated_classes.cs");

    var list = namespaces.OrderByDescending(kvp => kvp.Value).ToList();
    var list2 = namespaces.OrderBy(kvp => kvp.Key).ToList();
    return;

    bool MatchName(string toCompare, params ReadOnlySpan<string> args) {
      foreach (string arg in args) {
        if (toCompare.Contains(arg, StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }

      return false;
    }

    void CreateTypeForNs(TagRecord tagRecord, PdbFile p, ref FileScopedNamespaceDeclarationSyntax ns) {
      MemberDeclarationSyntax member = CreateType(tagRecord, p);
      ns = ns.AddMember(member);
    }
  }

  private static MemberDeclarationSyntax CreateType(TagRecord tagRecord, PdbFile pdb) {
    return tagRecord switch {
      ClassRecord classRecord => CreateType(classRecord, pdb),
      UnionRecord unionRecord => CreateType(unionRecord, pdb),
      EnumRecord enumRecord => CreateType(enumRecord, pdb),
      _ => throw new InvalidDataException($"Unexpected tag record kind: {tagRecord.Kind}")
    };
  }

  private static StructDeclarationSyntax CreateType(ClassRecord classRecord, PdbFile pdb) {
    var fields = classRecord.FieldList.As<FieldListRecord>(pdb).Fields;
    var s = fields
      .Where(f => f is not (VirtualFunctionPointerRecord or BaseClassRecord or VirtualBaseClassRecord
        or DataMemberRecord
        or StaticDataMemberRecord or OneMethodRecord or OverloadedMethodRecord or NestedTypeRecord))
      .Select(f => Comment($"// ({f.Kind} | {f.GetType().Name}) {f.ToString(pdb)}")).ToArray();
    if (s.Length > 0) {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"Warning: Class {classRecord.Name.String} has unhandled fields:");
      foreach (SyntaxTrivia field in s) {
        Console.WriteLine(field.ToString());
      }

      Console.ResetColor();
    }

    StructDeclarationSyntax @class = StructDeclaration(classRecord.Name.String.SanitizeName())
      .WithAttributeLists(ClassAttribute(classRecord.Size))
      .WithModifiers(TypeKeywordMod)
      .WithCloseBraceToken(
        Token(
          TriviaList(s),
          SyntaxKind.CloseBraceToken,
          TriviaList()))
      .WithLeadingTrivia(Comment($"/// Struct type: {classRecord.Name.String} ({classRecord.UniqueName})"));

    var baseClassRecords = fields.OfType<BaseClassRecord>();
    // int count = baseClassRecords.Count();
    // if (count != 0) {
    //   if (count > 1) {
    //     Console.ForegroundColor = ConsoleColor.Yellow;
    //     Console.WriteLine(
    //       $"Warning: Class {classRecord.Name.String} has multiple base classes ({string.Join(", ", baseClassRecords.Select(b => b.Type.ToString(pdb)))}). This may not be supported in C#.");
    //     Console.ResetColor();
    //   }
    //
    //   @class = @class.WithBaseList(
    //     BaseList(
    //       SeparatedList<BaseTypeSyntax>(
    //         baseClassRecords.Select(b => SimpleBaseType(IdentifierName(b.Type.ToString(pdb).SanitizeName())))
    //       )
    //     )
    //   );
    // }

    // Static fields
    foreach (StaticDataMemberRecord staticF in fields.OfType<StaticDataMemberRecord>()) {
      FieldDeclarationSyntax field = FieldDeclaration(
          VariableDeclaration(IdentifierName(staticF.Type.ToString(pdb)))
            .AddVariables(VariableDeclarator(staticF.Name.String)))
        .WithModifiers(PublicKeywordMod);
      @class = @class.AddMember(field);
    }

    // Instance fields
    foreach (DataMemberRecord f in fields.OfType<DataMemberRecord>()) {
      FieldDeclarationSyntax field = CreateField(f, pdb);

      @class = @class.AddMember(field);
    }

    // Methods
    foreach (OneMethodRecord m in fields.OfType<OneMethodRecord>()) {
      BaseMethodDeclarationSyntax? method = CreateMethodDeclaration(m, pdb, m.Name.String);
      if (method is not null) {
        @class = @class.AddMember(method);
      }
    }

    // Methods that share the same name (overloaded methods)
    foreach (OverloadedMethodRecord olM in fields.OfType<OverloadedMethodRecord>()) {
      foreach (OneMethodRecord m in olM.MethodList.As<MethodOverloadListRecord>(pdb).Methods) {
        BaseMethodDeclarationSyntax? method = CreateMethodDeclaration(m, pdb, olM.Name.String);
        if (method is not null) {
          @class = @class.AddMember(method);
        }
      }
    }

    // Nested types
    foreach (TagRecord nested in fields
               .OfType<NestedTypeRecord>()
               .Where(n => !n.Type.IsSimple)
               .Select(n => pdb.GetRecord(n.Type) as TagRecord)
               .OfType<TagRecord>()
               .Where(n => !n.IsForwardReference)
            ) {
      if (classRecord.Name.String.Contains('<')) {
        // Skip template classes
        continue;
      }

      MemberDeclarationSyntax memberDeclarationSyntax = CreateType(nested, pdb);
      @class = @class.AddMember(memberDeclarationSyntax);
    }

    return @class;
  }

  private static EnumDeclarationSyntax CreateType(EnumRecord enumRecord, PdbFile pdb) {
    EnumDeclarationSyntax @enum = EnumDeclaration(enumRecord.Name.String.SanitizeName())
      .AddAttributeLists(AttributeList(SingletonSeparatedList(CompGenAttribute)))
      .WithModifiers(PublicKeywordMod)
      .AddBaseListTypes(SimpleBaseType(ParseTypeName(
        enumRecord.UnderlyingType.SimpleTypeName
      )))
      .WithLeadingTrivia(Comment($"/// Struct type: {enumRecord.Name.String} ({enumRecord.UniqueName})"));
    ;

    foreach (EnumeratorRecord enumFieldRecord in enumRecord.FieldList
               .As<FieldListRecord>(pdb).Fields
               .OfType<EnumeratorRecord>()) {
      object? value = enumFieldRecord.Value;
      EnumMemberDeclarationSyntax enumMember = EnumMemberDeclaration(enumFieldRecord.Name.String)
        .WithEqualsValue(EqualsValueClause(LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            value switch {
              int v => Literal(v),
              uint v => Literal(v),
              short v => Literal(v),
              ushort v => Literal(v),
              sbyte v => Literal(v),
              byte v => Literal(v),
              long v => Literal(v),
              ulong v => Literal(v),
              float v => Literal(v),
              double v => Literal(v),
              _ => throw new InvalidDataException(
                $"Unexpected underlying type: {enumRecord.UnderlyingType.SimpleTypeName}")
            }
          )
        ));
      @enum = @enum.AddMember(enumMember);
    }

    return @enum;
  }

  private static StructDeclarationSyntax CreateType(UnionRecord unionRecord, PdbFile pdb) {
    // TODO: proper handling of Union type
    var fields = unionRecord.FieldList.As<FieldListRecord>(pdb).Fields;
    var s = fields
      .Where(f => f is not (VirtualFunctionPointerRecord or BaseClassRecord or VirtualBaseClassRecord
        or DataMemberRecord
        or StaticDataMemberRecord or OneMethodRecord or OverloadedMethodRecord or NestedTypeRecord))
      .Select(f => Comment($"// ({f.Kind} | {f.GetType().Name}) {f.ToString(pdb)}")).ToArray();
    if (s.Length > 0) {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"Warning: Class {unionRecord.Name.String} has unhandled fields:");
      foreach (SyntaxTrivia field in s) {
        Console.WriteLine(field.ToString());
      }

      Console.ResetColor();
    }

    StructDeclarationSyntax @union = StructDeclaration(unionRecord.Name.String.SanitizeName())
      .WithAttributeLists(ClassAttribute(unionRecord.Size))
      .WithModifiers(TypeKeywordMod)
      .WithLeadingTrivia(Comment($"/// Union type: {unionRecord.Name.String} ({unionRecord.UniqueName})"));

    var baseClassRecords = fields.OfType<BaseClassRecord>();
    int baseCount = baseClassRecords.Count();
    if (baseCount != 0) {
      if (baseCount > 1) {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(
          $"Warning: Union class {unionRecord.Name.String} has multiple base classes ({string.Join(", ", baseClassRecords.Select(b => b.Type.ToString(pdb)))}). This may not be supported in C#.");
        Console.ResetColor();
      }

      @union = @union.WithBaseList(
        BaseList(
          SeparatedList<BaseTypeSyntax>(
            baseClassRecords.Select(b => SimpleBaseType(IdentifierName(b.Type.ToString(pdb).SanitizeName())))
          )
        )
      );
    }

    foreach (StaticDataMemberRecord staticF in fields.OfType<StaticDataMemberRecord>()) {
      FieldDeclarationSyntax field = FieldDeclaration(
          VariableDeclaration(IdentifierName(staticF.Type.ToString(pdb)))
            .AddVariables(VariableDeclarator(staticF.Name.String)))
        .WithModifiers(PublicKeywordMod);
      @union = @union.AddMember(field);
    }

    foreach (DataMemberRecord f in fields.OfType<DataMemberRecord>()) {
      FieldDeclarationSyntax field = CreateField(f, pdb);

      @union = @union.AddMember(field);
    }

    foreach (OneMethodRecord m in fields.OfType<OneMethodRecord>()) {
      BaseMethodDeclarationSyntax? method = CreateMethodDeclaration(m, pdb, m.Name.String);
      if (method is not null) {
        @union = @union.AddMember(method);
      }
    }

    foreach (OverloadedMethodRecord olM in fields.OfType<OverloadedMethodRecord>()) {
      foreach (OneMethodRecord m in olM.MethodList.As<MethodOverloadListRecord>(pdb).Methods) {
        BaseMethodDeclarationSyntax? method = CreateMethodDeclaration(m, pdb, olM.Name.String);
        if (method is not null) {
          @union = @union.AddMember(method);
        }
      }
    }

    foreach (TagRecord nested in fields
               .OfType<NestedTypeRecord>()
               .Where(n => !n.Type.IsSimple)
               .Select(n => pdb.GetRecord(n.Type) as TagRecord)
               .OfType<TagRecord>()
               .Where(n => !n.IsForwardReference)
            ) {
      MemberDeclarationSyntax memberDeclarationSyntax = CreateType(nested, pdb);
      @union = @union.AddMember(memberDeclarationSyntax);
    }

    return @union;
  }

  private static FieldDeclarationSyntax CreateField(DataMemberRecord fieldMember, PdbFile pdb) {
    int offset = (int)fieldMember.FieldOffset;
    AttributeSyntax fieldOffsetAttribute = CreateFieldOffsetAttribute(offset);
    FieldDeclarationSyntax field = FieldDeclaration(
        VariableDeclaration(IdentifierName(fieldMember.Type.ToString(pdb).SanitizeName()))
          .AddVariables(VariableDeclarator(fieldMember.Name.String.SanitizeName())))
      .AddAttributeLists(AttributeList(SingletonSeparatedList(fieldOffsetAttribute)))
      .WithModifiers(PublicKeywordMod);
    if (fieldMember.Attributes.MethodKind.HasFlag(MethodKind.Static)) {
      field = field.AddModifiers(Token(SyntaxKind.StaticKeyword));
    }

    return field;
  }

  private static BaseMethodDeclarationSyntax? CreateMethodDeclaration(OneMethodRecord methodRecord, PdbFile pdb,
    string? name = null) {
    name ??= methodRecord.Name.String;
    MemberFunctionRecord funcRecord = methodRecord.Type.As<MemberFunctionRecord>(pdb);
    bool isConstructor = funcRecord.Options.HasFlag(FunctionOptions.Constructor);
    var args = funcRecord.ArgumentList.As<ArgumentListRecord>(pdb).Arguments;
    bool hasProc = ProcedureHelper.Names.TryGetValue(methodRecord.Type, out ProcedureInfo pInfo);


    // Create parameters list
    int i = 0;
    List<ParameterSyntax> parameterSyntaxes = [];
    foreach (TypeIndex typeIndex in args) {
      string arg = pInfo.GoodSize ? pInfo.Args[i].Name : $"arg{i + 1}";
      i++;
      parameterSyntaxes.Add(
        Parameter(Identifier(arg))
          .WithType(IdentifierName(typeIndex.ToString(pdb)))
      );
    }

    BaseMethodDeclarationSyntax methodDeclaration;
    if (isConstructor) {
      return null;

      // Constructor with parameter list
      methodDeclaration =
        ConstructorDeclaration(name)
          .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
          .WithParameterList(ParameterList(SeparatedList(parameterSyntaxes)))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
    else if (name.Contains('~')) {
      return null;

      // This is a destructor
      methodDeclaration =
        DestructorDeclaration(Identifier(name[1..]))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
    else {
      methodDeclaration =
        MethodDeclaration(IdentifierName(funcRecord.ReturnType.ToString(pdb).SanitizeName()), name)
          .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
          .WithParameterList(ParameterList(SeparatedList(parameterSyntaxes)))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

      // Static method
      if (funcRecord.ThisType is { IsSimple: true, SimpleKind: SimpleTypeKind.Void }) {
        methodDeclaration = methodDeclaration
          .AddModifiers(Token(SyntaxKind.StaticKeyword));
      }
    }

    string typeParams = string.Join(", ", args.Select(a => a.ToString(pdb).SanitizeName()));
    var delegateParams = parameterSyntaxes.Select(p => Argument(IdentifierName(p.Identifier.Text)));
    if (hasProc) {
      string delegateBody =
        $"((delegate* unmanaged<{typeParams}>)(ModuleBase + {pInfo.Procedure.Offset}))";
      methodDeclaration = methodDeclaration
        .WithExpressionBody(ArrowExpressionClause(
          InvocationExpression(IdentifierName(delegateBody))
            .WithArgumentList(ArgumentList(SeparatedList(delegateParams)))
        ));
    }
    else {
      return null;

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

  private static readonly MemberDeclarationSyntax[] NsMembers = new MemberDeclarationSyntax[1];
  private static readonly EnumMemberDeclarationSyntax[] EuMembers = new EnumMemberDeclarationSyntax[1];

  extension(string str) {
    private string Sanitize() {
      return str
        .Replace("`anonymous-namespace'::", "")
        .Replace("::", "__")
        .Replace('-', '_')
        .Replace('<', '_')
        .Replace(" >", "_")
        .Replace("> ", "_")
        .Replace('>', '_')
        .Replace("&&", "*")
        .Replace("&", "*")
        .Replace("**", "*")
        // .Replace("*", "_ptr")
        // .Replace("&", "_ref")
        .Replace(' ', '_')
        .Replace(',', '_')
        .Replace('(', '_')
        .Replace(')', '_')
        .Replace('[', '_')
        .Replace(']', '_')
        .Replace('`', '_')
        .Replace('\'', '_');
    }

    private string SanitizeName() {
      bool endsInPtr = str.EndsWith('*') || str.EndsWith('&');
      string result;
      if (endsInPtr) {
        result = str[..^1]
            .Sanitize()
            .Replace("~", "_dtor")
            .Replace("*", "_ptr")
            .Replace("&", "_ref")
          + '*';
      }
      else {
        result = str
          .Sanitize()
          .Replace("~", "_dtor")
          .Replace("*", "_ptr")
          .Replace("&", "_ref");
      }

      if (result == "String_ptr") {
        ;
      }

      return result;
    }
  }

  extension(FileScopedNamespaceDeclarationSyntax ns) {
    private void WriteToFile(string filePath) {
      using StreamWriter writer = new(filePath);
      ns.NormalizeWhitespace().WriteTo(writer);
    }
  }


  // ReSharper disable SuggestVarOrType_SimpleTypes
  extension(FileScopedNamespaceDeclarationSyntax ns) {
    public FileScopedNamespaceDeclarationSyntax AddMember(MemberDeclarationSyntax member) {
      NsMembers[0] = member;
      ns = ns.AddMembers(NsMembers);
      NsMembers[0] = null!;
      return ns;
    }
  }

  extension(StructDeclarationSyntax structDecl) {
    public StructDeclarationSyntax AddMember(MemberDeclarationSyntax member) {
      NsMembers[0] = member;
      structDecl = structDecl.AddMembers(NsMembers);
      NsMembers[0] = null!;
      return structDecl;
    }
  }

  extension(EnumDeclarationSyntax enumDecl) {
    public EnumDeclarationSyntax AddMember(EnumMemberDeclarationSyntax member) {
      EuMembers[0] = member;
      enumDecl = enumDecl.AddMembers(EuMembers);
      EuMembers[0] = null!;
      return enumDecl;
    }
    // ReSharper restore SuggestVarOrType_SimpleTypes
  }
}
