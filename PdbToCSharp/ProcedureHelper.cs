using System.Diagnostics;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

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
);

/// <summary>
/// Class that loads and stores procedure info. Includes parameter names
/// </summary>
public static class ProcedureHelper {
  public static Dictionary<TypeIndex, ProcedureInfo> Names { get; } = new();

  public static void Load(PdbFile pdb) {
    if (Names.Count > 0) {
      return;
    }

    Names.Clear();
    Program.ReplaceNullSymbols(pdb);
    List<(TypeIndex, string)> paramNamesList = [];
    foreach (ProcedureSymbol proc in pdb.DbiStream.Modules
               .Where(m => m.LocalSymbolStream is not null)
               .SelectMany(m => m.LocalSymbolStream.AsEnumerable()
                 .OfType<ProcedureSymbol>())
            ) {
      TypeRecord? untypedRecord = pdb.TryGetRecord(proc.FunctionType);

      if (untypedRecord is not ProcedureRecord and not MemberFunctionRecord) {
        continue;
      }

      if (Names.TryGetValue(proc.FunctionType, out ProcedureInfo test) && test.GoodSize) {
        continue;
      }

      int paramCount = untypedRecord switch {
        ProcedureRecord procRecord => procRecord.ParameterCount,
        MemberFunctionRecord funcRecord => funcRecord.ParameterCount,
        _ => throw new UnreachableException()
      };
      TypeIndex argList = untypedRecord switch {
        ProcedureRecord procRecord => procRecord.ArgumentList,
        MemberFunctionRecord funcRecord => funcRecord.ArgumentList,
        _ => throw new UnreachableException()
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
      Names[proc.FunctionType] = new ProcedureInfo(
        proc,
        paramNames,
        paramsLeft == 0
      );
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
        // Only warn if not a typical destructor
        if (!procName.Contains('~') || paramCount != 0 || paramNames.Length != 1) {
          Console.ForegroundColor = ConsoleColor.Yellow;
          Console.WriteLine(
            $"Warning: {untypedRecord.Kind} Procedure {procName} with {paramCount} args has extra named args, total {paramNames.Length}");
          Console.ResetColor();
        }
      }
    }
  }
}
