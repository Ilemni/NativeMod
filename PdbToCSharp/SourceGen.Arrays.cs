using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using PdbToCSharp.Types;

namespace PdbToCSharp;

public sealed partial class SourceGen {
  /// Many fields are a fixed-size array buffers.
  /// To support these, we create a <see cref="InlineArrayAttribute"/>
  /// struct for every unique occurence of a fixed-size array type.
  private void WriteInlineArrays() {
    if (!CsTypes.OfType<CsArray>().Any()) {
      return;
    }

    Log.Step("Writing inline array types");
    IndentedTextWriter writer = _writers.InlineArrayWriter;
    // We are using pointers in many of the inline arrays
    // TODO: since they are not supported, should strongly consider switching these to IntPtr or ulong
    writer.WriteLine("#pragma warning disable CS9184 // Inline array attribute is has unsupported type");

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

      // XmlDoc type info
      writer.Write("/// Inline array: ");
      writer.WriteXmlDocText(elementType.ToString());
      writer.WriteXmlDocText("[");
      writer.WriteXmlDocText(count.ToString());
      writer.WriteXmlDocText("] (TypeIndex ");
      writer.WriteXmlDocText(arr.TypeIndex.ToString());
      writer.WriteXmlDocTextLine(")");

      // GeneratedCode attribute
      writer.WriteGeneratedCodeAttribute();

      // InlineArray attribute
      writer.Write("[System.Runtime.CompilerServices.InlineArray(");
      writer.Write(Math.Max(count, 1));
      writer.WriteLine(")]");

      // Write struct declaration
      writer.Write("public ");
      if (elementType is CsPointerType or CsSimplePointerType) {
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
