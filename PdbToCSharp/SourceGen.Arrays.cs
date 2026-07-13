using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using PdbToCSharp.Types;

namespace PdbToCSharp;

public sealed partial class SourceGen {
  /// Many fields are a fixed-size array buffers.
  /// To support these, we create a <see cref="InlineArrayAttribute"/>
  /// struct for every unique occurence of a fixed-size array type.
  private void WriteInlineArrays() {
    HashSet<string> inlineArrayNames = [];
    // Sort arrays so that we process non-array element types first.
    foreach (CsArray arr in CsTypes.OfType<CsArray>()) {
      string name = arr.FullName;
      ulong count = arr.Count;

      CsType elementType = arr.ElementType;
      while (elementType is CsArray innerArray) {
        elementType = innerArray.ElementType;
      }

      // Only create the type if it's not a duplicate
      if (!inlineArrayNames.Add(name)) {
        // Log.Warn($"Duplicate inline array type: {name}");
        continue;
      }

      IndentedTextWriter writer = _ns.InlineArrayWriter;
      // XmlDoc type info
      writer.Write("// Inline array: ");
      writer.Write(elementType.ToString());
      writer.Write('[');
      writer.Write(count);
      writer.Write("] (TypeIndex ");
      writer.Write(arr.TypeIndex);
      writer.WriteLine(")");

      // GeneratedCode attribute
      WriteGeneratedCodeAttribute(writer);

      // InlineArray attribute
      writer.Write("[InlineArray(");
      writer.Write(count);
      writer.WriteLine(")]");

      // Write struct declaration
      writer.Write("public ");
      if (elementType is CsPointerType) {
        writer.Write("unsafe ");
      }
      writer.Write("struct ");
      writer.Write(name);

      // Write struct body
      writer.WriteLine(" {");
      writer.Indent++;
      writer.Write("private ");
      writer.Write(elementType.FullyQualifiedName);
      writer.WriteLine(" _element0;");
      writer.Indent--;
      writer.WriteLine('}');
      writer.WriteLine();
    }
  }
}
