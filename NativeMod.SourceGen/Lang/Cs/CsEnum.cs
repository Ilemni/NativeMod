using System.Diagnostics;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed class CsEnum : CsUdt {
  public CsEnum(CsGen gen, TypeIndex index, EnumRecord record) : base(gen, index, record) {
    Values = Record.MemberCount > 0
      ? Record.FieldList.As<FieldListRecord>(PdbFile).Fields
        .OfType<EnumeratorRecord>()
        .Select(e => new CsEnumField(e))
        .ToArray()
      : [];
  }

  public override EnumRecord Record => (EnumRecord)base.Record;
  public CsType Underlying => field ??= Gen.GetOrCreate(Record.UnderlyingType);

  public override string ToString() => Parent is null
    ? $"enum {FullName}"
    : $"enum {FullName} ({SelfName})";

  public override ulong Size => Underlying.Size;

  [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
  public readonly CsEnumField[] Values;

  protected override bool EqualsCore(CsType? other) {
    return other is CsEnum otherEnum && TypeIndex == otherEnum.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
}

public sealed class CsEnumField(EnumeratorRecord record) {
  public readonly string Name = record.Name.String;
  public readonly object Value = record.Value;

  public override string ToString() => $"{Name} = {Value}";
}
