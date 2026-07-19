using System.CodeDom.Compiler;
using System.Globalization;
using PdbToCSharp.Dissect;
using PdbToCSharp.Types;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public partial class SourceGen {
  private static void WriteStruct(CsStructure csStruct, IndentedTextWriter writer) {
    if (csStruct.Record.IsForwardReference) {
      WriteForwardReferenceStruct(csStruct, writer);
      return;
    }

    // Write XML doc
    writer.Write("/// ");
    writer.Write("Struct type: ");
    writer.WriteXmlDocText(csStruct.Record.Name.String);
    writer.WriteXmlDocLinebreak();
    writer.Write("TypeIndex: ");
    writer.WriteXmlDocText(csStruct.TypeIndex.ToString());
    writer.WriteXmlDocLinebreak();
    writer.Write("UniqueName: ");
    writer.WriteXmlDocText(csStruct.Record.UniqueName.String);
    writer.WriteLine();

    // Write attributes
    writer.WriteGeneratedCodeAttribute();
    writer.WriteStructLayoutAttribute(csStruct.Size, prependGlobal: true);

    // public [unsafe] partial struct [@]StructName {
    writer.Write("public ");
    if (csStruct.InstanceFields.Any(f => f.FieldType is CsPointerType or CsSimplePointerType)) {
      writer.Write("unsafe ");
    }

    writer.Write("partial struct ");

    // Handle warning CS8981: The type name only contains lower-cased ascii characters.
    //                        Such types may become reserved for the language.
    if (csStruct.SelfName.All(char.IsLower)) {
      writer.Write('@');
    }

    writer.Write(csStruct.SelfName);
    writer.WriteLine(" {");
    writer.Indent++;

    // Write inner static class Pointers for memory address access
    WritePointers(csStruct, writer);

    VirtualFunctionTableShapeRecord? vfTable = csStruct.FindVfTable(out CsStructure? vfTableHolder);
    if (csStruct.InstanceMethods.Length > 0 && (csStruct is not { VfAddress: 0, VfTable: null } || vfTable is not null)) {
      writer.WriteLine(vfTable is not null
        ? "/// Pointer to the virtual function table (vtable) of this struct."
        : "// Warning: VfAddress is non-zero but VfTable is null");
      writer.WriteFieldOffsetAttribute(0);
      writer.Write(" public readonly unsafe Pointers.VTable* vTable;");

      writer.WriteLine();
    }

    bool hasBaseClasses = csStruct.BaseClasses.Length > 0;
    bool hasStaticFields = csStruct.StaticFields.Any(s => s is CsConstantField or CsRegularStaticField);
    bool hasMethods = csStruct.InstanceMethods.Any(m => m.ProcedureInfo is not null);
    bool hasNestedClasses = csStruct.NestedClasses.Count > 0;
    bool hasInstanceFields = csStruct.InstanceFields.Length > 0;
    WriteIf(hasBaseClasses, csStruct, writer, WriteBaseClasses);
    WriteIf(hasStaticFields, csStruct, writer, WriteStaticFields);
    WriteIf(hasInstanceFields, csStruct, writer, WriteInstanceFields);
    WriteIf(hasMethods, csStruct, writer, WriteMethods);
    WriteIf(hasNestedClasses, csStruct, writer, WriteNestedTypes);

    writer.Indent--;
    writer.WriteLine("}");
  }

  private static void WriteIf(bool condition, CsStructure csStruct, IndentedTextWriter writer,
    Action<CsStructure, IndentedTextWriter> action) {
    if (condition) {
      action(csStruct, writer);
    }
  }

  private static void WriteInstanceFields(CsStructure csStruct, IndentedTextWriter writer) {
    writer.WriteLine("#region Instance Fields");
    foreach (CsInstanceField field in csStruct.InstanceFields) {
      WriteInstanceField(csStruct, writer, field);
    }

    writer.WriteLine("#endregion");
  }

  private static void WriteInstanceField(CsStructure csStruct, IndentedTextWriter writer, CsInstanceField field) {
    // TODO: Support fields of functions and instance methods
    if (field.FieldType is CsFunctionType or CsPointerType { InnerElement: CsFunctionType or CsInstanceMethod }) {
      return;
    }

    // XML doc for field
    writer.Write("/// Field: ");
    if (csStruct.PdbFile.TryGetRecord(field.FieldType.TypeIndex) is { } fieldTypeRecord) {
      writer.WriteXmlDocText(field.FieldType.GetType().Name);
      writer.WriteXmlDocText(" ");
      writer.WriteXmlDocText(fieldTypeRecord.ToString(csStruct.PdbFile));
      writer.WriteXmlDocLinebreak();
      writer.Write("TypeIndex: ");
      writer.WriteXmlDocText(field.FieldType.TypeIndex.ToString());
      writer.WriteLine();
    }
    else {
      writer.WriteXmlDocTextLine(field.FieldType.TypeIndex.ToString());
    }

    // FieldOffset attribute
    bool prependGlobal = false;
    uint offset = field.Offset;
    writer.WriteFieldOffsetAttribute(offset, prependGlobal);

    // Field declaration
    writer.Write(" public ");
    writer.Write(field.FieldType.FullyQualifiedName);
    writer.Write(' ');
    string fieldName = field.Name.KeywordToVerbatim();

    // Rename field if it is identical name to an immediately nested type
    if (field.FieldType is CsUdt fUdt && fUdt.Parent == csStruct && field.Name == fUdt.SelfName) {
      for (int i = 0; i < fieldName.Length; i++) {
        char c = fieldName[i];
        if (!char.IsLetter(c)) {
          continue;
        }

        char[] fieldNameChars = fieldName.ToCharArray();
        fieldNameChars[i] = char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c);
        fieldName = new string(fieldNameChars);
        break;
      }
    }

    writer.Write(fieldName);
    writer.WriteLine(';');
  }

  private static void WriteStaticFields(CsStructure csStruct, IndentedTextWriter writer) {
    writer.WriteLine("#region Static Fields");
    foreach (CsStaticField field in csStruct.StaticFields) {
      WriteStaticFields(csStruct, writer, field);
    }

    writer.WriteLine("#endregion");
  }

  private static void WriteStaticFields(CsStructure csStruct, IndentedTextWriter writer, CsStaticField field) {
    // XML doc for field
    writer.Write("/// Field: ");
    if (csStruct.PdbFile.TryGetRecord(field.FieldType.TypeIndex) is { } fieldTypeRecord) {
      writer.WriteXmlDocText(fieldTypeRecord.ToString(csStruct.PdbFile));
      writer.WriteXmlDocLinebreak();
    }

    writer.Write("TypeIndex ");
    writer.WriteXmlDocText(field.FieldType.TypeIndex.ToString());
    if (field is CsConstantField c) {
      writer.WriteXmlDocText(" Value: ");
      writer.WriteXmlDocText(c.Value.ToString()!);
      writer.WriteXmlDocText(" (C# Type: ");
      writer.WriteXmlDocText(c.Value.GetType().Name);
      writer.WriteXmlDocText(")");
    }

    writer.WriteLine();

    switch (field) {
      case CsConstantField constant: {
        WriteConstantField(writer, constant);
        break;
      }
      case CsRegularStaticField staticField:
        WriteRegularStaticField(csStruct, writer, staticField);
        break;
    }
  }

  private static void WriteConstantField(IndentedTextWriter writer, CsConstantField constant) {
    TypeIndex fType = constant.FieldType.TypeIndex;
    bool needsCast = fType.SimpleKind is not SimpleTypeKind.Float32 and not SimpleTypeKind.Boolean8;
    bool writeUnchecked = (fType.SimpleKind is SimpleTypeKind.UInt16 or SimpleTypeKind.UInt32
        or SimpleTypeKind.UInt64
        or SimpleTypeKind.UInt16Short or SimpleTypeKind.UInt32Long or SimpleTypeKind.UInt64Quad)
      && constant.Value is < 0 or sbyte and < 0 or short and < 0 or long and < 0;

    string value = !fType.IsSimple
      ? constant.Value.ToString()!
      : fType.SimpleKind switch {
        SimpleTypeKind.Boolean8 => (ushort)constant.Value > 0 ? "true" : "false",
        SimpleTypeKind.Float32 => BitConverter.UInt32BitsToSingle((uint)constant.Value)
          .ToString(CultureInfo.InvariantCulture) is { } f and not "NaN"
          ? f + 'f'
          : "float.NaN",
        _ => constant.Value.ToString()!
      };

    writer.Write("public const ");
    writer.Write(constant.FieldType.FullyQualifiedName);
    writer.Write(' ');
    writer.Write(constant.Name);
    writer.Write(" = ");

    if (writeUnchecked) {
      writer.Write("unchecked(");
    }

    if (needsCast) {
      writer.Write("(");
      writer.Write(constant.FieldType.FullyQualifiedName);
      writer.Write(")(");
    }

    writer.Write(value);
    writer.WriteIf(")", needsCast);
    writer.WriteIf(")", writeUnchecked);
    writer.WriteLine(";");
  }

  private static void WriteRegularStaticField(CsStructure csStruct, IndentedTextWriter writer,
    CsRegularStaticField staticField) {
    writer.Write("public static unsafe ref ");
    writer.Write(staticField.FieldType.FullyQualifiedName);
    writer.Write(' ');
    writer.Write(staticField.Name.KeywordToVerbatim());
    writer.Write(" => ref *((");
    writer.Write(staticField.FieldType.FullyQualifiedName);
    writer.Write("*)(");
    writer.Write(csStruct.SourceGen.MemoryAddressFieldName);
    writer.Write(" + ");
    writer.Write(staticField.RelativeVirtualAddress);
    writer.WriteLine("));");
  }

  private static void WriteForwardReferenceStruct(CsStructure csStruct, IndentedTextWriter writer) {
    writer.Write("/// ");
    writer.Write("[Forward Reference ");
    writer.Write(csStruct.NestedClasses.Count > 0 ? "with Nested Types" : "Only");
    writer.Write("] ");

    writer.Write("Struct type: ");
    writer.WriteXmlDocText(csStruct.Record.Name.String);
    writer.WriteXmlDocLinebreak();
    writer.Write("TypeIndex: ");
    writer.WriteXmlDocText(csStruct.TypeIndex.ToString());
    writer.WriteXmlDocLinebreak();
    writer.Write("UniqueName: ");
    writer.WriteXmlDocText(csStruct.Record.UniqueName.String);
    writer.WriteLine();

    // public partial struct [@]StructName <;|{ Nested classes ... }>
    writer.WriteGeneratedCodeAttribute(newLine: false);
    writer.Write(" public partial struct ");
    if (csStruct.SelfName.All(char.IsLower)) {
      writer.Write('@');
    }

    writer.Write(csStruct.SelfName);
    if (csStruct.NestedClasses.Count == 0) {
      writer.WriteLine(";");
      return;
    }

    writer.WriteLine(" {");
    writer.Indent++;
    WriteNestedTypes(csStruct, writer);
    writer.Indent--;
    writer.WriteLine('}');
  }

  private static void WritePointers(CsStructure csStruct, IndentedTextWriter writer) {
    // Get own vtable or parent's vtable if it exists
    bool anyPointers = csStruct.InstanceMethods.Any(m => m.ProcedureInfo is not null);
    VirtualFunctionTableShapeRecord? vfTable = csStruct.FindVfTable(out CsStructure? vfTableHolder);
    if (!anyPointers && csStruct is { VfAddress: 0, VfTable: null } && vfTable is null) {
      return;
    }

    if (csStruct.InstanceMethods.Length == 0) {
      return;
    }

    writer.WriteLine("public static class Pointers {");
    writer.Indent++;

    if (vfTable is not null) {
      int highestVirtualIndex = csStruct.InstanceMethods.Select(m => m.Record.VFTableOffset).Max() / 8;
      bool isLargerThanVfTable = highestVirtualIndex >= vfTable.Slots.Length;
      if (isLargerThanVfTable) {
        Log.Warn($"VfTable for {csStruct.SelfName} has {vfTable.Slots.Length} slots, but highest virtual index is {highestVirtualIndex}");
      }

      int numSlots = Math.Max(vfTable.Slots.Length, highestVirtualIndex + 1);

      // Write InlineArray type for this VTable
      writer.Write("[System.Runtime.CompilerServices.InlineArray(");
      writer.Write(numSlots);
      writer.WriteLine(")]");
      writer.WriteLine("public struct VTable {");
      writer.Indent++;
      writer.WriteLine("private ulong _slot0;");
      writer.Indent--;
      writer.WriteLine("}");

      writer.Write("/// Pointer to the virtual function table (vtable) of ");
      if (vfTableHolder is not null) {
        writer.Write("the base class ");
        writer.WriteXmlDocText(vfTableHolder.Record.Name.String);
        writer.WriteXmlDocLinebreak();
        writer.Write("TypeIndex: ");
        writer.WriteXmlDocText(vfTableHolder.TypeIndex.ToString());
      }
      else {
        writer.Write("this struct ");
        writer.WriteXmlDocText(csStruct.Record.Name.String);
        writer.WriteXmlDocLinebreak();
        writer.Write("TypeIndex: ");
        writer.WriteXmlDocText(csStruct.TypeIndex.ToString());
      }
      writer.WriteLine();
      writer.Write("public static readonly unsafe VTable* vTable = (VTable*)(");
      writer.Write(csStruct.SourceGen.MemoryAddressFieldName);
      writer.Write(" + ");
      writer.Write(csStruct.VfAddress);
      writer.Write(");");
      writer.WriteIf("// Missing VfTable address", csStruct.VfAddress == 0);
      writer.WriteLine();
    }

    foreach (CsInstanceMethod m in csStruct.InstanceMethods.Where(m => m.ProcedureInfo is not null)) {
      if (m.ParameterTypes.Any(p => p.FullyQualifiedName is "__arglist") ||
          m.ReturnType.FullyQualifiedName is "__arglist") {
        continue;
      }

      if (m.DelegateFieldName.Contains("operator")) {
        continue;
      }

      // public static readonly unsafe delegate* unmanaged[CallConv]<T1, T2, ..., TResult>
      // MethodName = (delegate* unmanaged[CallConv]<T1, T2, ..., TResult>)(FunctionAddress + Offset);
      writer.Write("public static readonly unsafe ");
      WriteDelegateType(csStruct, writer, m);
      writer.Write(" ");
      writer.Write(m.DelegateFieldName);
      writer.Write(" = (");
      WriteDelegateType(csStruct, writer, m);
      writer.Write(")(");
      writer.Write(csStruct.SourceGen.FunctionAddressFieldName);
      writer.Write(" + ");
      writer.Write(m.ProcedureInfo!.Value.Procedure.Offset);
      writer.WriteLine(");");
    }

    writer.Indent--;
    writer.WriteLine("}");
  }

  private static void WriteBaseClasses(CsStructure csStruct, IndentedTextWriter writer) {
    writer.Write("// Base classes");
    for (int i = 0; i < csStruct.BaseClasses.Length; i++) {
      WriteBaseClass(csStruct, writer, i);
    }

    writer.WriteLine();
  }

  private static void WriteBaseClass(CsStructure csStruct, IndentedTextWriter writer, int i) {
    CsBaseClass baseClass = csStruct.BaseClasses[i];
    writer.Write("/// Base class: ");
    writer.WriteXmlDocText(baseClass.BaseType.Record.Name.String);
    writer.WriteXmlDocLinebreak();
    writer.Write("TypeIndex: ");
    writer.WriteXmlDocText(baseClass.BaseType.TypeIndex.ToString());
    writer.WriteLine();

    // FieldOffset attribute
    bool prependGlobal = false;
    writer.Write("[");
    writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
    writer.Write("FieldOffset(");
    writer.Write(baseClass.Record.Offset);
    writer.Write(")] ");

    // Field declaration
    writer.Write("public ");
    writer.Write(baseClass.BaseType.FullyQualifiedName);
    writer.Write(" Base");
    if (csStruct.BaseClasses.Length > 1) {
      writer.Write(i + 1);
    }

    writer.WriteLine(';');
  }

  private static void WriteMethods(CsStructure csStruct, IndentedTextWriter writer) {
    // TODO: Move static methods out of here (ideally out of CsStructure.InstanceMethods)
    //  Do we even have any static methods here?
    writer.WriteLine("#region Instance Methods");
    foreach (CsInstanceMethod method in csStruct.InstanceMethods.Distinct(CsInstanceMethod.Comparer.Instance)) {
      int vfOffset = method.Record.VFTableOffset;
      if (method.ProcedureInfo is null && vfOffset == -1) {
        continue;
      }

      if (method.ParameterTypes.Any(p => p.SelfName == "__arglist")) {
        continue;
      }

      if (method.Name.Contains("operator")) {
        continue;
      }

      if (vfOffset == -1) {
        WriteMethod(csStruct, writer, method);
      }
      else {
        WriteVirtualMethod(csStruct, writer, method);
      }
    }

    writer.WriteLine("#endregion");
  }

  private static void WriteMethod(CsStructure csStruct, IndentedTextWriter writer, CsInstanceMethod method) {
    // public [static] unsafe <T|void*> MethodName([T arg1][, T arg2] ...) {
    writer.Write("public ");
    writer.WriteIf("static ", method.IsStatic);
    writer.Write("unsafe ");

    if (method.ReturnType is CsUdt { Record.IsForwardReference: true }) {
      writer.Write("void* ");
    }
    else {
      writer.Write(method.ReturnType.FullyQualifiedName);
    }

    writer.Write(' ');
    writer.Write(method.Name.KeywordToVerbatim());
    writer.Write('(');
    foreach ((int i, (CsType type, string name)) in method.Parameters.Index()) {
      if (i > 0) {
        writer.Write(", ");
      }

      writer.Write(type.FullyQualifiedName);
      writer.Write(' ');
      writer.Write(name);
    }

    writer.WriteLine(") {");
    writer.Indent++;

    // fixed ([T]* pThis = &this) {
    writer.Write("fixed (");
    writer.Write(csStruct.FullyQualifiedName);
    writer.Write("* pThis = &this) {");
    writer.WriteLine();
    writer.Indent++;

    // [return] Pointers.MethodName([pThis][, arg1] ...]);
    if (method.ReturnType.SelfName != "void") {
      writer.Write("return ");
    }

    writer.Write("Pointers.");
    writer.Write(method.DelegateFieldName);
    writer.Write("(");

    if (!method.IsStatic) {
      writer.Write("pThis");
      if (method.ParameterTypes.Length > 0) {
        writer.Write(", ");
      }
    }

    foreach ((int i, string name) in method.Args.Index()) {
      if (i > 0) {
        writer.Write(", ");
      }

      writer.Write(name);
    }

    writer.WriteLine(");");
    writer.Indent--;
    writer.WriteLine("}");
    writer.Indent--;
    writer.WriteLine("}");
  }

  private static void WriteVirtualMethod(CsStructure csStruct, IndentedTextWriter writer, CsInstanceMethod method) {
    // public [static] unsafe <T|void*> MethodName([T arg1][, T arg2] ...) {
    writer.Write("public ");
    writer.WriteIf("static ", method.IsStatic);
    writer.Write("unsafe ");

    if (method.ReturnType is CsUdt { Record.IsForwardReference: true }) {
      writer.Write("void* ");
    }
    else {
      writer.Write(method.ReturnType.FullyQualifiedName);
    }

    writer.Write(' ');
    writer.Write(method.Name.KeywordToVerbatim());
    writer.Write('(');
    foreach ((int i, (CsType type, string name)) in method.Parameters.Index()) {
      if (i > 0) {
        writer.Write(", ");
      }

      writer.Write(type.FullyQualifiedName);
      writer.Write(' ');
      writer.Write(name);
    }

    writer.WriteLine(") {");
    writer.Indent++;

    // fixed (<T>* pThis = &this) {
    writer.Write("fixed (");
    writer.Write(csStruct.FullyQualifiedName);
    writer.Write("* pThis = &this) {");
    writer.WriteLine();
    writer.Indent++;

    writer.WriteIf("return ", method.ReturnType.SelfName != "void");
    writer.Write("((");
    WriteDelegateType(csStruct, writer, method);
    writer.Write(")(*vTable)[");
    writer.Write(method.Record.VFTableOffset / 8);
    writer.Write("])(");
    // write args
    if (!method.IsStatic) {
      writer.Write("pThis");
      if (method.ParameterTypes.Length > 0) {
        writer.Write(", ");
      }
    }

    foreach ((int i, string name) in method.Args.Index()) {
      if (i > 0) {
        writer.Write(", ");
      }

      writer.Write(name);
    }

    writer.WriteLine(");");

    writer.Indent--;
    writer.WriteLine("}");
    writer.Indent--;
    writer.WriteLine("}");
  }

  private static void WriteDelegateType(CsStructure csStruct, IndentedTextWriter writer, CsInstanceMethod method) {
    writer.Write("delegate* unmanaged[");
    string conv = method.CallingConvention switch {
      CallingConvention.NearC => "Cdecl",
      CallingConvention.NearStdCall => "Stdcall",
      CallingConvention.NearFast => "Fastcall",
      CallingConvention.ThisCall => "Thiscall",
      _ => "Cdecl"
    };
    writer.Write(conv);
    writer.Write("]<");
    if (!method.IsStatic) {
      writer.Write(csStruct.FullyQualifiedName);
      writer.Write("*, ");
    }

    for (int i = 0; i < method.ParameterTypes.Length; i++) {
      if (i > 0) {
        writer.Write(", ");
      }

      string name = method.ParameterTypes[i].FullyQualifiedName;
      // Cannot support variadics in a simple way.
      // However, custom delegates can be created to replace this __argList with any number of parameters.
      // TODO: make it clear that this is a variadic function
      if (name is "__arglist") {
        break;
      }

      writer.Write(name);
    }

    if (method.ParameterTypes.Length > 0) {
      writer.Write(", ");
    }

    writer.Write(method.ReturnType.FullyQualifiedName);
    writer.Write('>');
  }

  private static void WriteNestedTypes(CsStructure csStruct, IndentedTextWriter writer) {
    writer.WriteLine("#region Nested Types");
    HashSet<string> classes = [];
    foreach (CsUdt nested in csStruct.NestedClasses) {
      if (!classes.Add(nested.SelfName)) {
        continue;
      }

      switch (nested) {
        case CsEnum e:
          WriteEnumType(e, writer);
          break;
        case CsStructure s:
          WriteStruct(s, writer);
          break;
      }
    }

    writer.WriteLine("#endregion");
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

    // Write XML doc
    writer.Write("/// Enum type: ");
    writer.WriteXmlDocText(csEnum.Record.Name.String);
    writer.WriteXmlDocLinebreak();
    writer.Write("UniqueName: ");
    writer.WriteXmlDocText(csEnum.Record.UniqueName.String);
    writer.WriteLine();

    // Write GeneratedCode attribute
    writer.WriteGeneratedCodeAttribute();

    // Write enum declaration
    writer.Write("public enum ");
    if (csEnum.SelfName.All(char.IsLower)) {
      writer.Write('@');
    }

    writer.Write(csEnum.SelfName);
    if (underlying is not "int") {
      writer.Write(" : ");
      writer.Write(underlying);
    }

    // Write enum body
    writer.WriteLine(" {");
    writer.Indent++;
    foreach (CsEnumField enumValue in csEnum.Values) {
      writer.Write(enumValue.Name.KeywordToVerbatim());
      writer.Write(" = ");
      writer.Write(enumValue.Value);
      writer.WriteLine(',');
    }

    writer.Indent--;
    writer.WriteLine("}");
    writer.WriteLine();
  }
}
