using System.Diagnostics;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

// TODO: make this an instance class that is a member of SourceGen
//  ...or not, seeing that this class is now entirely unused
/// <summary>
/// Class that loads and stores procedure info. Includes parameter names
/// </summary>
public static class ProcedureHelper {
  private static readonly Dictionary<(TypeIndex FunctionType, string Name), ProcedureInfo> GlobalNames = [];
  private static readonly Dictionary<(TypeIndex FunctionType, string Name), ProcedureInfo> MemberNames = [];
  private static readonly Dictionary<TypeIndex, ProcedureInfo> Functions = [];

  public static void Load(PdbFileReader pdbReader) {
    PdbFile pdb = pdbReader.PdbFile;
    if (GlobalNames.Count > 0 || MemberNames.Count > 0) {
      return;
    }

    GlobalNames.Clear();
    MemberNames.Clear();
    Functions.Clear();
    pdb.FixNulls();

    Dictionary<string, List<string>> collisions = [];
    Dictionary<ulong, string> syms = [];

    foreach (PdbPublicSymbol sym in pdbReader.PublicSymbols) {
      syms[sym.RelativeVirtualAddress] = Lang.Cs.CsNameUndecorator.UnDecorateSymbolName(sym.Name);
    }

    foreach (ProcedureSymbol proc in pdb.DbiStream.Modules
               .Where(m => m.LocalSymbolStream is not null)
               .SelectMany(m => m.LocalSymbolStream.AsEnumerable()
                 .OfType<ProcedureSymbol>())
            ) {
      if (proc.FunctionType.Index == 0) {
        continue;
      }

      string? sym = syms.GetValueOrDefault(pdb.FindRelativeVirtualAddress(proc.Segment, proc.Offset));

      TypeRecord untypedRecord = proc.FunctionType.AsRecord(pdb);
      var names = untypedRecord switch {
        ProcedureRecord => GlobalNames,
        MemberFunctionRecord => MemberNames,
        _ => throw new UnreachableException("Unexpected type record for procedure")
      };

      if (names.ContainsKey((proc.FunctionType, proc.Name.String))) {
        if (!collisions.TryGetValue(proc.Name.String, out var list)) {
          list = [];
          collisions[proc.Name.String] = list;
          list.Add(sym);
        }

        list.Add(sym);
        continue;
      }

      ProcedureInfo pInfo = new(proc);
      names[(proc.FunctionType, proc.Name.String)] = pInfo;
      Functions[proc.FunctionType] = pInfo;
    }

    if (collisions.Count > 0) {
      var collisionsWeCareAbout = collisions
        .Where(kv => !kv.Key.Contains('~') && !kv.Key.Contains("destructor for"));

      Log.Warn($"{collisions.Sum(kv => kv.Value.Count)} procedures had duplicate names and were skipped.");
    }
  }
}
