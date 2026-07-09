using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
using PdbToCSharp.Dissect;
using SharpPdb.Windows;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

internal static class TypeIndexExtensions {
  private static readonly ReadOnlyDictionary<SimpleTypeKind, string> CSharpSimpleTypeNames =
    new Dictionary<SimpleTypeKind, string> {
      [SimpleTypeKind.Void] = "void",
      [SimpleTypeKind.HResult] = "uint",
      [SimpleTypeKind.SignedCharacter] = "sbyte",
      [SimpleTypeKind.Int16Short] = "short",
      [SimpleTypeKind.Int32Long] = "int",
      [SimpleTypeKind.Int64Quad] = "long",
      [SimpleTypeKind.UnsignedCharacter] = "byte",
      [SimpleTypeKind.UInt16Short] = "ushort",
      [SimpleTypeKind.UInt32Long] = "uint",
      [SimpleTypeKind.UInt64Quad] = "ulong",
      [SimpleTypeKind.Boolean8] = "bool",
      [SimpleTypeKind.Float32] = "float",
      [SimpleTypeKind.Float64] = "double",
      [SimpleTypeKind.NarrowCharacter] = "sbyte",
      [SimpleTypeKind.WideCharacter] = "short",
      [SimpleTypeKind.Int32] = "int",
      [SimpleTypeKind.UInt32] = "uint",
      [SimpleTypeKind.UInt64] = "ulong",
      [SimpleTypeKind.Character16] = "short",
      [SimpleTypeKind.Character32] = "int",
    }.AsReadOnly();

  extension(TypeIndex index) {
    [Pure]
    public string ToString(PdbFile pdb) {
      if (!index.IsSimple) {
        return pdb.TpiStream[index].ToString(pdb);
      }

      return CSharpSimpleTypeNames.TryGetValue(index.SimpleKind, out string? name)
        ? name
        : $"<unknown simple type {index.SimpleKind}>";
    }

    [Pure]
    public T As<T>(PdbFile pdb) where T : TypeRecord => index.As<T>(pdb.TpiStream);

    [Pure]
    public T As<T>(TpiStream stream) where T : TypeRecord {
      return stream[index] as T ??
        throw new ArgumentException($"Expected a {typeof(T).Name}, but got {stream[index].GetType().Name}");
    }
  }
}
