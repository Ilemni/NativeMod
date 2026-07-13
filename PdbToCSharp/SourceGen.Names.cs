using System.Diagnostics.CodeAnalysis;
using PdbToCSharp.Types;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows;
using PdbUdt = SharpPdb.Native.Types.PdbUserDefinedType;

namespace PdbToCSharp;

public sealed partial class SourceGen {
  // TODO: Make sure CsTypes make use of the functionality of the remaining methods here before removing them
  //  This partial is to be removed once it is no longed needed as a reference.
  /// Used to replace unnamed enums with "unnamed_enum_n"
  internal int UnnamedStructs;
  internal int UnnamedUnions;
  internal int UnnamedEnums;

  private readonly Dictionary<string, PdbType> _uniqueCsNames = [];
  private readonly Dictionary<string, List<PdbType?>> _duplicateCsNames = [];
  private readonly Dictionary<PdbType, string> _typeNames = [];

  private readonly Dictionary<TypeIndex, string> _fullNames = [];
  private readonly Dictionary<string, string> _fullNamesByUnique = [];

  private string GetOrCreateTypeName<T>(T type, string? createName = null) where T : PdbType {
    if (type.TypeIndex.IsSimple) {
      return ToCsName(type.TypeIndex);
    }

    if (TryResolveType(type, out PdbType? resolved)) {
      type = (T)resolved;
    }

    if (_typeNames.TryGetValue(type, out string? existingName)) {
      return existingName;
    }

    if (type is PdbUdt udt && TryGetQualifiedName(udt, out string? fullName)) {
      _typeNames[type] = fullName;
      return fullName;
    }

    switch (type) {
      // Types which are typically just fields
      case PdbPointerType pointerType: {
        // int depth = GetPointerDepthAndElement(pointerType, out PdbType elementType);
        // string name = GetOrCreateTypeName(elementType) + new string('*', depth);
        // _typeNames[type] = name;
        return null;
      }
      case PdbFunctionType functionType: {
        string returnTypeName = GetOrCreateTypeName(functionType.ReturnType);
        string funcPointerName = $"func_{returnTypeName}_args{functionType.ParameterCount}";
        _typeNames[type] = funcPointerName;
        return funcPointerName;
      }
      // Although C++ uses fixed size arrays easily, in C# we're creating InlineArray structs for them
      case PdbArrayType arr: {
        ulong count = Math.Max(arr.Count, 1);
        if (arr.ElementType is PdbArrayType innerArray && _inlineArrayTypes.TryGetValue(innerArray, out string? n)) {
          // remove inner "InlineArray_" prefix from nested array name
          string inner = n[12..];
          createName = $"InlineArray_{inner}_{count}";
          break;
        }

        string elementName = GetOrCreateTypeName(arr.ElementType).SanitizeName(true);
        createName = $"InlineArray_{elementName}{count}";

        break;
      }
      case PdbEnumType enumType:
        createName = CreateEnumTypeName(enumType);
        break;
    }

    string attempt = createName ?? type.Name.SanitizeName(removePtr: type is PdbUdt);

    if (_uniqueCsNames.TryAdd(attempt, type)) {
      _typeNames[type] = attempt;
      if (resolved is not null && !ReferenceEquals(type, resolved)) {
        _typeNames[resolved] = attempt;
      }
    }
    else {
      PdbType existing = _uniqueCsNames[attempt];
      string? classUnique = (type as PdbUdt)?.UniqueName;
      string? existingUnique = (existing as PdbUdt)?.UniqueName;
      bool uniquesMatch = existingUnique == classUnique || existingUnique is null && classUnique is null;

      if (!uniquesMatch && !attempt.StartsWith("unnamed_")) {
        if (_duplicateCsNames.TryGetValue(attempt, out var duplicates)) {
          duplicates.Add(type);
        }
        else {
          _duplicateCsNames[attempt] = [existing, type];
        }

        Log.Warn(
          $"Type name '{attempt}' already exists. From {type.GetType().Name}\n" +
          $"       Existing: {existing} ({existing.TypeIndex.ArrayIndex})\n" +
          $"            New: {type} ({type.TypeIndex.ArrayIndex})" +
          (!string.IsNullOrEmpty(existingUnique) || !string.IsNullOrEmpty(classUnique)
            ? $"\n       Existing: {existingUnique}\n" +
            $"            New: {classUnique}"
            : ""));
      }

      _typeNames[type] = attempt;
    }

    return attempt;
  }

  private string? CreateEnumTypeName(PdbEnumType enumType) {
    if (enumType.Name == "<unnamed-tag>") {
      return "unnamed_enum_" + ++UnnamedEnums;
    }

    if (enumType.Name.StartsWith("<unnamed-type-")) {
      //type is named after field, i.e. "<unnamed-type-myFieldName>", extract myFieldName
      int start = "<unnamed-type-".Length;
      int end = enumType.Name.IndexOf('>', start);
      if (end > start) {
        string fieldName = enumType.Name[start..end];
        return fieldName.SanitizeName();
      }
    }

    string? createName = null;
    if (!enumType.IsNested) {
      createName = enumType.Name.SanitizeName();
    }

    else if (_fullNames.TryGetValue(enumType.TypeIndex, out string? fullName)) {
      createName = fullName;
    }

    if (!enumType.TagRecord.HasUniqueName) {
      return createName;
    }

    var span = enumType.UniqueName.AsSpan();
    if (enumType.Name != "<unnamed-tag>") {
      // A different enum of the same name may be numbered differently in its UniqueName, so we extract that number
      int index = span.IndexOf("@?", StringComparison.Ordinal);
      if (index <= 0) {
        return createName;
      }

      char c = span[index + 2];
      if (char.IsDigit(c) && char.GetNumericValue(c) > 1) {
        createName = enumType.Name.SanitizeName() + '_' + c;
      }

      return createName;
    }

    // Extract the existing naming from the UniqueName
    // if (span.IndexOf(".?AW4", StringComparison.Ordinal) is var idx and > -1) {
    //   span = span[(idx + 5)..];
    //   if (span[0] != '<') {
    //     if (span.IndexOf("@@", StringComparison.Ordinal) is var idx2 and > -1) {
    //       span = span[..idx2];
    //       // Skip "<unnamed-enum>"
    //       createName = span.ToString().SanitizeName();
    //     }
    //   }
    // }

    return createName;
  }

  private string GetQualifiedName(PdbType type) {
    return type switch {
      PdbSimpleType => ToCsName(type.TypeIndex),
      // Hack: should store this instead of creating it just-in-time
      PdbPointerType pointer => GetPointerDepthAndElement(/*pointer*/ null, out CsType element) switch {
        var depth => GetQualifiedName(/*element*/ null) + "Ptr" + depth
      },
      PdbFunctionType function => function.ReturnType switch {
        PdbSimpleType { Name: "void" } => $"action_{string.Join('_', function.Arguments.Select(GetQualifiedName))}",
        var ret => $"func_{GetQualifiedName(ret)}_{string.Join('_', function.Arguments.Select(GetQualifiedName))}"
      },
      // TODO: sigh, yet another kind of name to handle
      PdbMemberFunctionType mFunction => "void*",
      PdbUdt udt => GetQualifiedName(udt),
      // TODO: another type that has missing names
      PdbArrayType arr => _inlineArrayNames.GetValueOrDefault(arr.Name, "INLINE_ARRAY_MISSING_TYPE"),
      //throw new KeyNotFoundException($"Inline array type not found for {arr.Name} ({arr.TypeIndex})"),
      _ => throw new KeyNotFoundException($"Unhandled type {type.GetType().Name} for qualified name retrieval")
    };
  }

  private string GetQualifiedName(PdbUdt udt) {
    if (_fullNames.TryGetValue(udt.TypeIndex, out string? fullName)) {
      return fullName;
    }

    if (udt.UniqueName is not null && _fullNamesByUnique.TryGetValue(udt.UniqueName, out fullName)) {
      _fullNames[udt.TypeIndex] = fullName;
      return fullName;
    }

    // Hack, prefer to create the name ahead of time
    return udt.Name.SanitizeName(true);
  }

  private bool TryGetQualifiedName(PdbUdt udt, [NotNullWhen(true)] out string? fullName) {
    if (_fullNames.TryGetValue(udt.TypeIndex, out fullName)) {
      return true;
    }

    if (udt.UniqueName is { } unique &&
        _fullNamesByUnique.TryGetValue(unique, out fullName)) {
      _fullNames[udt.TypeIndex] = fullName;
      return true;
    }

    return false;
  }
}
