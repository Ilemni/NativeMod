using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Lang.Cs;

public abstract class CsField {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  public readonly CsStructure Container;

  protected CsField(CsStructure container, TypeRecord record) {
    Container = container;
    Record = record;
    _ = FieldType; // Ensure FieldType is initialized
  }

  public virtual TypeRecord Record { get; }
  public abstract string Name { get; }
  public abstract TypeIndex Type { get; }

  public CsType FieldType => Container.Gen.GetOrCreate(Type);
  public string FieldTypeName => FieldType.GlobalQualifiedName;

  public override string ToString() => $"{FieldTypeName} {Name}";

  public static string AccessStr(MemberAttributes attributes) => attributes.Access switch {
    MemberAccess.Public => "public",
    MemberAccess.Protected => "protected",
    MemberAccess.Private => "private",
    _ => string.Empty
  };
}

public class CsInstanceField(CsStructure container, DataMemberRecord record) : CsField(container, record) {
  public override DataMemberRecord Record => (DataMemberRecord)base.Record;
  public override string Name => Record.Name.String;
  public override TypeIndex Type => Record.Type;

  public uint Offset => (uint)Record.FieldOffset;

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} {FieldTypeName} {Name} (offset: 0x{Offset:X})";
}

public class CsStaticField(CsStructure container, StaticDataMemberRecord record) : CsField(container, record) {
  public override StaticDataMemberRecord Record => (StaticDataMemberRecord)base.Record;
  public override string Name => Record.Name.String;
  public override TypeIndex Type => Record.Type;

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} static {FieldTypeName} {Name}";
}

public sealed class CsRegularStaticField(CsStructure container, StaticDataMemberRecord record, DataSymbol data)
  : CsStaticField(container, record) {
  public readonly ulong RelativeVirtualAddress =
    container.PdbFile.FindRelativeVirtualAddress(data.Segment, data.Offset);

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} static {FieldTypeName} {Name} (RVA: 0x{RelativeVirtualAddress:X})";
}

public sealed class CsConstantField : CsStaticField {
  public CsConstantField(CsStructure container, StaticDataMemberRecord record, ConstantSymbol symbol) : base(container,
    record) {
    Symbol = symbol;

    string symName = symbol.Name.String;
    int index = symName.LastIndexOf("::", StringComparison.Ordinal);
    Name = (index != -1 ? symName[(index + 2)..] : symName).KeywordToVerbatim();
  }

  public readonly ConstantSymbol Symbol;

  public override string Name { get; }
  public object Value => Symbol.Value;

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} const {FieldTypeName} {Name} = {Symbol.Value}";
}

public sealed class CsBitField(CsStructure container, BitFieldRecord bitRecord, DataMemberRecord record)
  : CsInstanceField(container, record) {
  public override TypeIndex Type { get; } = bitRecord.Type;

  public readonly uint BitSize = bitRecord.BitSize;
  public readonly uint BitOffset = bitRecord.BitOffset;

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} {FieldTypeName} {Name} : {BitSize} (offset: 0x{Offset:X}, bit: {BitOffset}:{BitSize})";
}

// Currently unimplemented, and used in very few places in MIO.exe
public sealed class CsThreadLocalStorageField(
  CsStructure container,
  StaticDataMemberRecord record,
  ThreadLocalDataSymbol threadLocalData)
  : CsStaticField(container, record) {
  public readonly ThreadLocalDataSymbol ThreadLocalData = threadLocalData;

  [DllImport("ntdll.dll")]
  private static extern IntPtr NtCurrentTeb();

  public override string ToString() =>
    $"{AccessStr(Record.Attributes)} static /*thread_local*/ {FieldTypeName} {Name}";
}
