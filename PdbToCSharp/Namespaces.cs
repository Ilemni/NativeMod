using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PdbToCSharp.Types;

namespace PdbToCSharp;

internal sealed class Namespaces {
  private record struct Item(FileScopedNamespaceDeclarationSyntax Ns, string OutputName) {
    public FileScopedNamespaceDeclarationSyntax Ns = Ns;
  }

  public Namespaces(string namespaceName) {
    _namespaceName = namespaceName;
    _baseNs = SourceGen.CreateNamespaceSyntax(namespaceName);
    _rootClassNamespace = new Item(_baseNs, "Generated__Root_Classes.cs");
    _arrayNs = _rootClassNamespace with { OutputName = "Generated__Root_InlineArrays.cs" };
    _rootEnumNamespace = _rootClassNamespace with { OutputName = "Generated__Root_Enums.cs" };
    _rootUnionNamespace = _rootClassNamespace with { OutputName = "Generated__Root_Unions.cs" };
    _rootTemplateClassNamespace = _rootClassNamespace with { OutputName = "Generated__Root_TemplateClasses.cs" };
    _rootTemplateUnionNamespace = _rootClassNamespace with { OutputName = "Generated__Root_TemplateUnions.cs" };
  }

  private readonly string _namespaceName;

  private readonly FileScopedNamespaceDeclarationSyntax _baseNs;

  private readonly Dictionary<string, Item> _namespaceMap = [];
  private Item _rootClassNamespace;
  private Item _rootEnumNamespace;
  private Item _rootUnionNamespace;
  private Item _rootTemplateClassNamespace;
  private Item _rootTemplateUnionNamespace;
  private Item _arrayNs; // Inline arrays

  public ref FileScopedNamespaceDeclarationSyntax InlineArrayNs => ref _arrayNs.Ns;
  public ref FileScopedNamespaceDeclarationSyntax EnumNs => ref _rootEnumNamespace.Ns;

  public ref FileScopedNamespaceDeclarationSyntax GetMatching(CsUdt udt) {
    string? csNamespace = udt.Namespace;
    if (csNamespace is not null) {
      if (!_namespaceMap.TryGetValue(csNamespace, out Item item)) {
        item = new Item(_baseNs.WithName(SyntaxFactory.ParseName(_namespaceName + '.' + csNamespace)), $"Generated_{csNamespace.Replace('.', '_')}.cs");
        _namespaceMap[csNamespace] = item;
      }

      return ref CollectionsMarshal.GetValueRefOrAddDefault(_namespaceMap, csNamespace, out bool _).Ns;
    }

    string origName = udt.Record.Name.String;
    if (udt is CsEnum) {
      return ref EnumNs;
    }

    // TODO: Replace all of the above fields with a dictionary.
    //  Probably not needed, if we eventually just Parallel.ForEach over things and write them to their own files.
    //  In that case, this type will be deleted.
    string? name = udt.Namespace;
    if (name is null) {
      if (udt is CsUnion) {
        return ref _rootUnionNamespace.Ns;
      }

      return ref _rootClassNamespace.Ns;
    }

    if (origName.Contains('<')) {
      if (udt is CsUnion) {
        return ref _rootTemplateUnionNamespace.Ns;
      }

      return ref _rootTemplateClassNamespace.Ns;
    }

    if (udt is CsUnion) {
      return ref _rootUnionNamespace.Ns;
    }

    return ref _rootClassNamespace.Ns;
  }

  public void WriteAllToFiles(string outputPath) {
    var arr = _namespaceMap.Values
      .Append(_arrayNs)
      .Append(_rootClassNamespace)
      .Append(_rootEnumNamespace)
      .Append(_rootUnionNamespace)
      .Append(_rootTemplateClassNamespace)
      .Append(_rootTemplateUnionNamespace);
    Parallel.ForEach(arr, item => {
      if (item.Ns.Members.Any()) {
        item.Ns.WriteToFile(Path.Join(outputPath, item.OutputName));
      }
    });
  }

  private static bool MatchName(string toCompare, params ReadOnlySpan<string> args) {
    foreach (string arg in args) {
      if (toCompare.Contains(arg, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }
}
