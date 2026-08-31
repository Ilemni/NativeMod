using System.CodeDom.Compiler;
using System.Text;
using SharpPdb.Windows;
using SharpPdb.Windows.DBI;
using SharpPdb.Windows.TypeRecords;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedParameter.Global

namespace PdbToCSharp.Dissect;

internal static partial class TypeRecordExtensions {
  extension(TextWriter writer) {
    public void WriteRecord(ArgumentListRecord argumentList, PdbFile pdb) {
      bool needsComma = false;
      foreach (TypeIndex arg in argumentList.Arguments) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.Write(arg.ToString(pdb));
        needsComma = true;
      }
    }

    public void WriteRecord(ArrayRecord arrayRecord, PdbFile pdb) {
      writer.WriteMany(arrayRecord.ElementType.ToString(pdb), "[", arrayRecord.Size.ToString(), "]");
    }

    public void WriteRecord(BaseClassRecord baseClassRecord, PdbFile pdb) {
      if (baseClassRecord.Attributes.Access is var access and not MemberAccess.None) {
        writer.WriteMany(typeof(MemberAccess).GetEnumName(access)!, ": ");
      }

      writer.Write(baseClassRecord.Type.ToString(pdb));
    }

    public void WriteRecord(BitFieldRecord bitFieldRecord, PdbFile pdb) {
      writer.WriteMany(bitFieldRecord.Type.ToString(pdb), " : ", bitFieldRecord.BitOffset.ToString(), ":",
        bitFieldRecord.BitSize.ToString());
    }

    public void WriteRecord(BuildInfoRecord buildInfoRecord, PdbFile pdb) {
      writer.Write("Build Info: ");
      IndentedTextWriter? iWriter = writer as IndentedTextWriter;
      TextWriterExtensions.BracedIndent bracedScope = iWriter is not null ? iWriter.BracedScope() : default;
      foreach (TypeIndex bType in buildInfoRecord.Indexes) {
        StringIdRecord child = bType.As<StringIdRecord>(pdb.IpiStream);
        writer.WriteLine($"\"{child.String.String}\"");
      }

      if (iWriter is not null) bracedScope.Dispose();
    }

    public void WriteRecord(DataMemberRecord data, PdbFile pdb) {
      writer.Write("[Offset 0x");
      writer.Write(data.FieldOffset.ToString("X4"));
      writer.Write("] ");
      writer.Write(data.Type.ToString(pdb));
      writer.Write(' ');
      writer.Write(data.Name.String);

      switch (data.Type.TryAsRecord(pdb)) {
        case ArrayRecord arrayRecord:
          writer.Write('[');
          writer.Write(arrayRecord.Size);
          writer.Write(']');
          break;
        case BitFieldRecord bitFieldRecord:
          writer.Write(" : ");
          writer.Write(bitFieldRecord.BitSize);
          break;
      }

      writer.Write(';');
    }

    public void WriteRecord(EnumeratorRecord e, PdbFile pdb) {
      writer.WriteMany(
        e.Name.String, " = ", e.Value.ToString()!,
        $", /* 0x{e.Value:X8} */");
    }

    public void WriteRecord(FieldListRecord fieldListRecord, PdbFile pdb) {
      writer.Write("Field List");
      IndentedTextWriter? iWriter = writer as IndentedTextWriter;
      TextWriterExtensions.BracedIndent bracedScope = iWriter is not null ? iWriter.BracedScope() : default;
      foreach (TypeRecord field in fieldListRecord.Fields
                 .Where(f => f is not BaseClassRecord and not VirtualBaseClassRecord)) {
        writer.WriteLine();
        writer.WriteRecord(field, pdb);
      }

      if (iWriter is not null) bracedScope.Dispose();
    }

    public void WriteRecord(FunctionIdRecord id, PdbFile pdb) {
      writer.WriteMany(
        "/* FunctionId:",
        " Name = ", id.Name.String,
        " Type = ", id.FunctionType.ToString(pdb),
        " ParentScope = ", id.ParentScope.Index.ToString(),
        " */");
    }

    public void WriteRecord(LabelRecord labelRecord, PdbFile pdb) {
      writer.WriteMany(
        "/* Label:",
        " Mode = ", labelRecord.Mode.ToString(),
        " */");
    }

    public void WriteRecord(ListContinuationRecord list, PdbFile pdb) {
      writer.WriteRecord(list.ContinuationIndex.As<FieldListRecord>(pdb), pdb);
    }

    public void WriteRecord(MemberFunctionIdRecord mFuncId, PdbFile pdb) {
      writer.WriteMany("/* MemberFunctionId: ",
        mFuncId.ClassType.ToString(pdb), "::", mFuncId.Name.String,
        " = ");
      MemberFunctionRecord mFunc = mFuncId.FunctionType.As<MemberFunctionRecord>(pdb);
      writer.WriteRecord(mFunc, pdb);
      writer.Write(" */");
    }

    public void WriteRecord(MemberFunctionRecord mFunc, PdbFile pdb) {
      bool isConstructor = mFunc.Options.HasFlag(FunctionOptions.Constructor);
      bool isStatic = mFunc.ThisType is
        { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void };
      bool hasRet = mFunc.ReturnType is not { IsSimple: true, SimpleKind: SimpleTypeKind.Void };
      writer.Write("/* MPROC */ ");
      writer.WriteIf("static ", isStatic);
      writer.WriteIf("ctor ", isConstructor);
      if (mFunc.ClassType.TryAsRecord(pdb) is { } cType) {
        writer.WriteRecord(cType, pdb);
      }
      else {
        writer.Write(mFunc.ClassType.ToString(pdb));
      }

      ArgumentListRecord args = mFunc.ArgumentList.As<ArgumentListRecord>(pdb);
      writer.Write('(');
      writer.WriteRecord(args, pdb);
      writer.Write(')');
      if (hasRet) {
        writer.Write(" -> ");
        if (mFunc.ReturnType.TryAsRecord(pdb) is { } rType) {
          writer.WriteRecord(rType, pdb);
        }
        else {
          writer.Write(mFunc.ReturnType.ToString(pdb));
        }
      }
    }

    public void WriteRecord(MethodOverloadListRecord methodOverloadListRecord, PdbFile pdb, string overloadName) {
      foreach (OneMethodRecord oneMethodRecord in methodOverloadListRecord.Methods) {
        writer.WriteRecord(oneMethodRecord, pdb, overloadName);
        writer.WriteLine();
      }
    }

    public void WriteRecord(ModifierRecord modifierRecord, PdbFile pdb) {
      writer.WriteFlagIfHasFlag(modifierRecord.Modifiers, ModifierOptions.Const, '\0', ' ');
      writer.WriteFlagIfHasFlag(modifierRecord.Modifiers, ModifierOptions.Volatile, '\0', ' ');
      writer.WriteFlagIfHasFlag(modifierRecord.Modifiers, ModifierOptions.Unaligned, '\0', ' ');
      if (modifierRecord.ModifiedType.TryAsRecord(pdb) is { } rType) {
        writer.WriteRecord(rType, pdb);
      }
      else {
        writer.Write(modifierRecord.ModifiedType.ToString(pdb));
      }
    }

    public void WriteRecord(NestedTypeRecord nestedTypeRecord, PdbFile pdb) {
      if (nestedTypeRecord.Type.TryAsRecord(pdb) is { } rType) {
        writer.Write("/* Nested :");
        writer.WriteRecord(rType, pdb);
      }
      else {
        writer.Write("/* Nested simple type:");
        writer.Write(nestedTypeRecord.Type.ToString(pdb));
      }

      writer.WriteMany(" (", nestedTypeRecord.Name.String, ") */");
    }

    public void WriteRecord(OneMethodRecord method, PdbFile pdb, string? overloadName = null) {
      string name = overloadName ?? method.Name.String;
      MemberFunctionRecord mFunc = method.Type.As<MemberFunctionRecord>(pdb);
      writer.Write(name);
      writer.WriteRecord(mFunc, pdb);
    }

    public void WriteRecord(OverloadedMethodRecord overload, PdbFile pdb) {
      writer.WriteRecord(overload.MethodList.As<MethodOverloadListRecord>(pdb), pdb, overload.Name.String);
    }

    public void WriteRecord(PointerRecord pointerRecord, PdbFile pdb, string? typeName = null) {
      writer.WriteIf("const ", pointerRecord.IsConst);
      writer.WriteIf("volatile ", pointerRecord.IsVolatile);
      writer.Write(typeName ?? pointerRecord.ReferentType.ToString(pdb));
      writer.Write(pointerRecord.Mode switch {
        PointerMode.Pointer => "*",
        PointerMode.LValueReference => "&",
        PointerMode.RValueReference => "&&",
        _ => string.Empty
      });
    }

    public void WriteRecord(ProcedureRecord proc, PdbFile pdb) {
      writer.WriteFlagIfHasFlag(proc.Options, FunctionOptions.Constructor, "ctor ");
      writer.WriteFlagIfHasFlag(proc.Options, FunctionOptions.ConstructorWithVirtualBases, "vctor ");
      writer.WriteFlagIfHasFlag(proc.Options, FunctionOptions.CxxReturnUdt, "cxxretudt ");
      writer.Write(proc.CallingConvention switch {
        CallingConvention.NearC or CallingConvention.FarC => "cdecl",
        CallingConvention.NearFast or CallingConvention.FarFast => "fastcall",
        CallingConvention.NearStdCall or CallingConvention.FarStdCall => "stdcall",
        CallingConvention.ThisCall => "thiscall",
        _ => proc.CallingConvention.ToString()
      });
      writer.Write(" (");
      writer.WriteRecord(proc.ArgumentList.As<ArgumentListRecord>(pdb), pdb);
      writer.Write(")");
      if (proc.ReturnType is not { SimpleMode: SimpleTypeMode.Direct, SimpleKind: SimpleTypeKind.Void }) {
        writer.Write(" -> ");
        writer.Write(proc.ReturnType.ToString(pdb));
      }
    }

    public void WriteRecord(StaticDataMemberRecord data, PdbFile pdb) {
      writer.WriteMany("static ", data.Type.ToString(pdb), " ", data.Name.String, ";");
    }

    public void WriteRecord(StringIdRecord stringIdRecord, PdbFile pdb) {
      writer.Write("/* StringId: ");
      writer.WriteManyIf(["Id = ", stringIdRecord.Id.Index.ToString()], stringIdRecord.Id.Index != 0);
      writer.WriteMany(" Name = \"", stringIdRecord.String.String, "\" */");
    }

    public void WriteRecord(StringListRecord stringListRecord, PdbFile pdb) {
      writer.Write("/* StringList:");
      IndentedTextWriter? iWriter = writer as IndentedTextWriter;
      TextWriterExtensions.BracedIndent bracedScope = iWriter is not null ? iWriter.BracedScope() : default;
      foreach (TypeIndex str in stringListRecord.StringIndices) {
        writer.WriteRecord(str.As<StringIdRecord>(pdb.IpiStream), pdb);
      }

      if (iWriter is not null) bracedScope.Dispose();
    }

    public void WriteRecord(TagRecord tagRecord, PdbFile pdb) => writer.Write(tagRecord.Name.String);

    public void WriteRecord(UdtModuleSourceLineRecord sourceLine, PdbFile pdb) {
      DbiModuleList moduleList = pdb.DbiStream.Modules;
      var namesDict = pdb.InfoStream.NamesMap.Dictionary;
      TypeIndex udt = sourceLine.UDT;
      TagRecord record = udt.As<TagRecord>(pdb);
      writer.WriteMany(
        "/* UdtModuleSourceLine:",
        record.IsForwardReference ? " FORWARD" : "",
        " UDT = ", record.Name.String, " (", udt.Index.ToString("X"), ")",
        " Module = ", moduleList[sourceLine.Module].ModuleName.String,
        " SourceFile = ", namesDict[sourceLine.SourceFile.Index],
        " LineNumber = ", sourceLine.LineNumber.ToString(),
        " */");
    }

    public void WriteRecord(VirtualBaseClassRecord vBaseClass, PdbFile pdb) {
      if (vBaseClass.Attributes.Access is var access and not MemberAccess.None) {
        writer.Write(access.ToString());
        writer.Write(" ");
      }

      writer.Write("virtual ");
      writer.WriteRecord(vBaseClass.BaseType.As<TagRecord>(pdb), pdb);
    }

    public void WriteRecord(VirtualFunctionPointerRecord vfPtr, PdbFile pdb) {
      writer.WriteMany(
        "/* VF Pointer:",
        " Type = ", vfPtr.Type.ToString(pdb),
        " */");
    }

    public void WriteRecord(VirtualFunctionTableShapeRecord vfts, PdbFile pdb) {
      writer.WriteMany("/* VFT Shape: Slots Count = ", vfts.Slots.Length.ToString());
    }

    public void WriteRecord(TypeRecord record, PdbFile pdb) {
      switch (record) {
        case ArgumentListRecord argumentListRecord:
          writer.WriteRecord(argumentListRecord, pdb);
          break;
        case ArrayRecord arrayRecord:
          writer.WriteRecord(arrayRecord, pdb);
          break;
        case BaseClassRecord baseClassRecord:
          writer.WriteRecord(baseClassRecord, pdb);
          break;
        case BitFieldRecord bitFieldRecord:
          writer.WriteRecord(bitFieldRecord, pdb);
          break;
        case BuildInfoRecord buildInfoRecord:
          writer.WriteRecord(buildInfoRecord, pdb);
          break;
        // ClassRecord, covered by TagRecord
        case DataMemberRecord dataMemberRecord:
          writer.WriteRecord(dataMemberRecord, pdb);
          break;
        // EnumRecord, covered by TagRecord
        case EnumeratorRecord enumeratorRecord:
          writer.WriteRecord(enumeratorRecord, pdb);
          break;
        case FieldListRecord fieldListRecord:
          writer.WriteRecord(fieldListRecord, pdb);
          break;
        case FunctionIdRecord functionIdRecord:
          writer.WriteRecord(functionIdRecord, pdb);
          break;
        case LabelRecord labelRecord:
          writer.WriteRecord(labelRecord, pdb);
          break;
        case ListContinuationRecord listContinuationRecord:
          writer.WriteRecord(listContinuationRecord, pdb);
          break;
        case MemberFunctionIdRecord memberFunctionIdRecord:
          writer.WriteRecord(memberFunctionIdRecord, pdb);
          break;
        case MemberFunctionRecord memberFunctionRecord:
          writer.WriteRecord(memberFunctionRecord, pdb);
          break;
        case ModifierRecord modifierRecord:
          writer.WriteRecord(modifierRecord, pdb);
          break;
        case NestedTypeRecord nestedTypeRecord:
          writer.WriteRecord(nestedTypeRecord, pdb);
          break;
        case OneMethodRecord methodMember:
          writer.WriteRecord(methodMember, pdb);
          break;
        case OverloadedMethodRecord overloadedMethodRecord:
          writer.WriteRecord(overloadedMethodRecord, pdb);
          break;
        case PointerRecord pointerRecord:
          writer.WriteRecord(pointerRecord, pdb);
          break;
        case ProcedureRecord procedureRecord:
          writer.WriteRecord(procedureRecord, pdb);
          break;
        case StaticDataMemberRecord staticDataMemberRecord:
          writer.WriteRecord(staticDataMemberRecord, pdb);
          break;
        case StringIdRecord stringIdRecord:
          writer.WriteRecord(stringIdRecord, pdb);
          break;
        case StringListRecord stringListRecord:
          writer.WriteRecord(stringListRecord, pdb);
          break;
        case TagRecord tagRecord:
          writer.WriteRecord(tagRecord, pdb);
          break;
        case UdtModuleSourceLineRecord udtModuleSourceLineRecord:
          writer.WriteRecord(udtModuleSourceLineRecord, pdb);
          break;
        // UnionRecord, covered by TagRecord
        case VirtualBaseClassRecord virtualBaseClassRecord:
          writer.WriteRecord(virtualBaseClassRecord, pdb);
          break;
        case VirtualFunctionPointerRecord virtualFunctionPointerRecord:
          writer.WriteRecord(virtualFunctionPointerRecord, pdb);
          break;
        case VirtualFunctionTableShapeRecord vfts:
          writer.WriteRecord(vfts, pdb);
          break;
        case NullRecord:
          writer.Write("/* Null Record */");
          break;
      }
    }
  }

  public static string ToString(this TypeRecord record, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    IndentedTextWriter writer = new(new StringWriter(sb));
    writer.WriteRecord(record, pdb);
    writer.Flush();
    return sb.ToString();
  }
}
