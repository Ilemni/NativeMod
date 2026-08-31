using System.Text;
using JetBrains.Annotations;
using Microsoft.Extensions.ObjectPool;

namespace NativeMod.SourceGen.Dissect;

internal static partial class TypeRecordExtensions {
  // This partial is an attempt to make string creation a bit cheaper, by resuing StringBuilder instances.
  private static readonly ObjectPool<StringBuilder> Pool = ObjectPool.Create<StringBuilder>();

  [MustDisposeResource]
  internal static RentedState<StringBuilder> Rent(out StringBuilder sb) {
    sb = Pool.Get();
    RentedState<StringBuilder> rent = new(sb, ClearAction);
    return rent;

    static void ClearAction(StringBuilder sb) {
      sb.Clear();
      Pool.Return(sb);
    }
  }

  internal ref struct RentedState<T>(T value, Action<T>? returnAction) : IDisposable where T : class {
    private bool _isReturned;

    void IDisposable.Dispose() {
      if (_isReturned) {
        return;
      }

      returnAction?.Invoke(value);
      _isReturned = true;
    }
  }

  private static string? _currentMethodOverloadName;

  extension(StringBuilder sb) {
    internal StringBuilder AppendIf(bool condition, ReadOnlySpan<char> value) => condition ? sb.Append(value) : sb;
  }
}
