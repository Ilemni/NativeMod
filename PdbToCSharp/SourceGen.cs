using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using PdbUdt = SharpPdb.Native.Types.PdbUserDefinedType;
using FsNamespaceDecl = Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax;
using MethodDecl = Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PdbToCSharp;

public sealed partial class SourceGen(string path, string namespaceName) : IDisposable {
  private readonly PdbFileReader _pdb = new(path);
  private PdbFile PdbFile => _pdb.PdbFile;
  private readonly Namespaces _ns = new(namespaceName);

  /// Filtered array of User Defined Types, excluding forward references, scoped types, and compiler generated types.
  private PdbUdt[] _udts = null!;

  private readonly Dictionary<string, PdbUdt> _addedClasses = [];
  private readonly Dictionary<string, PdbEnumType> _addedEnums = [];

  /// Types which only have a forward reference.
  private readonly HashSet<string> _missingTypes = [];

  /// Types which are nested but lack a parent type.
  private readonly HashSet<PdbUdt> _orphanedNestedTypes = [];

  public void PdbToCSharp(string outputPath) {
    Log.Step("Processing PDB...");
    Process();

    Log.Step("Writing generated C# code...");
    _ns.WriteAllToFiles(outputPath);
    Log.Step("Done.");
  }

  private void Process() {
    PreProcess();
    Log.Step("Creating inline array types...");
    ProcessInlineArrays();
    // DebugPdb();

    Log.Step("Creating all other types...");
    foreach (PdbUdt udt in _udts.Where(u => !u.IsNested)) {
      string name = GetQualifiedName(udt);
      if (udt is PdbEnumType enumType) {
        if (_addedEnums.TryGetValue(name, out PdbEnumType? existingEnum)) {
          if (existingEnum.UniqueName == enumType.UniqueName && !string.IsNullOrEmpty(enumType.UniqueName)) {
            continue;
          }

          Log.Warn(
            $"Duplicate enum name {name} for {enumType.Name} and {existingEnum.Name}. Skipping {enumType.Name}");
        }

        ref FsNamespaceDecl enumNs = ref _ns.EnumNs;
        enumNs = enumNs.AddMember(CreateEnum(enumType));
        _addedEnums[name] = enumType;
        continue;
      }

      if (!_addedClasses.TryAdd(name, udt)) {
        // Log.Info($"Skipping duplicate class {genName} for {udt.Name}");
        continue;
      }

      ref FsNamespaceDecl nsToAdd = ref _ns.GetMatching(udt);
      CreateTypeForNs(udt, ref nsToAdd);
    }
  }

  [Conditional("DEBUG")]
  [SuppressMessage("ReSharper", "CollectionNeverQueried.Local")]
  [SuppressMessage("ReSharper", "UnusedVariable")]
  // Random linq methods to poke at things
  private void DebugPdb() {
    // Debug enum scopes
    List<PdbEnumType> scopedEnums = [];
    List<PdbEnumType> unscopedEnums = [];
    foreach (PdbEnumType pdbEnumType in _udts.OfType<PdbEnumType>().Where(e => !e.Name.Contains('<'))) {
      (pdbEnumType.IsScoped ? scopedEnums : unscopedEnums).Add(pdbEnumType);
    }

    // Debug pointer fields
    var pointerFields = new Dictionary<PdbUdt, PdbUdt[]>();
    foreach (PdbUdt udt in _udts.Where(u => !u.IsScoped)) {
      var scopedFields = udt.Fields
        .Select(f => f.Type)
        .Select(f =>
          f as PdbUdt ??
          (f as PdbPointerType)?.ElementType as PdbUdt)
        .Where(u => u is { IsScoped: true }).ToArray();
      if (scopedFields.Length > 0) {
        pointerFields[udt] = scopedFields!;
      }
    }

    // Debug pointer depth
    var pDict = new Dictionary<int, List<PdbTypeField>>();
    foreach (var fields in _udts.Select(u => u.Fields)) {
      foreach (PdbTypeField f in fields) {
        if (f.Type is PdbPointerType pointer && GetPointerDepth(pointer) is > 0 and var pointerDepth) {
          if (!pDict.TryGetValue(pointerDepth, out var l)) {
            l = [];
            pDict[pointerDepth] = l;
          }

          l.Add(f);
        }
      }
    }

    // Debug pdb namespaces
    Dictionary<string, int> namespaces = [];
    foreach (PdbUdt udt in _udts.Where(u => !u.IsNested)) {
      string name = udt.Name;
      if (!name.Contains('<') && name.Contains("::")) {
        string toAdd = name[..name.LastIndexOf("::", StringComparison.Ordinal)];
        namespaces.Increment(toAdd);
      }
    }

    var list = namespaces.OrderByDescending(kvp => kvp.Value).ToArray();
    var list2 = namespaces.OrderBy(kvp => kvp.Key).ToArray();


    var fromRecords = _pdb.AsRecordEnumerable()
      .OfType<FieldListRecord>()
      .Select(f => f.Fields
        .OfType<NestedTypeRecord>()
        .Select(n => (n, _pdb.TryGetType<PdbUdt>(n.Type) is { IsNested: true } u ? u : null))
        .Where(r => r.Item2 is not null)
        .OfType<(NestedTypeRecord, PdbUdt)>()
        .ToArray())
      .Where(r => r.Length > 0)
      .ToArray();

    var unnamedNestedTypes = fromRecords
      .SelectMany(r => r)
      .Where(r => r.Item1.Name.String.Contains('<'))
      .OrderBy(r => r.Item1.Name.String)
      .ToArray();

    var fromManaged = _pdb.UDTs
      .Where(u => u.ContainsNestedClass)
      .Select(u => (u, u.NestedTypes.ToArray()))
      .Where(t => t.Item2.Length > 0)
      .OrderBy(t => t.u.Name)
      .ToArray();

    var bosses = _udts.Where(u => u.BaseClasses.Any(b => b.BaseType.Name.Contains("Boss"))).ToArray();

    var virtuals = _udts
      .Where(u => !u.Name.StartsWith("std::") && u.VirtualBaseClasses.Any())
      .Select(u => (u, u.FieldRecords))
      .Where(u => u.FieldRecords.Any(f => f.Kind == TypeLeafKind.LF_VFUNCTAB))
      .Select(u => (u.u, u.FieldRecords, u.FieldRecords
        .OfType<VirtualFunctionPointerRecord>()
        .Select(v =>
          PdbFile.GetRecord(PdbFile.GetRecord<PointerRecord>(v.Type).ReferentType))
        .ToArray()))
      .ToArray();
    var virtualBCs = _udts
      .Where(u => !u.Name.StartsWith("std::") && u.VirtualBaseClasses.Any())
      .Select(u => (u, u.FieldRecords))
      .ToArray();

    var scoped = _pdb.UDTs.Where(u =>
      !u.TagRecord.IsForwardReference &&
      u.IsScoped &&
      u is not PdbEnumType &&
      !u.Name.Contains("unnamed struct at") &&
      !u.Name.Contains("`lambda at") &&
      !u.Name.Contains("<lambda_")
    ).ToArray();
  }

  private void CreateTypeForNs(PdbUdt udt, ref FsNamespaceDecl ns) {
    string name = GetSelfName(udt);
    MemberDeclarationSyntax member = CreateType(udt, name);
    ns = ns.AddMember(member);
  }


  private void PreProcess() {
    // Collect all types which we're interested in generating, and create their names.
    _udts = _pdb.UDTs.Where(u =>
      !u.TagRecord.IsForwardReference &&
      !u.IsScoped &&
      !u.Name.Contains("unnamed struct at") &&
      !u.Name.Contains("`lambda at") &&
      !u.Name.Contains("<lambda_")
    ).ToArray();

    var nestedTypes = _pdb.UDTs
      .Where(u => u.ContainsNestedClass)
      .SelectMany(p => p.NestedTypes.Select(n => (n, p)));
    var unnamedNestedTypes = nestedTypes
      .Select(a => (p: a.p.Name, n: a.n.Item1.Name.String))
      .Where(n => n.n.Contains('<'))
      .OrderBy(n => n.n)
      .ToArray();
    // foreach ((NestedTypeRecord record, PdbUdt nestedUdt) in nestedTypes) {
    //   string name = record.Name.String;
    //   if (name.Contains('<') || name.Contains("unnamed struct at") || name.Contains("`lambda at")) {
    //     continue;
    //   }
    // }

    // Create names for all top-level types first, so that nested types can use the parent name as a prefix.
    Log.Step("Creating initial names for all types...");
    foreach (PdbUdt udt in _pdb.UDTs.Where(u =>
               !u.TagRecord.IsForwardReference &&
               !u.Name.Contains("unnamed struct at") &&
               !u.Name.Contains("`lambda at") &&
               !u.Name.Contains("lambda_")
             )) {
      GetOrCreateTypeName(udt);
    }

    Log.Step("Creating qualified names for all types...");
    QualifyAllNames();

    var enumNames = _fullNames.Select(kvp => (_pdb.TryGetType<PdbEnumType>(kvp.Key), kvp.Value))
      .Where(t => t.Item1 is not null)
      .ToArray();

    // Collect types which have forward-references but lack actual bodies
    Log.Step("Looking for missing types (only forward-referenced)...");
    var forwardRefs = _pdb.UDTs.Where(u => u.TagRecord.IsForwardReference).ToArray();
    var nonForwardRefs = _pdb.UDTs.Where(u => !u.TagRecord.IsForwardReference).ToArray();
    _missingTypes.UnionWith(
      forwardRefs.Where(u =>
          nonForwardRefs.All(uu => uu.UniqueName != u.UniqueName))
        .Select(u => u.Name));
  }

  /// We try to qualify all names so that we can support creating nested structs.
  /// A struct must refer to itself when defining itself by its own name (i.e. Tag)
  /// while all other types must refer to it by its full name (i.e. Outer.Inner.Tag)
  /// Fortunately a struct can refer to itself with the fully qualified name.
  private void QualifyAllNames() {
    foreach (PdbEnumType enumType in _pdb.UDTs.OfType<PdbEnumType>().Where(e => !e.IsNested)) {
      _typeNames[enumType] = CreateEnumTypeName(enumType);
    }

    Dictionary<PdbUdt, (PdbUdt parent, string name)> typesToQualify = [];
    foreach (PdbUdt parent in _pdb.UDTs.Where(u => u.ContainsNestedClass)) {
      foreach ((NestedTypeRecord n, PdbUdt nested) in parent.NestedTypes) {
        string name = n.Name.String.SanitizeName();
        typesToQualify[nested] = (parent, name);
        _typeNames[nested] = name;
      }
    }

    // List is populated before creating names, to ensure we have full parent chains for all nested types.
    foreach (var kvp in typesToQualify) {
      string qualifiedName = QualifyName(kvp.Key);
      AddQualifiedName(kvp.Key, qualifiedName);
    }

    // Find nested types which are missing a parent type
    foreach (PdbUdt nested in _udts.Where(u => u.IsNested)) {
      if (!HasQualifiedName(nested)) {
        _orphanedNestedTypes.Add(nested);
      }
    }

    // Add top-level name which has no nesting.
    foreach ((PdbType key, string value) in _typeNames) {
      if (key is PdbUdt udt) {
        TryAddSelfName(udt, value);
        TryAddQualifiedName(udt, value);
      }
    }

    if (_orphanedNestedTypes.Count > 0) {
      Log.Warn($"Found {_orphanedNestedTypes.Count} orphaned nested types.\n    "
        + string.Join("\n    ", _orphanedNestedTypes.Select(n => $"\"{n.Name}\"").Order(StringComparer.Ordinal)));
    }

    var a = _fullNames.Where(kvp => kvp.Value.Contains("Watcher"));
    var b = _selfNames.Where(kvp => kvp.Value.Contains("Watcher"));

    return;

    string QualifyName(PdbUdt type) {
      if (!type.IsNested) {
        return _typeNames[type];
      }

      if (typesToQualify.TryGetValue(type, out (PdbUdt parent, string name) parentInfo)) {
        PdbUdt parent = parentInfo.parent;
        string parentName = QualifyName(parent);
        return $"{parentName}.{parentInfo.name}";
      }

      if (_fullNames.TryGetValue(type.TypeIndex, out string? fullName)) {
        return fullName;
      }

      if (_fullNames.FirstOrDefault(kvp => ((PdbUdt)_pdb.GetType(kvp.Key)).UniqueName == type.UniqueName).Value is
          { } found) {
        return found;
      }

      string name =
        typesToQualify.TryGetValue(type, out (PdbUdt _, string name) res)
          ? res.name
          : typesToQualify.First(q => q.Key.UniqueName == type.UniqueName).Value.name;

      return name;
    }
  }

  private MemberDeclarationSyntax CreateType(PdbUdt udt, string? name = null) {
    if (udt.TagRecord.IsForwardReference) {
      throw new ArgumentException($"Cannot create type for forward reference: {udt.Name}");
    }

    return udt switch {
      PdbClassType or PdbUnionType => CreateStruct(udt, name),
      PdbEnumType enumType => CreateEnum(enumType, name),
      _ => throw new InvalidDataException($"Unexpected tag record kind: {udt.GetType().Name}, name: {udt.Name}")
    };
  }

  private StructDeclarationSyntax CreateStruct(PdbUdt udtType, string? name = null) {
    name ??= GetOrCreateTypeName(udtType);
    StructDeclarationSyntax csClass = CreateStructSyntax(udtType, name);

    int baseClassesCount = udtType.BaseClasses.Count;
    for (int i = 0; i < baseClassesCount; i++) {
      PdbTypeBaseClass baseClass = udtType.BaseClasses[i];
      if (baseClass.Access == MemberAccess.Private) {
        continue;
      }

      string baseName = GetOrCreateTypeName(baseClass.BaseType);
      FieldDeclarationSyntax field = CreateBaseTypeFieldSyntax(baseClass, baseName, baseClassesCount > 1 ? i : null);
      csClass = csClass.AddMember(field);
    }

    // Static fields
    foreach (PdbTypeStaticField staticF in udtType.StaticFields) {
      string fieldTypeName = GetQualifiedName(staticF.Type);
      switch (staticF) {
        case PdbTypeConstant constant:
          string value = fieldTypeName == "bool"
            ? (ushort)constant.Value > 0 ? "true" : "false"
            : constant.Value.ToString()!;

          // Support for: const ulong MyConst = unchecked((ulong)-1)
          if (constant.Value is sbyte and < 0 && fieldTypeName == "ulong") {
            value = $"unchecked((ulong){constant.Value})";
          }

          FieldDeclarationSyntax field = CreateConstFieldSyntax(constant, fieldTypeName, value);
          csClass = csClass.AddMember(field);
          continue;
        case PdbTypeRegularStaticField staticField:
          PropertyDeclarationSyntax prop = CreateStaticField(staticField, fieldTypeName);
          csClass = csClass.AddMember(prop);
          continue;
      }
    }

    // Instance fields
    foreach (PdbTypeField f in udtType.Fields) {
      // TODO: Generate properties that can handle get and set to bit fields.
      if (f is PdbTypeBitField) {
        continue;
      }

      // TODO: Handle this better, maybe by creating an empty placeholder type for the missing type.
      //  Some fields are a pointer to a type, which the PDB may only have as a forward reference.
      if (_missingTypes.Contains(f.Type.Name) ||
          f.Type is PdbPointerType p && _missingTypes.Contains(p.ElementType.Name)) {
        continue;
      }

      string fieldName = GetQualifiedName(f.Type);
      FieldDeclarationSyntax field = CreateInstanceFieldSyntax(f, fieldName);
      csClass = csClass.AddMember(field);
    }

    // Methods
    // PdbFileReader doesn't handle methods, so we do it manually
    var pdbFieldList = udtType.FieldRecords;
    foreach (OneMethodRecord m in pdbFieldList.OfType<OneMethodRecord>()) {
      break;

      MethodDecl? method = CreateMethodDeclaration(m, m.Name.String);
      if (method is not null) {
        csClass = csClass.AddMember(method);
      }
    }

    // Methods that share the same name (overloaded methods)
    foreach (OverloadedMethodRecord olM in pdbFieldList.OfType<OverloadedMethodRecord>()) {
      break;

      foreach (OneMethodRecord m in olM.MethodList.As<MethodOverloadListRecord>(udtType.Pdb.PdbFile).Methods) {
        MethodDecl? method = CreateMethodDeclaration(m, olM.Name.String);
        if (method is not null) {
          csClass = csClass.AddMember(method);
        }
      }
    }

    // Nested types
    if (udtType.ContainsNestedClass) {
      foreach (NestedTypeRecord nested in pdbFieldList
                 .OfType<NestedTypeRecord>()
                 .Where(n => !n.Type.IsSimple)
              ) {
        // Must check that IsNested is false.
        // For example, Array<String> may have a nested struct String, which refers to the top-level String class.
        if (_pdb.GetType(nested.Type) is not PdbUdt { IsNested: true } nestedUdt) {
          continue;
        }

        string fulName = GetQualifiedName(nestedUdt);

        continue;

        if (nested.Name.String.Contains('<')) {
          // Skip template classes
          continue;
        }

        KeyValuePair<TypeIndex, PdbUdt> testUdt = default;

        if (nestedUdt is not { TagRecord.IsForwardReference: false }) {
          foreach (PdbType pdbType in _pdb.AsEnumerable()) {
            if (pdbType is PdbUdt { TagRecord.IsForwardReference: false } udt &&
                udt.UniqueName == nested.Name.String) {
              nestedUdt = udt;
              break;
            }
          }
        }

        if (nestedUdt is not null) {
          if (nestedUdt.Name.Contains('<')) {
            // Skip template classes
            continue;
          }

          if (nestedUdt.TypeIndex == udtType.TypeIndex) {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
              $"Warning: Skipping nested type {nestedUdt.Name} in {udtType.Name} because it is the same type as the parent.");
            Console.ResetColor();
            continue;
          }

          MemberDeclarationSyntax memberDeclarationSyntax = CreateType(nestedUdt);
          csClass = csClass.AddMember(memberDeclarationSyntax);
        }
      }
    }

    return csClass;
  }

  private EnumDeclarationSyntax CreateEnum(PdbEnumType enumType, string? name = null) {
    name ??= GetOrCreateTypeName(enumType);

    string underlying = GetOrCreateTypeName(enumType.UnderlyingType);
    if (underlying == "bool") {
      underlying = "byte";
    }

    if (enumType.Values.Any(v => v.Value is uint and > int.MaxValue)) {
      underlying = "uint";
    }

    EnumDeclarationSyntax csEnum = CreateEnumSyntax(enumType, name);
    if (underlying != "int") {
      csEnum = csEnum.AddBaseListTypes(SimpleBaseType(ParseTypeName(underlying)));
    }

    foreach (PdbEnumeratorValue enumValue in enumType.Values) {
      EnumMemberDeclarationSyntax enumMember = CreateEnumMemberSyntax(enumValue);
      csEnum = csEnum.AddMembers(enumMember);
    }

    return csEnum;
  }

  private MethodDecl? CreateMethodDeclaration(OneMethodRecord methodRecord, string? name = null) {
    name ??= methodRecord.Name.String;
    MemberFunctionRecord funcRecord = methodRecord.Type.As<MemberFunctionRecord>(PdbFile);
    bool isConstructor = funcRecord.Options.HasFlag(FunctionOptions.Constructor);
    var args = funcRecord.ArgumentList.As<ArgumentListRecord>(PdbFile).Arguments;
    bool hasProc = ProcedureHelper.Names.TryGetValue(methodRecord.Type, out ProcedureInfo pInfo);

    // Create parameters list
    int i = 0;
    List<ParameterSyntax> parameterSyntaxes = [];
    foreach (TypeIndex typeIndex in args) {
      string arg = pInfo.GoodSize ? pInfo.Args[i].Name : $"arg{i + 1}";
      i++;
      parameterSyntaxes.Add(
        Parameter(Identifier(arg))
          // TODO: do NOT use typeIndex.ToString
          .WithType(IdentifierName(typeIndex.ToString(PdbFile)))
      );
    }

    MethodDecl methodDeclaration;
    if (isConstructor) {
      return null;

      // Constructor with parameter list
      methodDeclaration =
        ConstructorDeclaration(name)
          .WithModifiers(TokenList(PubKw))
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
        // TODO: do NOT use typeIndex.ToString
        MethodDeclaration(IdentifierName(funcRecord.ReturnType.ToString(PdbFile).SanitizeName()), name)
          .WithModifiers(TokenList(PubKw))
          .WithParameterList(ParameterList(SeparatedList(parameterSyntaxes)))
          .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

      // Static method
      if (funcRecord.ThisType is { IsSimple: true, SimpleKind: SimpleTypeKind.Void }) {
        methodDeclaration = methodDeclaration
          .AddModifiers(StaticKw);
      }
    }

    // TODO: do NOT use typeIndex.ToString
    string typeParams = string.Join(", ", args.Select(a => a.ToString(PdbFile).Sanitize()));
    var delegateParams = parameterSyntaxes.Select(p => Argument(IdentifierName(p.Identifier.Text)));
    if (hasProc) {
      string delegateBody =
        $"((delegate* unmanaged<{typeParams}>)(mioMemoryAddress + {pInfo.Procedure.Offset}))";
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

  private bool TryResolveType(PdbType orig, [NotNullWhen(true)] out PdbType? resolved) {
    resolved = null;
    if (orig is not PdbUdt { TagRecord.IsForwardReference: true } udt) {
      resolved = null;
      return false;
    }

    resolved = _pdb.UserDefinedTypes
      .OfType<PdbUdt>()
      .LastOrDefault(r => !r.TagRecord.IsForwardReference && r.UniqueName == udt.TagRecord.UniqueName.String);
    return resolved is not null;
  }

  private static int GetPointerDepth(PdbPointerType type) => GetPointerDepthAndElement(type, out _);

  private static int GetPointerDepthAndElement(PdbPointerType type, out PdbType element) {
    element = type;
    int depth = 0;
    while (element is PdbPointerType pointer) {
      depth++;
      element = pointer.ElementType;
    }

    return depth;
  }

  public void Dispose() {
    _pdb.Dispose();
  }
}
