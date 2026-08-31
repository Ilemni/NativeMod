using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SharpPdb.Windows;

namespace NativeMod.SourceGen.Lang.Cs;

public abstract class CsType(CsGen gen, TypeIndex index)
  : LangType(gen, index), IEquatable<CsType> {
  protected internal sealed override CsGen Gen => (CsGen)base.Gen;

  /// <summary>
  /// Indicates that this is a variadic. If this is a parameter, the C# equivalent would be <c>__arglist</c>
  /// </summary>
  public bool IsVariadic => TypeIndex.IsNoneType;

  public abstract string CppName { get; }
  /// <summary>
  /// Gets the XML-escaped C++ name of the type, suitable for use in XML documentation comments.
  /// </summary>
  public string XmlCppName => field ??= System.Security.SecurityElement.Escape(CppName);

  /// <summary>
  /// Gets a value that represents the type that this type wraps, if any.
  /// For example, a <see cref="CsModifiedType"/> wraps another type, and this property would return that type.
  /// If this type is not a wrapper type, this value must return <see langword="null"/>.
  /// </summary>
  public virtual CsType? Unwrapped => null;

  public virtual CsMarshaller? Marshaller => null;

  public void WriteFromCpp(TextWriter writer, string arg) {
    if (Marshaller is not null) {
      Marshaller.WriteFromCpp(writer, arg);
      return;
    }

    writer.Write(arg);
  }

  public void WriteToCpp(TextWriter writer, string arg) {
    if (Marshaller is not null) {
      Marshaller.WriteToCpp(writer, arg);
      return;
    }

    writer.Write(arg);
  }

  [DebuggerBrowsable(DebuggerBrowsableState.Never)]
  protected internal PdbFile PdbFile => Gen.Pdb;

  public virtual ulong Size => 0;

  [AllowNull]
  public string SelfName {
    get => field ??= ValidateName(CreateSelfName(), true);
    protected set;
  }

  /// <summary>
  /// Gets the full name of the type, excluding namespace.
  /// <para /> This value should differ from <see cref="SelfName"/> only if this instance represents a nested type.
  /// </summary>
  /// <seealso cref="SelfName"/>
  /// <seealso cref="FullyQualifiedName"/>
  /// <seealso cref="GlobalQualifiedName"/>
  [AllowNull]
  public string FullName {
    get => field ??= ValidateName(CreateFullName(), false);
    protected set;
  }

  /// <summary>
  /// Gets the fully qualified name of the type, including namespace,
  /// but not prepended with <c>global::</c> or the project's namespace.
  /// </summary>
  /// <seealso cref="SelfName"/>
  /// <seealso cref="FullName"/>
  /// <seealso cref="GlobalQualifiedName"/>
  public virtual string FullyQualifiedName => SelfName;

  /// <summary>
  /// Gets the globally qualified name of the type, including <c>global::</c> and the project's namespace.
  /// </summary>
  /// <seealso cref="SelfName"/>
  /// <seealso cref="FullName"/>
  /// <seealso cref="FullyQualifiedName"/>
  public virtual string GlobalQualifiedName => SelfName;

  public virtual string? Namespace { get; set; }

  public CsType GetInnerMostType() {
    CsType type = this;
    while (((type as CsArray)?.InnerElement ?? (type as CsPointerType)?.InnerElement) is { } inner) {
      type = inner;
    }

    return type;
  }

  public CsType Unwrap() {
    CsType type = this;
    while (type.Unwrapped is { } unwrapped) {
      type = unwrapped;
    }

    return type;
  }

  protected abstract string CreateSelfName();
  protected virtual string CreateFullName() => SelfName;

  private string ValidateName(string name, bool isSelfName) {
    if (this is CsProcedureType or CsMethod or CsPointerType or CsSimplePointerType or CsArray or CsVft or CsModifiedType) {
      // Functions, pointers, and arrays should pass by default. Any inner arguments should throw.
      return name;
    }

    if (string.IsNullOrWhiteSpace(name)) {
      throw new ArgumentException("Name cannot be null or whitespace.");
    }

    // Check if all characters are A-z, 0-9, or _
    bool hasPtr = false;
    foreach ((int i, char c) in name.Index()) {
      if (c == '@' && (i == 0 || name[i - 1] == '.')) {
        // Allow @ at the start of the name, but not elsewhere
        continue;
      }

      if (!char.IsLetterOrDigit(c) && c is not '_' and not '.') {
        if (this is CsPointerType && c == '*') {
          // Allow * in pointer types
          hasPtr = true;
          continue;
        }

        throw new ArgumentException($"Name {name} contains invalid character: {c}");
      }

      if (hasPtr) {
        throw new ArgumentException($"Name {name} cannot contain '*' before other characters");
      }
    }

    if (isSelfName) {
      if (name.Contains('.')) {
        throw new ArgumentException($"Self name {name} cannot contain '.'");
      }
    }
    else {
      // For fully qualified names, cannot start or end with a '.',
      if (name.StartsWith('.') || name.EndsWith('.')) {
        throw new ArgumentException($"Full name {name} cannot start or end with a '.'");
      }

      // Types must not start with a number, including after a '.'. Cannot contain consecutive '.' characters.
      var parts = name.AsSpan().Split('.');
      foreach (Range part in parts) {
        if (char.IsDigit(name[part.Start.Value])) {
          throw new ArgumentException($"Full name {name} cannot start with a number");
        }

        if (part.End.Value == part.Start.Value) {
          throw new ArgumentException($"Full name {name} cannot contain consecutive '.' characters");
        }
      }
    }


    return name;
  }

  protected string FullyQualify() {
    string fullName = FullName;
    string result =
      (Namespace is { } ns ? ns + '.' : "") +
      fullName;
    return result;
  }

  public override string ToString() => FullyQualifiedName;

  public sealed override bool Equals(object? obj) => EqualsCore(obj as CsType);

  public bool Equals(CsType? other) {
    // Unwrap the types before comparing
    // For example, we want to consider modified types as equal to their unmodified counterparts.
    // A "void Foo(const int)" must be equal to "void Foo(int)".
    // A "const int Bar()" must be equal to "int Bar()".
    if (other is null) return false;
    return Unwrap().EqualsCore(other.Unwrap());
  }

  protected abstract bool EqualsCore(CsType? other);

  public abstract override int GetHashCode();
}
