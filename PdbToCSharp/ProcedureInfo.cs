using SharpPdb.Windows.SymbolRecords;

namespace PdbToCSharp;

/// <summary>
/// Struct that holds procedure info, including parameter names
/// </summary>
/// <param name="Procedure"></param>
public readonly record struct ProcedureInfo(ProcedureSymbol Procedure) {
  public uint Address => Procedure.Offset;
  public readonly (Lang.Cs.CsType Type, string Name)[] Args = Procedure.GetNamedArgs();

  public override string ToString() {
    return $"{Procedure.Name.String}({string.Join(", ", Args.Select(a => $"{a.Type} {a.Name}"))}) (Address: 0x{Address:X})";
  }
}
