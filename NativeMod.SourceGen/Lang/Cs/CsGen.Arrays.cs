using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed partial class CsGen {
  /// Many fields are a fixed-size array buffers.
  /// To support these in C#, we create a <see cref="InlineArrayAttribute"/>
  /// struct for every unique occurence of a fixed-size array type.
  private void WriteInlineArrays() {
    if (!Types.OfType<CsArray>().Any()) {
      return;
    }

    Log.Info("Writing inline array types");
    IndentedTextWriter writer = Writers.InlineArrayWriter;
    // We are using pointers in many of the inline arrays

    HashSet<string> duplicates = [];
    // Sort arrays so that we process non-array element types first.
    foreach (CsArray arr in Types.OfType<CsArray>()) {
      WriteArray(arr, writer, duplicates);
    }
  }

  private static void WriteArray(CsArray arr, IndentedTextWriter writer, HashSet<string> duplicates) {
    if (arr.ElementType is CsArray innerArray && !duplicates.Contains(innerArray.FullName)) {
      // Write the inner array first
      WriteArray(innerArray, writer, duplicates);
    }


    // Only create the type if it's not a duplicate
    if (!duplicates.Add(arr.FullName)) {
      // Log.Warn($"Duplicate inline array type: {arr.FullName}");
      return;
    }

    WriteArray(arr, writer);
  }

  private static void WriteArray(CsArray arr, IndentedTextWriter writer) {
    CsType elementType = arr.ElementType.Unwrap();
    bool isPtr = elementType is CsPointerType or CsSimplePointerType;

    XmlDocs.Types.WriteArray(writer, arr);
    writer.WriteGeneratedCodeAttribute();

    // InlineArray attribute
    writer.Write("[global::System.Runtime.CompilerServices.InlineArray(");
    writer.Write(Math.Max(arr.Count, 1));
    writer.WriteLine(")]");

    // Write struct declaration
    writer.WriteMany("public struct ", arr.SelfName);

    // Write struct body
    if (!isPtr) {
      using (writer.BracedScope()) {
        writer.WriteManyLine("private ", elementType.GlobalQualifiedName, " _element0;");
      }

      return;
    }

    using (writer.BracedScope()) {
      ulong ptrSize = (elementType as CsPointerType)?.Size ?? (elementType as CsSimplePointerType)!.Size;
      string ptrTypeName = ptrSize switch {
        4 => "uint",
        8 => "ulong",
        _ => throw new InvalidOperationException($"Unsupported pointer size: {ptrSize}")
      };

      string elementTypeName = elementType.GlobalQualifiedName;
      writer.WriteManyLine("private ", ptrTypeName, " _element0;");
      writer.WriteLine("/// Gets the element at <paramref name=\"index\"/>, cast to the correct pointer.");
      writer.WriteManyLine("public unsafe ", elementTypeName, " GetPtr(int index)");
      writer.WriteManyLine(" => (", elementTypeName, ")this[index];");
    }

    writer.WriteLine();
  }
}
