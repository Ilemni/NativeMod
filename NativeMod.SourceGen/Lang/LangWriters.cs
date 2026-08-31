using JetBrains.Annotations;

namespace NativeMod.SourceGen.Lang;

[MustDisposeResource]
public abstract class LangWriters(LangGen gen) : IDisposable {
  public virtual LangGen Gen { get; } = gen;
  public readonly string OutputPath = gen.OutputPath;
  public readonly string Namespace = gen.Namespace;

  public abstract void Dispose();
}
