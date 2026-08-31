using System.Runtime.InteropServices;

namespace PdbToCSharp;

public readonly record struct GraphLeaf<T>(string Name, string FullName, T Value);

public abstract class NestGraph {
  public static RootNestGraph<T> Create<T>(IEnumerable<(string?, T)> paths) {
    RootNestGraph<T> rootNestGraph = new();
    foreach ((string? s, T value) in paths) {
      if (string.IsNullOrWhiteSpace(s)) {
        rootNestGraph.AddUnnamed(value);
        continue;
      }

      if (Path.IsPathRooted(s) || Path.IsPathFullyQualified(s) || s.StartsWith("..\\")) {
        rootNestGraph.AddOther(s, value);
        continue;
      }

      int extensionIndex = s.LastIndexOf('.');
      if (extensionIndex == -1) {
        extensionIndex = s.Length;
      }

      var path = s.AsSpan(0, extensionIndex);
      NestGraph<T> currentNestGraph = rootNestGraph;
      int nestSeparatorIndex = path.IndexOf('\\');
      while (nestSeparatorIndex != -1) {
        var nestSpan = path[..nestSeparatorIndex];
        currentNestGraph = currentNestGraph.GetOrAddGraph(nestSpan);
        path = path[(nestSeparatorIndex + 1)..];
        nestSeparatorIndex = path.IndexOf('\\');
      }

      currentNestGraph.AddLeaf(path.ToString(), value);
    }

    return rootNestGraph;
  }
}

public class NestGraph<T>(string? name, NestGraph<T>? parent) : NestGraph {
  public string? Name = name;
  public IReadOnlyList<GraphLeaf<T>> Leaves => _leaves;
  public IReadOnlyDictionary<string, NestGraph<T>> Nested => _nested;

  public virtual bool HasAny => _leaves.Count > 0 || _nested.Count > 0;

  private readonly List<GraphLeaf<T>> _leaves = [];
  private readonly Dictionary<string, NestGraph<T>> _nested = [];

  public readonly string Namespace =
    (!string.IsNullOrWhiteSpace(parent?.Namespace) ? parent.Namespace + "." : "") + name;

  public NestGraph<T> GetOrAddGraph(ReadOnlySpan<char> name) {
    var lookup = _nested.GetAlternateLookup<ReadOnlySpan<char>>();
    ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, name, out bool exists);
    if (!exists) {
      value = new NestGraph<T>(name.Length == 0 ? null : name.ToString(), this);
    }

    return value!;
  }

  public void AddLeaf(string name, T value) {
    string fullName = (!string.IsNullOrWhiteSpace(Namespace) ? Namespace + "." : "") + name;
    _leaves.Add(new GraphLeaf<T>(name, fullName, value));
  }
}

public sealed class RootNestGraph<T>() : NestGraph<T>(null, null) {
  public IReadOnlyList<(string? name, T value)> OtherLeaves => _otherLeaves;
  public IReadOnlyList<T> UnnamedLeaves => _unnamedLeaves;

  private readonly List<(string? name, T value)> _otherLeaves = [];
  private readonly List<T> _unnamedLeaves = [];

  public override bool HasAny => base.HasAny || _unnamedLeaves.Count > 0 || _otherLeaves.Count > 0;

  public void AddOther(string name, T value) => _otherLeaves.Add((name, value));
  public void AddUnnamed(T value) => _unnamedLeaves.Add(value);
}
