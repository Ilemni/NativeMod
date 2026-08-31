using SharpPdb.Native;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TypeRecords;
using Writer = System.CodeDom.Compiler.IndentedTextWriter;

namespace NativeMod.SourceGen.Lang.Cs;

public sealed partial class CsGen {
  private void WriteGlobalFields() {
    if (Reader.GlobalVariables.Length <= 0) {
      return;
    }

    Log.Info("Writing Global Fields");
    Writer writer = Writers.GlobalsWriter;

    writer.WriteLine("/// <summary>");
    writer.WriteLine("/// Struct that contains all global variables as static ref fields.");
    writer.WriteLine("/// </summary>");
    writer.WriteLine("/// <remarks>");
    writer.WriteLine("/// This type is a struct solely for debugging purposes, ");
    writer.WriteLine("/// and does not contain any instance fields or methods.");
    writer.WriteLine("/// </remarks>");
    writer.Write("public unsafe struct Globals");

    // var test1 = Pdb.GlobalsStream.Data.Where(g => g.Name.String.Contains("type_id")).Select(g => g.Name.String).ToArray();
    // var test2 = Pdb.PublicsStream.PublicSymbols
    //   .Where(g => g.Flags == PublicSymbolFlags.None)
    //   .Select(Parse)
    //   .OrderBy(g => g.Rva)
    //   .Select(g => (g.Name, Rva: g.Rva.ToString("X"), g.Sym))
    //   .GroupBy(g => g.Name == g.Sym.Name.String && g.Sym.Name.String.StartsWith('?') ? "Decorated" : "Undecorated")
    //   .ToArray();

    HashSet<string> fields = [];
    using (writer.BracedScope()) {
      writer.WriteLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
      ulong prevRva = 0;
      foreach (PdbGlobalVariable globalVar in Reader.GlobalVariables.OrderBy(g => g.RelativeVirtualAddress)) {
        if (globalVar.RelativeVirtualAddress == 0 || globalVar.RelativeVirtualAddress == prevRva) {
          continue;
        }

        if (!fields.Add(globalVar.Name)) {
          // continue;
          writer.Write("// DATA: [");
          writer.Write(globalVar.Data.Name.String);
          writer.Write("] ");
        }

        CsType csType = GetOrCreate(globalVar.Type.TypeIndex);
        string type = csType.GlobalQualifiedName;
        string name = globalVar.Name.SanitizeName(true, true).Trim();

        writer.WriteMany("public static ref ", type, " ", name, " => ");
        writer.WriteMany("ref *(", type, "*)");
        writer.WriteMany("(", MemoryAddress, " + 0x", globalVar.RelativeVirtualAddress.ToString("X"), ");");
        writer.WriteLine();
        prevRva = globalVar.RelativeVirtualAddress;
      }

      writer.WriteLine("#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member");
    }

    var allTld = Pdb.GlobalsStream.ThreadLocalData;
    if (allTld.Count == 0) {
      return;
    }

    fields.Clear();
    writer.WriteLine();
    XmlDocs.WriteSummary(writer, "Struct that contains all <c>thread_local</c> fields.");
    writer.WriteStructLayoutAttribute(0);
    writer.Write("public partial struct ThreadLocalData");
    using (writer.BracedScope()) {
      writer.WriteLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
      foreach (ThreadLocalDataSymbol tls in allTld.OrderBy(t => t.Offset)) {
        // if (!fields.Add(tls.Name.String)) {
        //   continue;
        // }

        CsType csType = GetOrCreate(tls.Type);
        string type = csType.GlobalQualifiedName;
        string name = tls.Name.String.SanitizeName(true, true);

        writer.WriteFieldOffsetAttribute(tls.Offset, hex: true);
        writer.Write(" public ");
        writer.WriteIf("unsafe ", csType is CsPointerType or CsSimplePointerType);
        writer.WriteManyLine(type, " ", name, ";");
      }

      writer.WriteLine("#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member");
      writer.WriteLine();

      WriteGetThreadLocalData(writer);
    }

    // Special case for MIO's TypeIds
    fields.Clear();
    writer.WriteLine("/// <summary> ");
    writer.Write("/// Struct that contains all ");
    XmlDocs.WriteSeeTag(writer, "global::MioGame.Type", "Types");
    writer.WriteLine("as static ref fields.");
    writer.WriteLine("/// </summary>");
    writer.WriteLine("/// <remarks>");
    writer.WriteLine("/// This type is a struct solely for debugging purposes, ");
    writer.WriteLine("/// and does not contain any instance fields or methods.");
    writer.WriteLine("/// </remarks>");
    writer.Write("public unsafe struct Types");
    using (writer.BracedScope()) {
      foreach (PdbPublicSymbol pub in Reader.PublicSymbols
                 .Where(p => p.Flags == PublicSymbolFlags.None)
                 .OrderBy(p => p.RelativeVirtualAddress)
                 .DistinctBy(p => p.RelativeVirtualAddress)) {
        string name = Parse(pub);
        const string typeId = "Type* type_id<";
        if (!name.StartsWith(typeId) || !fields.Add(name)) {
          continue;
        }

        string typeName = name[typeId.Length..^1];
        string fieldName = typeName.SanitizeName(true, true).KeywordToVerbatim();
        writer.Write("/// <c>");
        writer.WriteXmlDocText(typeName);
        writer.WriteLine("</c>");
        writer.Write("public static ref Type* ");
        writer.Write(fieldName);
        writer.Write(" => ref *(Type**)");
        writer.WriteManyLine("(", MemoryAddress, " + 0x", pub.RelativeVirtualAddress.ToString("X"), ");");
      }
    }

    writer.Flush();
    return;

    static string Parse(PdbPublicSymbol sym) {
      const NameUndecorator.Flags flags =
        NameUndecorator.Flags.NoAllocationLanguage | NameUndecorator.Flags.NoAccessSpecifiers |
        NameUndecorator.Flags.NoLeadingUnderscores |
        NameUndecorator.Flags.NoMicrosoftKeywords | NameUndecorator.Flags.NoComplexType;

      const string funcInfo = "FUNC_INFO_";
      const string ctor = "CTOR__";
      string name = sym.Name switch {
        { Length: > 0 } s when s.StartsWith(funcInfo) => s[funcInfo.Length..],
        { Length: > 0 } s when s.StartsWith(ctor) => s[ctor.Length..],
        { Length: > 0 } s => s,
        _ => throw new InvalidOperationException("Public symbol name is empty")
      };


      return CsNameUndecorator.UnDecorateSymbolName(name, flags);
    }
  }

  private void WriteGlobalFunctions() {
    var fileNames = Pdb.DbiStream.Modules
      .GetStreams()
      .Select(s => (
        // Get file name used for this module
        name: s.AsEnumerable()
          .OfType<BuildInfoSymbol>().FirstOrDefault()?
          .BuildId.As<BuildInfoRecord>(Pdb.IpiStream)
          .Indexes[2].As<StringIdRecord>(Pdb.IpiStream).String.String,
        procs: s.AsEnumerable()
          .OfType<ProcedureSymbol>().Where(AllowProcedureSymbol)
          .GroupBy(p => p.Name.String)
          .OrderBy(p => p.Key)
          .ToArray())
      )
      .Where(s => s.procs.Length > 0 && (s.name is null || !s.name.EndsWith(".inst.cpp")))
      .OrderBy(s => s.name);

    var gProcs = NestGraph.Create(fileNames);
    if (gProcs.HasAny) {
      Log.Info("Writing Global Functions");
      WriteGlobalFunctionsClass(gProcs, Writers.GlobalFunctionsWriter);
      if (WriteHooks) {
        WriteGlobalFunctionFileHooks(gProcs, Writers);
      }
    }

    bool AllowProcedureSymbol(ProcedureSymbol p) {
      if (p.FunctionType.TryAsRecord(p.Pdb) is not ProcedureRecord) return false;

      string n = p.Name.String;
      return !(
        n.StartsWith("operator") ||
        n.Contains("::operator") ||
        n.Contains("atexit destructor") ||
        n.StartsWith("dynamic initializer for")
      );
    }
  }

  private void WriteGlobalFunctionsClass(NestGraph<IGrouping<string, ProcedureSymbol>[]> graph, Writer writer) {
    writer.Write("// Folder: ");
    writer.WriteLine(graph.Namespace);
    writer.Write("namespace ");
    var rootGraph = graph as RootNestGraph<IGrouping<string, ProcedureSymbol>[]>;
    if (rootGraph is not null) {
      writer.WriteMany(Namespace, ".GlobalFunctions");
    }
    else {
      writer.Write(graph.Name!.SanitizeName(true, true).KeywordToVerbatim());
    }

    using (writer.BracedScope()) {
      foreach (var leaf in graph.Leaves) {
        string leafName = leaf.Name.SanitizeName(true, true).KeywordToVerbatim(isType: true);
        XmlDocs.GlobalFunctions.WriteFileClass(writer, leaf.FullName);
        writer.WriteMany("public static class ", leafName);
        using (writer.BracedScope()) {
          WriteGlobalFunctions(leaf.Value, writer, leafName);
        }
      }

      foreach ((string _, var value) in graph.Nested) {
        if (value.HasAny) {
          WriteGlobalFunctionsClass(value, writer);
        }
      }

      if (rootGraph is not null) {
        WriteRootFunctions();
      }
    }

    return;

    void WriteRootFunctions() {
      if (rootGraph.OtherLeaves.Count > 0) {
        using (writer.Region("Other Files")) {
          // TODO: Most names here have a C++ namespace.
          //  try to put those in a proper C# namespace,
          //  e.g. GlobalFunctions.ImGui.ImFunc()
          XmlDocs.GlobalFunctions.WriteUnknownFileClass(writer);
          writer.Write("public static class Functions");
          using (writer.BracedScope()) {
            foreach (var otherLeaf in rootGraph.OtherLeaves) {
              using (writer.Region(["File: ", otherLeaf.name!])) {
                WriteGlobalFunctions(otherLeaf.value, writer, "Functions");
              }
            }
          }
        }
      }

      if (rootGraph.UnnamedLeaves.Count > 0) {
        using (writer.Region("Unnamed Files")) {
          XmlDocs.GlobalFunctions.WriteInternalsClass(writer);
          writer.Write("public static class Internals");
          using (writer.BracedScope()) {
            foreach (var unnamedLeaf in rootGraph.UnnamedLeaves.Index()) {
              writer.Write("// File ");
              writer.WriteLine(unnamedLeaf.Index);
              WriteGlobalFunctions(unnamedLeaf.Item, writer, "Internals");
            }
          }
        }
      }
    }
  }

  private void WriteGlobalFunctions(IGrouping<string, ProcedureSymbol>[] groups, Writer writer, string outer) {
    foreach (var procSyms in groups) {
      foreach ((ProcedureSymbol procSym, CsProcedureType proc) in procSyms
                 .Select(p => (procSym: p, proc: GetOrCreate<CsProcedureType>(p.FunctionType)))
                 .DistinctBy(p => p.proc)) {
        if (proc.HasAnyVariadic) {
          writer.Write("// Omitted Variadic function: ");
          writer.Write(procSym.Name.String);
          writer.WriteLine();
          continue;
        }


        HookMethod method = HookMethod.FromGlobalMethod(procSym, 0);
        XmlDocs.GlobalFunctions.WriteFunction(writer, method);
        WriteGlobalFunction(writer, method, proc, outer);
      }
    }
  }

  private static void WriteGlobalFunction(Writer writer, HookMethod hookMethod, CsProcedureType csFunc, string outer) {
    CsType ret = csFunc.ReturnType;
    CsMarshaller? retMarshaller = ret.Marshaller;
    writer.Write("public static unsafe ");
    writer.Write(ret.GlobalQualifiedName);
    writer.Write(' ');
    writer.Write(hookMethod.Name);
    writer.WriteIf("_", hookMethod.Name == outer);
    writer.Write('(');
    bool needsComma = false;
    foreach ((CsType argType, string argName) in hookMethod.Args) {
      writer.WriteCommaIfNeeded(ref needsComma);
      writer.Write(argType.GlobalQualifiedName);
      writer.Write(' ');
      writer.Write(argName);
      needsComma = true;
    }

    writer.Write(") ");
    if (csFunc.NeedsReturnBuffer) {
      writer.WriteLine("{");
      writer.Indent++;

      writer.Write(ret.GlobalQualifiedName);
      writer.WriteLine(" retBuffer;");
    }
    else if (retMarshaller is not null) {
      writer.WriteLine("{");
      writer.Indent++;
      writer.WriteManyLine(retMarshaller.CppType, " returnValue = ");
    }
    else {
      writer.Write("=> ");
    }

    needsComma = false;
    writer.WriteMany("((", csFunc.DelegateType, ")(", FunctionAddress, " + ", hookMethod.Address, "))");
    writer.Write('(');
    writer.WriteIf("&retBuffer", csFunc.NeedsReturnBuffer, ref needsComma);
    writer.WriteParameterNamesToCpp(hookMethod.Args, ref needsComma);
    writer.WriteLine(");");

    if (csFunc.NeedsReturnBuffer) {
      writer.WriteLine("return retBuffer;");
      writer.Indent--;
      writer.WriteLine('}');
    }
    else if (retMarshaller is not null) {
      writer.Write("return ");
      retMarshaller.WriteFromCpp(writer, "returnValue");
      writer.WriteLine(';');
      writer.Indent--;
      writer.WriteLine('}');
    }
  }

  private static void WriteGetThreadLocalData(Writer writer) {
    writer.WriteLine("[LibraryImport(\"ntdll.dll\", EntryPoint = \"NtCurrentTeb\")]");
    writer.WriteLine("private static partial IntPtr NtCurrentTeb();");
    writer.WriteLine();

    writer.WriteLine("/// <summary>");
    writer.WriteLine("/// Gets a reference to the current thread's <c>thread_local</c> data.");
    writer.WriteLine("/// </summary>");
    writer.Write("public static unsafe ref ThreadLocalData GetCurrent()");
    using (writer.BracedScope()) {
      writer.WriteLine("IntPtr teb = NtCurrentTeb();");
      writer.Write("if (teb == IntPtr.Zero)");
      using (writer.BracedScope()) {
        writer.WriteLine("throw new InvalidOperationException(\"Could not retrieve NtCurrentTeb.\");");
      }

      writer.WriteLine();
      writer.WriteLine("IntPtr tlsPointer = *(IntPtr*)(teb + 0x58);");
      writer.Write("if (tlsPointer == IntPtr.Zero)");
      using (writer.BracedScope()) {
        writer.WriteLine(
          "throw new InvalidOperationException(\"ThreadLocalStoragePointer is null. TLS not initialized for this thread.\");");
      }

      writer.WriteLine();
      writer.WriteLine("uint tlsIndex = Globals._tls_index;");
      writer.WriteLine("IntPtr moduleTlsArraySlot = tlsPointer + (nint)tlsIndex * 8;");
      writer.WriteLine("IntPtr tlsBase = *(IntPtr*)moduleTlsArraySlot;");
      writer.Write("if (tlsBase == IntPtr.Zero)");
      using (writer.BracedScope()) {
        writer.WriteLine(
          "throw new InvalidOperationException(\"TLS memory block has not been allocated yet on this thread.\");");
      }

      writer.WriteLine();
      writer.WriteLine("return ref *(ThreadLocalData*)tlsBase;");
    }
  }
}
