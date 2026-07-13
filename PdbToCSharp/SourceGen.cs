using System.CodeDom.Compiler;
using System.Globalization;
using PdbToCSharp.Dissect;
using PdbToCSharp.ThirdParty;
using PdbToCSharp.Types;
using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.TypeRecords;

namespace PdbToCSharp;

public sealed partial class SourceGen : IDisposable {
  public SourceGen(string pdbPath, string namespaceName, string outputPath) {
    Namespace = namespaceName;
    Pdb = new PdbFileReader(pdbPath);
    _writers = new CsWriters(outputPath, namespaceName);
    CsTypes = new CsType[Pdb.PdbFile.TpiStream.TypeRecordCount];

    MemoryAddressFieldName = namespaceName.Replace(".", "") + "MemoryAddress";
  }

  /// Root namespace for all generated code.
  public readonly string Namespace;

  /// String for how generated code will reference the memory address of the module.
  public readonly string MemoryAddressFieldName;

  internal int UnnamedStructs;
  internal int UnnamedUnions;
  internal int UnnamedEnums;

  internal readonly Dictionary<TypeIndex, CsType> CsSimpleTypes = [];
  internal readonly CsType?[] CsTypes;
  internal readonly Dictionary<TypeIndex, CsUdt> CsUdts = [];

  // Debug inspect to see which CsTypes are not mapped to records
  private IEnumerable<(CsType? Cs, TypeRecord Pdb)> CsPdbTypePairs =>
    CsTypes.Select((cs, i) => (cs, Pdb.PdbFile.GetRecord(TypeIndex.FromArrayIndex(i))));

  // Debug inspect to see which CsTypes do get mapped to records
  private IEnumerable<(CsType Cs, TypeRecord Pdb)> CsNotNullPairs =>
    CsPdbTypePairs.Where(p => p.Cs is not null).Cast<(CsType Cs, TypeRecord Pdb)>();


  internal readonly PdbFileReader Pdb;
  private PdbFile PdbFile => Pdb.PdbFile;
  private readonly CsWriters _writers;

  internal TypeRecord[] Records = null!;
  private (TagRecord tag, TypeIndex index)[] _tagRecords = null!;

  public void PdbToCSharp() {
    Log.Step("Processing PDB");
    Process();
    Log.Step("Done.");

#if DEBUG
    // Inspecting constant fields, as they may be read as a different type
    var constFieldTypes = CsConstantField.types;
#endif
  }

  private void Process() {
    PreProcess();
    Log.Step("Creating inline array types");
    WriteInlineArrays();

    Log.Step("Creating all other types... ");
    int total = CsUdts.Values.Count(u => u.Parent is null);
    int i = 0;
    HashSet<TypeIndex> created = [];
    Dictionary<string, Dictionary<string, CsUdt>> addedClassesByNamespace = [];
    Dictionary<string, Dictionary<string, CsEnum>> addedEnumsByNamespace = [];

    using ProgressBar progressBar = new();
    foreach (CsUdt udt in CsUdts.Values.Where(u => u.Parent is null)) {
      progressBar.Report((double)++i / total);
      if (!created.Add(udt.TypeIndex)) {
        // Was a forward reference that has a body defined in the pdb; both entries are identical
        continue;
      }

      switch (udt) {
        case CsEnum csEnum: {
          if (CheckDuplicateName(csEnum, addedEnumsByNamespace)) {
            IndentedTextWriter writer = _writers.GetMatching(csEnum);
            WriteEnum(csEnum, writer);
          }

          break;
        }
        case CsStructure csStructure: {
          if (CheckDuplicateName(csStructure, addedClassesByNamespace)) {
            IndentedTextWriter writer = _writers.GetMatching(csStructure);
            WriteStruct(csStructure, writer);
          }

          break;
        }
      }
    }

    return;

    static bool CheckDuplicateName<T>(T udt, Dictionary<string, Dictionary<string, T>> dict) where T : CsUdt {
      string fullName = udt.Namespace is { } ns
        ? ns + '.' + udt.FullName
        : udt.FullName;
      if (!dict.TryGetValue(fullName, out var nsDict)) {
        nsDict = [];
        dict[fullName] = nsDict;
      }
      else {
        if (!nsDict.TryAdd(fullName, udt)) {
          // TODO: find a way to do proper de-duplicating
          // Log.Warn($"Duplicate class name \"{udt.FullName}\" in namespace \"{udt.Namespace}\".");
          return false;
        }
      }

      return true;
    }
  }

  private void PreProcess() {
    // Lets us get argument names for the methods, which are not available in the TPI stream.
    Log.Step("Loading procedure info");
    ProcedureHelper.Load(Pdb);
    Records = PdbFile.TpiStream.GetTypeRecords();

    Log.Step("Collecting tag records");
    _tagRecords = Records
      .Index()
      .Where(r => r.Item is TagRecord tag && AllowedName(tag.Name.String))
      .Select(r => ((TagRecord)r.Item, TypeIndex.FromArrayIndex(r.Index)))
      .ToArray();

    Log.Step("Creating non-forward reference types");
    Parallel.ForEach(_tagRecords.Where(r => !r.tag.IsForwardReference),
      iter => { CsType.GetOrCreate(this, iter.index); });

    Log.Step("Resolving forward references");
    Parallel.ForEach(_tagRecords.Index().Where(r => r.Item.tag.IsForwardReference), iter => {
      (TagRecord tag, TypeIndex i) = iter.Item;

      CsTypes[i.ArrayIndex] = ResolveForwardReference(tag, iter.Index, out TypeIndex rIndex)
        ? CsTypes[rIndex.ArrayIndex]!
        : CsType.GetOrCreate(this, i);
    });

    // Cannot use Paralle.ForEach to assign to dictionary
    foreach ((int i, CsUdt udt) in CsTypes.Index().Where(r => r.Item is CsUdt).Select(r => (r.Index, (CsUdt)r.Item!))) {
      CsUdts[udt.TypeIndex] = udt;
      if (i != udt.TypeIndex.ArrayIndex) {
        CsUdts[TypeIndex.FromArrayIndex(i)] = udt;
      }
    }

    Log.Step("Setting parents for all nested types");
    var nestedIter = CsUdts.Values
      .Where(p => p.Record.Options.HasFlag(ClassOptions.ContainsNestedClass))
      .Select(p => (parent: p, p.Record.GetFields(Pdb).OfType<NestedTypeRecord>()));

    Parallel.ForEach(nestedIter, iter => {
      foreach (NestedTypeRecord nested in iter.Item2) {
        if (CsUdts.TryGetValue(nested.Type, out CsUdt? nestedCs) && nestedCs.Record.IsNested) {
          nestedCs.SetParent(iter.parent, nested);
        }
      }
    });

#if DEBUG
    // Force loading of lazy-loaded props
    // If anything throws, we'll know before letting the program do IO.
    foreach (CsUdt csUdt in CsUdts.Values) {
      _ = csUdt.FullyQualifiedName;

      if (csUdt is CsStructure csStruct) {
        _ = csStruct.BaseClasses;
        _ = csStruct.StaticFields;
        foreach (CsInstanceField f in csStruct.InstanceFields) {
          _ = f.FieldType;
        }

        foreach (CsInstanceMethod m in csStruct.InstanceMethods) {
          _ = m.ParameterTypes;
        }
      }
    }
#endif
  }

  private bool ResolveForwardReference(TagRecord tag, int start, out TypeIndex index) {
    // Try resolving forward first
    (TagRecord? resolved, index) = _tagRecords
      .Skip(start)
      .FirstOrDefault(r => !r.tag.IsForwardReference &&
        r.tag.Name.String == tag.Name.String &&
        r.tag.UniqueName.String == tag.UniqueName.String);

    if (resolved is null) {
      // Much less common, some types may resolve backwards
      (resolved, index) = _tagRecords
        .Take(start)
        .LastOrDefault(r => !r.tag.IsForwardReference &&
          r.tag.Name.String == tag.Name.String &&
          r.tag.UniqueName.String == tag.UniqueName.String);
    }

    return resolved is not null;
  }

  private static bool AllowedName(string name) {
    return !name.Contains("unnamed struct at") &&
      !name.Contains("`lambda at") && !name.Contains("<lambda_");
  }

  private static void WriteStruct(CsStructure csStruct, IndentedTextWriter writer) {
    // Write XML doc
    writer.Write("/// Struct type: ");
    writer.WriteXmlDocText(csStruct.Record.Name.String);
    writer.WriteXmlDocLinebreak();
    writer.Write("UniqueName: ");
    writer.WriteXmlDocText(csStruct.Record.UniqueName.String);
    writer.WriteLine();

    // Write GeneratedCode attribute
    writer.WriteGeneratedCodeAttribute();

    // Write StructLayout attribute with size
    bool prependGlobal = false;
    writer.Write("[");
    writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
    writer.Write("StructLayout(");
    writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
    writer.Write("LayoutKind.Explicit");
    if (csStruct.Size > 0) {
      writer.Write(", Size = ");
      writer.Write(csStruct.Size);
    }

    writer.WriteLine(")]");

    // Write struct declaration
    writer.Write("public struct ");
    writer.Write(csStruct.SelfName);
    writer.WriteLine(" {");
    writer.Indent++;

    // Write base classes as fields
    if (csStruct.BaseClasses.Length > 0) {
      writer.WriteLine("#region Base Classes");
      for (int i = 0; i < csStruct.BaseClasses.Length; i++) {
        CsBaseClass baseClass = csStruct.BaseClasses[i];
        if (baseClass.Record.Attributes.Access == MemberAccess.Private) {
          continue;
        }

        writer.Write("/// Base class: ");
        writer.WriteXmlDocText(baseClass.BaseClass.Record.Name.String);
        writer.WriteXmlDocLinebreak();
        writer.Write("TypeIndex: ");
        writer.WriteXmlDocText(baseClass.BaseClass.TypeIndex.ToString());
        writer.WriteLine();

        // FieldOffset attribute
        prependGlobal = false;
        writer.Write("[");
        writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
        writer.Write("FieldOffset(");
        writer.Write(baseClass.Record.Offset);
        writer.Write(")] ");

        // Field declaration
        writer.Write("public ");
        writer.Write(baseClass.BaseClass.FullyQualifiedName);
        writer.Write(" Base");
        if (csStruct.BaseClasses.Length > 1) {
          writer.Write(i + 1);
        }

        writer.WriteLine(';');
      }

      writer.WriteLine("#endregion");
    }

    // Write static fields
    if (csStruct.StaticFields.Any(s => s is CsConstantField or CsRegularStaticField)) {
      writer.WriteLine("#region Static Fields");
      foreach (CsStaticField field in csStruct.StaticFields) {
        // XML doc for field
        writer.Write("/// Field: ");
        if (csStruct.PdbFile.TryGetRecord(field.FieldType.TypeIndex) is { } fieldTypeRecord) {
          writer.WriteXmlDocText(fieldTypeRecord.ToString(csStruct.PdbFile));
          writer.WriteXmlDocLinebreak();
          writer.Write("TypeIndex ");
          writer.WriteXmlDocText(field.FieldType.TypeIndex.ToString());
          writer.WriteLine();
        }
        else {
          writer.WriteXmlDocTextLine(field.FieldType.TypeIndex.ToString());
        }

        if (field is CsConstantField constant) {
          TypeIndex fType = constant.FieldType.TypeIndex;
          bool needsCast = fType.SimpleKind is not SimpleTypeKind.Float32 and not SimpleTypeKind.Boolean8;
          writer.Write("public const ");
          writer.Write(constant.FieldType.FullyQualifiedName);
          writer.Write(' ');
          writer.Write(constant.Name);
          writer.Write(" = ");
          if (needsCast) {
            writer.Write("(");
            writer.Write(constant.FieldType.FullyQualifiedName);
            writer.Write(")(");
          }

          string value = !fType.IsSimple
            ? constant.Value.ToString()!
            : fType.SimpleKind switch {
              SimpleTypeKind.Boolean8 => (ushort)constant.Value > 0 ? "true" : "false",
              SimpleTypeKind.Float32 => BitConverter.UInt32BitsToSingle((uint)constant.Value)
                .ToString(CultureInfo.CurrentCulture),
              _ => constant.Value.ToString()!
            };

          writer.Write(value);
          writer.WriteIf(")", needsCast);
          writer.WriteLine(";");
        }
        else if (field is CsRegularStaticField staticField) {
          writer.Write("public static ref ");
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
      }

      writer.WriteLine("#endregion");
    }

    // Write instance fields
    if (csStruct.InstanceFields.Length > 0) {
      writer.WriteLine("#region Instance Fields");
      foreach (CsInstanceField field in csStruct.InstanceFields) {
        // XML doc for field
        writer.Write("/// Field: ");
        if (csStruct.PdbFile.TryGetRecord(field.FieldType.TypeIndex) is { } fieldTypeRecord) {
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
        prependGlobal = false;
        writer.Write("[");
        writer.WriteIf("global::System.Runtime.InteropServices.", prependGlobal);
        writer.Write("FieldOffset(");
        writer.Write(field.Offset);
        writer.Write(")] ");

        // Field declaration
        writer.Write("public ");
        writer.Write(field.FieldType.FullyQualifiedName);
        writer.Write(' ');
        writer.Write(field.Name.KeywordToVerbatim());
        writer.WriteLine(';');
      }

      writer.WriteLine("#endregion");
    }

    // Write methods
    if (csStruct.InstanceMethods.Length > 0) {
      // TODO: Move static methods out of here (ideally out of CsStructure.InstanceMethods)
      writer.WriteLine("#region Instance Methods");
      foreach (CsInstanceMethod method in csStruct.InstanceMethods) {
        writer.Write("public ");
        writer.WriteIf("static ", method.IsStatic);

        if (method.ReturnType is CsUdt { Record.IsForwardReference: true }) {
          writer.Write("void* ");
        }
        else {
          writer.Write(method.ReturnType.FullyQualifiedName);
        }

        writer.Write(' ');
        writer.Write(method.Name.KeywordToVerbatim());
        writer.Write('(');
        var args = method.MethodRecord.ArgumentList.As<ArgumentListRecord>(method.PdbFile).Arguments;
        for (int i = 0; i < args.Length; i++) {
          if (i > 0) {
            writer.Write(", ");
          }

          CsType argType = method.ParameterTypes[i];
          string argName = method.ProcedureInfo?.GoodSize == true ? method.Args[i] : $"arg{i + 1}";
          writer.Write(argType.FullName);
          writer.Write(' ');
          writer.Write(argName.KeywordToVerbatim());
        }

        writer.WriteLine(");");
      }

      writer.WriteLine("#endregion");
    }

    // Write nested types
    if (csStruct.NestedClasses.Length > 0) {
      writer.WriteLine("#region Nested Types");
      foreach (CsStructure nested in csStruct.NestedClasses) {
        WriteStruct(nested, writer);
      }

      writer.WriteLine("#endregion");
    }

    writer.Indent--;
    writer.WriteLine("}");
  }

  private static void WriteEnum(CsEnum csEnum, IndentedTextWriter writer) {
    // Get a compatible underlying type
    string underlying = csEnum.Underlying.FullName;
    if (underlying == "bool") {
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

  public void Dispose() {
    Pdb.Dispose();
    _writers.Dispose();
  }
}
