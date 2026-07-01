using System.Collections;
using System.Runtime.CompilerServices;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;

namespace PdbToCSharp;

internal static class PdbExtensions {

  extension(PdbFile pdb) {
    public TypeRecord GetRecord(TypeIndex typeIndex, TpiStream? stream = null) => (stream ?? pdb.TpiStream)[typeIndex];

    public T GetRecord<T>(TypeIndex typeIndex, TpiStream? stream = null) where T : TypeRecord {
      TypeRecord record = pdb.GetRecord(typeIndex, stream);
      return record as T ?? throw new ArgumentException($"Expected a {typeof(T).Name}, but got {record.GetType().Name}");
    }

    public TypeRecord? TryGetRecord(TypeIndex typeIndex, TpiStream? stream = null) =>
      !typeIndex.IsSimple ? pdb.GetRecord(typeIndex, stream) : null;
  }

  [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "symbols")]
  internal static extern ref ArrayCache<SymbolRecord> GetSymbolsCache(SymbolStream stream);

  extension(SymbolStream symbols) {
    public SymbolStreamEnumerable.SymbolEnumerator GetEnumerator() => new(symbols);

    public SymbolStreamEnumerable AsEnumerable() => new(symbols);
  }

  public readonly struct SymbolStreamEnumerable(SymbolStream symbols) : IEnumerable<SymbolRecord> {
    public SymbolEnumerator GetEnumerator() => new(symbols);
    IEnumerator<SymbolRecord> IEnumerable<SymbolRecord>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal struct SymbolEnumerator(SymbolStream symbols) : IEnumerator<SymbolRecord> {
      private int _index = 0;

      public bool MoveNext() => ++_index < symbols.References.Count;
      object IEnumerator.Current => Current;

      public SymbolRecord Current => symbols[_index];

      public void Reset() => _index = 0;

      public void Dispose() {
      }
    }
  }
}
