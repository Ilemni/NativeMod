using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen;

/// <summary>
/// NativeMod.SourceGen internal class to represent a null symbol in the symbol stream.
/// This is used to replace any null symbols in the symbol stream to avoid exceptions when accessing the Children property of SymbolRecord.
/// </summary>
public sealed class NullSymbol : SymbolRecord {
  public NullSymbol(SymbolStream stream, int index) {
    SymbolStream = stream;
    SymbolStreamIndex = index;
  }
}

public sealed class NullRecord : TypeRecord;
