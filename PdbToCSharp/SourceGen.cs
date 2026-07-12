using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PdbToCSharp.Dissect;
using PdbToCSharp.ThirdParty;
using PdbToCSharp.Types;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;
using FsNamespaceDecl = Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax;
using MethodDecl = Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace PdbToCSharp;

public sealed partial class SourceGen : IDisposable {
  // TODO: Actually use the root namespace.
  /// Root namespace for all generated code.
  public readonly string Namespace;

  public SourceGen(string path, string namespaceName) {
    Namespace = namespaceName;
    Pdb = new PdbFileReader(path);
    _ns = new Namespaces(namespaceName);
    CsTypes = new CsType[Pdb.PdbFile.TpiStream.TypeRecordCount];
  }

  internal readonly Dictionary<TypeIndex, CsType> CsSimpleTypes = [];
  internal readonly CsType?[] CsTypes;
  internal readonly Dictionary<TypeIndex, CsUdt> CsUdts = [];

  // Debug inspect to see which CsTypes are not mapped to records
  private IEnumerable<(CsType? Cs, TypeRecord Pdb)> CsPdbTypePairs =>
    CsTypes.Select((cs, i) => (cs, Pdb.PdbFile.GetRecord(TypeIndex.FromArrayIndex(i))));

  // Debug inspect to see which CsTypes do get mapped to records
  private IEnumerable<(CsType Cs, TypeRecord Pdb)> CsNotNullPairs =>
    CsPdbTypePairs.Where(p => p.Cs is not null).Cast<(CsType Cs, TypeRecord Pdb)>();


  internal readonly PdbFileReader Pdb;
  private PdbFile PdbFile => Pdb.PdbFile;
  private readonly Namespaces _ns;

  internal TypeRecord[] Records = null!;
  private (TagRecord tag, TypeIndex index)[] _tagRecords = null!;

  private readonly Dictionary<string, Dictionary<string, CsUdt>> _addedClassesByNamespace = [];
  private readonly Dictionary<string, Dictionary<string, CsEnum>> _addedEnumsByNamespace = [];

  /// Types which only have a forward reference.
  private readonly HashSet<string> _missingTypes = [];

  /// Types which are nested but lack a parent type.
  private readonly HashSet<CsUdt> _orphanedNestedTypes = [];

  public void PdbToCSharp(string outputPath) {
    Log.Step("Processing PDB");
    Process();

    Log.Step("Writing generated C# code");
    _ns.WriteAllToFiles(outputPath);
    Log.Step("Done.");
  }

  private void Process() {
    PreProcess();
    Log.Step("Creating inline array types");
    ProcessInlineArrays();
    // DebugPdb();

    Log.Step("Creating all other types... ");
    int total = CsUdts.Values.Count(u => u.Parent is null);
    int i = 0;
    HashSet<TypeIndex> created = [];
    using ProgressBar progressBar = new();
    foreach (CsUdt udt in CsUdts.Values.Where(u => u.Parent is null)) {
      progressBar.Report((double)++i / total);
      if (!created.Add(udt.TypeIndex)) {
        // TODO: allow declaration of empty forward references
        // Was a forward reference
        continue;
      }

      string name = udt.FullName;
      if (udt is CsEnum csEnum) {
        if (!_addedEnumsByNamespace.TryGetValue(name, out var nsEDict)) {
          nsEDict = [];
          _addedEnumsByNamespace[name] = nsEDict;
        }
        else {
          if (!nsEDict.TryAdd(name, csEnum)) {
            // Log.Warn($"Duplicate enum name \"{name}\" in namespace \"{csEnum.Namespace}\".");
            continue;
          }
        }

        ref FsNamespaceDecl enumNs = ref _ns.EnumNs;
        enumNs = enumNs.AddMember(CreateEnum(csEnum));
        continue;
      }

      if (!_addedClassesByNamespace.TryGetValue(name, out var nsDict)) {
        nsDict = [];
        _addedClassesByNamespace[name] = nsDict;
      }
      else {
        if (!nsDict.TryAdd(name, udt)) {
          // Log.Warn($"Duplicate class name \"{name}\" in namespace \"{udt.Namespace}\".");
          continue;
        }
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
    // Debug inspect InlineArray types
    var arrays = CsTypes.OfType<CsArray>().OrderBy(a => a.ToString()).ToArray();

    // Debug inspect a complex CsTypes
    CsUdt? shiiBoss = CsUdts.Values.FirstOrDefault(u => u.SelfName == "Shii_boss");

    // Debug enum scopes
    List<CsEnum> scopedEnums = [];
    List<CsEnum> unscopedEnums = [];
    foreach (CsEnum csEnum in CsUdts.Values.OfType<CsEnum>().Where(e => !e.Record.Name.String.Contains('<'))) {
      (csEnum.Record.Options.HasFlag(ClassOptions.Scoped) ? scopedEnums : unscopedEnums).Add(csEnum);
    }

    // Debug pointer fields
    var pointerFields = new Dictionary<CsStructure, CsUdt[]>();
    foreach (CsStructure udt in CsUdts.Values.OfType<CsStructure>().Where(u => !u.Record.Options.HasFlag(ClassOptions.Scoped))) {
      var scopedFields = udt.InstanceFields
        .Select(f => f.FieldType)
        .Select(f =>
          f as CsUdt ??
          (f as CsPointerType)?.ElementType as CsUdt)
        .OfType<CsUdt>()
        .Where(u => u.Record.Options.HasFlag(ClassOptions.Scoped)).ToArray();
      if (scopedFields.Length > 0) {
        pointerFields[udt] = scopedFields;
      }
    }

    // Debug pointer depth
    var pDict = new Dictionary<int, List<CsInstanceField>>();
    foreach (var fields in CsUdts.Values.OfType<CsStructure>().Select(u => u.InstanceFields)) {
      foreach (CsInstanceField f in fields) {
        if (f.FieldType is CsPointerType pointer && GetPointerDepth(pointer) is > 0 and var pointerDepth) {
          if (!pDict.TryGetValue(pointerDepth, out var l)) {
            l = [];
            pDict[pointerDepth] = l;
          }

          l.Add(f);
        }
      }
    }


    var nestedCsTypesFromRecords = Pdb.AsRecordEnumerable()
      .OfType<FieldListRecord>()
      .Select(f => f.Fields
        .OfType<NestedTypeRecord>()
        .Select(n => (n, CsUdts.TryGetValue(n.Type, out CsUdt? udt) && udt.Record.IsNested ? udt : null))
        .Where(r => r.Item2 is not null)
        .OfType<(NestedTypeRecord, CsUdt)>()
        .ToArray())
      .Where(r => r.Length > 0)
      .ToArray();
  }

  private void CreateTypeForNs(CsUdt udt, ref FsNamespaceDecl ns) {
    string name = udt.SelfName;
    MemberDeclarationSyntax member = CreateType(udt);
    ns = ns.AddMember(member);
  }


  private void PreProcess() {
    // Lets us get argument names for the methods, which are not available in the TPI stream.
    Log.Step("Loading procedure info");
    ProcedureHelper.Load(Pdb);
    Records = PdbFile.TpiStream.GetTypeRecords();

    Log.Step("Collecting tag records");
    _tagRecords = Records
      .Index()
      .Where(r => r.Item is TagRecord tag && AllowedName(tag.Name.String))
      .Select(r => ((TagRecord)r.Item, TypeIndex.FromArrayIndex(r.Index)))
      .ToArray();

    Log.Step("Creating non-forward reference types");
    Parallel.ForEach(_tagRecords.Where(r => !r.tag.IsForwardReference),
      iter => { CsType.GetOrCreate(this, iter.index); });

    Log.Step("Resolving forward references");
    Parallel.ForEach(_tagRecords.Index().Where(r => r.Item.tag.IsForwardReference), iter => {
      (TagRecord tag, TypeIndex i) = iter.Item;

      ResolveForwardReference(tag, iter.Index, out TagRecord? resolved, out TypeIndex resolvedIndex);
      CsType csType = resolved is null ? CsType.GetOrCreate(this, i) : CsTypes[resolvedIndex.ArrayIndex]!;
      CsTypes[i.ArrayIndex] = csType;
    });

    foreach ((int i, CsUdt udt) in CsTypes.Index().Where(r => r.Item is CsUdt).Select(r => (r.Index, (CsUdt)r.Item!))) {
      CsUdts[udt.TypeIndex] = udt;
      if (i != udt.TypeIndex.ArrayIndex) {
        CsUdts[TypeIndex.FromArrayIndex(i)] = udt;
      }
    }

    Log.Step("Finding parents for all nested types");
    var nestedIter = CsUdts.Values
      .Where(p => p.Record.Options.HasFlag(ClassOptions.ContainsNestedClass))
      .Select(p => (parent: p, p.Record.GetFields(Pdb).OfType<NestedTypeRecord>()));

    Parallel.ForEach(nestedIter, iter => {
      foreach (NestedTypeRecord nested in iter.Item2) {
        if (CsUdts.TryGetValue(nested.Type, out CsUdt? nestedCs)) {
          nestedCs.SetParent(iter.parent, nested);
        }
      }
    });

    // Force loading of lazy-loaded props
    foreach (CsUdt csUdt in CsUdts.Values) {
      _ = csUdt.FullName;
      if (csUdt is CsStructure csStruct) {
        _ = csStruct.BaseClasses;
        _ = csStruct.InstanceMethods;
        var fields = csStruct.InstanceFields;
        foreach (CsInstanceField f in fields) {
          _ = f.FieldType;
        }
      }
    }
  }

  private bool ResolveForwardReference(TagRecord tag, int start, [NotNullWhen(true)] out TagRecord? resolved,
    out TypeIndex index) {
    // Try resolving forward first
    (resolved, index) = _tagRecords
      .Skip(start)
      .FirstOrDefault(r => !r.tag.IsForwardReference &&
        r.tag.Name.String == tag.Name.String &&
        r.tag.UniqueName.String == tag.UniqueName.String);

    if (resolved is null) {
      // Much less common, some types may resolve backwards
      (resolved, index) = _tagRecords
        .Take(start)
        .LastOrDefault(r => !r.tag.IsForwardReference &&
          r.tag.Name.String == tag.Name.String &&
          r.tag.UniqueName.String == tag.UniqueName.String);
    }

    return resolved is not null;
  }

  private bool AllowedName(string name) {
    return !name.Contains("unnamed struct at") &&
      !name.Contains("`lambda at") && !name.Contains("<lambda_");
  }

  private MemberDeclarationSyntax CreateType(CsUdt udt) {
    if (udt.Record.IsForwardReference) {
      // TODO: Create an empty struct for these. A forward reference here means there is NOT a fully defined type for this.
      // Log.Warn($"Skipping forward-reference-only type: {udt.FullName}");
      // throw new ArgumentException($"Cannot create type for forward reference: {udt.FullName}");
    }

    return udt switch {
      CsStructure structure => CreateStruct(structure),
      CsEnum enumType => CreateEnum(enumType),
      _ => throw new InvalidDataException($"Unexpected tag record kind: {udt.GetType().Name}, name: {udt.FullName}")
    };
  }

  private StructDeclarationSyntax CreateStruct(CsStructure csStruct) {
    StructDeclarationSyntax csClass = CreateStructSyntax(csStruct);
    if (csStruct.AllFields.Count == 0) {
      return csClass;
    }

    int baseClassesCount = csStruct.BaseClasses.Length;
    for (int i = 0; i < baseClassesCount; i++) {
      CsBaseClass baseClass = csStruct.BaseClasses[i];
      if (baseClass.Record.Attributes.Access != MemberAccess.Private) {
        FieldDeclarationSyntax field = CreateBaseTypeFieldSyntax(baseClass, baseClassesCount > 1 ? i : null);
        csClass = csClass.AddMember(field);
      }
    }

    // Static fields
    foreach (CsStaticField staticF in csStruct.StaticFields) {
      string fieldTypeName = staticF.FieldType.FullName;
      switch (staticF) {
        case CsConstantField constant:
          string value = fieldTypeName == "bool"
            ? (ushort)constant.Constant.Value > 0 ? "true" : "false"
            : constant.Constant.Value.ToString()!;

          // Support for: const ulong MyConst = unchecked((ulong)-1)
          if (constant.Constant.Value is sbyte and < 0 && fieldTypeName == "ulong") {
            value = $"unchecked((ulong){constant.Constant.Value})";
          }

          FieldDeclarationSyntax field = CreateConstFieldSyntax(constant, fieldTypeName, value);
          csClass = csClass.AddMember(field);
          continue;
        case CsRegularStaticField staticField:
          PropertyDeclarationSyntax prop = CreateStaticField(staticField, fieldTypeName);
          csClass = csClass.AddMember(prop);
          continue;
      }
    }

    // Instance fields
    foreach (CsInstanceField f in csStruct.InstanceFields) {
      // TODO: Generate properties that can handle get and set to bit fields.
      if (f is CsBitField) {
        continue;
      }

      // TODO: Handle this better, maybe by creating an empty placeholder type for the missing type.
      //  Some fields are a pointer to a type, which the PDB may only have as a forward reference.
      if (_missingTypes.Contains(f.FieldType.FullName) ||
          f.FieldType is CsPointerType p && _missingTypes.Contains(p.ElementType.FullName)) {
        continue;
      }

      FieldDeclarationSyntax field = CreateInstanceFieldSyntax(f);
      csClass = csClass.AddMember(field);
    }

    // Methods
    foreach (CsInstanceMethod m in csStruct.InstanceMethods) {
      MethodDecl? method = CreateMethodDeclaration(m);
      if (method is not null) {
        csClass = csClass.AddMember(method);
      }
    }

    // Nested types
    foreach (CsStructure nested in csStruct.NestedClasses) {
      if (nested.FullName.Contains('<')) {
        // Log.Warn($"Skipping nested type {nested.FullName} in {csStruct.FullName} because it is a template type.");
        continue;
      }

      if (nested.Record.IsForwardReference) {
        // Log.Warn($"Skipping nested forward reference type: {nested.FullName} in {csStruct.FullName}.");
        continue;
        // throw new InvalidOperationException($"Cannot create nested type for forward reference: {nested.FullName}");
      }

      if (nested.TypeIndex == csStruct.TypeIndex) {
        // Log.Warn(
        //   $"Skipping nested type {nested.FullName} in {csStruct.FullName} because it is the same type as the parent.");
        continue;
      }

      MemberDeclarationSyntax memberDeclarationSyntax = CreateType(nested);
      csClass = csClass.AddMember(memberDeclarationSyntax);
    }

    return csClass;
  }

  private static EnumDeclarationSyntax CreateEnum(CsEnum csEnum) {
    string underlying = csEnum.Underlying.FullName;
    if (underlying == "bool") {
      underlying = "byte";
    }

    if (csEnum.Values.Any(v => v.Value is uint and > int.MaxValue)) {
      underlying = "uint";
    }

    EnumDeclarationSyntax enumSyntax = CreateEnumSyntax(csEnum);
    if (underlying != "int") {
      enumSyntax = enumSyntax.AddBaseListTypes(SimpleBaseType(ParseTypeName(underlying)));
    }

    foreach (CsEnumField enumValue in csEnum.Values) {
      EnumMemberDeclarationSyntax enumMember = CreateEnumMemberSyntax(enumValue);
      enumSyntax = enumSyntax.AddMembers(enumMember);
    }

    return enumSyntax;
  }

  private MethodDecl? CreateMethodDeclaration(CsInstanceMethod method, string? name = null) {
    name ??= method.Name;
    MemberFunctionRecord funcRecord = method.MethodRecord;
    bool isConstructor = funcRecord.Options.HasFlag(FunctionOptions.Constructor);
    var args = funcRecord.ArgumentList.As<ArgumentListRecord>(PdbFile).Arguments;
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

    MethodDecl methodDeclaration;
    if (isConstructor) {
      return null;
      // TODO: Create constructor

      // Constructor with parameter list
      methodDeclaration =
        ConstructorDeclaration(name)
          .WithModifiers(TokenList(PubKw))
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

    // TODO: do NOT use typeIndex.ToString, use CsType.ToString
    string typeParams = string.Join(", ", args.Select(a => a.ToString(PdbFile).Sanitize()));
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

  private bool TryResolveType(PdbType orig, [NotNullWhen(true)] out PdbType? resolved) {
    resolved = null;
    if (orig is not PdbUserDefinedType { TagRecord.IsForwardReference: true } udt) {
      resolved = null;
      return false;
    }

    resolved = Pdb.UserDefinedTypes
      .OfType<PdbUserDefinedType>()
      .LastOrDefault(r => !r.TagRecord.IsForwardReference && r.UniqueName == udt.TagRecord.UniqueName.String);
    return resolved is not null;
  }

  private static int GetPointerDepth(CsPointerType type) => GetPointerDepthAndElement(type, out _);

  private static int GetPointerDepthAndElement(CsPointerType type, out CsType element) {
    element = type;
    int depth = 0;
    while (element is CsPointerType pointer) {
      depth++;
      element = pointer.ElementType;
    }

    return depth;
  }

  public void Dispose() {
    Pdb.Dispose();
  }

  // You must use CallingConvention.Cdecl
  [DllImport("MyNativeLibrary.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
  public static extern void NativeLog(int level, __arglist);
}
