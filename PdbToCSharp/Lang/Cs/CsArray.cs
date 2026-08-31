using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Lang.Cs;

public sealed class CsArray : CsType {
  public readonly ArrayRecord Record;

  public CsArray(CsGen gen, TypeIndex index, ArrayRecord record) : base(gen, index) {
    Record = record;
    ElementType = Gen.GetOrCreate(record.ElementType);
    CsType inner = ElementType;
    while (inner is CsArray array) {
      inner = array.ElementType;
    }

    InnerElement = inner;
    CppName = $"{ElementType.CppName}[{Count}]";
  }

  public override string CppName { get; }

  public readonly CsType ElementType;
  public readonly CsType InnerElement;

  public ulong Count => ElementType.Size != 0 ? Record.Size / ElementType.Size : 0;

  public override ulong Size => Record.Size;

  public override string GlobalQualifiedName => "global::" + Gen.Namespace + '.' + FullyQualifiedName;

  public override string ToString() => $"Array of {ElementType} [{Count}]";

  protected override string CreateSelfName() {
    const string start = "InlineArray_";
    string end = "";
    CsType rootElement = this;
    while (rootElement is CsArray a) {
      rootElement = a.ElementType;
      end += '_' + ((int)a.Count).ToString();
    }

    string innerName = rootElement.Namespace is null
      ? rootElement.FullName
      : rootElement.Namespace + '.' + rootElement.FullName;

    string elementName = innerName.SanitizeName(true, true);
    string result = start + elementName + end;
    return result;
  }

  protected override bool EqualsCore(CsType? other) {
    return other is CsArray otherArray &&
      Count == otherArray.Count &&
      ElementType.Equals(otherArray.ElementType);
  }

  public override int GetHashCode() => HashCode.Combine(Count, InnerElement);
}
