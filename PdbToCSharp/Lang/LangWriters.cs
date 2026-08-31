using JetBrains.Annotations;

namespace PdbToCSharp.Lang;

[MustDisposeResource]
public abstract class LangWriters(SourceGen gen) : IDisposable {
  public virtual SourceGen Gen { get; } = gen;
  public readonly string OutputPath = gen.OutputPath;
  public readonly string Namespace = gen.Namespace;

  public abstract void Dispose();
}
