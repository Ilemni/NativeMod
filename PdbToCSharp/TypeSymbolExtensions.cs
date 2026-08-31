using PdbToCSharp.Lang.Cs;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public static class TypeSymbolExtensions {
  extension(SymbolRecord sym) {
    public PdbFile Pdb => sym.SymbolStream.Stream.File;
  }

  private static readonly HashSet<string> DuplicateArgNames = [];
  private static readonly string[] ArgFallbackNames = Enumerable.Range(1, 32).Select(i => $"arg{i}").ToArray();

  public static (CsType type, string name)[] GetNamedArgs(this ProcedureSymbol procSym) {
    var pdb = procSym.Pdb;
    TypeRecord functionRecord = procSym.FunctionType.AsRecord(pdb);
    var argList =
      (functionRecord as ProcedureRecord)?.ArgumentList.As<ArgumentListRecord>(pdb).Arguments ??
      (functionRecord as MemberFunctionRecord)!.ArgumentList.As<ArgumentListRecord>(pdb).Arguments;
    CsGen csGen = CsGen.GetGen(pdb);

    var csArgs = argList.Select((a, i) => (type: csGen.GetOrCreate(a), name: ArgFallbackNames[i])).ToArray();
    var csParams = procSym.Children.OfType<LocalSymbol>().Where(l =>
        l.Flags.HasFlag(LocalVariableFlags.IsParam) &&
        l.Name.String != "this")
      .Select(l => (type: csGen.GetOrCreate(l.Type), name: l.Name.String))
      .ToArray();
    // Sanitize and deduplicate argument names, as they may be missing or invalid
    DuplicateArgNames.Clear();

    if (csParams.Length == 0) {
      return csArgs;
    }

    // Make sure that aligned types have their names first
    bool anyMissed = false;
    int unusedCount = 0;
    Span<bool> assigned = stackalloc bool[csArgs.Length];
    Span<bool> unused = stackalloc bool[csArgs.Length];
    for (int i = 0; i < csArgs.Length; i++) {
      if (!FindType(csArgs[i].type, i - unusedCount, csParams, out int _)) {
        csArgs[i].name = $"unused_{++unusedCount}";
        assigned[i] = true;
        unused[i] = true;
      }
    }

    unusedCount = 0;
    foreach ((int i, (CsType pType, _)) in csArgs.Index()) {
      if (unused[i]) {
        unusedCount++;
        continue;
      }

      if (unusedCount > 0) {
        int i2 = i - unusedCount;
        if (i2 < csParams.Length && pType.Equals(csParams[i2].type)) {
          CreateArgName(csParams[i2].name, i, ref csArgs[i].name, DuplicateArgNames);
          assigned[i] = true;
          continue;
        }
      }

      if (i < csParams.Length && pType.Equals(csParams[i].type)) {
        CreateArgName(csParams[i].name, i, ref csArgs[i].name, DuplicateArgNames);
        assigned[i] = true;
        continue;
      }

      if (!assigned[i]) {
        anyMissed = true;
      }
    }

    if (csArgs.Length == csParams.Length && !anyMissed) {
      return csArgs;
    }

    // In case of misaligned types, try to get them a valid name
    // If it grabs a duplicate, it should result in a deduplicated name

    unusedCount = 0;
    foreach ((int i, (CsType aType, _)) in csArgs.Index()) {
      if (unused[i]) {
        unusedCount++;
        continue;
      }
      if (assigned[i]) {
        continue;
      }

      if (FindType(aType, i - unusedCount, csParams, out int foundIndex)) {
        CreateArgName(csParams[foundIndex].name, i, ref csArgs[i].name, DuplicateArgNames);
        assigned[i] = true;
      }
    }

    return csArgs;

    static bool FindType(CsType aType, int i, (CsType type, string name)[] pArgs, out int index) {
      i = Math.Min(i, pArgs.Length - 1);
      index = i;
      if (aType.Equals(pArgs[i].type)) {
        return true;
      }


      int oldI = index;
      while (++index < pArgs.Length) {
        if (aType.Equals(pArgs[index].type)) {
          return true;
        }
      }

      index = oldI;
      while (--index > 0) {
        if (aType.Equals(pArgs[index].type)) {
          return true;
        }
      }

      index = -1;
      return false;
    }

    static void CreateArgName(string pName, int pIndex, ref string name, HashSet<string> duplicates) {
      if (string.IsNullOrWhiteSpace(pName)) {
        return;
      }

      name = duplicates.Add(pName) ? pName : $"{pName}_{pIndex + 1}";
      name = name.SanitizeName(true, true).KeywordToVerbatim();
    }
  }



  extension(ProcedureReferenceSymbol refSym) {
    public ProcedureSymbol GetProcedureSymbol(DbiModuleList? modules = null) {
      modules ??= refSym.Pdb.DbiStream.Modules;
      SymbolStream stream = modules[refSym.Module - 1].LocalSymbolStream;

      stream.TryGetSymbolRecordByOffset(refSym.Offset, out SymbolRecord? localSym);
      return (ProcedureSymbol)localSym;
    }
  }
}
