using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

[Obsolete("No longer supporting C++ output.")]
public class CppWriter : StreamWriter {
  internal readonly PdbFile File;
  private ushort _padding;
  private string? _currentMethodOverloadName;

  public CppWriter(PdbFile file) : base(Stream.Null) {
    File = file;
  }

  public CppWriter(PdbFile file, string path) : base(path) {
    File = file;
  }

  public void WriteDefinition(TagRecord tagRecord) {
    if (tagRecord.Name.String is { } str) {
      // if (str.StartsWith("Array<")) {
      //   Write("/* Skipping Array tag record: ");
      //   Write(tagRecord.Name.String);
      //   WriteLine(" */");
      //   return;
      // }

      if (str.StartsWith("Slice<")) {
        Write("/* Skipping Slice tag record: ");
        Write(tagRecord.Name.String);
        WriteLine(" */");
        return;
      }

      if (str.StartsWith("Mutable_slice<")) {
        Write("/* Skipping Mutable_slice tag record: ");
        Write(tagRecord.Name.String);
        WriteLine(" */");
        return;
      }

      if (str.Contains("crashpad::")) {
        Write("/* Skipped crashpad tag record: ");
        Write(tagRecord.Name.String);
        WriteLine(" */");
        return;
      }
    }

    if (tagRecord.IsForwardReference) {
      Write("/* forward */ ");
    }

    Write(tagRecord.Kind switch {
      TypeLeafKind.LF_CLASS => "class ",
      TypeLeafKind.LF_STRUCTURE => "struct ",
      TypeLeafKind.LF_INTERFACE => "interface ",
      TypeLeafKind.LF_UNION => "union ",
      TypeLeafKind.LF_ENUM => "enum class ", // Using "enum class" for scoped enums
      _ => throw new InvalidDataException($"Unexpected tag record kind: {tagRecord.Kind}")
    });
    string nameString = tagRecord.Name.String;
    string name = !tagRecord.IsNested
      ? nameString
      : nameString[(nameString.LastIndexOf("::", StringComparison.Ordinal) + 2)..];

    Write(name);
    if (tagRecord is EnumRecord enumRecord) {
      Write(" : ");
      Write(enumRecord.UnderlyingType.ToString(File));
    }

    if (tagRecord.IsForwardReference) {
      WriteLine(";");
      return;
    }

    if (tagRecord.MemberCount == 0) {
      WriteLine(" { }; /* children = 0 */");
      return;
    }

    int childCount = tagRecord.MemberCount;
    FieldListRecord fieldListRecord = tagRecord.FieldList.As<FieldListRecord>(File);
    bool hasWrittenFirstBaseClass = false;
    _padding += 4;
    foreach (TypeRecord baseClassRecord in fieldListRecord.Fields
               .Where(f => f is BaseClassRecord or VirtualBaseClassRecord)) {
      childCount--;
      WriteLine();
      if (!hasWrittenFirstBaseClass) {
        Write(": ");
        hasWrittenFirstBaseClass = true;
      }
      else {
        Write(", ");
      }

      switch (baseClassRecord) {
        case BaseClassRecord b:
          Write(b.ToString(File));
          break;
        case VirtualBaseClassRecord vb:
          Write(vb.ToString(File));
          break;
      }
    }

    _padding -= 4;
    if (hasWrittenFirstBaseClass) {
      WriteLine();
    }


    Write(" { /* children = ");
    Write(childCount);
    Write(" */");

    _padding += 4;
    Write(fieldListRecord.ToString(File));
    _padding -= 4;
    WriteLine();
    Write("};");
    if (tagRecord is ClassRecord classRecord) {
      Write(" /* size = ");
      Write(classRecord.Size);
      Write(" */");
    }
    WriteLine();
  }

  public override void Write(string? value) {
    if (value is not null) {
      Write(value.AsSpan());
    }
  }

  public override void Write(ReadOnlySpan<char> str) {
    if (_padding <= 0) {
      base.Write(str);
      return;
    }

    bool isFirst = true;
    foreach (Range range in str.Split(Environment.NewLine)) {
      if (isFirst) {
        isFirst = false;
      }
      else {
        base.WriteLine();
        WritePadding();
      }
      base.Write(str[range]);
    }
  }

  public override void WriteLine() {
    base.WriteLine();
    WritePadding();
  }

  public override void WriteLine(string? value) {
    base.WriteLine(value);
    WritePadding();
  }

  private void WritePadding() {
    for (int i = 0; i < _padding; i++) {
      Write(' ');
    }
  }
}
