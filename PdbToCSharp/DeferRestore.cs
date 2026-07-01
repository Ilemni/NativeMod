using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace PdbToCSharp;

public readonly ref struct DeferRestore<T> : IDisposable {
  private readonly ref T _value;
  private readonly T _originalValue;

  [MustDisposeResource]
  public DeferRestore(ref T value) {
    _value = ref value;
    _originalValue = value;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Dispose() {
    _value = _originalValue;
  }
}
