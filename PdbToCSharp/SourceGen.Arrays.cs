using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PdbToCSharp.Types;
using SharpPdb.Native.Types;

namespace PdbToCSharp;

public sealed partial class SourceGen {
  private readonly Dictionary<PdbArrayType, string> _inlineArrayTypes = [];
  private readonly Dictionary<string, string> _inlineArrayNames = [];

  /// Many fields are a fixed-size array buffers.
  /// To support these, we create a <see cref="InlineArrayAttribute"/>
  /// struct for every unique occurence of a fixed-size array type.
  private void ProcessInlineArrays() {
    // TODO: Use CsType here
    Log.Warn("Inline Arrays creation not fully moved to CsType yet.");
    var csArrays = CsTypes.OfType<CsArray>();

    HashSet<string> inlineArrayNames = [];
    // Sort arrays so that we process non-array element types first.
    foreach (CsArray arr in csArrays) {
      // TODO: CsArray should create its self-name.
      //  For simple types (byte, etc), to avoid duplicates, and show original intent,
      //  it should be have its original type appended to its name.
      string name = arr.FullName;
      string elementName = arr.ElementType.FullName;
      ulong count = arr.Count;

      // Only create the type if it's not a duplicate
      if (inlineArrayNames.Add(name)) {
        StructDeclarationSyntax member = CreateInlineArraySyntax(arr, name, elementName, count);
        _ns.InlineArrayNs = _ns.InlineArrayNs.AddMember(member);
      }
    }
  }
}
