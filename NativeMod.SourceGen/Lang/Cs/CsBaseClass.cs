using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed class CsBaseClass(CsGen gen, BaseClassRecord record) {
  public readonly CsStructure BaseType = gen.GetOrCreate<CsStructure>(record.Type);
  public readonly uint Offset = (uint)record.Offset;

  public override string ToString() => $"base class {BaseType.FullName} (offset: 0x{Offset:X})";
}
