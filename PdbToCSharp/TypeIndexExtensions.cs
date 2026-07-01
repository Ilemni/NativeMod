using System.Diagnostics.Contracts;
using SharpPdb.Windows;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

internal static class TypeIndexExtensions {
  private static readonly Dictionary<SimpleTypeKind, string> CSharpSimpleTypeNames = new() {
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
  };

  private static readonly Dictionary<SimpleTypeKind, int> SimpleTypeCounts = new();
  private static readonly Dictionary<(SimpleTypeMode, SimpleTypeKind), int> SimpleModeCounts = new();

  internal static void Order() {
    var a = SimpleTypeCounts.OrderBy(kvp => kvp.Key).ToList();
    var b = SimpleModeCounts.OrderBy(kvp => kvp.Key.Item1).ThenBy(kvp => kvp.Key.Item2).ToList();
    ; // slap a breakpoint here to inspect the counts
  }

  extension(TypeIndex index) {
    [Pure]
    public string ToString(PdbFile pdb) => index.ToString(pdb, pdb.TpiStream);

    [Pure]
    public string ToString(PdbFile pdb, TpiStream stream) {
      return !index.IsSimple ? stream[index].ToString(pdb) : index.ToStringSimpleType();
    }

    [Pure]
    public string ToStringSimpleType() {
      // SimpleTypeCounts.Increment(index.SimpleKind);
      // if (index.SimpleMode != SimpleTypeMode.Direct) {
      //   SimpleModeCounts.Increment((index.SimpleMode, index.SimpleKind));
      // }

      return CSharpSimpleTypeNames.TryGetValue(index.SimpleKind, out string? name)
        ? name
        : $"<unknown simple type {index.SimpleKind}>";
    }

    [Pure]
    public string GetTypeName(PdbFile pdb) => index.GetTypeName(pdb.TpiStream);

    [Pure]
    public string GetTypeName(TpiStream stream) {
      return index.IsSimple ? index.SimpleTypeName : stream[index].GetType().Name;
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
