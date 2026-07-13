using System.CodeDom.Compiler;
using System.Reflection;

namespace PdbToCSharp;

public static class IndentedTextWriterExtensions {
  private static readonly AssemblyName ThisAssemblyName = typeof(SourceGen).Assembly.GetName();
  private static readonly string Version = ThisAssemblyName.Version?.ToString()!;

  extension(IndentedTextWriter writer) {
    public void WriteIf(ReadOnlySpan<char> value, bool condition) {
      if (condition) {
        writer.Write(value);
      }
    }

    public void WriteGeneratedCodeAttribute(bool prependGlobal = false) {
      writer.Write("[");
      writer.WriteIf("global::System.CodeDom.Compiler.", prependGlobal);
      writer.Write("GeneratedCode(\"");
      writer.Write(ThisAssemblyName.Name);
      writer.Write("\", \"");
      writer.Write(Version);
      writer.WriteLine("\")]");
    }

    public void WriteXmlDocText(string text) {
      writer.Write(System.Security.SecurityElement.Escape(text));
    }
    public void WriteXmlDocTextLine(string text) {
      writer.WriteLine(System.Security.SecurityElement.Escape(text));
    }
  }
}
