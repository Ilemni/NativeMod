using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;

namespace PdbToCSharp;

internal static class PdbExtensions {
  extension(PdbFileReader reader) {
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Item")]
    public extern PdbType GetType(TypeIndex typeIndex);

    public PdbType GetType(int index) => reader.GetType(new TypeIndex((uint)index + 4096));

    public T? TryGetType<T>(TypeIndex index) where T : PdbType =>
      reader.TryGetType(index, out T? type) ? type : null;

    public bool TryGetType<T>(TypeIndex index, [NotNullWhen(true)] out T? type) where T : PdbType {
      if (index.IsSimple) {
        type = null;
        return false;
      }

      type = reader.GetType(index) as T;
      return type is not null;
    }

    public bool TryGetType<T>(int index, [NotNullWhen(true)] out T? type) where T : PdbType =>
      reader.TryGetType(TypeIndex.FromArrayIndex(index), out type);

    public IEnumerable<PdbUserDefinedType> UDTs => reader.UserDefinedTypes.Cast<PdbUserDefinedType>();


    public PdbTypeEnumerable.PdbTypeEnumerator GetEnumerator() => new(reader);
    public PdbTypeEnumerable AsEnumerable() => new(reader);
    public PdbRecordEnumerable AsRecordEnumerable() => new(reader.PdbFile);
  }

  extension(PdbFile pdb) {
    public TypeRecord GetRecord(TypeIndex typeIndex, TpiStream? stream = null) => (stream ?? pdb.TpiStream)[typeIndex];

    public T GetRecord<T>(TypeIndex typeIndex, TpiStream? stream = null) where T : TypeRecord {
      TypeRecord record = pdb.GetRecord(typeIndex, stream);
      return record as T ??
        throw new ArgumentException($"Expected a {typeof(T).Name}, but got {record.GetType().Name}");
    }

    public TypeRecord? TryGetRecord(TypeIndex typeIndex, TpiStream? stream = null) =>
      !typeIndex.IsSimple ? pdb.GetRecord(typeIndex, stream) : null;
  }

  extension(SymbolStream symbols) {
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "symbols")]
    internal extern ref ArrayCache<SymbolRecord> GetSymbolsCache();

    public SymbolStreamEnumerable.SymbolEnumerator GetEnumerator() => new(symbols);

    public SymbolStreamEnumerable AsEnumerable() => new(symbols);
  }

  public readonly ref struct PdbTypeEnumerable(PdbFileReader reader) : IEnumerable<PdbType> {
    public PdbTypeEnumerator GetEnumerator() => new(reader);
    IEnumerator<PdbType> IEnumerable<PdbType>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct PdbTypeEnumerator(PdbFileReader reader) : IEnumerator<PdbType> {
      private int _i;
      public bool MoveNext() => ++_i < reader.PdbFile.TpiStream.TypeRecordCount;

      public PdbType Current => reader.GetType(TypeIndex.FromArrayIndex(_i));

      public void Reset() => _i = -1;
      object? IEnumerator.Current => Current;

      public void Dispose() {
        throw new NotImplementedException();
      }
    }
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

  public readonly struct PdbRecordEnumerable(PdbFile pdb) : IEnumerable<TypeRecord> {
    public PdbRecordEnumerator GetEnumerator() => new(pdb);
    IEnumerator<TypeRecord> IEnumerable<TypeRecord>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct PdbRecordEnumerator(PdbFile pdb) : IEnumerator<TypeRecord> {
      private int _i;
      public bool MoveNext() => ++_i < pdb.TpiStream.TypeRecordCount;

      public TypeRecord Current => pdb.TpiStream[TypeIndex.FromArrayIndex(_i)];

      public void Reset() => _i = -1;
      object? IEnumerator.Current => Current;

      public void Dispose() {
      }
    }
  }
}
