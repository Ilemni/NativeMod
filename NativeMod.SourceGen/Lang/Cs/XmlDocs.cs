using System.Diagnostics;
using SharpPdb.Windows.TypeRecords;
using Writer = System.IO.TextWriter;

namespace NativeMod.SourceGen.Lang.Cs;

/// <summary>
/// Class that contains all methods for the generation of documentation code.
/// </summary>
public static class XmlDocs {
  public static void WriteLine(Writer writer, string text) {
    writer.Write("/// ");
    writer.WriteXmlDocTextLine(text);
  }

  public static void WriteSummary(Writer writer, string text, bool escapeXml = false) {
    writer.Write("/// <summary>");
    if (escapeXml) {
      text = System.Security.SecurityElement.Escape(text);
    }

    writer.Write(text);
    writer.WriteLine("</summary>");
  }

  public static void WriteReturn(Writer writer, string description) {
    writer.Write("/// <returns>");
    writer.Write(description);
    writer.WriteLine("</returns>");
  }

  public static void WriteSeeTag(Writer writer, CsUdt udt) {
    writer.Write("<see cref=\"");
    writer.Write(udt.GlobalQualifiedName);
    writer.Write("\">");
    writer.Write(udt.XmlCppName);
    writer.Write("</see>");
  }

  public static void WriteSeeTag(Writer writer, CsArray array) {
    writer.Write("<see cref=\"");
    writer.Write(array.GlobalQualifiedName);
    writer.Write("\">");
    writer.Write(array.XmlCppName);
    writer.Write("</see>");
  }

  private static void WriteSeeTag(Writer writer, CsMethod method) {
    CsMemberFunctionType mFunc = method.MemberFunction;
    string className = mFunc.ClassType.GlobalQualifiedName;
    var parameters = mFunc.ParameterTypes;

    writer.Write("<see cref=\"");
    writer.Write(className);
    writer.Write('.');
    if (method.CppName.StartsWith("operator")) {
      if (method.CppName is "operator[]") {
        writer.Write(CsGen.IndexerHasConflictingName(mFunc) ? method.Name : "get_Item");
      }
      else {
        writer.Write(method.CleanName);
      }
    }
    else {
      writer.Write(method.Name);
    }

    if (method.OverloadId != 0) {
      writer.Write('(');
      writer.WriteParameterTypes(parameters);
      writer.Write(')');
    }

    writer.Write("\">");
    writer.Write(mFunc.ClassType.XmlCppName);
    writer.Write('.');
    writer.Write(method.XmlCppName);
    if (method.OverloadId != 0) {
      writer.Write('(');
      writer.WriteParameterTypes(parameters, SelectXmlCppName);
      writer.Write(')');
    }

    writer.Write("</see>");
  }

  public static string SelectXmlCppName(CsType type) => type.XmlCppName;

  public static void WriteSeeTag(Writer writer, string cref, string text) {
    writer.Write("<see cref=\"");
    writer.Write(cref);
    writer.Write("\">");
    writer.Write(text);
    writer.Write("</see>");
  }

  /// <summary>
  /// Writes param tags for the given arguments, either in a single line or multiple lines
  /// depending on the number of arguments.
  /// <br /> This must be called when on an empty line, as it will write the "/// " prefix for each line.
  /// </summary>
  /// <remarks>
  /// This method exists to attempt to reduce the amount of whitespace produced in large XML doc files,
  /// while still allowing the XML doc to be parsed.
  /// </remarks>
  private static void WriteParamTagsAutoLines(Writer writer, (CsType type, string name)[] args, int threshold = 5) {
    if (args.Length == 0) return;

    if (args.Length > threshold) {
      foreach ((CsType argType, string argName) in args) {
        WriteParamTagLine(writer, argName, argType.XmlCppName);
      }

      return;
    }

    writer.Write("/// ");
    foreach ((CsType argType, string argName) in args) {
      WriteParamTag(writer, argName, argType.XmlCppName);
    }

    writer.WriteLine();
  }

  private static void WriteParamTag(Writer writer, string name, string description) {
    var a = name.AsSpan(name.StartsWith('@') ? 1 : 0);
    writer.Write("<param name=\"");
    writer.Write(a);
    writer.Write("\">");
    writer.Write(description);
    writer.Write("</param>");
  }

  public static void WriteParamTagLine(Writer writer, string name, string description) {
    var a = name.AsSpan(name.StartsWith('@') ? 1 : 0);
    writer.Write("/// <param name=\"");
    writer.Write(a);
    writer.Write("\">");
    writer.Write(description);
    writer.WriteLine("</param>");
  }

  /// <remarks>
  /// Derived types
  /// <br /> - <see cref="object"/>
  /// <br /> - <see cref="int"/>
  /// </remarks>
  /// <summary>
  /// Derived types (table)
  /// <list type="bullet">
  /// <item><description><see cref="object"/></description></item>
  /// <item><description><see cref="int"/></description></item>
  /// </list>
  /// </summary>
  public static class Types {
    public static void WriteArray(Writer writer, CsArray arr) {
      CsType elementType = arr.ElementType;
      writer.Write("/// <summary>");
      writer.Write(arr.XmlCppName);
      writer.WriteXmlDocLinebreak();
      writer.Write("Element: ");
      switch (elementType) {
        case CsUdt udt:
          WriteSeeTag(writer, udt);
          break;
        case CsSimpleType simple:
          writer.Write(simple.XmlCppName);
          break;
        default:
          writer.Write(elementType.XmlCppName);
          break;
      }

      if (arr.ElementType is not CsUdt && arr.GetInnerMostType() is CsUdt innerUdt) {
        writer.WriteXmlDocLinebreak();
        writer.Write("Inner-most element: ");
        WriteSeeTag(writer, innerUdt);
      }

      writer.WriteXmlDocLinebreak();
      writer.WriteMany("Count: ");
      writer.Write(arr.Count);
      writer.WriteLine("</summary>");
    }

    public static void WriteEnumType(Writer writer, CsEnum csEnum) {
      writer.Write("/// <summary>");
      WriteUdtInner(writer, csEnum);
      writer.WriteLine("</summary>");
    }

    public static void WriteStructType(Writer writer, CsStructure csStruct) {
      writer.Write("/// <summary>");
      if (csStruct.AllMethods.Any(m =>
            m.Record.Attributes.MethodKind is MethodKind.PureVirtual or MethodKind.PureIntroducingVirtual)) {
        writer.Write("<see langword=\"abstract\"/> ");
      }

      WriteUdtInner(writer, csStruct);
      writer.WriteLine("</summary>");

      int numBases = csStruct.BaseClasses.Length;
      int numDerived = csStruct.DerivedTypes.Count;
      if (numBases == 0 && numDerived == 0) {
        return;
      }

      // Output as little newline as reasonable to reduce size of generated XML file
      // See tags will fail to render if the line they're in is too long, so newlines are required for longer lists
      if (numBases == 1 && numDerived == 0) {
        writer.Write("/// <remarks>Base type:<br/>- ");
        WriteSeeTag(writer, csStruct.BaseClasses[0].BaseType);
        writer.WriteLine("</remarks>");
        return;
      }

      if (numBases == 0 && numDerived == 1) {
        writer.Write("/// <remarks>Derived type:<br/>- ");
        WriteSeeTag(writer, csStruct.DerivedTypes.First());
        writer.WriteLine("</remarks>");
        return;
      }

      if (numBases == 1 && numDerived == 1) {
        writer.Write("/// <remarks>Base type:<br/>- ");
        WriteSeeTag(writer, csStruct.BaseClasses[0].BaseType);
        writer.Write("<br/>Derived type:<br/>- ");
        WriteSeeTag(writer, csStruct.DerivedTypes.First());
        writer.WriteLine("</remarks>");
        return;
      }

      // If we reached this point, either base or derived or both have >= 1 types
      writer.WriteLine("/// <remarks>");
      if (numBases > 0) {
        writer.Write("/// ");
        if (numBases == 1) {
          writer.Write("Base type:<br/>- ");
          WriteSeeTag(writer, csStruct.BaseClasses[0].BaseType);
          writer.WriteLine();
        }
        else {
          writer.WriteLine("Base types:");
          foreach (CsBaseClass baseClass in csStruct.BaseClasses) {
            writer.Write("/// <br/>- ");
            WriteSeeTag(writer, baseClass.BaseType);
            writer.WriteLine();
          }
        }
      }

      if (numDerived > 0) {
        writer.Write("/// ");
        writer.WriteIf("<br/>", numBases > 0);
        if (numDerived == 1) {
          writer.Write("Derived type:<br/>- ");
          WriteSeeTag(writer, csStruct.DerivedTypes.First());
          writer.WriteLine();
        }
        else {
          writer.WriteLine("Derived types:");
          foreach (CsStructure derived in csStruct.DerivedTypes
                     .DistinctBy(d => d.FullyQualifiedName)
                     .OrderBy(d => d.FullyQualifiedName)) {
            writer.Write("/// <br/>- ");
            WriteSeeTag(writer, derived);
            writer.WriteLine();
          }
        }
      }

      writer.WriteLine("/// </remarks>");
    }

    public static void WriteForwardReferenceType(Writer writer, CsStructure csStruct) {
      writer.Write("/// <summary> ");
      writer.Write("[Forward Reference ");
      writer.Write(csStruct.NestedClasses.Count > 0 ? "with Nested Types" : "Only");
      writer.Write("] ");
      WriteUdtInner(writer, csStruct);
      writer.WriteLine("</summary>");
    }

    internal static void WriteUdtInner(Writer writer, CsUdt udt, bool seeTag = false) {
      writer.Write(udt switch {
        CsUnion => "Union: ",
        CsStructure => "Struct: ",
        CsEnum => "Enum: ",
        _ => throw new UnreachableException()
      });
      if (seeTag) {
        WriteSeeTag(writer, udt);
      }
      else {
        writer.WriteXmlDocText(udt.Record.Name.String);
      }
    }

    public static void WriteVftType(Writer writer, CsStructure csStruct, int slots) {
      writer.Write("/// <summary>");
      writer.WriteMany("Virtual Table for ");
      WriteSeeTag(writer, csStruct);
      writer.WriteMany(" with ", slots.ToString(), " slots.");
      writer.WriteLine("</summary>");

      if (csStruct.BaseClasses.FirstOrDefault() is not { } bClass) {
        return;
      }

      writer.Write("/// <remarks>");
      if (bClass.BaseType.VfTable is not { } bTable) {
        writer.Write("Base type has no virtual function table.");
        writer.WriteLine("</remarks>");
        return;
      }

      if (!ReferenceEquals(csStruct.VfTable, bTable)) {
        writer.Write("VTable is different from base type ");
        WriteSeeTag(writer, bClass.BaseType);
        int numBaseSlots = bTable.Slots.Length;
        if (numBaseSlots != slots) {
          writer.WriteXmlDocLinebreak();
          writer.WriteMany("VfTable Slots ", numBaseSlots.ToString(), " -> ", slots.ToString());
        }

        writer.WriteLine("</remarks>");
        return;
      }

      while (true) {
        CsBaseClass? innerClass = bClass.BaseType.BaseClasses.FirstOrDefault();
        if (innerClass?.BaseType.VfTable is not { } innerTable || !ReferenceEquals(bTable, innerTable)) {
          break;
        }

        bClass = innerClass;
        bTable = innerTable;
      }

      writer.WriteMany("VTable is identical to base type ");
      WriteSeeTag(writer, bClass.BaseType);
      writer.WriteLine("</remarks>");
    }
  }

  public static class Members {
    public static void WriteBaseClass(Writer writer, CsBaseClass baseClass) {
      writer.Write("/// <summary>");
      writer.Write("Base type: ");
      WriteSeeTag(writer, baseClass.BaseType);
      writer.WriteLine("</summary>");
    }

    public static void WriteStaticField(Writer writer, CsStaticField field) {
      string modifier = field switch {
        CsConstantField => "const",
        CsRegularStaticField => "static",
        CsThreadLocalStorageField => "thread_local",
        _ => throw new UnreachableException("Unhandled static field type: " + field.GetType().Name)
      };

      writer.Write("/// <summary>");
      writer.WriteMany(modifier, " ");
      writer.Write(field.FieldType.XmlCppName);
      if (field is CsConstantField c) {
        writer.WriteMany(" = ", c.Value.ToString()!);
      }

      writer.WriteLine("</summary>");
    }

    public static void WriteInstanceField(Writer writer, CsInstanceField field, bool isInherited) {
      writer.Write("/// <summary>");
      CsType fieldType = field.FieldType;
      switch (fieldType) {
        case CsSimpleType or CsSimplePointerType:
          writer.WriteIf("Pointer to ", fieldType is CsSimplePointerType);
          writer.Write(fieldType.XmlCppName);
          break;
        case CsUdt udt:
          Types.WriteUdtInner(writer, udt, seeTag: true);
          break;
        case CsArray array:
          writer.Write("Array ");
          writer.Write(array.XmlCppName);
          break;
        case CsPointerType p:
          writer.Write("Pointer ");
          if (p.Depth > 1) {
            writer.Write("(Depth: ");
            writer.Write(p.Depth);
            writer.Write(") ");
          }

          switch (p.InnerElement) {
            case CsSimpleType simple:
              writer.Write("to ");
              writer.Write(simple.XmlCppName);
              break;
            case CsProcedureType proc:
              writer.Write("to ");
              writer.Write(proc is CsMemberFunctionType ? "Member function pointer" : "Function pointer");
              break;
            // case CsUdt pUdt:
            //   WriteSeeTag(writer, pUdt);
            //   break;
            // case CsArray array:
            //   WriteSeeTag(writer, array);
            //   break;
          }

          break;


        default:
          writer.WriteXmlDocText(fieldType.ToString());
          break;
      }

      writer.WriteXmlDocLinebreak();
      writer.Write("Offset: 0x");
      writer.WriteXmlDocText(field.Offset.ToString("X"));

      if (isInherited) {
        writer.WriteXmlDocLinebreak();
        writer.Write("Inherited from ");
        WriteSeeTag(writer, field.Container);
      }

      writer.WriteLine("</summary>");
    }

    public static void WriteBitfieldBacking(Writer writer, uint byteOffset, uint bits, uint bytes) {
      writer.Write("/// <summary>Bitfield group at offset ");
      writer.WriteXmlDocText($"0x{byteOffset:X}");
      writer.WriteXmlDocLinebreak();
      writer.Write("Total bits: ");
      writer.WriteXmlDocText(bits.ToString());
      writer.WriteXmlDocLinebreak();
      writer.Write("Total bytes: ");
      writer.WriteXmlDocText(bytes.ToString());
      writer.WriteLine("</summary>");
    }

    public static void WriteBitfield(Writer writer, CsBitField field) {
      writer.Write("/// <summary>Bitfield: ");
      writer.WriteXmlDocText(field.Name);
      writer.WriteXmlDocLinebreak();
      writer.Write("Type: ");
      writer.Write(field.FieldType.XmlCppName);
      writer.WriteXmlDocLinebreak();
      writer.Write("Bit offset: ");
      writer.WriteXmlDocText(field.BitOffset.ToString());
      writer.WriteXmlDocLinebreak();
      writer.Write("Bit size: ");
      writer.WriteXmlDocText(field.BitSize.ToString());
      writer.WriteLine("</summary>");
    }

    public static void WriteMethodPointer(Writer writer, CsStructure csStruct, CsMethod method) {
      writer.Write("/// <summary>Function pointer for method <see cref=\"");
      writer.Write(csStruct.GlobalQualifiedName);

      if ((method.MethodRecord.Options & FunctionOptions.Constructor) == 0) {
        writer.Write('.');
        if (method.CppName is "operator[]" && !CsGen.IndexerHasConflictingName(method.MemberFunction)) {
          writer.Write("get_Item");
        }
        else if (method.Name.StartsWith("operator")) {
          writer.Write(method.CleanName);
        }
        else {
          writer.Write(method.Name);
        }
      }

      var parameters = method.MemberFunction.ParameterTypes;
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(parameters);
        writer.Write(")");
      }

      writer.Write("\">");
      writer.Write(csStruct.SelfName);
      writer.Write('.');
      writer.Write(method.Name);
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(parameters, SelectXmlCppName);
        writer.Write(")");
      }

      writer.WriteLine("</see></summary>");
    }

    public static void WriteVftPointer(Writer writer) {
      WriteSummary(writer, "Pointer to the virtual function table (vtable).");
    }

    public static void WriteVfShapeMethod(Writer writer, CsMethod method, int slot) {
      writer.Write("/// <summary>VTable method: ");
      WriteSeeTag(writer, method);
      writer.WriteXmlDocLinebreak();
      writer.WriteMany("<br/>Slot: ", slot.ToString());
      writer.WriteLine("</summary>");
    }

    public static void WriteMethod(Writer writer, CsMethod method) {
      writer.Write("/// <summary>");
      ReadOnlySpan<string> modifiers = method.Record.Attributes.MethodKind switch {
        MethodKind.Vanilla => [],
        MethodKind.Virtual => ["override"],
        MethodKind.Static => ["static"],
        MethodKind.Friend => [],
        MethodKind.IntroducingVirtual => ["virtual"],
        MethodKind.PureVirtual => ["abstract", "override"],
        MethodKind.PureIntroducingVirtual => ["abstract"],
        _ => []
      };

      foreach (string modifier in modifiers) {
        writer.Write("<see langword=\"");
        writer.Write(modifier);
        writer.Write("\"/> ");
      }

      writer.Write((method.MethodRecord.Options & FunctionOptions.Constructor) != 0 ? "Constructor: " : "Method: ");
      writer.Write(method.XmlCppName);

      CsType ret = method.MemberFunction.ReturnType;
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(method.MemberFunction.ParameterTypes, SelectXmlCppName);
        writer.Write(')');
      }

      if (method.IsVirtual) {
        writer.WriteXmlDocLinebreak();
        writer.Write("VFTable Slot: ");
        writer.Write(method.VfSlot);
      }

      writer.WriteLine("</summary>");
      WriteParamTagsAutoLines(writer, method.Parameters);

      // Provide descriptive return if it cannot be inferred from the C# return type (i.e. modified or const)
      if (method.MemberFunction.HasReturnType) {
        WriteReturn(writer, ret.XmlCppName);
      }
    }

    public static void WriteIndexerOperator(Writer writer, CsMethod method) {
      CsType retType = method.MemberFunction.ReturnType;

      writer.Write("/// <summary>Indexer operator[");
      if (method.OverloadId != 0) {
        writer.WriteParameterTypes(method.MemberFunction.ParameterTypes, SelectXmlCppName);
      }

      writer.Write("], returns ");
      writer.Write(retType.XmlCppName);
      writer.WriteLine("</summary>");
    }

    public static void WriteOperator(Writer writer, CsMethod method) {
      writer.Write("/// <summary>Operator overload for ");
      writer.WriteXmlDocText(method.OperatorName!);
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(method.MemberFunction.ParameterTypes, SelectXmlCppName);
        writer.Write(')');
      }

      writer.WriteLine("</summary>");
    }
  }

  public static class GlobalFunctions {
    public static void WriteFileClass(Writer writer, string name) {
      WriteSummary(writer, "File: " + name, true);
    }

    public static void WriteUnknownFileClass(Writer writer) {
      WriteSummary(writer,
        "This class contains functions that were not from cleanly named file paths, " +
        "and may be from third party libraries.");
    }

    public static void WriteInternalsClass(Writer writer) {
      WriteSummary(writer, "This class contains global functions from unnamed files, " +
        "that are expected to be used internally.");
    }

    public static void WriteFunction(Writer writer, CsGen.HookMethod method) {
      WriteSummary(writer, "Function: " + method.OriginalName, true);
      WriteParamTagsAutoLines(writer, method.Args);
      CsType ret = method.RetType;
      if (ret is CsModifiedType or CsArray or CsPointerType or CsUdt) {
        WriteReturn(writer, ret.XmlCppName);
      }
    }
  }

  public static class Hooks {
    public static void WriteHookForGlobalFunctionsClass(Writer writer, string root, string ns, string name) {
      writer.Write("/// <summary>Hooks for global functions in class ");
      writer.Write("<see cref=\"");
      writer.WriteMany("global::", root, ".GlobalFunctions.", ns, ".", name);
      writer.Write("\">");
      writer.WriteMany(ns, ".", name);
      writer.WriteLine("</see></summary>");
    }

    public static void WriteHookForGlobalFunction(Writer writer, CsGen.HookMethod method, string ns, string file) {
      string mName = method.OriginalName.SanitizeName(true, true);
      writer.WriteLine();
      writer.Write("/// <summary>Hook for global function <see cref=\"");
      writer.Write("global::");
      writer.Write(method.Procedure.Gen.Namespace);
      writer.Write(".GlobalFunctions.");
      writer.Write(ns);
      writer.Write('.');
      writer.Write(file);
      writer.Write('.');
      writer.WriteXmlDocText(mName);
      writer.WriteIf("_", file == mName);
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(method.Procedure.ParameterTypes);
        writer.Write(')');
      }

      writer.Write("\">");
      writer.Write(ns);
      writer.Write('.');
      writer.Write(file);
      writer.Write('.');
      writer.WriteXmlDocText(mName);
      if (method.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(method.Procedure.ParameterTypes, SelectXmlCppName);
        writer.Write(')');
      }

      writer.WriteLine("</see></summary>");

      // Inject params tags to the hook class definition,
      // since added delegates don't inheritdoc the delegates by default
      writer.WriteManyLine("/// <inheritdoc cref=\"orig_", method.Name, "\"/>");
    }

    public static void WriteHookForClass(Writer writer, CsStructure csStruct) {
      writer.Write("/// <summary>Hooks for struct ");
      WriteSeeTag(writer, csStruct);
      writer.WriteLine("</summary>");
    }

    public static void WriteHookForInstanceMethod(Writer writer, CsGen.HookMethod method) {
      CsMethod m = method.Method!;
      CsMemberFunctionType mFunc = m.MemberFunction;
      CsType classType = mFunc.ClassType;
      bool isCtor = (m.MethodRecord.Options & FunctionOptions.Constructor) != 0;
      writer.Write("/// <summary>Hook for struct ");
      writer.Write(isCtor ? "constructor " : "method ");
      writer.Write("<see cref=\"");
      writer.Write(classType.GlobalQualifiedName);
      if (!isCtor) {
        writer.Write('.');
        if (m.CppName is not "operator[]" || CsGen.IndexerHasConflictingName(mFunc)) {
          writer.Write(m.Name);
        }
        else {
          writer.Write("get_Item");
        }
      }

      if (m.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(mFunc.ParameterTypes);
        writer.Write(')');
      }

      writer.Write("\">");
      writer.Write(classType.FullyQualifiedName);
      writer.Write('.');
      writer.Write(m.XmlCppName);

      if (m.OverloadId != 0) {
        writer.Write('(');
        writer.WriteParameterTypes(mFunc.ParameterTypes, SelectXmlCppName);
        writer.Write(')');
      }

      // Inject params tags to the hook class definition,
      // since added delegates don't inheritdoc the delegates by default
      writer.WriteLine("</see></summary>");
      writer.WriteManyLine("/// <inheritdoc cref=\"orig_", method.Name, "\"/>");
    }

    public static void WritePrefixDelegate(Writer writer, CsGen.HookMethod method) {
      WriteSummary(writer, "Prefix delegate, invoked before the original C++ function.");
      TryWriteSelfParam(writer, method);
      WriteParamTagsAutoLines(writer, method.Args);
    }

    public static void WriteSuffixDelegate(Writer writer, CsGen.HookMethod method) {
      WriteSummary(writer, "Suffix delegate, invoked after the original C++ function.");
      TryWriteSelfParam(writer, method);
      WriteParamTagLine(writer, "__return", method.NeedsRetBuffer
        ? "The return value buffer."
        : "The return value.");

      WriteParamTagsAutoLines(writer, method.Args);
    }

    public static void WriteOrigDelegate(Writer writer, CsGen.HookMethod method) {
      WriteSummary(writer, "Orig delegate. Must be invoked to call the original C++ function.");
      TryWriteSelfParam(writer, method);
      TryWriteReturnBufferParam(writer, method);
      WriteParamTagsAutoLines(writer, method.Args);
    }

    public static void WriteHookDelegate(Writer writer, CsGen.HookMethod method) {
      WriteSummary(writer,
        "Hook delegate that a mod registers. " +
        "Invoked instead of the original C++ function, " +
        "and may call the original C++ function via the provided orig parameter.");
      WriteParamTagLine(writer, "orig",
        "The original function delegate. " +
        "Must be invoked to call the original C++ function.");
      TryWriteSelfParam(writer, method);
      TryWriteReturnBufferParam(writer, method);
      WriteParamTagsAutoLines(writer, method.Args);
    }

    public static void WritePrefixEvent(Writer writer) {
      WriteSummary(writer, "Prefix event that mods register to. " +
        "Invoked before the original C++ function.");
    }

    public static void WriteSuffixEvent(Writer writer) {
      WriteSummary(writer, "Suffix event that mods register to. " +
        "Invoked after the original C++ function, " +
        "and may modify the return value.");
    }

    public static void WriteHookEvent(Writer writer) {
      WriteSummary(writer, "Hook event that mods register to. " +
        "Invoked instead of the original C++ function, " +
        "and may call the original C++ function via the provided orig parameter.");
    }

    private static void TryWriteSelfParam(Writer writer, CsGen.HookMethod method) {
      if (method.HasThis) {
        WriteParamTagLine(writer, "__this", "The <see langword=\"this\"/> value.");
      }
    }

    private static void TryWriteReturnBufferParam(Writer writer, CsGen.HookMethod method) {
      if (method.NeedsRetBuffer) {
        WriteParamTagLine(writer, "__return", "The return value buffer.");
      }
    }
  }
}
