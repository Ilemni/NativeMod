using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPdb.Native.Types;

namespace PdbToCSharp;

internal sealed class Namespaces {
  private record struct Item(FileScopedNamespaceDeclarationSyntax Ns, string OutputName) {
    public FileScopedNamespaceDeclarationSyntax Ns = Ns;
  }

  public Namespaces(string namespaceName) {
    _classNs = new Item(SourceGen.CreateNamespaceSyntax(namespaceName), "GeneratedClasses.cs");
    _arrayNs = _classNs with { OutputName = "GeneratedArrayTypes.cs" };
    _enumNs = _classNs with { OutputName = "GeneratedEnums.cs" };
    _unionNs = _classNs with { OutputName = "GeneratedUnions.cs" };
    _templateNs = _classNs with { OutputName = "GeneratedTemplateClasses.cs" };
    _templateUnionNs = _classNs with { OutputName = "GeneratedTemplateUnion.cs" };
    _stdNs = _classNs with { OutputName = "GeneratedStdClasses.cs" };
    _imNs = _classNs with { OutputName = "GeneratedImguiClasses.cs" };
    _hbNs = _classNs with { OutputName = "GeneratedHbClasses.cs" };
    _d3DNs = _classNs with { OutputName = "GeneratedD3dClasses.cs" };
    _pfNs = _classNs with { OutputName = "GeneratedPlayfabClasses.cs" };
    _fmodNs = _classNs with { OutputName = "GeneratedFmodClasses.cs" };
    _jsonNs = _classNs with { OutputName = "GeneratedJsonClasses.cs" };
    _internalNs = _classNs with { OutputName = "GeneratedInternalClasses.cs" };
    _cgNs = _classNs with { OutputName = "GeneratedCppGeneratedClasses.cs" };
  }

  private Item _classNs;
  private Item _arrayNs; // Inline arrays
  private Item _enumNs; // Enums
  private Item _unionNs; // Unions
  private Item _templateNs; // Template classes
  private Item _templateUnionNs; // Template unions
  private Item _stdNs; // std:: classes
  private Item _imNs; // ImGui classes
  private Item _hbNs; // HarfBuzz classes
  private Item _d3DNs; // Direct3D classes
  private Item _pfNs; // PlayFab classes
  private Item _fmodNs; // FMOD classes
  private Item _jsonNs; // JSON classes
  private Item _internalNs; // Internal classes
  private Item _cgNs;

  public ref FileScopedNamespaceDeclarationSyntax InlineArrayNs => ref _arrayNs.Ns;
  public ref FileScopedNamespaceDeclarationSyntax EnumNs => ref _enumNs.Ns;

  public ref FileScopedNamespaceDeclarationSyntax GetMatching(PdbUserDefinedType udt) {
    if (udt is PdbEnumType) {
      return ref _enumNs.Ns;
    }

    string name = udt.Name;
    if (name.StartsWith('_')) {
      return ref _internalNs.Ns;
    }

    if (name.StartsWith('$')) {
      return ref _cgNs.Ns;
    }

    if (name.Contains('<')) {
      if (udt is PdbUnionType) {
        return ref _templateUnionNs.Ns;
      }

      return ref _templateNs.Ns;
    }

    if (MatchName(name, "std::")) {
      return ref _stdNs.Ns;
    }

    if (MatchName(name, "ImGui::") || name.StartsWith("Im") && name.Length > 2 && char.IsUpper(name[2])) {
      return ref _imNs.Ns;
    }

    if (MatchName(name, "hb_")) {
      return ref _hbNs.Ns;
    }

    if (MatchName(name, "DXGI", "D3D", "D2D")) {
      return ref _d3DNs.Ns;
    }

    if (MatchName(name, "PlayFab")) {
      return ref _pfNs.Ns;
    }

    if (MatchName(name, "FMOD")) {
      return ref _fmodNs.Ns;
    }

    if (MatchName(name, "Json")) {
      return ref _jsonNs.Ns;
    }

    if (udt is PdbUnionType) {
      return ref _unionNs.Ns;
    }

    return ref _classNs.Ns;
  }

  public void WriteAllToFiles(string outputPath) {
    Item[] arr = [
      _arrayNs,
      _classNs,
      _enumNs,
      _unionNs,
      _templateNs,
      _templateUnionNs,
      _stdNs,
      _imNs,
      _hbNs,
      _d3DNs,
      _pfNs,
      _fmodNs,
      _jsonNs,
      _internalNs,
      _cgNs
    ];
    Parallel.ForEach(arr, item => {
      item.Ns.WriteToFile(Path.Join(outputPath, item.OutputName));
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
