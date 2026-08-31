using System.Runtime.InteropServices;

namespace NativeMod.SourceGen.Lang.Cs;

/// <summary>
/// Represents simple marshaling behavior for converting between C++ and C# types.
/// This class is responsible for handling the conversion of data types,
/// ensuring that data is correctly marshaled between the two languages.
/// </summary>
public class CsMarshaller {
  /// <summary>
  /// Indicates the type that the unmanaged function pointer must use to be compatible with this marshaller.
  /// </summary>
  public required string CppType { get; init; }

  /// <summary>
  /// Represents when a type is read from C++ (such as an <see cref="UnmanagedCallersOnlyAttribute"/> argument)
  /// and needs to be converted to a C# type.
  /// This delegate defines the method signature for writing the C# expression to convert the value.
  /// </summary>
  public required Action<TextWriter, string> WriteFromCpp { get; init; }

  /// <summary>
  /// Represents when a type is written to C++ (such as invoking an originally C++ function)
  /// and needs to be converted from a C# type.
  /// This delegate defines the method signature for writing the C# expression to convert the value.
  /// </summary>
  public required Action<TextWriter, string> WriteToCpp { get; init; }
}
