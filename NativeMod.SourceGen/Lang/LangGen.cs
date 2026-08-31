using JetBrains.Annotations;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang;

public abstract class LangGen(PdbFileReader pdb, string namespaceName, string bindsPath, string nativeModPath) : IDisposable {
  public readonly PdbFileReader Reader = pdb;
  public readonly PdbFile Pdb = pdb.PdbFile;
  public readonly string Namespace = namespaceName;
  public readonly string OutputPath = bindsPath;
  public readonly string NativeModPath = nativeModPath;
  internal TypeRecord[] Records = null!;
  public abstract LangType?[] Types { get; }

  [HandlesResourceDisposal]
  protected abstract LangWriters Writers { get; }

  public const string MemoryAddress = "global::NativeMod.NativeModule.MemoryAddress";
  public const string FunctionAddress = "global::NativeMod.NativeModule.FunctionAddress";

  protected (TagRecord tag, TypeIndex index)[] TagRecords { get; private set; } = null!;
  public readonly Dictionary<string, ulong> VTableAddresses = [];

  public readonly Dictionary<(string Class, string Method, TypeIndex MFuncType), ProcedureInfo> ProcCache = [];

  internal int UnnamedStructs;
  internal int UnnamedUnions;
  internal int UnnamedEnums;

  public virtual void PreProcess() {
    Log.Step("Loading VTables");
    ProcessVTables();

    Log.Step("Collecting tag records");
    TagRecords = Records
      .Index()
      .Where(r => r.Item is TagRecord)
      .Select(r => ((TagRecord)r.Item, TypeIndex.FromArrayIndex(r.Index)))
      .ToArray();

    Log.Step("Creating non-forward reference types");
    foreach ((int index, TypeRecord record) in Records.Index()) {
      if (record is TagRecord) {
        TryGetOrCreate(TypeIndex.FromArrayIndex(index));
      }
    }

    Log.Step("Resolving forward references");
    ResolveAllForwardReferences();

    foreach ((int index, TypeRecord record) in Records.Index()) {
      if (record is not TagRecord) {
        TryGetOrCreate(TypeIndex.FromArrayIndex(index));
      }
    }

    Log.Step("Loading procedure info");
    ProcessInstanceProcedureInfo();
  }

  private void ResolveAllForwardReferences() {
    foreach ((int index, (TagRecord tag, TypeIndex i)) in
             TagRecords.Index().Where(r => r.Item.tag.IsForwardReference)) {
      Types[i.ArrayIndex] = ResolveForwardReference(tag, index, out TypeIndex rIndex)
        ? Types[rIndex.ArrayIndex]!
        : GetOrCreate(i);
    }
  }

  public abstract void WriteAll();

  public abstract LangType GetOrCreate(TypeIndex index);
  public abstract LangType? TryGetOrCreate(TypeIndex index);

  public virtual string UnDecorateSymbolName(string name) => NameUndecorator.UnDecorateSymbolName(name);

  private void ProcessVTables() {
    foreach (Public32Symbol? s in Pdb.PublicsStream.PublicSymbols) {
      string name = s.Name.String;
      if (name.StartsWith("??_7")) {
        string undecoratedName = NameUndecorator.UnDecorateSymbolName(name);
        int end = undecoratedName.IndexOf("::`vftable'", StringComparison.Ordinal);
        int start = undecoratedName.StartsWith("const ") ? 6 : 0;
        if (end != -1) {
          string typeName = undecoratedName[start..end];
          VTableAddresses[typeName] = Pdb.FindRelativeVirtualAddress(s.Segment, s.Offset);
        }
      }
    }
  }

  private void ProcessInstanceProcedureInfo() {
    var symbolStreams = Pdb.DbiStream.Modules
      .Select(m => m.LocalSymbolStream)
      .Where(s => s is not null);

    foreach (SymbolStream symbols in symbolStreams) {
      var procSyms = symbols.AsEnumerable().OfType<ProcedureSymbol>();
      foreach (ProcedureSymbol procSym in procSyms) {
        if (!procSym.FunctionType.TryAs(Pdb, out MemberFunctionRecord? _)) {
          continue;
        }

        string fullyQualifiedName = procSym.Name.String;

        // Extract method name and parent class name
        int lastColon = fullyQualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        if (lastColon == -1) continue; // Skip free/global functions

        string methodName = fullyQualifiedName[(lastColon + 2)..];
        string parentClassName = fullyQualifiedName[..lastColon];

        (string, string, TypeIndex) mKey = (parentClassName, methodName, procSym.FunctionType);

        // Handle potential duplicate entries from templates or incremental linking safely
        ProcCache.TryAdd(mKey, new ProcedureInfo(procSym));
      }
    }
  }

  private bool ResolveForwardReference(TagRecord tag, int start, out TypeIndex index) {
    // Try resolving forward first
    (TagRecord? resolved, index) = TagRecords
      .Skip(start)
      .FirstOrDefault(r => !r.tag.IsForwardReference &&
        r.tag.Name.String == tag.Name.String &&
        r.tag.UniqueName.String == tag.UniqueName.String);

    if (resolved is null) {
      // Much less common, some types may resolve backwards
      (resolved, index) = TagRecords
        .Take(start)
        .LastOrDefault(r => !r.tag.IsForwardReference &&
          r.tag.Name.String == tag.Name.String &&
          r.tag.UniqueName.String == tag.UniqueName.String);
    }

    return resolved is not null;
  }

  public void Dispose() {
    Reader.Dispose();
    Writers.Dispose();
  }
}
