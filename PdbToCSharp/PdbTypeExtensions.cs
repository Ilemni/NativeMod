using SharpPdb.Native;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public static class PdbTypeExtensions {
  extension(TagRecord tag) {
    public FieldListRecord GetFieldList(PdbFileReader pdb) => tag.FieldList.As<FieldListRecord>(pdb.PdbFile);
    public IReadOnlyList<TypeRecord> GetFields(PdbFileReader pdb) => tag.GetFieldList(pdb).Fields;
  }
}
