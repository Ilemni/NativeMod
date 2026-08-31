using System.Collections;
using System.Runtime.CompilerServices;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;

namespace PdbToCSharp;

internal static class PdbExtensions {
  extension(SymbolStream symbols) {
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "symbols")]
    internal extern ref ArrayCache<SymbolRecord> GetSymbolsCache();

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

  extension(DbiModuleList modules) {
    public IEnumerable<SymbolStream> GetStreams() {
      foreach (DbiModuleDescriptor module in modules) {
        if (module?.LocalSymbolStream is {} stream) {
          yield return stream;
        }
      }
    }
  }


  extension(TpiStream tpi) {
    internal TypeRecord[] GetTypeRecords() {
      var records = new TypeRecord[tpi.TypeRecordCount];
      uint count = (uint)tpi.TypeRecordCount + 4096U;
      var cache = GetCache(tpi);
      for (uint i = 0; i < tpi.TypeRecordCount; i++) {
        uint index = i + 4096U;
          TypeIndex typeIndex = new(index);
        try {
          records[i] = tpi[typeIndex];
        }
        catch (Exception ex) {
          // Console.WriteLine(
          //   $"{ex.GetType().Name} thrown while reading type record at index {index}/{count}: {ex.Message}");
          NullRecord nullRecord = new();
          records[i] = nullRecord;
          cache[(int)typeIndex.ArrayIndex] = nullRecord;
        }
      }

      return records;

      [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "typesCache")]
      static extern ref ArrayCache<TypeRecord> GetCache(TpiStream stream);
    }
  }

  extension(PdbFile pdb) {
    public void FixNulls() {
      pdb.ReplaceDbiNullSymbols();
      pdb.FixNullDbiModuleStreams();
      PdbFile.FixNullTpiStreams(pdb.TpiStream);
      PdbFile.FixNullTpiStreams(pdb.IpiStream);
    }

    private static void FixNullTpiStreams(TpiStream stream) {
      var cache = GetCache(stream);
      for (uint i = 0; i < stream.TypeRecordCount; i++) {
        uint index = i + 4096U;
        TypeIndex typeIndex = new(index);
        try {
          _ = stream[typeIndex];
        }
        catch (Exception) {
          cache[(int)typeIndex.ArrayIndex] = new NullRecord();
        }
      }

      return;

      [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "typesCache")]
      static extern ref ArrayCache<TypeRecord> GetCache(TpiStream stream);
    }

    /// Sets the LocalSymbolStream of each module to the PdbStream if it is not null.
    /// <br /> This allows access to the <see cref="PdbFile"/> from any <see cref="SymbolRecord"/>.
    private void FixNullDbiModuleStreams() {
      PdbStream dbi = pdb.DbiStream.Stream;
      foreach (DbiModuleDescriptor module in pdb.DbiStream.Modules) {
        if (module.LocalSymbolStream is { } locals) {
          SetStream(locals, dbi);
        }
      }

      return;

      [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Stream")]
      static extern void SetStream(SymbolStream stream, PdbStream pdbStream);
    }

    /// Ensure all members not null
    /// SymbolRecord.Children property WILL throw if any children are null
    private void ReplaceDbiNullSymbols() {
      var enumerable = pdb.DbiStream.Modules
        .Select(m => m.LocalSymbolStream)
        .Where(s => s is not null);
      Parallel.ForEach(enumerable, mSymbols => {
        var cache = mSymbols.GetSymbolsCache();
        for (int i = 0; i < mSymbols.References.Count; i++) {
          if (mSymbols[i] is null) {
            cache[i] = new NullSymbol(mSymbols, i);
          }
        }
      });
    }
  }
}
