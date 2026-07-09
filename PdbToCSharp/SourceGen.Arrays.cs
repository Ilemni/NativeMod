using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPdb.Native;
using SharpPdb.Native.Types;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public sealed partial class SourceGen {
  private readonly Dictionary<PdbArrayType, string> _inlineArrayTypes = [];
  private readonly Dictionary<string, string> _inlineArrayNames = [];

  // TODO: !!! InlineArrays need to use fully qualified name, replacing all separators with '_'

  /// Many fields are a fixed-size array buffers.
  /// To support these, we create a <see cref="InlineArrayAttribute"/>
  /// struct for every unique occurence of a fixed-size array type.
  private void ProcessInlineArrays() {
    List<PdbArrayType> arrays = [];
    ProcedureHelper.ReplaceNullSymbols(PdbFile);
    for (int i = 0; i < PdbFile.TpiStream.TypeRecordCount; i++) {
      try {
        if (_pdb.TryGetType(i, out PdbArrayType? arr)) {
          arrays.Add(arr);
        }
      }
      catch (Exception) {
        // Sometimes we get null-refs, such as complex pointer whose element is null.
        // Just skip those.
      }
    }

    var test = PdbFile.TpiStream[TypeLeafKind.LF_ARRAY];

    HashSet<string> inlineArrayNames = [];
    // Sort arrays so that we process non-array element types first.
    foreach (PdbArrayType arr in arrays.OrderBy(GetArrayDimensions)) {
      StructDeclarationSyntax? member = TryCreateInlineArrayType(arr);
      if (member is not null) {
        _ns.InlineArrayNs = _ns.InlineArrayNs.AddMember(member);
      }
    }

    return;

    StructDeclarationSyntax? TryCreateInlineArrayType(PdbArrayType arr) {
      GetArrayDimensionsAndElement(arr, out PdbType type);
      if (_missingTypes.Contains(type.Name)) {
        return null;
      }

      string elementName = GetQualifiedName(type);
      string start = $"InlineArray_{elementName.SanitizeName(true, true)}";
      string end = $"_{arr.Count}";
      PdbType elementType = arr.ElementType;
      while (elementType is PdbArrayType inner) {
        end = $"_{inner.Count}{end}";
        elementType = inner.ElementType;
      }

      string name = start + end;

      AddQualifiedName(arr, name);
      _inlineArrayTypes[arr] = name;
      _inlineArrayNames[arr.Name] = name;

      ulong count = Math.Max(arr.Count, 1);
      if (arr.Count == 0) {
        Log.Warn($"Array type {arr.Name} has count 0, using 1 instead.");
      }

      // Only create the type if it's not a duplicate
      return inlineArrayNames.Add(name) ? CreateInlineArraySyntax(arr, name, elementName, count) : null;
    }
  }

  private static int GetArrayDimensions(PdbType type) => GetArrayDimensionsAndElement(type, out _);

  private static int GetArrayDimensionsAndElement(PdbType type, out PdbType element) {
    element = type;
    int depth = 0;
    while (element is PdbArrayType array) {
      depth++;
      element = array.ElementType;
    }

    return depth;
  }
}
