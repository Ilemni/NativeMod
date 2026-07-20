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

    HashSet<string> inlineArrayNames = [];
    // Sort arrays so that we process non-array element types first.
    foreach (CsArray arr in CsTypes.OfType<CsArray>()) {
      WriteArray(arr, writer, inlineArrayNames);
    }
  }

  private static void WriteArray(CsArray arr, IndentedTextWriter writer, HashSet<string> inlineArrayNames) {
    if (arr.ElementType is CsArray innerArray && !inlineArrayNames.Contains(innerArray.FullName)) {
      // Write the inner array first
      WriteArray(innerArray, writer, inlineArrayNames);
    }

    string name = arr.FullName;
    ulong count = arr.Count;

    CsType elementType = arr.ElementType;
    bool isPtr = elementType is CsPointerType or CsSimplePointerType;
    string elementTypeName = isPtr
      ? "ulong"
      : elementType.FullyQualifiedName;

    // Only create the type if it's not a duplicate
    if (!inlineArrayNames.Add(name)) {
      // Log.Warn($"Duplicate inline array type: {name}");
      return;
    }

    // XmlDoc type info
    writer.Write("/// Inline array: ");
    writer.WriteXmlDocText(elementType.FullName);
    writer.WriteXmlDocText("[");
    writer.WriteXmlDocText(count.ToString());
    writer.WriteXmlDocText("] (TypeIndex ");
    writer.WriteXmlDocText(arr.TypeIndex.ToString());
    writer.WriteXmlDocTextLine(")");
    CsType inner = arr.InnerElement;
    if (inner is CsPointerType or CsSimplePointerType) {
      // Write notice that this must be cast to pointer
      if (isPtr) {
        writer.Write("/// NOTE! The element type ");
        writer.WriteXmlDocText(elementTypeName);
      }
      else {
        writer.Write("/// NOTE! The inner element type");
      }
      writer.Write(" must be cast to a pointer of type ");
      if (inner.Namespace is { } ns) {
        writer.WriteXmlDocText(ns);
        writer.Write('.');
      }
      writer.WriteXmlDocTextLine(inner.FullName);
    }

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
    writer.Write(elementTypeName);
    writer.WriteLine(" _element0;");
    writer.Indent--;
    writer.WriteLine('}');
    writer.WriteLine();
  }
}
