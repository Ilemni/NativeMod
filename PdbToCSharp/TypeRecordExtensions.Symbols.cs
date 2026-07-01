using System.Text;
using JetBrains.Annotations;
using Microsoft.Extensions.ObjectPool;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedParameter.Global

namespace PdbToCSharp;

internal static partial class TypeRecordExtensions {
  public static string ToString(this AnnotationReferenceSymbol s, PdbFile pdb) {
    return
      $"/* Annotation Reference: " +
      $"Module = {s.Module} " +
      $"Symbol Index = {s.SymbolIndex} " +
      $"Sum Name (TODO) = {s.SumName}) " +
      $"*/";
  }

  public static string ToString(this AnnotationSymbol s, PdbFile pdb) {
    return
      $"/* Annotation: " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Count = {s.AnnotationsCount} " +
      $"*/";
  }

  public static string ToString(this AttributeSlotSymbol s, PdbFile pdb) {
    return
      $"/* Attribute Slot: " +
      $"Name = \"{s.Name.String}\" " +
      $"Type = {s.TypeIndex.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this BlockSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb.Append("/* Block with ")
      .Append(s.Children.Length)
      .Append(" children")
      .ToString();
  }

  public static string ToString(this BuildInfoSymbol b, PdbFile pdb) {
    return b.BuildId.As<BuildInfoRecord>(pdb.IpiStream).ToString(pdb);
  }

  public static string ToString(this CallSiteInfoSymbol s, PdbFile pdb) {
    return
      $"/* Call Site Info: " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this CoffGroupSymbol s, PdbFile pdb) {
    return
      $"/* Coff Group: " +
      $"Offset = {s.Offset:X8}:{s.Size:X8} " +
      $"Name = {s.Name.String} " +
      $"Characteristics = {s.Characteristics} " +
      $"*/";
  }

  public static string ToString(this Compile2Symbol s, PdbFile pdb) {
    return
      $"/* Compile2: " +
      $"Machine = {s.Machine}" +
      $"Version = {s.Version}" +
      $"Flags = {s.Flags}" +
      $"*/";
  }

  public static string ToString(this Compile3Symbol s, PdbFile pdb) {
    return
      $"/* Compile3: " +
      $"Machine = {s.Machine}" +
      $"Version = {s.Version}" +
      $"Flags = {s.Flags} " +
      $"*/";
  }

  public static string ToString(this ConstantSymbol s, PdbFile pdb) {
    return
      $"/* const " +
      $"{s.TypeIndex.ToString(pdb)} " +
      $"{s.Name} " +
      $"= {s.Value} " +
      $"*/";
  }

  public static string ToString(this DataSymbol s, PdbFile pdb) {
    return
      $"/* Data:" +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Name = {s.Name.String} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this DefRangeFramePointerRelativeFullScopeSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb.ToString();
  }

  public static string ToString(this DefRangeFramePointerRelativeSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.Append("/* Frame Pointer Relative Range: ")
      .Append($"Offset = {s.Offset:X8} ")
      .Append($"Range = {s.Range.OffsetStart:X8}:{s.Range.Range:X4} ");
    sb.Append("Gaps: { ");
    bool notFirst = false;
    foreach (LocalVariableAddressGap localVariableAddressGap in s.Gaps) {
      if (notFirst) {
        sb.Append(", ");
      }
      sb.Append($"{localVariableAddressGap.GapStartOffset:X4}:{localVariableAddressGap.Range:X4}");
      notFirst = true;
    }

    sb.Append(" } */");

    return sb.ToString();
  }

  public static string ToString(this DefRangeRegisterRelativeSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.Append("/* Register Relative Range: ")
      .Append($"Register = {s.Register} ")
      .Append($"Flags = {s.Flags:X4} ")
      .Append($"Offset = {s.BasePointerOffset:X8} ")
      .Append($"Range = {s.Range.OffsetStart:X8}:{s.Range.Range:X4} ");
    sb.Append("Gaps: { ");
    bool notFirst = false;
    foreach (LocalVariableAddressGap localVariableAddressGap in s.Gaps) {
      if (notFirst) {
        sb.Append(", ");
      }
      sb.Append($"{localVariableAddressGap.GapStartOffset:X4}:{localVariableAddressGap.Range:X4}");
      notFirst = true;
    }

    sb.Append(" } */");

    return sb.ToString();
  }

  public static string ToString(this DefRangeRegisterSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.Append("/* Register Range: ")
      .Append($"Register = {s.Register} ")
      .AppendIf(s.MayHaveNoName != 0, $"MayHaveNoName = {s.MayHaveNoName:X4} ")
      .Append($"Range = {s.Range.OffsetStart:X8}:{s.Range.Range:X4} ");
    sb.Append("Gaps: { ");
    bool notFirst = false;
    foreach (LocalVariableAddressGap localVariableAddressGap in s.Gaps) {
      if (notFirst) {
        sb.Append(", ");
      }
      sb.Append($"{localVariableAddressGap.GapStartOffset:X4}:{localVariableAddressGap.Range:X4}");
      notFirst = true;
    }

    sb.Append(" } */");

    return sb.ToString();
  }

  public static string ToString(this DefRangeSubfieldRegisterSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.Append("/* Register Subfield Range: ")
      .Append($"Register = {s.Register} ")
      .AppendIf(s.MayHaveNoName != 0, $"MayHaveNoName = {s.MayHaveNoName:X4} ")
      .Append($"OffsetInParent = {s.OffsetInParent:X8} ")
      .Append($"Range = {s.Range.OffsetStart:X8}:{s.Range.Range:X4} ");
    sb.Append("Gaps: { ");
    bool notFirst = false;
    foreach (LocalVariableAddressGap localVariableAddressGap in s.Gaps) {
      if (notFirst) {
        sb.Append(", ");
      }
      sb.Append($"{localVariableAddressGap.GapStartOffset:X4}:{localVariableAddressGap.Range:X4}");
      notFirst = true;
    }

    sb.Append(" } */");

    return sb.ToString();
  }

  public static string ToString(this EndSymbol s, PdbFile pdb) {
    return s.Kind switch {
      SymbolRecordKind.S_END => "/*        Site End */",
      SymbolRecordKind.S_INLINESITE_END => "/* Inline Site End */",
      _ => $"/* UNHANDLED END SYMBOL KIND: {Enum.GetName(s.Kind)} */"
    };
  }

  public static string ToString(this EnvironmentBlockSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.AppendLine("/* Environment Block: {");
    for (int i = 0; i < s.Fields.Count - 1; i += 2) {
      string key = s.Fields[i];
      string value = s.Fields[i + 1];
      sb.Append("    ")
        .Append(key)
        .Append(" = ")
        .AppendLine(value);
    }

    return sb
      .Append("} */")
      .ToString();
  }

  public static string ToString(this ExportSymbol s, PdbFile pdb) {
    return
      $"/* Export " +
      $"Name = {s.Name.String} " +
      $"Ordinal = {s.Ordinal} " +
      $"Flags = {s.Flags}";
  }

  public static string ToString(this FileStaticSymbol s, PdbFile pdb) {
    return
      $"/* File Static: " +
      $"Name = {s.Name.String} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"Flags = {s.Flags} " +
      $"ModFilenameOffset = {s.ModFilenameOffset:X8} " +
      $"*/";
  }

  public static string ToString(this FrameCookieSymbol s, PdbFile pdb) {
    return
      $"/* Frame Cookie: " +
      $"Offset = {s.CodeOffset:X8} " +
      $"Register = {s.Register} " +
      $"Flags = {s.Flags} " +
      $"CookieKind = {s.CookieKind} " +
      $"*/";
  }

  public static string ToString(this FrameProcedureSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb
      .Append("/* Frame Procedure: ")
      .AppendIf(s.Flags != 0, $"Flags = {s.Flags} ")
      .AppendIf(s.TotalFrameBytes != 0, $"TotalFrameBytes = {s.TotalFrameBytes:X4} ")
      .AppendIf(s.PaddingFrameBytes != 0, $"PaddingBytes = {s.PaddingFrameBytes:X4} ")
      .AppendIf(s.OffsetToPadding != 0, $"OffsetToPadding = {s.OffsetToPadding:X8} ")
      .AppendIf(s.OffsetOfExceptionHandler != 0, $"OffsetOfExceptionHandler = {s.OffsetOfExceptionHandler:X8} ")
      .AppendIf(s.SectionIdOfExceptionHandler != 0, $"SectionIdOfExceptionHandler = {s.SectionIdOfExceptionHandler:X8} ")
      .AppendIf(s.BytesOfCalleeSavedRegisters != 0, $"BytesOfCalleeSavedRegisters = {s.BytesOfCalleeSavedRegisters:X4} ")
      .Append("*/")
      .ToString();
  }

  public static string ToString(this FunctionListSymbol s, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.AppendLine("/* Function List: Functions = {");
    foreach (TypeIndex func in s.Functions) {
      sb.Append("    ")
        .Append(func.ToString(pdb))
        .AppendLine();
    }

    sb.AppendLine("}")
      .Append("Invocations = {");
    foreach (uint numInvocations in s.Invocations) {
      sb.Append("    ")
        .Append(numInvocations)
        .AppendLine();
    }

    return sb
      .Append("} */")
      .ToString();
  }

  public static string ToString(this HeapAllocationSiteSymbol s, PdbFile pdb) {
    return
      $"/* Heap Allocation Site: " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"Call Instruction Size = {s.CallInstructionSize:X4} " +
      $"*/";
  }

  public static string ToString(this InlineSiteSymbol s, PdbFile pdb) {
    // Skipping for now
    TypeRecord record = pdb.GetRecord(s.Inlinee, pdb.IpiStream);
    if (record is MemberFunctionIdRecord mFuncId) {
      record = pdb.GetRecord(mFuncId.FunctionType);
    }
    else if (record is FunctionIdRecord funcId) {
      record = pdb.GetRecord(funcId.FunctionType);
    }

    return $"/* Inline Site: Inlinee = {record.ToString(pdb)} */";
  }

  public static string ToString(this LabelSymbol s, PdbFile pdb) {
    return
      $"/* Label " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Name = {s.Name} " +
      $"Flags = {s.Flags} " +
      $"*/";
  }

  public static string ToString(this LocalSymbol s, PdbFile pdb) {
    return
      $"/* Local " +
      $"{s.Type.ToString(pdb)} " +
      $"{s.Name} " +
      (s.Flags != 0 ? $"// Flags = {s.Flags} " : "") +
      "*/";
  }

  public static string ToString(this ManagedProcedureSymbol s, PdbFile pdb) {
    return
      $"/* Managed Procedure: " +
      $"Name = {s.Name.String} " +
      $"Type = {s.FunctionType.ToString(pdb)} " +
      $"Flags = {s.Flags} " +
      $"Code Offset = {s.CodeOffset:X8}:{s.CodeSize:X8} " +
      $"Debug Range = {s.DebugStart:X8} - {s.DebugEnd:X8} " +
      $"*/";
  }

  public static string ToString(this NamespaceSymbol s, PdbFile pdb) {
    return
      $"/* Namespace: " +
      $"Name = {s.Namespace.String} " +
      $"*/";
  }

  public static string ToString(this ObjectNameSymbol s, PdbFile pdb) {
    return
      $"/* Object Name: " +
      $"Name = {s.Name.String} " +
      $"Signature = {s.Signature} " +
      $"*/";
  }

  public static string ToString(this OemSymbol s, PdbFile pdb) {
    return
      $"/* OEM Symbol: " +
      $"ID = {s.Id} " +
      $"Type = {s.TypeIndex} " +
      $"*/";
  }

  public static string ToString(this ProcedureReferenceSymbol s, PdbFile pdb) {
    return
      $"/* Procedure Reference: " +
      $"Name = {s.Name.String} " +
      $"Module = {s.Module} " +
      $"Offset = {s.Offset:X8} " +
      $"Checksum = {s.Checksum:X8} " +
      $"*/";
  }

  public static string ToString(this ProcedureSymbol procedureSymbol, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    TypeRecord? funcRecord = pdb.TryGetRecord(procedureSymbol.FunctionType);
    string procName = procedureSymbol.Name.String;
    bool isStatic;
    int paramsLeft;
    switch (funcRecord) {
      case ProcedureRecord procRecord:
        paramsLeft = procRecord.ParameterCount;
        isStatic = procedureSymbol.Kind is SymbolRecordKind.S_GPROC32;

        sb.Append("/*    PROC */ ")
          .AppendIf(isStatic, "static ")
          .Append(procRecord.ReturnType.ToString(pdb))
          .Append(' ')
          .Append(procName);
        break;
      case MemberFunctionRecord mFunc: {
        paramsLeft = mFunc.ParameterCount;
        isStatic = mFunc.ThisType is
          { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void };
        bool isConstructor = mFunc.Options.HasFlag(FunctionOptions.Constructor);
        string className = mFunc.ClassType.ToString(pdb);

        sb.Append("/* MEMPROC */ ")
          .AppendIf(isStatic, "static ")
          .AppendIf(isConstructor, "/* Ctor */ ")
          // Return value is Void for constructors, but writing the class type is more informative
          .Append((isConstructor ? mFunc.ClassType : mFunc.ReturnType).ToString(pdb))
          .Append(' ')
          .Append(className)
          .AppendIf(!isConstructor, "::")
          .AppendIf(!isConstructor, procName.AsSpan()[(className.Length + 2)..]);

        break;
      }
      case null: {
        return string.Empty;
      }
      default: {
        paramsLeft = 0;
        sb.Append("/* UNKNOWN FUNCTION TYPE: ")
          .Append(funcRecord.GetType().Name)
          .Append(" */ ")
          .Append(procName);
        break;
      }
    }

    sb.Append('(');
    foreach (LocalSymbol local in procedureSymbol.Children.OfType<LocalSymbol>()) {
      if (local.Name.String == "this" || !local.Flags.HasFlag(LocalVariableFlags.IsParam)) {
        continue;
      }

      sb
        .AppendIf(paramsLeft == -1, "/* More local symbols that shouldn't be matched to a param */")
        .Append(local.Type.ToString(pdb))
        .Append(' ')
        .Append(local.Name.String)
        .AppendIf(--paramsLeft > 0, ", ");
    }

    return sb
      .AppendIf(paramsLeft > 0, "/* Missing " + paramsLeft + " parameters */")
      .Append(");")
      .ToString();
  }

  public static string ToString(this Public32Symbol s, PdbFile pdb) {
    return
      $"/* Public32: " +
      $"Name = {s.Name.String} " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Flags = {s.Flags} " +
      $"*/";
  }

  public static string ToString(this RegisterRelativeSymbol s, PdbFile pdb) {
    return
      $"/* Register Relative: " +
      $"Name = {s.Name} " +
      $"Register = {s.Register} " +
      $"Offset = {s.Offset:X8} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this SectionSymbol s, PdbFile pdb) {
    return
      $"/* Section: " +
      $"Name = {s.Name.String} " +
      $"SectionNumber = {s.SectionNumber} " +
      $"Offset = {s.RelativeVirtualAddress:X8}:{s.Length:X8} " +
      $"Characteristics = {s.Characteristics} " +
      $"*/";
  }

  public static string ToString(this ThreadLocalDataSymbol s, PdbFile pdb) {
    return
      $"/* Thread Local Data: " +
      $"Name = {s.Name.String} " +
      $"Offset = {s.Offset:X8}:{s.Segment:X4} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this Thunk32Symbol s, PdbFile pdb) {
    return
      $"/* Thunk32: " +
      $"Name = {s.Name.String} " +
      $"Offset = {s.Offset:X8}:{s.Length:X4} " +
      $"Segment = {s.Segment:X4} " +
      $"Ordinal = {s.Ordinal} " +
      $"*/";
  }

  public static string ToString(this TokenReferenceSymbol s, PdbFile pdb) {
    return
      $"/* Token Reference: " +
      $"Name = {s.Name.String} " +
      $"Offset = {s.Offset:X8} " +
      $"Module = {s.Module} " +
      $"Token = {s.Token:X8} " +
      $"*/";
  }

  public static string ToString(this TrampolineSymbol s, PdbFile pdb) {
    return
      $"/* Trampoline: " +
      $"Target Offset = {s.TargetOffset:X8}:{s.TargetSection:X4} " +
      $"Thunk Offset = {s.ThunkOffset:X8}:{s.ThunkSection:X4} " +
      $"Type = {s.Type} " +
      $"Size = {s.Size:X4} " +
      $"*/";
  }

  public static string ToString(this UdtSymbol s, PdbFile pdb) {
    return
      $"/* UDT: " +
      $"Name = {s.Name.String} " +
      $"Type = {s.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this SymbolRecord record, PdbFile pdb) {
    return record switch {
      null => "NULL SYMBOL",
      AnnotationReferenceSymbol annotationReferenceSymbol => annotationReferenceSymbol.ToString(pdb),
      AnnotationSymbol annotationSymbol => annotationSymbol.ToString(pdb),
      AttributeSlotSymbol attributeSlotSymbol => attributeSlotSymbol.ToString(pdb),
      BlockSymbol blockSymbol => blockSymbol.ToString(pdb),
      BuildInfoSymbol buildInfoSymbol => buildInfoSymbol.ToString(pdb),
      CallSiteInfoSymbol callSiteInfoSymbol => callSiteInfoSymbol.ToString(pdb),
      CoffGroupSymbol coffGroupSymbol => coffGroupSymbol.ToString(pdb),
      Compile2Symbol compile2Symbol => compile2Symbol.ToString(pdb),
      Compile3Symbol compile3Symbol => compile3Symbol.ToString(pdb),
      ConstantSymbol constantSymbol => constantSymbol.ToString(pdb),
      DataSymbol dataSymbol => dataSymbol.ToString(pdb),
      DefRangeFramePointerRelativeFullScopeSymbol defRangeFramePointerRelativeFullScopeSymbol =>
        defRangeFramePointerRelativeFullScopeSymbol.ToString(pdb),
      DefRangeFramePointerRelativeSymbol defRangeFramePointerRelativeSymbol => defRangeFramePointerRelativeSymbol
        .ToString(pdb),
      DefRangeRegisterRelativeSymbol defRangeRegisterRelativeSymbol => defRangeRegisterRelativeSymbol.ToString(pdb),
      DefRangeRegisterSymbol defRangeRegisterSymbol => defRangeRegisterSymbol.ToString(pdb),
      DefRangeSubfieldRegisterSymbol defRangeSubfieldRegisterSymbol => defRangeSubfieldRegisterSymbol.ToString(pdb),
      EndSymbol endSymbol => endSymbol.ToString(pdb),
      EnvironmentBlockSymbol environmentBlockSymbol => environmentBlockSymbol.ToString(pdb),
      ExportSymbol exportSymbol => exportSymbol.ToString(pdb),
      FileStaticSymbol fileStaticSymbol => fileStaticSymbol.ToString(pdb),
      FrameCookieSymbol frameCookieSymbol => frameCookieSymbol.ToString(pdb),
      FrameProcedureSymbol frameProcedureSymbol => frameProcedureSymbol.ToString(pdb),
      FunctionListSymbol functionListSymbol => functionListSymbol.ToString(pdb),
      HeapAllocationSiteSymbol heapAllocationSiteSymbol => heapAllocationSiteSymbol.ToString(pdb),
      InlineSiteSymbol inlineSiteSymbol => inlineSiteSymbol.ToString(pdb),
      LabelSymbol labelSymbol => labelSymbol.ToString(pdb),
      LocalSymbol localSymbol => localSymbol.ToString(pdb),
      ManagedProcedureSymbol managedProcedureSymbol => managedProcedureSymbol.ToString(pdb),
      NamespaceSymbol namespaceSymbol => namespaceSymbol.ToString(pdb),
      ObjectNameSymbol objectNameSymbol => objectNameSymbol.ToString(pdb),
      OemSymbol oemSymbol => oemSymbol.ToString(pdb),
      ProcedureReferenceSymbol procedureReferenceSymbol => procedureReferenceSymbol.ToString(pdb),
      ProcedureSymbol procedureSymbol => procedureSymbol.ToString(pdb),
      Public32Symbol public32Symbol => public32Symbol.ToString(pdb),
      RegisterRelativeSymbol registerRelativeSymbol => registerRelativeSymbol.ToString(pdb),
      SectionSymbol sectionSymbol => sectionSymbol.ToString(pdb),
      ThreadLocalDataSymbol threadLocalDataSymbol => threadLocalDataSymbol.ToString(pdb),
      Thunk32Symbol thunk32Symbol => thunk32Symbol.ToString(pdb),
      TokenReferenceSymbol tokenReferenceSymbol => tokenReferenceSymbol.ToString(pdb),
      TrampolineSymbol trampolineSymbol => trampolineSymbol.ToString(pdb),
      UdtSymbol udtSymbol => udtSymbol.ToString(pdb),
      _ => $"/* UNHANDLED {Enum.GetName(record.Kind)} - {record.GetType().Name} */"
    };
  }
}
