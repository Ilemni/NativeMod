using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed class CsModifiedType(CsGen gen, TypeIndex index, ModifierRecord record) : CsType(gen, index) {
  public CsType ModifiedType => field ??= Gen.GetOrCreate(record.ModifiedType);
  public readonly ModifierOptions Modifiers = record.Modifiers;

  public override CsMarshaller? Marshaller => ModifiedType.Marshaller;
  public override CsType Unwrapped => ModifiedType;

  public override string CppName => CreateCppName();

  public override string FullyQualifiedName => ModifiedType.FullyQualifiedName;
  public override string GlobalQualifiedName => ModifiedType.GlobalQualifiedName;
  protected override string CreateSelfName() => ModifiedType.SelfName;
  protected override string CreateFullName() => ModifiedType.FullName;

  private string CreateCppName() {
    string? modifiers = null;
    if ((Modifiers & ModifierOptions.Const) != 0) {
      modifiers += "const";
    }

    if ((Modifiers & ModifierOptions.Volatile) != 0) {
      if (modifiers is not null) {
        modifiers += ' ';
      }

      modifiers += "volatile";
    }

    if ((Modifiers & ModifierOptions.Unaligned) != 0) {
      if (modifiers is not null) {
        modifiers += ' ';
      }

      modifiers += "__unaligned";
    }

    string name = ModifiedType.CppName;
    return modifiers is not null
      ? $"{modifiers} {name}"
      : name;
  }

  // Different modifiers should not result in different types
  protected override bool EqualsCore(CsType? other) => other is not null && Unwrap().Equals(other.Unwrap());

  public override int GetHashCode() => ModifiedType.GetHashCode();
}
