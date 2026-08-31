using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using NativeMod.SourceGen.Lang.Cs;

namespace NativeMod.SourceGen;

public static class TextWriterExtensions {
  private static readonly AssemblyName ThisAssemblyName = typeof(Program).Assembly.GetName();
  private static readonly string Version = ThisAssemblyName.Version?.ToString()!;

  extension(TextWriter writer) {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParam((CsType type, string name) param) {
      writer.Write(param.type.GlobalQualifiedName);
      writer.Write(' ');
      writer.Write(param.name);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParamIf((CsType type, string name) param, bool condition) {
      if (condition) {
        writer.Write(param.type.GlobalQualifiedName);
        writer.Write(' ');
        writer.Write(param.name);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParam((CsType type, string name) param, ref bool needsComma) {
      writer.WriteCommaIfNeeded(ref needsComma);
      writer.Write(param.type.GlobalQualifiedName);
      writer.Write(' ');
      writer.Write(param.name);
      needsComma = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParamIf((CsType type, string name) param, bool condition, ref bool needsComma) {
      if (condition) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(param.type.GlobalQualifiedName);
        writer.Write(' ');
        writer.Write(param.name);
        needsComma = true;
      }
    }

    public void WriteParameterTypes(ReadOnlySpan<CsType> parameters, Func<CsType, string>? getName = null) {
      bool needsComma = false;
      writer.WriteParameterTypes(parameters, ref needsComma, getName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterTypes(ReadOnlySpan<CsType> parameterTypes, ref bool needsComma, Func<CsType, string>? getName = null) {
      foreach (CsType type in parameterTypes) {
        if (type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(getName?.Invoke(type) ?? type.GlobalQualifiedName);
        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCppParameterTypes(ReadOnlySpan<CsType> parameterTypes, ref bool needsComma) {
      foreach (CsType type in parameterTypes) {
        if (type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(type.Marshaller?.CppType ?? type.GlobalQualifiedName);
        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterNames(ReadOnlySpan<(CsType, string)> parameters, ref bool needsComma) {
      foreach ((CsType type, string name) in parameters) {
        if (type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(name);
        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterNamesToCpp(ReadOnlySpan<(CsType, string)> parameters, ref bool needsComma) {
      foreach ((CsType type, string arg) in parameters) {
        if (type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        type.WriteToCpp(writer, arg);
        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterNamesFromCpp(ReadOnlySpan<(CsType, string)> parameters, ref bool needsComma) {
      foreach ((CsType type, string arg) in parameters) {
        if (type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        type.WriteFromCpp(writer, arg);

        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterTypesAndNames(ReadOnlySpan<(CsType, string)> parameters) {
      bool needsComma = false;
      writer.WriteParameterTypesAndNames(parameters, ref needsComma);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteParameterTypesAndNames(ReadOnlySpan<(CsType, string)> parameters, ref bool needsComma) {
      foreach ((CsType type, string) param in parameters) {
        if (param.type.IsVariadic) {
          break;
        }

        writer.WriteParam(param, ref needsComma);
      }
    }

    public void WriteParameterTypesAndNamesFromCpp(ReadOnlySpan<(CsType, string)> parameters) {
      bool needsComma = false;
      writer.WriteParameterTypesAndNamesFromCpp(parameters, ref needsComma);
    }

    public void WriteParameterTypesAndNamesFromCpp(ReadOnlySpan<(CsType, string)> parameters, ref bool needsComma) {
      foreach ((CsType type, string name) param in parameters) {
        if (param.type.IsVariadic) {
          break;
        }

        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(param.type.Marshaller?.CppType ?? param.type.GlobalQualifiedName);
        writer.Write(' ');
        writer.Write(param.name);

        needsComma = true;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIf(ReadOnlySpan<char> value, bool condition) {
      if (condition) {
        writer.Write(value);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteManyIf(ReadOnlySpan<string> value, bool condition) {
      if (condition) {
        writer.WriteMany(value);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteKvpIf<T>(T value, bool condition, [CallerMemberName] string? key = null) {
      if (condition) {
        writer.Write(key);
        writer.Write(" = ");
        writer.Write(value?.ToString());
        writer.WriteLine(',');
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteKvpIf<T>(string key, T value, bool condition) {
      if (condition) {
        writer.Write(key);
        writer.Write(" = ");
        writer.Write(value?.ToString());
        writer.WriteLine(',');
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteKvpHexIf(string key, ulong value, bool condition) {
      if (condition) {
        writer.Write(key);
        writer.Write(" = ");
        writer.Write(value.ToString("X"));
        writer.WriteLine(',');
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFlagIfHasFlag<T>(T value, T flag, string? prefix = null, string? postfix = null) where T : Enum {
      if (value.HasFlag(flag)) {
        writer.Write(prefix);
        writer.Write(typeof(T).GetEnumName(flag));
        writer.Write(postfix);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFlagIfHasFlag<T>(T value, T flag, char prefix = '\0', char suffix = '\0') where T : Enum {
      if (!value.HasFlag(flag)) {
        return;
      }

      if (prefix is not '\0') {
        writer.Write(prefix);
      }

      writer.Write(typeof(T).GetEnumName(flag));
      if (suffix is not '\0') {
        writer.Write(suffix);
      }
    }

    /// <summary>
    /// Writes the specified value to the writer if the condition is true.
    /// If needsComma is true, a comma and space are written before the value.
    /// After writing, needsComma is set to true.
    /// </summary>
    /// <param name="value">
    /// The value to write if the condition is true.</param>
    /// <param name="condition">
    /// The condition to evaluate.
    /// This method is a no-op if this value is <see langword="false"/>.
    /// </param>
    /// <param name="needsComma">
    /// Indicates whether a comma is needed before writing the value.
    /// <br />This value is set to true if <paramref name="condition"/> is true and the value is written.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIf(ReadOnlySpan<char> value, bool condition, ref bool needsComma) {
      if (!condition) {
        return;
      }

      if (needsComma) {
        writer.Write(", ");
      }

      writer.Write(value);
      needsComma = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIf<T>(Action<TextWriter, T> action, T arg, bool condition) {
      if (condition) {
        action(writer, arg);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLineIf(bool condition) {
      if (condition) {
        writer.WriteLine();
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLineIf(ReadOnlySpan<char> value, bool condition) {
      if (condition) {
        writer.WriteLine(value);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteManyLineIf(ReadOnlySpan<string> value, bool condition) {
      if (condition) {
        writer.WriteManyLine(value);
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteCommaIfNeeded(ref bool needsComma) {
      if (needsComma) {
        writer.Write(", ");
        needsComma = false;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2) {
      writer.Write(s1);
      writer.Write(s2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3, string s4) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
      writer.Write(s4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3, string s4, string s5) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
      writer.Write(s4);
      writer.Write(s5);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3, string s4, string s5, string s6) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
      writer.Write(s4);
      writer.Write(s5);
      writer.Write(s6);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3, string s4, string s5, string s6, string s7) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
      writer.Write(s4);
      writer.Write(s5);
      writer.Write(s6);
      writer.Write(s7);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(string s1, string s2, string s3, string s4, string s5, string s6, string s7, string s8) {
      writer.Write(s1);
      writer.Write(s2);
      writer.Write(s3);
      writer.Write(s4);
      writer.Write(s5);
      writer.Write(s6);
      writer.Write(s7);
      writer.Write(s8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteMany(params ReadOnlySpan<string> values) {
      foreach (ReadOnlySpan<char> value in values) {
        writer.Write(value);
      }
    }

    /// <summary>
    /// Writes a series of strings, then calls <see cref="IndentedTextWriter.WriteLine()"/> at the end.
    /// </summary>
    /// <param name="values"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteManyLine(params ReadOnlySpan<string> values) {
      foreach (ReadOnlySpan<char> value in values) {
        writer.Write(value);
      }

      writer.WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGeneratedCodeAttribute(bool prependGlobal = false, bool newLine = true) {
      writer.Write("[");
      writer.WriteIf("global::System.CodeDom.Compiler.", prependGlobal);
      writer.Write("GeneratedCode(\"");
      writer.Write(ThisAssemblyName.Name);
      writer.Write("\", \"");
      writer.Write(Version);
      if (newLine) {
        writer.WriteLine("\")]");
      }
      else {
        writer.Write("\")]");
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructLayoutAttribute(ulong size, bool prependGlobal = false, bool newLine = true) {
      writer.Write("[");
      writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
      writer.Write("StructLayout(");
      writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
      writer.Write("LayoutKind.Explicit");
      if (size > 0) {
        writer.Write(", Size = ");
        writer.Write(size);
      }

      if (newLine) {
        writer.WriteLine(")]");
      }
      else {
        writer.Write(")]");
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFieldOffsetAttribute(ulong offset, bool prependGlobal = false, bool hex = false) {
      writer.Write("[");
      writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
      writer.Write("FieldOffset(");
      if (hex) {
        writer.Write("0x");
        writer.Write(offset.ToString("X"));
      } else {
        writer.Write(offset);
      }
      writer.Write(")]");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteXmlDocText(string text) {
      writer.Write(System.Security.SecurityElement.Escape(text));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteXmlDocTextLine(string text) {
      writer.WriteLine(System.Security.SecurityElement.Escape(text));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteXmlDocLinebreak() {
      writer.Write("<br/>");
    }

    [MustDisposeResource]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RegionScope Region(string regionName) => new(writer, regionName);

    [MustDisposeResource]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RegionScope Region(ReadOnlySpan<string> regionName) => new(writer, regionName);
  }

  extension(IndentedTextWriter writer) {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteIf<T>(Action<IndentedTextWriter, T> action, T arg, bool condition) {
      if (condition) {
        action(writer, arg);
      }
    }

    [MustDisposeResource]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SimpleIndent WithIndent() => new(writer);

    [MustDisposeResource]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BracedIndent BracedScope(bool newLine = true) => new(writer, newLine);
  }

  public readonly ref struct SimpleIndent : IDisposable {
    public readonly IndentedTextWriter Writer;

    public SimpleIndent(IndentedTextWriter writer) {
      Writer = writer;
      Writer.Indent++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {
      Writer.Indent--;
    }
  }

  public readonly ref struct BracedIndent : IDisposable {
    private readonly SimpleIndent _inner;
    private readonly bool _newLine;

    public BracedIndent(IndentedTextWriter writer, bool newLine) {
      _newLine = newLine;
      if (newLine) {
        writer.WriteLine(" {");
      }
      else {
        writer.Write(" { ");
      }
      _inner = new SimpleIndent(writer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {
      _inner.Dispose();
      if (_newLine) {
        _inner.Writer.WriteLine('}');
      }
      else {
        _inner.Writer.Write(" } ");
      }
    }
  }

  public readonly ref struct RegionScope : IDisposable {
    private readonly TextWriter _writer;

    public RegionScope(TextWriter writer, string regionName) {
      _writer = writer;
      _writer.Write("#region ");
      _writer.WriteLine(regionName);
    }

    public RegionScope(TextWriter writer, ReadOnlySpan<string> regionNameStrings) {
      writer.Write("#region ");
      foreach (string s in regionNameStrings) {
        writer.Write(s);
      }

      writer.WriteLine();
      _writer = writer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() {
      _writer.WriteLine("#endregion");
    }
  }
}
