using System.Text;

namespace PdbToCSharp;

internal static class SourceGenExtensions {
  extension(string str) {
    public string KeywordToVerbatim() => ReservedKeywords.Contains(str) || str.StartsWith("__") ? $"@{str}" : str;

    public string Sanitize() {
      if (str switch {
            "unsigned __int64*" => "ulong*",
            "unsigned __int64" => "ulong",
            "unsigned long*" => "uint*", // UInt32Long
            "unsigned long" => "uint",
            "unsigned*" => "uint*",
            "unsigned" => "uint",
            "unsigned short*" => "ushort*",
            "unsigned short" => "ushort",
            "unsigned char*" => "byte*",
            "unsigned char" => "byte",
            "signed char*" => "sbyte*",
            "signed char" => "sbyte",
            "wchar_t*" => "char*",
            "wchar_t" => "char",
            "event" => "@event",
            _ => null
          } is { } sanitized) {
        return sanitized;
      }

      str = str
        .Replace("`anonymous-namespace'::", "")
        .Replace("`anonymous namespace'::", "")
        .Replace("::", "__")
        .Replace("/*", "")
        .Replace("*/", "");

      var strSpan = str.AsSpan();
      int numPtrsOrRefs = 0;
      for (int i = strSpan.Length - 1; i >= 0; i--) {
        char c = strSpan[i];
        if (!"*&".Contains(c)) {
          break;
        }

        numPtrsOrRefs++;
      }

      StringBuilder sb = new();
      bool pendingUnderscore = false;
      foreach (char c in strSpan[..^numPtrsOrRefs]) {
        bool invalid = " ,<>()[]`'\\-&*$".Contains(c);
        bool isUnderscore = c == '_';
        if (invalid) {
          pendingUnderscore = true;
        }
        else {
          if (pendingUnderscore) {
            if (sb.Length > 0 || isUnderscore) {
              sb.Append('_');
            }

            pendingUnderscore = false;
          }

          sb.Append(c);
        }
      }

      for (int i = 0; i < numPtrsOrRefs; i++) {
        sb.Append('*');
      }

      return sb.ToString();
    }

    public string SanitizeName(bool removePtr = false, bool removeQualifier = false) {
      bool endsInPtr = str.EndsWith('*') || str.EndsWith('&');
      if (removeQualifier) {
        str = str.Replace('.', '_');
      }
      if (!removePtr && endsInPtr) {
        return str[..^1]
            .Replace("~", "Dtor")
            .Replace("*", "Ptr")
            .Replace("&", "Ref")
            .Sanitize()
          + '*';
      }

      return str
        .Replace("~", "Dtor")
        .Replace("*", "Ptr")
        .Replace("&", "Ref")
        .Sanitize();
    }
  }


  private static readonly string[] ReservedKeywords = [
    "abstract",
    "as",
    "base",
    "bool",
    "break",
    "byte",
    "case",
    "catch",
    "char",
    "checked",
    "class",
    "const",
    "continue",
    "decimal",
    "default",
    "delegate",
    "do",
    "double",
    "else",
    "enum",
    "event",
    "explicit",
    "extern",
    "false",
    "finally",
    "fixed",
    "float",
    "for",
    "foreach",
    "goto",
    "if",
    "implicit",
    "in",
    "int",
    "interface",
    "internal",
    "is",
    "lock",
    "long",
    "namespace",
    "new",
    "null",
    "object",
    "operator",
    "out",
    "override",
    "params",
    "private",
    "protected",
    "public",
    "readonly",
    "ref",
    "return",
    "sbyte",
    "sealed",
    "short",
    "sizeof",
    "stackalloc",
    "static",
    "string",
    "struct",
    "switch",
    "this",
    "throw",
    "true",
    "try",
    "typeof",
    "uint",
    "ulong",
    "unchecked",
    "unsafe",
    "ushort",
    "using",
    "virtual",
    "void",
    "volatile",
    "while"
  ];
}
