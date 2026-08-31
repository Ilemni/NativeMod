using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;
using Writer = System.CodeDom.Compiler.IndentedTextWriter;

// ReSharper disable MemberCanBePrivate.Global

namespace PdbToCSharp.Dissect;

internal static class DissectTypeSymbolExtensions {
  extension(Writer writer) {
    public void WriteSym(AnnotationReferenceSymbol s) {
      writer.WriteMany(
        "/* Annotation Reference:",
        " Module = ", s.Module.ToString(),
        " Symbol Index = ", s.SymbolIndex.ToString(),
        " Sum Name (TODO) = ", s.SumName.ToString(),
        " */");
    }

    public void WriteSym(AnnotationSymbol s) {
      writer.WriteMany(
        "/* Annotation:",
        " Offset = ", s.Offset.ToString("X8"), ":", s.Segment.ToString("X4"),
        ", Count = ", s.AnnotationsCount.ToString(),
        " */");
    }

    public void WriteSym(AttributeSlotSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* Attribute Slot: ",
        "Name = \"", s.Name.String, "\" ",
        "Type = ", s.TypeIndex.ToString(pdb),
        " */");
    }

    public void WriteSym(BlockSymbol s) {
      writer.WriteMany("Block \"", s.Name.String, "\"");
      using var _ = writer.BracedScope();
      foreach (SymbolRecord sym in s.Children) {
        writer.Write(sym);
      }
    }

    public void WriteSym(BuildInfoSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteRecord(s.BuildId.As<BuildInfoRecord>(pdb.IpiStream), pdb);
    }

    public void WriteSym(CallSiteInfoSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* Call Site Info:",
        " Offset = ", s.Offset.ToString("X8"), ":", s.Segment.ToString("X4"),
        " Type = ", s.Type.ToString(pdb),
        "*/");
    }

    public void WriteSym(CoffGroupSymbol s) {
      writer.WriteMany(
        "/* Coff Group:",
        " Offset = ", s.Offset.ToString("X8"), ":", s.Size.ToString("X8"),
        " Name = ", s.Name.String,
        " Characteristics = ", s.Characteristics.ToString(),
        "*/");
    }

    public void WriteSym(Compile2Symbol s) {
      writer.WriteMany(
        "/* Compile2:",
        " Machine = ", s.Machine.ToString(),
        " Version = ", s.Version.ToString(),
        " Flags = ", s.Flags.ToString(),
        "*/");
    }

    public void WriteSym(Compile3Symbol s) {
      writer.WriteMany(
        "/* Compile3:",
        " Machine = ", s.Machine.ToString(), " ",
        " Version = ", s.Version.ToString(),
        " Flags = ", s.Flags.ToString(),
        " */");
    }

    public void WriteSym(ConstantSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "const ", s.Name.String,
        " : ", s.TypeIndex.ToString(pdb),
        " = ", s.Value?.ToString()!, ";"
      );
    }

    public void WriteSym(DataSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* var ", s.Name.String,
        " : ", s.Type.ToString(pdb),
        " => 0x", pdb.FindRelativeVirtualAddress(s.Segment, s.Offset).ToString("X"),
        ";");
    }

    public void WriteSym(DefRangeFramePointerRelativeFullScopeSymbol s) {
      writer.WriteMany(
        "/* In Stack Frame (fixed) at ",
        $"Offset = ", s.Offset.ToString("X"),
        " */");
    }

    private void WriteGaps(LocalVariableAddressGap[] gaps) {
      if (gaps.Length == 0) {
        return;
      }

      writer.Write("Gaps: {");
      bool needsComma = false;
      foreach (LocalVariableAddressGap gap in gaps) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.WriteMany(gap.GapStartOffset.ToString("X4"), ":", gap.Range.ToString("X4"));
        needsComma = true;
      }

      writer.Write(" }");
    }

    public void WriteSym(DefRangeFramePointerRelativeSymbol s) {
      writer.WriteMany(
        "/* In Stack Frame at",
        " Offset = ", s.Offset.ToString("X"),
        " Range = ", s.Range.OffsetStart.ToString("X"), ":", s.Range.Range.ToString("X"));

      writer.WriteGaps(s.Gaps);
      writer.Write(" */");
    }

    public void WriteSym(DefRangeRegisterRelativeSymbol s) {
      writer.WriteMany(
        "/* Register Relative Range:",
        " Register = ", s.Register.ToString().PadLeft(10),
        " Offset = ", s.BasePointerOffset.ToString("X"),
        " Range = ", s.Range.OffsetStart.ToString("X"), ":", s.Range.Range.ToString("X"),
        " Flags = ", s.Flags.ToString("X4"));

      writer.WriteGaps(s.Gaps);
      writer.Write(" */");
    }

    public void WriteSym(DefRangeRegisterSymbol s) {
      uint start = s.Range.OffsetStart;
      ushort range = s.Range.Range;
      writer.WriteMany("/* Register Range: Register = ", s.Register.ToString().PadLeft(10));
      writer.WriteManyIf([" MayHaveNoName = ", s.MayHaveNoName.ToString("X4")], s.MayHaveNoName != 0);
      writer.WriteManyIf(["Range = ", start.ToString("X"), range.ToString("X")], start != 0 || range != 0);
      writer.WriteGaps(s.Gaps);
      writer.Write(" */");
    }

    public void WriteSym(DefRangeSubfieldRegisterSymbol s) {
      uint start = s.Range.OffsetStart;
      ushort range = s.Range.Range;
      writer.WriteMany("/* In Register Subfield Range ", s.Register.ToString().PadLeft(10));
      writer.WriteManyIf([" MayHaveNoName = ", s.MayHaveNoName.ToString("X4")], s.MayHaveNoName != 0);
      writer.WriteManyIf([" Range = ", start.ToString("X"), ":", range.ToString("X")], start != 0 || range != 0);
      writer.WriteManyIf([" OffsetInParent = ", s.OffsetInParent.ToString("X")], s.OffsetInParent != 0);
      writer.WriteGaps(s.Gaps);
      writer.Write(" */");
    }

    public void WriteSym(EndSymbol s) {
      writer.Write(s.Kind switch {
        SymbolRecordKind.S_END => "/*        Site End */",
        SymbolRecordKind.S_INLINESITE_END => "/* Inline Site End */",
        _ => $"/* UNHANDLED END SYMBOL KIND: {Enum.GetName(s.Kind)} */"
      });
    }

    public void WriteSym(EnvironmentBlockSymbol s) {
      writer.WriteLine("/* Environment Block: {");
      writer.Indent++;
      for (int i = 0; i < s.Fields.Count - 1; i += 2) {
        writer.WriteManyLine(s.Fields[i], " = ", s.Fields[i + 1]);
      }

      writer.Indent--;
      writer.WriteLine("} */");
    }

    public void WriteSym(ExportSymbol s) {
      writer.WriteMany(
        "/* Export:",
        " Name = ", s.Name.String,
        " Ordinal = ", s.Ordinal.ToString(),
        " Flags = ", s.Flags.ToString(),
        " */");
    }

    public void WriteSym(FileStaticSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* File Static:",
        " Name = ", s.Name.String,
        " Type = ", s.Type.ToString(pdb),
        " Flags = ", s.Flags.ToString(),
        " ModFilenameOffset = ", s.ModFilenameOffset.ToString("X8"),
        " */");
    }

    public void WriteSym(FrameCookieSymbol s) {
      writer.WriteMany(
        "/* Frame Cookie:",
        " Offset = ", s.CodeOffset.ToString("X8"),
        " Register = ", s.Register.ToString(),
        " Flags = ", s.Flags.ToString(),
        " CookieKind = ", s.CookieKind.ToString(),
        " */");
    }

    public void WriteSym(FrameProcedureSymbol s) {
      writer.Write("/* Frame Procedure:");
      writer.WriteKvpHexIf(" TotalFrameBytes", s.TotalFrameBytes, s.TotalFrameBytes != 0);
      writer.WriteKvpHexIf(" PaddingFrameBytes", s.PaddingFrameBytes, s.PaddingFrameBytes != 0);
      writer.WriteKvpHexIf(" OffsetToPadding", s.OffsetToPadding, s.OffsetToPadding != 0);
      writer.WriteKvpHexIf(" OffsetOfExceptionHandler", s.OffsetOfExceptionHandler, s.OffsetOfExceptionHandler != 0);
      writer.WriteKvpHexIf(" SectionIdOfExceptionHandler", s.SectionIdOfExceptionHandler,
        s.SectionIdOfExceptionHandler != 0);
      writer.WriteKvpHexIf(" BytesOfCalleeSavedRegisters", s.BytesOfCalleeSavedRegisters,
        s.BytesOfCalleeSavedRegisters != 0);

      FrameProcedureOptions f = s.Flags;
      if (f != 0) {
        writer.Write("Flags =");
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasAlloca, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasSetJmp, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasLongJmp, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasInlineAssembly, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasExceptionHandling, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.MarkedInline, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.HasStructuredExceptionHandling, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.Naked, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.SecurityChecks, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.AsynchronousExceptionHandling, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.NoStackOrderingForSecurityChecks, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.Inlined, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.StrictSecurityChecks, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.SafeBuffers, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.EncodedLocalBasePointerMask, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.EncodedParamBasePointerMask, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.ProfileGuidedOptimization, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.ValidProfileCounts, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.OptimizedForSpeed, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.GuardCfg, ' ');
        writer.WriteFlagIfHasFlag(f, FrameProcedureOptions.GuardCfw, ' ');
      }

      writer.Write(" */");
    }

    public void WriteSym(FunctionListSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteLine("/* Function List: {");
      using (writer.WithIndent()) {
        if (s.Functions.Length > 0) {
          writer.Write("Functions =");
          using (writer.BracedScope()) {
            foreach (TypeIndex func in s.Functions) {
              writer.WriteLine(func.ToString(pdb));
            }
          }
        }

        if (s.Invocations.Length > 0) {
          writer.Write("Invocations =");
          using (writer.BracedScope()) {
            foreach (uint numInvocations in s.Invocations) {
              writer.WriteLine(numInvocations.ToString());
            }
          }
        }
      }

      writer.Write("} */");
    }

    public void WriteSym(HeapAllocationSiteSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteLine("/* Heap Allocation Site: ");
      using (writer.WithIndent()) {
        writer.WriteLine($"Offset = {s.Offset:X8}:{s.Segment:X4} ");
        writer.WriteLine($"Type = {s.Type.ToString(pdb)} ");
        writer.WriteLine($"Call Instruction Size = {s.CallInstructionSize:X4} ");
      }

      writer.Write("*/");
    }

    public void WriteSym(InlineSiteSymbol s) {
      PdbFile pdb = s.Pdb;
      TypeRecord? record = s.Inlinee.TryAsRecord(pdb.IpiStream);
      string funcName;
      writer.WriteMany("Inlined[", (s.End - s.ParentOffset).ToString("X4").PadLeft(4), "] [", s.Inlinee.Index.ToString("X4"), "] ");
      switch (record) {
        case MemberFunctionIdRecord mFuncId: {
          MemberFunctionRecord mFunc = mFuncId.FunctionType.As<MemberFunctionRecord>(pdb);
          TypeRecord? classType = mFuncId.ClassType.TryAsRecord(pdb);
          string className = (classType as TagRecord)?.Name.String ?? mFuncId.ClassType.SimpleTypeName;
          funcName = mFuncId.Name.String;
          bool needsComma = false;
          writer.WriteIf("static memfunc ",
            mFunc.ThisType is { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void });
          writer.WriteMany(className, "::", funcName, "(");
          foreach (TypeIndex t in mFunc.ArgumentList.As<ArgumentListRecord>(pdb).Arguments) {
            writer.WriteCommaIfNeeded(ref needsComma);
            writer.Write(t.ToString(pdb));
            needsComma = true;
          }

          writer.Write(") -> ");
          writer.Write(mFunc.ReturnType.ToString(pdb));
          return;
        }
        case FunctionIdRecord funcId: {
          ProcedureRecord proc = funcId.FunctionType.As<ProcedureRecord>(pdb);
          funcName = funcId.Name.String;
          bool needsComma = false;
          writer.WriteMany("static ", funcName, "(");
          foreach (TypeIndex t in proc.ArgumentList.As<ArgumentListRecord>(pdb).Arguments) {
            writer.WriteCommaIfNeeded(ref needsComma);
            writer.Write(t.ToString(pdb));
            needsComma = true;
          }

          writer.Write(") -> ");
          writer.Write(proc.ReturnType.ToString(pdb));
          return;
        }
        default:
          writer.Write($"Unknown function type {record?.GetType().Name ?? s.Inlinee.ToString()} */");
          return;
      }
    }

    public void WriteSym(LabelSymbol s) {
      writer.WriteMany(
        "/* Label ", s.Name.String,
        " Offset = ", s.Offset.ToString("X8"), ":", s.Segment.ToString("X4"),
        " Flags = ", s.Flags.ToString(),
        " */"
      );
    }

    public void WriteSym(LocalSymbol s) {
      const LocalVariableFlags isParamFlag = LocalVariableFlags.IsParam;

      PdbFile pdb = s.Pdb;
      if (s.Name.String != "this") {
        writer.Write((s.Flags & isParamFlag) != 0 ? "param " : "var ");
      }
      writer.WriteMany(s.Name.String, " : ", s.Type.ToString(pdb), ";");

      LocalVariableFlags flagsWithoutParam = s.Flags & ~isParamFlag;
      if (flagsWithoutParam != 0) {
        writer.WriteMany(" // Flags = ", flagsWithoutParam.ToString());
      }
    }

    public void WriteSym(ManagedProcedureSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* Managed Procedure:",
        " Name = ", s.Name.String,
        " Type = ", s.FunctionType.ToString(pdb),
        " Flags = ", s.Flags.ToString(),
        " Code Offset = ", s.CodeOffset.ToString("X8"), ":", s.CodeSize.ToString("X8"),
        " Debug Range = ", s.DebugStart.ToString("X8"), ":", s.DebugEnd.ToString("X8"),
        " */");
    }

    public void WriteSym(NamespaceSymbol s) {
      writer.WriteMany("/* Namespace ", s.Namespace.String, " */");
    }

    public void WriteSym(ObjectNameSymbol s) {
      writer.WriteMany(
        "/* Object Name:",
        " Name = ", s.Name.String,
        " Signature = ", s.Signature.ToString(),
        " */");
    }

    public void WriteSym(OemSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* OEM Symbol:",
        " ID = ", s.Id.ToString(),
        " Type = ", s.TypeIndex.ToString(pdb),
        " */");
    }

    public void WriteSym(ProcedureReferenceSymbol s) {
      PdbFile pdb = s.Pdb;
      string module = pdb.DbiStream.Modules[s.Module].ModuleName.String;
      ProcedureSymbol procSym = s.GetProcedureSymbol();
      writer.Write("/* Procedure Reference:");
      writer.WriteManyIf([" Name = ", s.Name.String], s.Name.String != procSym.Name.String);
      writer.Write(" Procedure = { ");
      writer.WriteSym(procSym);
      writer.WriteMany(" } Module = ", module);
      writer.WriteManyIf([" Checksum = ", s.Checksum.ToString("X8")], s.Checksum != 0);
      writer.Write(" */");
    }

    public void WriteSym(ProcedureSymbol s) {
      PdbFile pdb = s.Pdb;
      string name = s.Name.String;
      string returnType;
      bool isStatic;
      bool isCtor;
      TypeRecord? funcRecord = s.FunctionType.TryAsRecord(pdb);
      switch (funcRecord) {
        case ProcedureRecord procRecord:
          returnType = procRecord.ReturnType.ToString(pdb);
          isStatic = s.Kind is SymbolRecordKind.S_GPROC32;
          isCtor = false;
          break;
        case MemberFunctionRecord mFunc: {
          returnType = mFunc.ReturnType.ToString(pdb);
          isStatic = mFunc.ThisType is { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void };
          isCtor = mFunc.Options.HasFlag(FunctionOptions.Constructor);
          break;
        }
        case null: {
          writer.WriteMany(
            "/* Procedure Symbol ", name,
            " with null function type: 0x", s.FunctionType.Index.ToString("X"), " */");
          return;
        }
        default: {
          writer.WriteMany(
            "/* Procedure Symbol with unknown function type: ",
            funcRecord.GetType().Name,
            " */");
          return;
        }
      }

      writer.WriteIf("static ", isStatic);
      writer.WriteMany(name, "(");
      writer.WriteParameterTypesAndNames(s.GetNamedArgs());
      writer.Write(')');
      if (!isCtor) {
        writer.WriteMany(" -> ", returnType);
      }
    }

    public void WriteSym(Public32Symbol s) {
      writer.WriteMany(
        "/* Public32:",
        " Name = ", s.Name.String,
        " Offset = ", s.Offset.ToString("X8"), ":", s.Segment.ToString("X4"),
        " Flags = ", s.Flags.ToString(),
        " */");
    }

    public void WriteSym(RegisterRelativeSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* Register Relative:",
        " Name = ", s.Name.String,
        " Register = ", s.Register.ToString(),
        " Offset = ", s.Offset.ToString("X8"),
        " Type = ", s.Type.ToString(pdb),
        " */");
    }

    public void WriteSym(SectionSymbol s) {
      writer.WriteMany(
        "/* Section: ",
        " Name = ", s.Name.String,
        " SectionNumber = ", s.SectionNumber.ToString(),
        " Offset = ", s.RelativeVirtualAddress.ToString("X8"), ":", s.Length.ToString("X8"),
        " Characteristics = ", s.Characteristics.ToString(),
        " */");
    }

    public void WriteSym(ThreadLocalDataSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* Thread Local Data: ",
        " Name = ", s.Name.String,
        " Offset = ", s.Offset.ToString("X8"), ":", s.Segment.ToString("X4"),
        " Type = ", s.Type.ToString(pdb),
        " */");
    }

    public void WriteSym(Thunk32Symbol s) {
      writer.WriteMany(
        "/* Thunk32: ",
        " Name = ", s.Name.String,
        " Offset = ", s.Offset.ToString("X8"), ":", s.Length.ToString("X4"),
        " Segment = ", s.Segment.ToString("X4"),
        " Ordinal = ", s.Ordinal.ToString(),
        " */");
    }

    public void WriteSym(TokenReferenceSymbol s) {
      writer.WriteMany(
        "/* Token Reference: ",
        " Name = ", s.Name.String,
        " Offset = ", s.Offset.ToString("X8"),
        " Module = ", s.Module.ToString(),
        " Token = ", s.Token.ToString("X8"),
        " */");
    }

    public void WriteSym(TrampolineSymbol s) {
      writer.WriteMany(
        "/* Trampoline: ",
        " Target Offset = ", s.TargetOffset.ToString("X8"), ":", s.TargetSection.ToString("X4"),
        " Thunk Offset = ", s.ThunkOffset.ToString("X8"), ":", s.ThunkSection.ToString("X4"),
        " Type = ", s.Type.ToString(),
        " Size = ", s.Size.ToString("X4"),
        " */");
    }

    public void WriteSym(UdtSymbol s) {
      PdbFile pdb = s.Pdb;
      writer.WriteMany(
        "/* UDT: ",
        " Name = ", s.Name.String,
        " Type = ", s.Type.ToString(pdb),
        " */");
    }

    public void WriteSym(SymbolRecord record) {
      switch (record) {
        case AnnotationReferenceSymbol annotationReferenceSymbol:
          writer.WriteSym(annotationReferenceSymbol);
          break;
        case AnnotationSymbol annotationSymbol:
          writer.WriteSym(annotationSymbol);
          break;
        case AttributeSlotSymbol attributeSlotSymbol:
          writer.WriteSym(attributeSlotSymbol);
          break;
        case BlockSymbol blockSymbol:
          writer.WriteSym(blockSymbol);
          break;
        case BuildInfoSymbol buildInfoSymbol:
          writer.WriteSym(buildInfoSymbol);
          break;
        case CallSiteInfoSymbol callSiteInfoSymbol:
          writer.WriteSym(callSiteInfoSymbol);
          break;
        case CoffGroupSymbol coffGroupSymbol:
          writer.WriteSym(coffGroupSymbol);
          break;
        case Compile2Symbol compile2Symbol:
          writer.WriteSym(compile2Symbol);
          break;
        case Compile3Symbol compile3Symbol:
          writer.WriteSym(compile3Symbol);
          break;
        case ConstantSymbol constantSymbol:
          writer.WriteSym(constantSymbol);
          break;
        case DataSymbol dataSymbol:
          writer.WriteSym(dataSymbol);
          break;
        case DefRangeFramePointerRelativeFullScopeSymbol defRangeFramePointerRelativeFullScopeSymbol:
          writer.WriteSym(defRangeFramePointerRelativeFullScopeSymbol);
          break;
        case DefRangeFramePointerRelativeSymbol defRangeFramePointerRelativeSymbol:
          writer.WriteSym(defRangeFramePointerRelativeSymbol);
          break;
        case DefRangeRegisterRelativeSymbol defRangeRegisterRelativeSymbol:
          writer.WriteSym(defRangeRegisterRelativeSymbol);
          break;
        case DefRangeRegisterSymbol defRangeRegisterSymbol:
          writer.WriteSym(defRangeRegisterSymbol);
          break;
        case DefRangeSubfieldRegisterSymbol defRangeSubfieldRegisterSymbol:
          writer.WriteSym(defRangeSubfieldRegisterSymbol);
          break;
        case EndSymbol endSymbol:
          writer.WriteSym(endSymbol);
          break;
        case EnvironmentBlockSymbol environmentBlockSymbol:
          writer.WriteSym(environmentBlockSymbol);
          break;
        case ExportSymbol exportSymbol:
          writer.WriteSym(exportSymbol);
          break;
        case FileStaticSymbol fileStaticSymbol:
          writer.WriteSym(fileStaticSymbol);
          break;
        case FrameCookieSymbol frameCookieSymbol:
          writer.WriteSym(frameCookieSymbol);
          break;
        case FrameProcedureSymbol frameProcedureSymbol:
          writer.WriteSym(frameProcedureSymbol);
          break;
        case FunctionListSymbol functionListSymbol:
          writer.WriteSym(functionListSymbol);
          break;
        case HeapAllocationSiteSymbol heapAllocationSiteSymbol:
          writer.WriteSym(heapAllocationSiteSymbol);
          break;
        case InlineSiteSymbol inlineSiteSymbol:
          writer.WriteSym(inlineSiteSymbol);
          break;
        case LabelSymbol labelSymbol:
          writer.WriteSym(labelSymbol);
          break;
        case LocalSymbol localSymbol:
          writer.WriteSym(localSymbol);
          break;
        case ManagedProcedureSymbol managedProcedureSymbol:
          writer.WriteSym(managedProcedureSymbol);
          break;
        case NamespaceSymbol namespaceSymbol:
          writer.WriteSym(namespaceSymbol);
          break;
        case ObjectNameSymbol objectNameSymbol:
          writer.WriteSym(objectNameSymbol);
          break;
        case OemSymbol oemSymbol:
          writer.WriteSym(oemSymbol);
          break;
        case ProcedureReferenceSymbol procedureReferenceSymbol:
          writer.WriteSym(procedureReferenceSymbol);
          break;
        case ProcedureSymbol procedureSymbol:
          writer.WriteSym(procedureSymbol);
          break;
        case Public32Symbol public32Symbol:
          writer.WriteSym(public32Symbol);
          break;
        case RegisterRelativeSymbol registerRelativeSymbol:
          writer.WriteSym(registerRelativeSymbol);
          break;
        case SectionSymbol sectionSymbol:
          writer.WriteSym(sectionSymbol);
          break;
        case ThreadLocalDataSymbol threadLocalDataSymbol:
          writer.WriteSym(threadLocalDataSymbol);
          break;
        case Thunk32Symbol thunk32Symbol:
          writer.WriteSym(thunk32Symbol);
          break;
        case TokenReferenceSymbol tokenReferenceSymbol:
          writer.WriteSym(tokenReferenceSymbol);
          break;
        case TrampolineSymbol trampolineSymbol:
          writer.WriteSym(trampolineSymbol);
          break;
        case UdtSymbol udtSymbol:
          writer.WriteSym(udtSymbol);
          break;
      }
    }

    public void WriteSymLine(SymbolRecord record) {
      writer.WriteSym(record);
      writer.WriteLine();
    }
  }
}
