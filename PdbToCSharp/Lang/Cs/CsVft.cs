using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Lang.Cs;

public class CsVft(CsGen csGen, TypeIndex index, VirtualFunctionTableShapeRecord vftRecord)
  : CsType(csGen, index) {
  protected override string CreateSelfName() => $"VfTable with {vftRecord.Slots.Length} slots";
  protected override bool EqualsCore(CsType? other) {
    return other is CsVft otherVft && TypeIndex == otherVft.TypeIndex;
  }

  public override int GetHashCode() => TypeIndex.GetHashCode();
  public override string CppName => string.Empty;
}
