using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed class CsPointerType(CsGen gen, TypeIndex index, PointerRecord record) : CsType(gen, index) {
  public readonly PointerRecord Record = record;

  // Always fetch, to avoid a readonly reference to a forward reference type
  public CsType ElementType => Gen.GetOrCreate(Record.ReferentType);

  public CsType InnerElement {
    get {
      if (field is not null) return field;

      CsType inner = ElementType;
      int depth = 1;
      while (inner is CsPointerType pointer) {
        depth++;
        inner = pointer.ElementType;
      }

      if (inner is CsSimplePointerType simplePtr) {
        depth++;
        inner = simplePtr.ElementType;
      }

      Depth = depth;
      return field = inner;
    }
  }

  public int Depth {
    get {
      _ = InnerElement;
      return field;
    }
    private set;
  }

  public override string CppName => field ??= CreateCppName();
  public override string? Namespace => ElementType.Namespace;

  protected override string CreateSelfName() => ElementType.SelfName + (ElementNeedsPtr ? '*' : null);
  protected override string CreateFullName() => ElementType.FullName + (ElementNeedsPtr ? '*' : null);
  public override string FullyQualifiedName => ElementType.FullyQualifiedName + (ElementNeedsPtr ? '*' : null);
  public override string GlobalQualifiedName => ElementType.GlobalQualifiedName + (ElementNeedsPtr ? '*' : null);

  public override ulong Size { get; } = record.Size == 0
    ? record.PointerKind == PointerKind.Near64 ? 8U : 4U
    : record.Size;

  public override string ToString() =>
    $"Pointer (Depth: {Depth}) to "
    + (InnerElement is CsUdt { IsForwardReference: true } ? "[FR] " : string.Empty)
    + InnerElement.CppName;

  private bool ElementNeedsPtr => ElementType is not CsProcedureType;

  private string CreateCppName() {
    string modif =
      (Record.IsConst ? " const" : "") +
      (Record.IsVolatile ? " volatile" : "");
    string ptr = !ElementNeedsPtr ? string.Empty : Record.Mode switch {
      PointerMode.Pointer => "*",
      PointerMode.LValueReference => "&",
      PointerMode.RValueReference => "&&",
      _ => string.Empty
    };

    string cppName = ElementType.CppName;
    string result = cppName + ptr + modif;
    return result;
  }

  protected override bool EqualsCore(CsType? other) {
    if (ReferenceEquals(this, other)) return true;

    CsType elementType = ElementType.Unwrap();
    if (elementType is CsSimpleType s) {
      TypeIndex t = new(s.TypeIndex.SimpleKind, SimpleTypeMode.NearPointer);
      return Gen.GetOrCreate<CsSimplePointerType>(t).Equals(other);
    }

    return other is CsPointerType otherPointer &&
      Depth == otherPointer.Depth &&
      InnerElement.Equals(otherPointer.InnerElement);
  }

  public override int GetHashCode() {
    CsType elementType = ElementType.Unwrap();
    if (elementType is CsSimpleType s) {
      TypeIndex t = new(s.TypeIndex.SimpleKind, SimpleTypeMode.NearPointer);
      return Gen.GetOrCreate<CsSimplePointerType>(t).GetHashCode();
    }

    return HashCode.Combine(Depth, InnerElement);
  }
}
