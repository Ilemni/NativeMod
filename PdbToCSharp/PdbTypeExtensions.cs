using SharpPdb.Native.Types;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public static class PdbTypeExtensions {
  extension(PdbUserDefinedType udt) {
    public FieldListRecord FieldListRecord => udt.TagRecord.FieldList.As<FieldListRecord>(udt.Pdb.PdbFile);
    public IReadOnlyList<TypeRecord> FieldRecords => udt.FieldListRecord.Fields;

    public IEnumerable<(NestedTypeRecord, PdbUserDefinedType)> NestedTypes =>
      udt.FieldRecords
        .OfType<NestedTypeRecord>()
        .Select(n => (n, udt.Pdb.GetType(n.Type) is PdbUserDefinedType { IsNested: true } u ? u : null))
        .Where(t => t.Item2 is not null)
        .Cast<(NestedTypeRecord, PdbUserDefinedType)>();
  }
}
