using System.Text;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedParameter.Global

namespace PdbToCSharp.Dissect;

internal static partial class TypeRecordExtensions {
  public static string ToString(this ArgumentListRecord argumentList, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    int len = argumentList.Arguments.Length;
    for (int i = 0; i < len; i++) {
      TypeIndex argument = argumentList.Arguments[i];
      bool isNotLast = i != len - 1;

      sb.Append(argument.ToString(pdb))
        .Append(" param_")
        .Append(i + 1)
        .AppendIf(isNotLast, ", ");
    }

    return sb.ToString();
  }

  // Cannot write the "[Count]" here, since it must come after the variable name, not available in this context.
  public static string ToString(this ArrayRecord arrayRecord, PdbFile pdb) => arrayRecord.ElementType.ToString(pdb);

  public static string ToString(this BaseClassRecord baseClassRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    MemberAccess access = baseClassRecord.Attributes.Access;
    return access is MemberAccess.None
      ? baseClassRecord.Type.ToString(pdb)
      : sb
        .Append(Enum.GetName(access)!.ToLower())
        .Append(": ")
        .Append(baseClassRecord.Type.ToString(pdb))
        .ToString();
  }

  // Cannot write the ": Size" here, since it must come after the variable name, not available in this context
  public static string ToString(this BitFieldRecord bitFieldRecord, PdbFile pdb) => bitFieldRecord.Type.ToString(pdb);

  public static string ToString(this BuildInfoRecord buildInfoRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.Append("/* Build Info: ")
      .AppendLine("Indexes = {");
    foreach (TypeIndex bType in buildInfoRecord.Indexes) {
      StringIdRecord child = (StringIdRecord)pdb.IpiStream[bType];
      sb.AppendLine($"    \"{child.String.String}\"");
    }

    return sb
      .Append("} */")
      .ToString();
  }

  // ClassRecord handled by TagRecord

  public static string ToString(this DataMemberRecord dataMemberRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);

    sb.Append("/* offset = 0x")
      .Append($"{dataMemberRecord.FieldOffset:X4}")
      .Append(" */ ")
      .Append(dataMemberRecord.Type.ToString(pdb))
      .Append(' ')
      .Append(dataMemberRecord.Name.String);
    if (!dataMemberRecord.Type.IsSimple) {
      TypeRecord t = pdb.GetRecord(dataMemberRecord.Type);
      switch (t) {
        case ArrayRecord arrayRecord:
          sb.Append('[')
            .Append(arrayRecord.Size)
            .Append(']');
          break;
        case BitFieldRecord bitFieldRecord:
          sb.Append(" : ")
            .Append(bitFieldRecord.BitSize);
          break;
      }
    }

    return sb
      .Append(';')
      .ToString();
  }

  // EnumRecord handled by TagRecord

  public static string ToString(this EnumeratorRecord enumeratorRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb
      .Append(enumeratorRecord.Name.String)
      .Append(" = ")
      .Append(enumeratorRecord.Value)
      .Append(", /* 0x")
      .Append($"{enumeratorRecord.Value:X8}")
      .Append(" */")
      .ToString();
  }

  public static string ToString(this FieldListRecord fieldListRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    foreach (TypeRecord field in fieldListRecord.Fields
               .Where(f => f is not BaseClassRecord and not VirtualBaseClassRecord)) {
      if (field is ListContinuationRecord listContinuationRecord) {
        FieldListRecord listRecord = pdb.GetRecord<FieldListRecord>(listContinuationRecord.ContinuationIndex);
        return sb
          .Append(listRecord.ToString(pdb))
          .ToString();
      }

      string value = field.ToString(pdb);
      sb.AppendLine()
        .Append(value);
    }

    return sb.ToString();
  }

  public static string ToString(this FunctionIdRecord functionIdRecord, PdbFile pdb) {
    return
      $"/* FunctionId: " +
      $"Name = {functionIdRecord.Name.String} " +
      $"Type = {functionIdRecord.FunctionType.ToString(pdb)} " +
      $"ParentScope = {functionIdRecord.ParentScope.Index} " +
      $"*/";
  }

  public static string ToString(this LabelRecord labelRecord, PdbFile pdb) {
    return
      $"/* Label: " +
      $"Mode = {labelRecord.Mode} " +
      $"*/";
  }

  public static string ToString(this ListContinuationRecord listContinuationRecord, PdbFile pdb) =>
    pdb.GetRecord<FieldListRecord>(listContinuationRecord.ContinuationIndex).ToString(pdb);

  public static string ToString(this MemberFunctionIdRecord memberFunctionIdRecord, PdbFile pdb) {
    return
      $"/* MemberFunctionId: " +
      $"Name = {memberFunctionIdRecord.Name.String}, " +
      $"Type = {memberFunctionIdRecord.FunctionType.ToString(pdb)}, " +
      $"ClassType = {memberFunctionIdRecord.ClassType.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this MemberFunctionRecord memberFunctionRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    ushort paramsLeft = memberFunctionRecord.ParameterCount;
    sb.Append("/* MEMPROC */ ");
    bool isConstructor = memberFunctionRecord.Options.HasFlag(FunctionOptions.Constructor);
    bool isStatic = memberFunctionRecord.ThisType is
      { IsSimple: true, SimpleKind: SimpleTypeKind.None or SimpleTypeKind.Void };
    if (isStatic) {
      sb.Append("static ");
    }

    if (isConstructor) {
      sb.Append("/* Ctor */ ");
      // Return value is Void for constructors, but writing the class type is more informative
      sb.Append(memberFunctionRecord.ClassType.ToString(pdb));
    }
    else {
      sb.Append(memberFunctionRecord.ReturnType.ToString(pdb));
    }

    sb.Append(' ');
    string className = memberFunctionRecord.ClassType.ToString(pdb);
    sb.Append(className);
    if (!isConstructor) {
      sb.Append("::");
      // sb.Append(procName.AsSpan()[(className.Length + 2)..]);
    }

    return sb.ToString();
  }

  public static string ToString(this MethodOverloadListRecord methodOverloadListRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    foreach (OneMethodRecord oneMethodRecord in methodOverloadListRecord.Methods) {
      sb.AppendLine();
      sb.Append(oneMethodRecord.ToString(pdb));
    }

    return sb.ToString();
  }

  public static string ToString(this ModifierRecord modifierRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb
      // commented out for C# code gen
      // .AppendIf(modifierRecord.Modifiers.HasFlag(ModifierOptions.Const), "const ")
      .AppendIf(modifierRecord.Modifiers.HasFlag(ModifierOptions.Volatile), "volatile ")
      .Append(modifierRecord.ModifiedType.ToString(pdb))
      .ToString();
  }

  public static string ToString(this NestedTypeRecord nestedTypeRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    if (nestedTypeRecord.Type.IsSimple) {
      return sb.Append("/* Nested simple type: ")
        .Append(nestedTypeRecord.Type.SimpleTypeName)
        .Append(" */")
        .ToString();
    }

    TypeRecord nestedType = pdb.GetRecord(nestedTypeRecord.Type);
    if (nestedType is TagRecord) {
      // WriteDefinition(tagRecord);
      return sb.ToString();
    }

    return sb.Append("/* Nested type: ")
      .Append(nestedTypeRecord.Type.ToString(pdb))
      .Append(" (")
      .Append(nestedType.GetType().Name)
      .Append(") ")
      .Append(" */")
      .ToString();
  }

  public static string ToString(this OneMethodRecord methodMember, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    MemberFunctionRecord methodFunction = pdb.GetRecord<MemberFunctionRecord>(methodMember.Type);
    if (methodMember.Name.String?.StartsWith('~') ?? false) {
      return sb.Append("/* Skipping Destructor function ")
        .Append(methodMember.Name.String)
        .Append("() */")
        .ToString();
    }

    ArgumentListRecord args = pdb.GetRecord<ArgumentListRecord>(methodFunction.ArgumentList);
    if (!methodFunction.Options.HasFlag(FunctionOptions.Constructor)) {
      sb.Append(methodFunction.ReturnType.ToString(pdb))
        .Append(' ');
    }

    return sb.Append(_currentMethodOverloadName ?? methodMember.Name.String)
      .Append('(')
      .Append(args.ToString(pdb))
      .Append(") {")
      // WriteMethodBody();
      .Append('}')
      .ToString();

    void WriteMethodBody() {
      // _padding += 4;
      sb.AppendLine()
        .Append("return CallAssembly__FromGenerated<")
        .Append(methodFunction.ReturnType.ToString(pdb))
        .Append(">(")
        .Append('0') // TODO: Replace this with an actual proper value
        // TODO: Pass arguments to CallAssembly__FromGenerated
        .Append(");");
      // _padding -= 4;
      sb.AppendLine();
    }
  }

  public static string ToString(this OverloadedMethodRecord overloadedMethodRecord, PdbFile pdb) {
    string? oldName = _currentMethodOverloadName;
    _currentMethodOverloadName = overloadedMethodRecord.Name.String;
    string result = overloadedMethodRecord.MethodList.As<MethodOverloadListRecord>(pdb).ToString(pdb);
    _currentMethodOverloadName = oldName;
    return result;
  }

  public static string ToString(this PointerRecord pointerRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb
        // TODO: along with this whole file, ensure everything is useful for C# code gen
      // commented out for C# code gen
      //.AppendIf(pointerRecord.IsConst, "const ")
      .AppendIf(pointerRecord.IsVolatile, "volatile ")
      .Append(pointerRecord.ReferentType.ToString(pdb))
      .Append(pointerRecord.Mode switch {
        PointerMode.Pointer => "*",
        PointerMode.LValueReference => "&",
        PointerMode.RValueReference => "&&",
        _ => string.Empty
      })
      .ToString();

    // PointerKindCounts.Increment(pointerRecord.PointerKind);
  }

  public static string ToString(this ProcedureRecord procedureRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    ushort paramsLeft = procedureRecord.ParameterCount;
    sb.Append("/*    PROC */ ");

    // bool isStatic = proc.Kind is SymbolRecordKind.S_GPROC32;
    // if (isStatic) {
    //   sb.Append("static ");
    // }

    sb.Append(procedureRecord.ReturnType.ToString(pdb));
    sb.Append(' ');
    // sb.Append(procName);
    return sb.ToString();
  }

  public static string ToString(this StaticDataMemberRecord staticDataMemberRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb.Append("static ")
      .Append(staticDataMemberRecord.Type.ToString(pdb))
      .Append(' ')
      .Append(staticDataMemberRecord.Name.String)
      .Append(';')
      .ToString();
  }

  public static string ToString(this StringIdRecord stringIdRecord, PdbFile pdb) {
    return
      $"/* StringId: " +
      $"Id = {stringIdRecord.Id.ToString(pdb)} " +
      $"Name = \"{stringIdRecord.String.String}\" */";
  }

  public static string ToString(this StringListRecord stringListRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    sb.AppendLine("/* StringList: {");
    foreach (TypeIndex str in stringListRecord.StringIndices) {
      sb.Append("    ")
        .AppendLine(str.ToString(pdb));
    }

    return sb
      .Append("} */")
      .ToString();
  }

  public static string ToString(this TagRecord tagRecord, PdbFile pdb) => tagRecord.Name.String.Replace("::", "__");

  public static string ToString(this UdtModuleSourceLineRecord udtModuleSourceLineRecord, PdbFile pdb) {
    return
      $"/* UdtModuleSourceLine: " +
      $"Module = {udtModuleSourceLineRecord.Module:X4}, " +
      $"LineNumber = {udtModuleSourceLineRecord.LineNumber} " +
      $"UDT = {udtModuleSourceLineRecord.UDT.ToString(pdb)} " +
      $"SourceFile = {udtModuleSourceLineRecord.SourceFile.Index} " +
      $"*/";
  }

  // UnionRecord handled by TagRecord

  public static string ToString(this VirtualBaseClassRecord virtualBaseClassRecord, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    MemberAccess access = virtualBaseClassRecord.Attributes.Access;
    bool hasAccessType = access is not MemberAccess.None;

    return sb
      .AppendIf(hasAccessType, Enum.GetName(access)!.ToLower())
      .Append("virtual ")
      .Append(virtualBaseClassRecord.BaseType.ToString(pdb))
      .ToString();
  }

  public static string ToString(this VirtualFunctionPointerRecord virtualFunctionPointerRecord, PdbFile pdb) {
    return
      $"/* VF Pointer: " +
      $"Type = {virtualFunctionPointerRecord.Type.ToString(pdb)} " +
      $"*/";
  }

  public static string ToString(this VirtualFunctionTableShapeRecord vfts, PdbFile pdb) {
    using var _ = Rent(out StringBuilder sb);
    return sb
      .Append("/* VFT Shape: Slots = ")
      .Append('(')
      .Append(string.Join(", ", vfts.Slots.Select(Enum.GetName)))
      .Append(')')
      .Append(" */")
      .ToString();
  }

  public static string ToString(this TypeRecord record, PdbFile pdb) {
    return record switch {
      ArgumentListRecord argumentListRecord => argumentListRecord.ToString(pdb),
      ArrayRecord arrayRecord => arrayRecord.ToString(pdb),
      BaseClassRecord baseClassRecord => baseClassRecord.ToString(pdb),
      BitFieldRecord bitFieldRecord => bitFieldRecord.ToString(pdb),
      BuildInfoRecord buildInfoRecord => buildInfoRecord.ToString(pdb),
      // ClassRecord, covered by TagRecord
      DataMemberRecord dataMemberRecord => dataMemberRecord.ToString(pdb),
      // EnumRecord, covered by TagRecord
      EnumeratorRecord enumeratorRecord => enumeratorRecord.ToString(pdb),
      FieldListRecord fieldListRecord => fieldListRecord.ToString(pdb),
      FunctionIdRecord functionIdRecord => functionIdRecord.ToString(pdb),
      LabelRecord labelRecord => labelRecord.ToString(pdb),
      ListContinuationRecord listContinuationRecord => listContinuationRecord.ToString(pdb),
      MemberFunctionIdRecord memberFunctionIdRecord => memberFunctionIdRecord.ToString(pdb),
      MemberFunctionRecord memberFunctionRecord => memberFunctionRecord.ToString(pdb),
      MethodOverloadListRecord methodOverloadListRecord => methodOverloadListRecord.ToString(pdb),
      ModifierRecord modifierRecord => modifierRecord.ToString(pdb),
      NestedTypeRecord nestedTypeRecord => nestedTypeRecord.ToString(pdb),
      OneMethodRecord methodMember => methodMember.ToString(pdb),
      OverloadedMethodRecord overloadedMethodRecord => overloadedMethodRecord.ToString(pdb),
      PointerRecord pointerRecord => pointerRecord.ToString(pdb),
      ProcedureRecord procedureRecord => procedureRecord.ToString(pdb),
      StaticDataMemberRecord staticDataMemberRecord => staticDataMemberRecord.ToString(pdb),
      StringIdRecord stringIdRecord => stringIdRecord.ToString(pdb),
      StringListRecord stringListRecord => stringListRecord.ToString(pdb),
      TagRecord tagRecord => tagRecord.ToString(pdb),
      UdtModuleSourceLineRecord udtModuleSourceLineRecord => udtModuleSourceLineRecord.ToString(pdb),
      // UnionRecord, covered by TagRecord
      VirtualBaseClassRecord virtualBaseClassRecord => virtualBaseClassRecord.ToString(pdb),
      VirtualFunctionPointerRecord virtualFunctionPointerRecord => virtualFunctionPointerRecord.ToString(pdb),
      VirtualFunctionTableShapeRecord vfts => vfts.ToString(pdb),
      NullRecord => "/* Null Record */",
      _ => $"/* UNHANDLED {Enum.GetName(record.Kind)} - {record.GetType().Name} */"
    };
  }
}
