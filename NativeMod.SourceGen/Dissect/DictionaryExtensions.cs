using System.Runtime.InteropServices;

namespace NativeMod.SourceGen.Dissect;

internal static class DictionaryExtensions {
  extension<T>(Dictionary<T, int> dict) where T : notnull {
    /// Basically dict[key]++, where key may not exist in the dictionary yet
    public void Increment(T key) {
      CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool _)++;
    }
  }
}
