using System.Diagnostics;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

// TODO: make this an instance class that is a member of SourceGen

/// <summary>
/// Struct that holds procedure info, including parameter names
/// </summary>
/// <param name="Procedure"></param>
/// <param name="Args"></param>
/// <param name="GoodSize">Whether <paramref name="Args"/>.Length is equal to the function record's ParameterCount</param>
public readonly record struct ProcedureInfo(
  ProcedureSymbol Procedure,
  (TypeIndex Type, string Name)[] Args,
  bool GoodSize
) {
  public required ulong Rva { get; init; }

  public override string ToString() {
    return $"{Procedure.Name.String}({string.Join(", ", Args.Select(a => $"{a.Type} {a.Name}"))}) (RVA: 0x{Rva:X})";
  }
}

/// <summary>
/// Class that loads and stores procedure info. Includes parameter names
/// </summary>
public static class ProcedureHelper {
  private static readonly Dictionary<(TypeIndex FunctionType, string Name), ProcedureInfo> Names = [];
  private static readonly Dictionary<(TypeIndex FunctionType, string Name), ProcedureInfo> MemberNames = [];
  private static readonly Dictionary<TypeIndex, ProcedureInfo> Functions = [];

  public static void Load(PdbFileReader pdbReader) {
    PdbFile pdb = pdbReader.PdbFile;
    if (Names.Count > 0 || MemberNames.Count > 0) {
      return;
    }

    Names.Clear();
    MemberNames.Clear();
    Functions.Clear();
    ReplaceNullSymbols(pdb);

    List<(TypeIndex, string)> paramNamesList = [];
    Dictionary<string, List<string>> collisions = [];
    Dictionary<ulong, string> syms = [];

    foreach (PdbPublicSymbol sym in pdbReader.PublicSymbols) {
      // TODO: use CsNameUndecorator.UnDecorateSymbolName
      syms[sym.RelativeVirtualAddress] = sym.GetUndecoratedName();
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

      TypeRecord untypedRecord = pdb.GetRecord(proc.FunctionType, pdb.TpiStream);
      var names = untypedRecord switch {
        ProcedureRecord => Names,
        MemberFunctionRecord => MemberNames,
        _ => throw new UnreachableException("Unexpected type record for procedure")
      };

      if (names.TryGetValue((proc.FunctionType, proc.Name.String), out ProcedureInfo test) && test.GoodSize) {
        if (!collisions.TryGetValue(proc.Name.String, out var list)) {
          list = [];
          collisions[proc.Name.String] = list;
          list.Add(sym);
        }

        list.Add(sym);
        continue;
      }

      (int paramCount, TypeIndex argList) = untypedRecord switch {
        ProcedureRecord procRecord => (procRecord.ParameterCount, procRecord.ArgumentList),
        MemberFunctionRecord funcRecord => (funcRecord.ParameterCount, funcRecord.ArgumentList),
        _ => throw new UnreachableException("Unexpected type record for procedure")
      };

      int paramsLeft = paramCount;

      bool hasThisArg = false;
      foreach (LocalSymbol local in proc.Children.OfType<LocalSymbol>()) {
        bool isThisArg = local.Name.String == "this";
        hasThisArg |= isThisArg;
        if (!local.Flags.HasFlag(LocalVariableFlags.IsParam) || isThisArg) {
          continue;
        }

        paramNamesList.Add((local.Type, local.Name.String));
        paramsLeft--;
      }

      var paramNames = paramNamesList.ToArray();
      ProcedureInfo pInfo = new(proc, paramNames, paramsLeft == 0) {
        Rva = pdb.FindRelativeVirtualAddress(proc.Segment, proc.Offset)
      };
      names[(proc.FunctionType, proc.Name.String)] = pInfo;
      Functions[proc.FunctionType] = pInfo;
      paramNamesList.Clear();

      string procName = proc.Name.String;
      if (paramNames.Length != paramCount) {
        // Console.ForegroundColor = ConsoleColor.Yellow;
        // Console.WriteLine(
        //   $"Warning: {untypedRecord.Kind} Procedure {procName} with {paramCount} args only has {paramNames.Length} named args.");
        // Console.WriteLine("    Types: " +
        //   string.Join(", ",
        //     argList.As<ArgumentListRecord>(pdb).Arguments.Skip(hasThisArg ? 1 : 0).Select(a => a.ToString(pdb))));
        // Console.WriteLine("     Args: " +
        //   string.Join(", ", paramNames.Select(l => $"{l.Item1.ToString(pdb)} {l.Item2}")));
        // Console.WriteLine();
        // Console.ResetColor();
      }

      if (paramsLeft > 0) {
        // Console.ForegroundColor = ConsoleColor.Yellow;
        // Console.WriteLine($"Warning: Procedure {procName} has {paramsLeft} parameters left unaccounted for.");
        // Console.ResetColor();
      }
      else if (paramsLeft < 0) {
        // Only warn if not a MSVC-generated destructor
        if (!procName.Contains('~') || paramCount != 0 || paramNames.Length != 1) {
          Log.Warn(
            $"Warning: {untypedRecord.Kind} Procedure {procName} with {paramCount} args has extra named args, total {paramNames.Length}");
        }
      }
    }

    if (collisions.Count > 0) {
      var collisionsWeCareAbout = collisions
        .Where(kv => !kv.Key.Contains('~') && !kv.Key.Contains("destructor for"));

      Log.Warn($"{collisions.Sum(kv => kv.Value.Count)} procedures had duplicate names and were skipped.");
    }
  }

  /// Ensure all members not null
  /// SymbolRecord.Children property WILL throw if any children are null
  internal static void ReplaceNullSymbols(PdbFile pdb) {
    var enumerable = pdb.DbiStream.Modules
      .Select(m => m.LocalSymbolStream)
      .Where(s => s is not null);
    Parallel.ForEach(enumerable, mSymbols => {
      var cache = mSymbols.GetSymbolsCache();
      for (int i = 0; i < mSymbols.References.Count; i++) {
        if (mSymbols[i] is null) {
          cache[i] = new NullSymbol(mSymbols, i);
        }
      }
    });
  }
}
