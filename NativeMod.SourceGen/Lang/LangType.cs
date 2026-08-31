using System.Diagnostics;
using SharpPdb.Windows;

namespace NativeMod.SourceGen.Lang;

public abstract class LangType(LangGen gen, TypeIndex index) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal virtual LangGen Gen { get; } = gen;
  public readonly TypeIndex TypeIndex = index;
}
