using System.Diagnostics;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Lang;

public abstract class LangType(SourceGen gen, TypeIndex index) {
  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal virtual SourceGen Gen { get; } = gen;
  public readonly TypeIndex TypeIndex = index;
}
