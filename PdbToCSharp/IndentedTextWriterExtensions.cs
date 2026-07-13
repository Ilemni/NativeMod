using System.CodeDom.Compiler;

namespace PdbToCSharp;

public static class IndentedTextWriterExtensions {
  extension(IndentedTextWriter writer) {
    public void WriteIf(ReadOnlySpan<char> value, bool condition) {
      if (condition) {
        writer.Write(value);
      }
    }
  }
}
