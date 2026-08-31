using System.CodeDom.Compiler;
using System.Globalization;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp.Lang.Cs;

public sealed partial class CsGen {
  private static void WriteStruct(CsStructure csStruct, IndentedTextWriter writer) {
    if (csStruct.IsForwardReference) {
      WriteForwardReferenceStruct(csStruct, writer);
      return;
    }

    XmlDocs.Types.WriteStructType(writer, csStruct);

    // Write attributes
    writer.WriteGeneratedCodeAttribute();
    writer.WriteStructLayoutAttribute(csStruct.Size, prependGlobal: true);

    // public struct [@]StructName {
    writer.Write("public struct ");

    // Handle warning CS8981: The type name only contains lower-cased ascii characters.
    //                        Such types may become reserved for the language.
    writer.WriteIf("@", csStruct.SelfName.All(char.IsLower));
    writer.Write(csStruct.SelfName);

    if (csStruct.AllFields.Count == 0) {
      writer.WriteLine(';');
      return;
    }

    using (writer.BracedScope()) {
      // Write inner static class Pointers for memory address access
      WritePointers(csStruct, writer);

      bool hasBaseClasses = csStruct.BaseClasses.Length > 0;
      bool hasStaticFields = csStruct.StaticFields.Any(s => s is CsConstantField or CsRegularStaticField);
      bool hasMethods = csStruct.NonVirtualMethods.Length > 0 || csStruct.VirtualMethods.Length > 0;
      bool hasNestedClasses = csStruct.NestedClasses.Count > 0;
      bool hasInstanceFields = csStruct.InstanceFields.Length > 0 || HasAnyInheritedFields(csStruct);
      bool hasBitfields = hasInstanceFields && csStruct.InstanceFields.OfType<CsBitField>().Any();

      if (csStruct.VfTable is not null) {
        writer.WriteLine("/// Pointer to the virtual function table (vtable) of this struct.");
        writer.WriteFieldOffsetAttribute(0);
        writer.Write(" public readonly unsafe Pointers.VTable* vTable;");

        if (hasBaseClasses || hasStaticFields || hasInstanceFields || hasMethods || hasNestedClasses) {
          writer.WriteLine();
        }
      }

      TextWriter tWriter = writer;
      tWriter.WriteIf(WriteBaseClasses, csStruct, hasBaseClasses);
      writer.WriteLineIf(hasBaseClasses && (hasStaticFields || hasInstanceFields || hasMethods || hasNestedClasses));
      tWriter.WriteIf(WriteStaticFields, csStruct, hasStaticFields);
      writer.WriteLineIf(hasStaticFields && (hasInstanceFields || hasMethods || hasNestedClasses));
      tWriter.WriteIf(WriteInstanceFields, csStruct, hasInstanceFields);
      writer.WriteLineIf(hasInstanceFields && (hasBitfields || hasMethods || hasNestedClasses));
      tWriter.WriteIf(WriteBitFields, csStruct, hasBitfields);
      writer.WriteIf(WriteMethods, csStruct, hasMethods);
      writer.WriteLineIf(hasMethods && hasNestedClasses);
      writer.WriteIf(WriteNestedTypes, csStruct, hasNestedClasses);
    }
  }

  private static bool HasAnyInheritedFields(CsStructure csStruct) {
    return csStruct.BaseClasses.Any(b => b.BaseType.InstanceFields.Length > 0 || HasAnyInheritedFields(b.BaseType));
  }

  private static void WriteForwardReferenceStruct(CsStructure csStruct, IndentedTextWriter writer) {
    XmlDocs.Types.WriteForwardReferenceType(writer, csStruct);

    // public partial struct [@]StructName <;|{ Nested classes ... }>
    writer.WriteGeneratedCodeAttribute(newLine: false);
    writer.Write(" public struct ");
    writer.WriteIf("@", csStruct.SelfName.All(char.IsLower));

    writer.Write(csStruct.SelfName);
    if (csStruct.NestedClasses.Count == 0) {
      writer.WriteLine(';');
      return;
    }

    using (writer.BracedScope()) {
      WriteNestedTypes(writer, csStruct);
    }
  }

  private static void WriteEnumType(CsEnum csEnum, IndentedTextWriter writer) {
    // Get a compatible underlying type
    string underlying = csEnum.Underlying.FullName;
    if (underlying is "bool" or "void") {
      underlying = "byte";
    }

    if (csEnum.Values.Any(v => v.Value is uint and > int.MaxValue)) {
      underlying = "uint";
    }

    XmlDocs.Types.WriteEnumType(writer, csEnum);
    writer.WriteGeneratedCodeAttribute();

    // Write enum declaration
    writer.Write("public enum ");
    writer.WriteIf("@", csEnum.SelfName.All(char.IsLower));
    writer.Write(csEnum.SelfName);
    writer.WriteManyIf([" : ", underlying], underlying is not "int");

    // Write enum body
    using (writer.BracedScope()) {
      writer.WriteLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
      foreach (CsEnumField enumValue in csEnum.Values) {
        string name = enumValue.Name.KeywordToVerbatim();
        string value = enumValue.Value.ToString()!;
        writer.WriteManyLine(name, " = ", value, ",");
      }

      writer.WriteLine("#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member");
    }

    writer.WriteLine();
  }

  #region Write Base Classes

  private static void WriteBaseClasses(TextWriter writer, CsStructure csStruct) {
    writer.WriteLine("// Base classes");
    for (int i = 0; i < csStruct.BaseClasses.Length; i++) {
      WriteBaseClass(csStruct, writer, i);
    }
  }

  private static void WriteBaseClass(CsStructure csStruct, TextWriter writer, int i) {
    CsBaseClass baseClass = csStruct.BaseClasses[i];
    XmlDocs.Members.WriteBaseClass(writer, baseClass);

    // Field declaration
    writer.WriteFieldOffsetAttribute(baseClass.Offset);

    writer.WriteMany(" public ", baseClass.BaseType.GlobalQualifiedName, " Base");
    writer.WriteIf((i + 1).ToString(), csStruct.BaseClasses.Length > 1);
    writer.WriteLine(';');
  }

  #endregion

  #region Write Static Fields

  private static void WriteStaticFields(TextWriter writer, CsStructure csStruct) {
    using (writer.Region("Static Fields")) {
      foreach (CsStaticField field in csStruct.StaticFields) {
        WriteStaticField(writer, field);
      }
    }
  }

  private static void WriteStaticField(TextWriter writer, CsStaticField field) {
    if (field is not CsConstantField and not CsRegularStaticField) {
      return;
    }

    XmlDocs.Members.WriteStaticField(writer, field);

    switch (field) {
      case CsConstantField constant: {
        WriteConstantField(writer, constant);
        break;
      }
      case CsRegularStaticField staticField:
        WriteRegularStaticField(writer, staticField);
        break;
    }
  }

  private static void WriteConstantField(TextWriter writer, CsConstantField constant) {
    CsType fType = constant.FieldType.Unwrap();

    TypeIndex fTypeIndex = fType.TypeIndex;
    bool needsCast = fTypeIndex.SimpleKind is not SimpleTypeKind.Float32 and not SimpleTypeKind.Boolean8;
    bool writeUnchecked = (fTypeIndex.SimpleKind is SimpleTypeKind.UInt16 or SimpleTypeKind.UInt32
        or SimpleTypeKind.UInt64
        or SimpleTypeKind.UInt16Short or SimpleTypeKind.UInt32Long or SimpleTypeKind.UInt64Quad)
      && constant.Value is < 0 or sbyte and < 0 or short and < 0 or long and < 0;

    string value = !fTypeIndex.IsSimple
      ? constant.Value.ToString()!
      : fTypeIndex.SimpleKind switch {
        SimpleTypeKind.Boolean8 => (ushort)constant.Value > 0 ? "true" : "false",
        SimpleTypeKind.Float32 => BitConverter.UInt32BitsToSingle((uint)constant.Value)
          .ToString(CultureInfo.InvariantCulture) is { } f and not "NaN"
          ? f + 'f'
          : "float.NaN",
        _ => constant.Value.ToString()!
      };

    string fieldType = fType.GlobalQualifiedName;
    writer.WriteMany("public const ", fieldType, " ", constant.Name, " = ");

    writer.WriteIf("unchecked(", writeUnchecked);
    writer.WriteManyIf(["(", fieldType, ")("], needsCast);

    writer.Write(value);

    writer.WriteIf(")", needsCast);
    writer.WriteIf(")", writeUnchecked);
    writer.WriteLine(";");
  }

  private static void WriteRegularStaticField(TextWriter writer, CsRegularStaticField staticField) {
    string fieldType = staticField.FieldType.GlobalQualifiedName;
    string fieldName = staticField.Name.KeywordToVerbatim();
    string address = staticField.RelativeVirtualAddress.ToString();
    writer.WriteMany("public static unsafe ref ", fieldType, " ", fieldName, " => ");
    writer.WriteManyLine("ref *((", fieldType, "*)(", MemoryAddress, " + ", address, "));");
  }

  #endregion

  #region Write Instance Fields

  private static void WriteInstanceFields(TextWriter writer, CsStructure csStruct) {
    if (csStruct.InstanceFields.Length > 0) {
      using (writer.Region("Instance Fields")) {
        foreach (CsInstanceField field in csStruct.InstanceFields) {
          if (field is not CsBitField) {
            WriteInstanceField(csStruct, writer, field);
          }
        }
      }
    }

    if (HasAnyInheritedFields(csStruct)) {
      using (writer.Region("Inherited Instance Fields")) {
        WriteInheritedInstanceFields(writer, csStruct, 0);
      }
    }
  }

  private static void WriteInheritedInstanceFields(TextWriter writer, CsStructure csStruct, uint offset) {
    foreach (CsBaseClass baseClass in csStruct.BaseClasses) {
      CsStructure baseType = baseClass.BaseType;
      uint baseOffset = baseClass.Offset + offset;
      foreach (CsInstanceField field in baseType.InstanceFields) {
        if (field is not CsBitField) {
          WriteInstanceField(csStruct, writer, field, baseOffset);
        }
      }

      WriteInheritedInstanceFields(writer, baseType, baseOffset);
    }
  }

  private static void WriteInstanceField(CsStructure csStruct, TextWriter writer, CsInstanceField field,
    uint baseOffset = 0) {
    // Rename field if the field name is identical to its type name, and is a nested type of the current struct
    // This modification prevents a name collision
    string fieldName;
    bool isInherited = !field.Container.Equals(csStruct);
    if (isInherited && csStruct.InstanceFields.Any(f => f.Name == field.Name)) {
      fieldName = "Base_" + field.Name;
    }
    else if (field.FieldType is CsUdt fUdt && csStruct.Equals(fUdt.Parent) && field.Name == fUdt.SelfName) {
      fieldName = field.Name.KeywordToVerbatim();
      if (!fieldName.Any(char.IsLetter)) {
        fieldName += '_';
      }
      else {
        fieldName = string.Create(fieldName.Length, fieldName, static (str, n) => {
          // Rename field to have inverse case for the first letter, to avoid name collision with the nested type
          n.AsSpan().CopyTo(str);
          for (int i = 0; i < n.Length; i++) {
            ref char c = ref str[i];
            if (char.IsLetter(c)) {
              c = char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c);
              break;
            }
          }
        });
      }
    }
    else {
      fieldName = field.Name.KeywordToVerbatim();
    }

    XmlDocs.Members.WriteInstanceField(writer, field, isInherited);

    string fieldType = field.FieldType.GlobalQualifiedName;
    if (field.FieldType is CsPointerType { ElementType: CsProcedureType e }) {
      fieldType = e.DelegateType;
    }

    // Field declaration
    writer.WriteFieldOffsetAttribute(field.Offset + baseOffset);
    writer.Write(" public ");
    writer.WriteIf("unsafe ", field.FieldType is CsPointerType or CsSimplePointerType);
    writer.WriteManyLine(fieldType, " ", fieldName, ";");
  }

  private static void WriteBitFields(TextWriter writer, CsStructure csStruct) {
    // TODO: Properly implement bitfields
    return;

    var group = csStruct.InstanceFields.OfType<CsBitField>().GroupBy(b => b.Offset);
    foreach (var bitFields in group) {
      WriteBitFields(writer, bitFields);
    }
  }

  private static void WriteBitFields(TextWriter writer, IGrouping<uint, CsBitField> bitFields) {
    const string propHeaderFormat = "public {0} {1}";
    const string getterNoShiftFormat = "get => ({0})(_bitfieldStorage{1} & {2});";
    const string getterShiftFormat = "get => ({0})((_bitfieldStorage{1} >> {2}) & {3});";
    const string setterNoShiftFormat =
      "set => _bitfieldStorage{1} = ({0})((_bitfieldStorage{1} & ~{2}) | (({0})value & {2}));";
    const string setterShiftFormat =
      "set => _bitfieldStorage{1} = ({0})((_bitfieldStorage{1} & ~({3} << {2})) | ((({0})value & {3}) << {2}));";
    uint byteOffset = bitFields.Key;
    uint totalBits = bitFields.Select(b => b.BitSize + b.BitOffset).Max();
    uint totalBytes = (totalBits + 7) / 8;
    string backingType = totalBytes switch {
      1 => "byte",
      2 => "ushort",
      4 => "uint",
      8 => "ulong",
      _ => throw new InvalidDataException(
        $"Bitfield group at offset 0x{byteOffset:X} has total size {totalBytes} bytes, which is not supported.")
    };

    string n = "__bitfield_0x" + byteOffset.ToString("X2");

    XmlDocs.Members.WriteBitfieldBacking(writer, byteOffset, totalBits, totalBytes);
    writer.WriteFieldOffsetAttribute(byteOffset);
    writer.WriteManyLine(" private ", backingType, " ", n, ";");

    foreach (CsBitField field in bitFields) {
      XmlDocs.Members.WriteBitfield(writer, field);

      string offset = field.BitOffset.ToString();
      string mask = ((1UL << (int)field.BitSize) - 1).ToString("X");
      string type = field.FieldType.GlobalQualifiedName;
      string name = field.Name.KeywordToVerbatim();

      // Write property for the bitfield
      writer.WriteMany("public ", type, " ", name, " {");
      writer.WriteMany(" get => (", type, ")(", n, " >> ", offset, ") & ", mask, ";");
      writer.WriteMany(" set => ", n, " = (", n, " & ~(", mask, " << ", offset, ")) | (((ulong)value & ", mask, ") << ",
        offset, ");");
      writer.WriteLine(" }");
    }
  }

  #endregion

  #region Write Methods and related structures

  private static void WritePointers(CsStructure csStruct, IndentedTextWriter writer) {
    // Get own vtable or parent's vtable if it exists
    bool anyPointers = csStruct.DefinedMethods.Length != 0;
    VirtualFunctionTableShapeRecord? vfTable = csStruct.VfTable;
    if (!anyPointers && csStruct is { VfAddress: 0, VfTable: null } && vfTable is null) {
      return;
    }

    writer.WriteLine("/// Inner static class that contains function pointers for methods.");
    writer.Write("public static class Pointers");
    using (writer.BracedScope()) {
      if (vfTable is not null) {
        WriteVTable(csStruct, writer, vfTable);
      }

      foreach (CsMethod m in csStruct.NonVirtualMethods.Where(m => m.IsDefined).Distinct()) {
        WritePointer(csStruct, m, writer);
      }

      foreach (CsMethod m in csStruct.VirtualMethods.Where(m => m.IsDefined &&
                 ReferenceEquals(csStruct, m.MemberFunction.ClassType))) {
        WritePointer(csStruct, m, writer);
      }
    }

    return;

    static void WritePointer(CsStructure csStruct, CsMethod m, IndentedTextWriter writer) {
      CsMemberFunctionType mFunc = m.MemberFunction;
      if (mFunc.HasAnyVariadic) {
        return;
      }

      if (m.MethodRecord.ThisPointerAdjustment != 0) {
        return;
      }

      bool cannotWriteXml = m.OverloadId != 0 && m.MemberFunction.ParameterTypes
        .Any(p => p is CsPointerType { InnerElement: CsFunctionType or CsMemberFunctionType });
      if (cannotWriteXml) {
        // https://github.com/dotnet/roslyn/issues/48363
        writer.WriteLine("# pragma warning disable CS1591 // Requires https://github.com/dotnet/roslyn/issues/48363");
      }
      else {
        XmlDocs.Members.WriteMethodPointer(writer, csStruct, m);
      }

      // public static readonly unsafe delegate* unmanaged[CallConv]<T1, T2, ..., TResult>
      // MethodName = (delegate* unmanaged[CallConv]<T1, T2, ..., TResult>)(FunctionAddress + Offset);
      string offset = m.Address.ToString();
      writer.WriteManyLine("public static readonly unsafe ", mFunc.DelegateType);
      using (writer.WithIndent()) {
        writer.WriteMany(m.DelegateFieldName, " = (", mFunc.DelegateType, ")");
        writer.WriteMany("(", FunctionAddress, " + ", offset, ");");
      }

      writer.WriteLine();

      if (cannotWriteXml) {
        writer.WriteLine("# pragma warning restore CS1591");
      }
    }
  }

  private static void WriteVTable(CsStructure csStruct, IndentedTextWriter writer,
    VirtualFunctionTableShapeRecord vfTable) {
    // int highestVirtualIndex = csStruct.Methods.Select(m => m.Record.VFTableOffset).Max() / 8;
    int vfSlots = vfTable.Slots.Length;
    // bool isLargerThanVfTable = highestVirtualIndex >= vfSlots;
    // if (isLargerThanVfTable) {
    //   string name = csStruct.SelfName;
    //   Log.Warn($"VfTable for {name} has {vfSlots} slots, but highest virtual index is {highestVirtualIndex}");
    // }

    // int numSlots = Math.Max(vfSlots, highestVirtualIndex + 1);

    // Write InlineArray type for this VTable
    XmlDocs.Types.WriteVftType(writer, csStruct, vfSlots);
    writer.WriteManyLine("[System.Runtime.CompilerServices.InlineArray(", vfSlots.ToString(), ")]");
    writer.Write("public struct VTable");
    Dictionary<int, CsMethod> vfMethods = new();
    var virtualMethods = csStruct.VirtualMethods;

    foreach (CsMethod method in virtualMethods) {
      // BinaryNinja behavior is to overwrite the method in the vtable if it has the same index, so we will do the same
      vfMethods[method.VfSlot] = method;
    }

    if (vfMethods.Count != vfSlots) {
      CsStructure? baseType = csStruct.BaseClasses.FirstOrDefault()?.BaseType;
      while (baseType is not null && vfMethods.Count < vfSlots) {
        foreach (CsMethod m in baseType.AllMethods.Where(m => m.IsVirtual)) {
          vfMethods.TryAdd(m.VfSlot, m);
        }

        baseType = baseType.BaseClasses.FirstOrDefault()?.BaseType;
      }
    }

    using (writer.BracedScope()) {
      writer.WriteLine("private ulong _slot0;");

      foreach (CsMethod m in vfMethods.Values.Where(m => m.IsVirtual)
                 .OrderBy(m => m.VfSlot)
                 .Distinct()) {
        XmlDocs.Members.WriteVfShapeMethod(writer, m, m.VfSlot);
        writer.Write("public readonly unsafe ");
        writer.WriteMany(m.MemberFunction.DelegateType, " ", m.DelegateFieldName, " => ");
        writer.WriteMany("(", m.MemberFunction.DelegateType, ")");
        writer.WriteMany("this[", m.VfSlot.ToString(), "];");
        writer.WriteLine();
      }
    }

    if (csStruct.VfAddress is > 0 and var addr) {
      XmlDocs.Members.WriteVftPointer(writer);
      writer.Write("public static readonly unsafe VTable* vTable = ");
      writer.WriteMany("(VTable*)(", MemoryAddress, " + ", addr.ToString(), ");");
      writer.WriteLine();
    }
    else {
      writer.WriteManyLine("// Missing VfTable address for ", csStruct.FullyQualifiedName);
    }
  }

  private static void WriteMethods(IndentedTextWriter writer, CsStructure csStruct) {
    var methods = csStruct.NonVirtualMethods;
    if (methods.Length > 0) {
      var staticMethods = methods.Where(m => m.MemberFunction.IsStatic);
      if (staticMethods.Any()) {
        using (writer.Region("Static Methods")) {
          foreach (CsMethod method in staticMethods) {
            WriteMethod(writer, csStruct, method);
          }
        }
      }


      var instanceMethods = methods.Where(m => !m.MemberFunction.IsStatic);
      if (instanceMethods.Any()) {
        using (writer.Region("Instance Methods")) {
          foreach (CsMethod method in instanceMethods) {
            WriteMethod(writer, csStruct, method);
          }
        }
      }
    }

    if (csStruct.VirtualMethods.Any(v => ReferenceEquals(csStruct, v.MemberFunction.ClassType))) {
      using (writer.Region("Virtual Methods")) {
        foreach (CsMethod method in csStruct.VirtualMethods.Distinct()) {
          if (ReferenceEquals(csStruct, method.MemberFunction.ClassType)) {
            WriteMethod(writer, csStruct, method);
          }
        }
      }
    }
  }

  private static void WriteMethod(IndentedTextWriter writer, CsStructure csStruct, CsMethod method) {
    int vfOffset = method.VfSlot;
    bool isDefined = method.IsDefined;

    if (method.Record.Attributes.MethodKind is MethodKind.PureVirtual or MethodKind.PureIntroducingVirtual) {
      if (!method.IsVirtual) {
        WriteMissingMethodInfo(writer, method, "Pure virtual method with no vtable offset.");
        return;
      }

      XmlDocs.Members.WriteMethod(writer, method);
      WriteVirtualMethod(writer, method);
      return;
    }

    if (method.CppName.StartsWith("operator")) {
      if (isDefined) {
        WriteOperator(writer, method);
      }
      else {
        WriteMissingMethodInfo(writer, method, "Operator method with no procedure info.");
      }

      return;
    }

    if (!isDefined && vfOffset == -1) {
      if (method.Name is "d" && csStruct.SelfName.StartsWith("TNode")) {
        WriteSpecialCaseTNodeMethod(writer, method);
      }
      else {
        WriteMissingMethodInfo(writer, method, "No procedure info and not override.");
      }

      return;
    }

    if (method.MemberFunction.HasAnyVariadic) {
      WriteMissingMethodInfo(writer, method, "Contain variadic parameters.");
      return;
    }

    if (method.MethodRecord.ThisPointerAdjustment != 0) {
      WriteMissingMethodInfo(writer, method, "Non-zero this pointer adjustment.");
      return;
    }

    XmlDocs.Members.WriteMethod(writer, method);
    if (!method.IsVirtual) {
      WriteNormalMethod(writer, method);
    }
    else {
      WriteVirtualMethod(writer, method);
    }

    return;

    static void WriteMissingMethodInfo(IndentedTextWriter writer, CsMethod method, string reason) {
      // This is regular comment, so avoid CsType.XmlCppName
      writer.Write("// Skipping method ");
      writer.Write('[');
      writer.Write(method.CppName);
      writer.Write('(');
      writer.WriteParameterTypes(method.MemberFunction.ParameterTypes, p => p.CppName);
      writer.Write(") -> ");
      writer.Write(method.MemberFunction.ReturnType.CppName);
      writer.Write(']');
      writer.Write(" because: ");
      writer.WriteLine(reason);
    }
  }

  private static void WriteNormalMethod(IndentedTextWriter writer, CsMethod method) {
    CsMemberFunctionType mFunc = method.MemberFunction;
    string returnType = mFunc.ReturnType.GlobalQualifiedName;

    // public [static] unsafe <T|void*> MethodName([T arg1][, T arg2] ...) {
    writer.Write("public ");
    writer.WriteIf("static ", mFunc.IsStatic);
    writer.Write("unsafe ");

    writer.WriteMany(returnType, " ", method.Name, "(");
    writer.WriteParameterTypesAndNames(method.Parameters);
    writer.Write(")");
    using (writer.BracedScope(newLine: false)) {
      WriteFixedMethodBody(writer, method);
    }

    writer.WriteLine();
  }

  private static void WriteVirtualMethod(IndentedTextWriter writer, CsMethod method) {
    // public [static] unsafe <T|void*> MethodName([T arg1][, T arg2] ...) {
    CsMemberFunctionType mFunc = method.MemberFunction;
    string returnTypeName = mFunc.ReturnType.GlobalQualifiedName;

    writer.Write("public ");
    writer.WriteIf("static ", mFunc.IsStatic);
    writer.Write("unsafe ");

    writer.WriteMany(returnTypeName, " ", method.Name, "(");
    writer.WriteParameterTypesAndNames(method.Parameters);

    writer.Write(")");
    using (writer.BracedScope()) {
      writer.WriteManyLineIf([returnTypeName, " returnBuffer;"], mFunc.NeedsReturnBuffer);

      // fixed (<T>* pThis = &this) {
      string thisType = mFunc.ThisType.GlobalQualifiedName;
      writer.WriteMany("fixed (", thisType, " pThis = &this) ");
      writer.WriteIf("return ", mFunc.HasRealReturn);

      writer.WriteMany("vTable->", method.DelegateFieldName);
      // string vfPos = (method.Record.VFTableOffset / 8).ToString();
      // writer.Write("(");
      // writer.WriteMany("(", mFunc.DelegateType, ")");
      // writer.WriteMany("(*vTable)[", vfPos, "]");
      // writer.Write(")");

      bool needsComma = false;
      writer.Write("(");
      writer.WriteIf("&returnBuffer", mFunc.NeedsReturnBuffer, ref needsComma);
      writer.WriteIf("pThis", !mFunc.IsStatic, ref needsComma);
      writer.WriteParameterNamesToCpp(method.Parameters, ref needsComma);
      writer.WriteLine(");");
      writer.WriteLineIf("return returnBuffer;", mFunc.NeedsReturnBuffer);
    }
  }

  /// <summary>
  /// Writes out the native operator overload template and/or the fallback method format using IndentedTextWriter.
  /// </summary>
  private static void WriteOperator(IndentedTextWriter writer, CsMethod method) {
    if (method.CppName is "operator[]") {
      WriteIndexerOperator(writer, method);
      return;
    }

    CsMemberFunctionType mFunc = method.MemberFunction;
    CsType r = mFunc.ReturnType;
    string returnType = r.GlobalQualifiedName;
    string methodName = method.Name;

    // TODO: Implement operators as actual operators.
    XmlDocs.Members.WriteOperator(writer, method);
    writer.WriteMany("public unsafe ", returnType, " ", methodName, "(");
    writer.WriteParameterTypesAndNames(method.Parameters);
    writer.Write(')');
    using (writer.BracedScope(newLine: false)) {
      WriteFixedMethodBody(writer, method);
    }

    writer.WriteLine();
  }

  private static void WriteIndexerOperator(IndentedTextWriter writer, CsMethod method) {
    CsMemberFunctionType mFunc = method.MemberFunction;
    CsType r = mFunc.ReturnType;
    string returnType = r.GlobalQualifiedName;
    string methodName = method.DelegateFieldName;
    CsType? refRet = (r as CsPointerType)?.ElementType ?? (r as CsSimplePointerType)?.ElementType;
    bool isRefReturn = refRet is not null;

    XmlDocs.Members.WriteIndexerOperator(writer, method);

    // If any member is named "Item", we have to apply IndexerNameAttribute to the indexer.
    if (IndexerHasConflictingName(mFunc)) {
      writer.Write("[global::System.Runtime.CompilerServices.IndexerName(\"");
      writer.Write(methodName);
      writer.WriteLine("\")]");
    }

    // We can write an indexer instead of an operator. Write it here.
    writer.Write("public unsafe ");
    writer.WriteIf("ref ", isRefReturn);
    writer.Write(refRet?.GlobalQualifiedName ?? returnType);
    writer.Write(" this[");
    writer.WriteParam(method.Parameters[0]);
    writer.Write(']');
    using (writer.BracedScope()) {
      writer.Write("get");
      using (writer.BracedScope()) {
        WriteFixedMethodBody(writer, method, isRefReturn);
      }
    }

    writer.WriteLine();
  }

  public static bool IndexerHasConflictingName(CsMemberFunctionType mFunc) {
    CsStructure csStruct = mFunc.ClassType;
    return mFunc.CppName is "operator[]" && (
      csStruct.AllMethods.Any(m => m.Name == "Item") ||
      csStruct.NestedClasses.Any(n => n.SelfName == "Item"));
  }

  private static void WriteFixedMethodBody(IndentedTextWriter writer, CsMethod method, bool isRefReturn = false) {
    CsMemberFunctionType mFunc = method.MemberFunction;
    // var returnBuffer;
    if (mFunc.NeedsReturnBuffer) {
      string retType = mFunc.ReturnType.GlobalQualifiedName;
      writer.WriteMany(retType, " returnBuffer; ");
    }

    if (!mFunc.IsStatic) {
      // fixed ([T]* pThis = &this) {
      string thisType = mFunc.ThisType.GlobalQualifiedName;
      writer.WriteMany("fixed (", thisType, " pThis = &this) ");
    }

    // [return] Pointers.DelegateFieldName([returnBuffer, ][pThis][, arg1] ...]);
    writer.WriteIf("return ", mFunc.HasRealReturn);
    writer.WriteIf("ref *", isRefReturn);
    writer.WriteMany("Pointers.", method.DelegateFieldName, "(");

    bool needsComma = false;
    writer.WriteIf("&returnBuffer", mFunc.NeedsReturnBuffer, ref needsComma);
    writer.WriteIf("pThis", !mFunc.IsStatic, ref needsComma);
    writer.WriteParameterNamesToCpp(method.Parameters, ref needsComma);
    writer.Write(");");

    if (mFunc.NeedsReturnBuffer) {
      writer.Write(" return returnBuffer;");
    }
  }

  private static void WriteSpecialCaseTNodeMethod(IndentedTextWriter writer, CsMethod method) {
    // Writes the templated method `TNode<T>::d() -> T` as a property.
    // This method is always inlined, and procedure info is stripped from the executable.
    // BinaryNinja analysis shows that the method always returns the `n->data.mem` field of the struct.
    XmlDocs.Members.WriteMethod(writer, method);
    string ret = method.MemberFunction.ReturnType.GlobalQualifiedName;
    writer.WriteMany("public unsafe ", ret, " ", method.Name);
    writer.WriteMany(" => (", ret, ")this.n->data.mem;");
    writer.WriteLine();
  }

  #endregion

  #region Write Nested Types

  private static void WriteNestedTypes(IndentedTextWriter writer, CsStructure csStruct) {
    using (writer.Region("Nested Types")) {
      HashSet<string> classes = [];
      foreach (CsUdt nested in csStruct.NestedClasses) {
        if (classes.Add(nested.SelfName)) {
          WriteNestedType(writer, nested);
        }
      }
    }
  }

  private static void WriteNestedType(IndentedTextWriter writer, CsUdt nested) {
    switch (nested) {
      case CsEnum e:
        WriteEnumType(e, writer);
        break;
      case CsStructure s:
        WriteStruct(s, writer);
        break;
    }
  }

  #endregion
}
