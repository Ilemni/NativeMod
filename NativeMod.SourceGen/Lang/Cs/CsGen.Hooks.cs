using System.CodeDom.Compiler;
using System.Runtime.CompilerServices;
using SharpPdb.Windows.SymbolRecords;
using CallingConvention = SharpPdb.Windows.TypeRecords.CallingConvention;

namespace NativeMod.SourceGen.Lang.Cs;

public partial class CsGen {
  public readonly struct HookMethod(
    CsProcedureType proc,
    (CsType type, string name)[] args,
    string name,
    string originalName,
    uint addr,
    int overloadId) {
    public CsMethod? Method { get; init; }
    public readonly CsProcedureType Procedure = proc;
    public readonly (CsType type, string name)[] Args = args;
    public readonly string Name = name;
    public readonly string OriginalName = originalName;
    public readonly string Id = proc.TypeIndex.ArrayIndex.ToString();
    public readonly string Address = addr.ToString();
    public readonly CsType RetType = proc.ReturnType;
    public readonly string RetTypeName = proc.ReturnType.GlobalQualifiedName;
    public readonly CsType ThisType = proc.ThisType;
    public readonly string ThisTypeName = proc.ThisType.GlobalQualifiedName;
    public readonly int OverloadId = overloadId;
    public bool HasThis => !Procedure.IsStatic;

    /// <inheritdoc cref="CsProcedureType.NeedsReturnBuffer" />
    public bool NeedsRetBuffer => Procedure.NeedsReturnBuffer;

    /// <inheritdoc cref="CsProcedureType.HasReturnType" />
    public bool HasRet => Procedure.HasReturnType;

    /// <inheritdoc cref="CsProcedureType.HasRealReturn" />
    public bool RealRet => Procedure.HasRealReturn;

    public readonly string CallConv = proc.CallingConvention switch {
      CallingConvention.NearC => nameof(CallConvCdecl),
      CallingConvention.NearStdCall => nameof(CallConvStdcall),
      CallingConvention.NearFast => nameof(CallConvFastcall),
      CallingConvention.ThisCall => nameof(CallConvThiscall),
      _ => throw new NotSupportedException($"Calling convention {proc.CallingConvention} not supported")
    };

    public static HookMethod FromClassMethod(CsMethod m) =>
      new(m.MemberFunction, m.Parameters, m.CleanName, m.CppName, m.Address, m.OverloadId) {
        Method = m
      };

    public static HookMethod FromGlobalMethod(ProcedureSymbol p, int overloadId) {
      CsGen gen = GetGen(p.Pdb);
      var args = p.GetNamedArgs();
      string originalName = p.Name.String;
      string name = originalName.SanitizeName(true, true);
      if (overloadId > 0) {
        name += "_" + overloadId;
      }

      CsProcedureType proc = gen.GetOrCreate<CsProcedureType>(p.FunctionType);
      return new HookMethod(proc, args, name, originalName, p.Offset, overloadId);
    }
  }

  private static void WriteClassHooks(CsStructure csStruct, IndentedTextWriter writer) {
    if (csStruct.DefinedMethods.Length == 0) {
      return;
    }

    XmlDocs.Hooks.WriteHookForClass(writer, csStruct);

    writer.WriteMany("public static unsafe class On_", csStruct.SelfName);
    using (writer.BracedScope()) {
      var methods = csStruct.AllMethods
        .OrderBy(m => !m.IsDefined).Distinct()
        .Where(m => m is {
          IsDefined: true,
          MemberFunction.HasAnyVariadic: false,
          MethodRecord.ThisPointerAdjustment: 0
        } && (!m.IsVirtual || ReferenceEquals(m, csStruct.VirtualMethods[m.VfSlot])))
        .Select(HookMethod.FromClassMethod).ToArray();

      WriteClassMethodHooks(writer, methods);

      if (csStruct.NestedClasses.OfType<CsStructure>()
          .Any(nc => nc.AllMethods.Any(m => m.IsDefined))) {
        writer.WriteLine();
        foreach (CsStructure nestedClass in csStruct.NestedClasses.OfType<CsStructure>()) {
          WriteClassHooks(nestedClass, writer);
        }
      }
    }

    return;

    static void WriteClassMethodHooks(IndentedTextWriter writer, HookMethod[] procs) {
      bool isFirst = true;
      foreach (HookMethod method in procs) {
        if (isFirst) {
          isFirst = false;
        }
        else {
          writer.WriteLine();
        }

        CsProcedureType proc = method.Procedure;
        if (proc.HasAnyVariadic) {
          writer.WriteMany("// Omitted variadic function: ", method.Name, " (", method.OriginalName, ")");
          continue;
        }

        WriteHookDelegateDefinitions(method, writer);
        writer.WriteLine();

        XmlDocs.Hooks.WriteHookForInstanceMethod(writer, method);
        WriteHookType(method, writer);
      }
    }
  }

  private void WriteGlobalFunctionFileHooks(NestGraph<IGrouping<string, ProcedureSymbol>[]> graph,
    CsWriters writers) {
    foreach ((string fName, _, var methods) in graph.Leaves) {
      if (methods.Length == 0) {
        continue;
      }

      string ns = graph.Namespace.SanitizeName();
      string name = fName.SanitizeName(true, true);
      using IndentedTextWriter writer = writers.CreateGlobalHookWriter(ns, name);
      XmlDocs.Hooks.WriteHookForGlobalFunctionsClass(writer, Namespace, ns, name);
      writer.Write("public static unsafe class On_");
      writer.Write(name);
      using (writer.BracedScope()) {
        WriteGlobalFunctionHooks(writer, methods, ns, name);
      }

      writer.Flush();
    }

    foreach (var value in graph.Nested.Values.Where(v => v.HasAny)) {
      WriteGlobalFunctionFileHooks(value, writers);
    }

    return;


    static void WriteGlobalFunctionHooks(IndentedTextWriter writer, IGrouping<string, ProcedureSymbol>[] procGroups,
      string ns, string name) {
      bool isFirst = true;
      foreach (var procSyms in procGroups) {
        if (isFirst) {
          isFirst = false;
        }
        else {
          writer.WriteLine();
        }

        bool isOverload = procSyms.Count() > 1;
        foreach ((int index, ProcedureSymbol procSym) in procSyms.Index()) {
          HookMethod method = HookMethod.FromGlobalMethod(procSym, isOverload ? index + 1 : 0);
          CsProcedureType proc = method.Procedure;
          if (proc.HasAnyVariadic) {
            writer.WriteMany("// Omitted variadic function: ", method.Name, " (", method.OriginalName, ")");
            continue;
          }


          WriteHookDelegateDefinitions(method, writer);
          writer.WriteLine();

          XmlDocs.Hooks.WriteHookForGlobalFunction(writer, method, ns, name);
          WriteHookType(method, writer);
        }
      }
    }
  }

  private static void WriteHookDelegateDefinitions(HookMethod m, IndentedTextWriter writer) {
    CsProcedureType proc = m.Procedure;
    (CsType, string) thisParam = (m.ThisType, "__this");
    (CsType, string) retParam = (m.RetType, "__return");
    bool needsComma;

    // only create prefix/suffix delegate if they would differ from orig delegate
    if (proc.HasReturnType) {
      // public delegate void prefix_myMethod([T* __this][, ]T arg1...]);
      XmlDocs.Hooks.WritePrefixDelegate(writer, m);

      needsComma = false;
      writer.WriteMany("public delegate void prefix_", m.Name, "(");
      writer.WriteParamIf(thisParam, m.HasThis, ref needsComma);
      writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
      writer.WriteLine(");");

      // public delegate void suffix_myMethod([T* __this, ]<ref T|T*> __return[, T arg1...]);
      XmlDocs.Hooks.WriteSuffixDelegate(writer, m);

      needsComma = false;
      writer.WriteMany("public delegate void suffix_", m.Name, "(");
      writer.WriteParamIf(thisParam, m.HasThis, ref needsComma);
      writer.WriteCommaIfNeeded(ref needsComma);
      if (m.NeedsRetBuffer) {
        writer.WriteMany(m.RetTypeName, "* __return");
      }
      else {
        writer.WriteMany("ref ", m.RetTypeName, " __return");
      }

      needsComma = true;
      writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
      writer.WriteLine(");");
    }

    string delegateRet = !m.NeedsRetBuffer ? m.RetTypeName : "void";
    // public delegate <T|void> orig_myMethod([ref T self][, ]T arg1]);
    XmlDocs.Hooks.WriteOrigDelegate(writer, m);

    needsComma = false;
    writer.WriteMany("public delegate ", delegateRet, " orig_", m.Name, "(");
    writer.WriteParamIf(thisParam, m.HasThis, ref needsComma);
    if (m.NeedsRetBuffer) {
      writer.WriteCommaIfNeeded(ref needsComma);
      writer.WriteMany(m.RetTypeName, "* __return");
      needsComma = true;
    }

    writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
    writer.WriteLine(");");

    // public delegate <T|void> hook_myMethod(orig_myMethod orig[, ref T self][, T arg1]);
    XmlDocs.Hooks.WriteHookDelegate(writer, m);

    needsComma = true; // "orig" parameter is required
    writer.WriteMany("public delegate ", delegateRet, " hook_", m.Name, "(");
    writer.WriteMany("orig_", m.Name, " orig");
    writer.WriteParamIf(thisParam, m.HasThis, ref needsComma);
    if (m.NeedsRetBuffer) {
      writer.WriteMany(", ", m.RetTypeName, "* __return");
    }

    writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
    writer.WriteLine(");");
  }

  private static void WriteHookType(HookMethod m, IndentedTextWriter writer) {
    CsProcedureType proc = m.Procedure;

    writer.WriteMany("public static unsafe class ", m.Name.KeywordToVerbatim(isType: true));
    using (writer.BracedScope()) {
      // ThreadStatic storage for return buffer
      if (m.NeedsRetBuffer) {
        string retType = m.RetTypeName;
        writer.WriteManyLine("[global::System.ThreadStatic] private static ", retType, "* __returnBuffer;");
      }

      #region Event Properties

      XmlDocs.Hooks.WritePrefixEvent(writer);
      writer.Write("public static event ");
      WritePrefixName(m, writer);
      writer.WriteManyLine(" Prefix { ",
        "add { HookInstance.Activate(); _prefix += value; } ",
        "remove => _prefix -= value; ",
        "}");

      XmlDocs.Hooks.WriteSuffixEvent(writer);
      writer.Write("public static event ");
      WriteSuffixName(m, writer);
      writer.WriteManyLine(" Suffix { ",
        "add { HookInstance.Activate(); _suffix += value; } ",
        "remove => _suffix -= value; ",
        "}");

      XmlDocs.Hooks.WriteHookEvent(writer);
      writer.Write("public static event ");
      WriteDelegateName(m, writer, "hook");
      writer.WriteLine(" Hook { add => HookInstance.Register(value); remove => HookInstance.Unregister(value); }");
      writer.WriteLine();

      writer.Write("private static ");
      WritePrefixName(m, writer);
      writer.WriteLine("? _prefix;");

      writer.Write("private static ");
      WriteSuffixName(m, writer);
      writer.WriteLine("? _suffix;");
      writer.WriteLine();

      #endregion

      writer.Write("private static readonly HookSet<");
      WriteDelegateName(m, writer, "orig");
      writer.Write(", ");
      WriteDelegateName(m, writer, "hook");
      writer.Write("> HookInstance = new((ulong)");
      writer.WriteMany("(", FunctionAddress, " + ", m.Address, "), ");
      writer.WriteMany("(ulong)(", proc.DelegateType, ")&Invoke", m.Id, ")");
      writer.WriteLine(" {");
      writer.Indent++;
      WriteCreateFirstHook(m, writer);
      WriteCreatePrevInvoker(m, writer);
      writer.Indent--;
      writer.WriteLine("};");

      writer.WriteLine();

      WriteInvokeFunction(m, writer);
    }

    return;

    // Writes the name for HookInstance
    static void WriteHookFieldName(HookMethod m, IndentedTextWriter writer) {
      CsType? classType = m.Procedure.ClassType;
      writer.WriteMany(classType is not null
        ? ["On_", classType.SelfName, ".", m.Name, ".HookInstance"]
        : [m.Name, ".HookInstance"]
      );
    }

    static void WritePrefixName(HookMethod m, IndentedTextWriter writer) =>
      WriteDelegateName(m, writer, m.HasRet ? "prefix" : "orig");

    static void WriteSuffixName(HookMethod m, IndentedTextWriter writer) =>
      WriteDelegateName(m, writer, m.HasRet ? "suffix" : "orig");

    static void WriteDelegateName(HookMethod m, IndentedTextWriter writer, string delegateType) {
      CsType? classType = m.Procedure.ClassType;
      writer.WriteMany(classType is not null
        ? ["On_", classType.SelfName, ".", delegateType, "_", m.Name]
        : [delegateType, "_", m.Name]
      );
    }

    static void WriteCreateFirstHook(HookMethod m, IndentedTextWriter writer) {
      CsProcedureType proc = m.Procedure;
      (CsType, string) self = (proc.ThisType, "self");

      bool needsComma = true;
      writer.Write("CreateFirstHook = static trampoline => (");
      WriteDelegateName(m, writer, "orig");
      writer.Write(" _");
      writer.WriteParamIf(self, !proc.IsStatic, ref needsComma);
      if (m.NeedsRetBuffer) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.WriteMany(m.RetTypeName, "* __return");
        needsComma = true;
      }

      writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
      writer.Write(") => ");
      needsComma = false;

      if (proc.NeedsReturnBuffer) {
        writer.WriteLine("{");
        writer.Indent++;
      }

      writer.WriteMany("((", proc.DelegateType, ")trampoline)(");
      if (proc.NeedsReturnBuffer) {
        writer.WriteMany("__return");
        needsComma = true;
      }

      writer.WriteIf("self", m.HasThis, ref needsComma);
      writer.WriteParameterNamesToCpp(m.Args, ref needsComma);
      writer.Write(")");
      writer.WriteLine(proc.NeedsReturnBuffer ? ';' : ',');

      if (proc.NeedsReturnBuffer) {
        writer.Indent--;
        writer.WriteLine("},");
      }
    }

    static void WriteCreatePrevInvoker(HookMethod m, IndentedTextWriter writer) {
      CsProcedureType proc = m.Procedure;
      bool needsComma = false;

      writer.Write("CreatePrevInvoker = static prevHookSet => (");
      writer.WriteParamIf((proc.ThisType, "self"), !proc.IsStatic, ref needsComma);
      if (m.NeedsRetBuffer) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.WriteMany(m.RetTypeName, "* __return");
        needsComma = true;
      }

      writer.WriteParameterTypesAndNames(m.Args, ref needsComma);
      writer.Write(") => prevHookSet.prevHook(prevHookSet.orig");
      needsComma = true;
      writer.WriteIf("self", !proc.IsStatic, ref needsComma);
      writer.WriteIf("__return", m.NeedsRetBuffer, ref needsComma);
      writer.WriteParameterNames(m.Args, ref needsComma);
      writer.WriteLine(")");
    }

    static void WriteInvokeFunction(HookMethod m, IndentedTextWriter writer) {
      #region Invoke() Method Signature and Attribute

      string retType = m.RetTypeName;
      string realRetType = m.RealRet ? retType : "void";

      writer.WriteManyLine("[UnmanagedCallersOnly(CallConvs = [typeof(", m.CallConv, ")])]");

      bool needsComma = false;
      writer.WriteMany("private static ", realRetType, " Invoke", m.Id, "(");
      if (m.NeedsRetBuffer) {
        writer.WriteCommaIfNeeded(ref needsComma);
        writer.WriteMany(retType, "* __return");
        needsComma = true;
      }

      writer.WriteParamIf((m.ThisType, "__this"), m.HasThis, ref needsComma);
      writer.WriteParameterTypesAndNamesFromCpp(m.Args, ref needsComma);
      writer.Write(")");

      #endregion

      using (writer.BracedScope()) {
        #region Invoke() Method Body

        needsComma = false;

        if (m.NeedsRetBuffer) {
          writer.WriteLine("var __oldReturnBuffer = __returnBuffer;");
          writer.WriteLine("__returnBuffer = __return;");
        }

        #region Invoke() - Prefix

        writer.Write("_prefix?.Invoke(");
        writer.WriteIf("__this", m.HasThis, ref needsComma);
        writer.WriteParameterNamesFromCpp(m.Args, ref needsComma);
        writer.WriteLine(");");

        #endregion

        writer.Write("var __lastHook = ");
        WriteHookFieldName(m, writer);
        writer.WriteLine(".LastHook;");

        writer.WriteManyIf([m.RetTypeName, " __return = "], m.RealRet);
        writer.Write("__lastHook.hook.Invoke(__lastHook.invokeNext");
        needsComma = true;
        writer.WriteIf("__this", m.HasThis, ref needsComma);
        writer.WriteIf("__return", m.NeedsRetBuffer, ref needsComma);
        writer.WriteParameterNamesFromCpp(m.Args, ref needsComma);
        writer.WriteLine(");");
        needsComma = false;

        #region Invoke() - Suffix

        writer.Write("_suffix?.Invoke(");
        writer.WriteIf("__this", m.HasThis, ref needsComma);
        if (m.HasRet) {
          writer.WriteCommaIfNeeded(ref needsComma);
          writer.WriteIf("ref ", m.RealRet);
          writer.WriteMany("__return");
          needsComma = true;
        }

        writer.WriteParameterNamesFromCpp(m.Args, ref needsComma);
        writer.WriteLine(");");

        #endregion

        writer.WriteLineIf("__returnBuffer = __oldReturnBuffer;", m.NeedsRetBuffer);
        writer.WriteLineIf("return __return;", m.RealRet);

        #endregion
      }
    }
  }

  private void WriteNativeModHookClasses() {
    const string hookManager =
      """
      using System.Runtime.InteropServices;
      using PolyHook2.API;

      namespace NativeMod;

      /// <summary>
      /// Manages all hooks applied to native functions, allowing multiple hooks to be registered and unregistered.
      /// </summary>
      public static class HookManager {
        internal static readonly Dictionary<(ulong, ulong), X64Detour> Hooks = [];

        /// <summary>
        /// Adds a hook to the specified original function, using the specified hook delegate.
        /// </summary>
        /// <param name="orig">The address of the original C++ function.</param>
        /// <param name="hookDelegate">The address of the hook delegate.
        /// This should be a C# function annotated with <see cref="global::System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/> 
        /// and the appropriate calling convention.
        /// </param>
        /// <returns>The created X64Detour instance.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown if the specified hook has already been applied to this method.
        /// That is, both <paramref name="orig"/> and <paramref name="hookDelegate"/> have already been added but not removed.
        /// </exception>
        public static X64Detour Add(ulong orig, ulong hookDelegate) {
          (ulong, ulong) key = (orig, hookDelegate);
          ref X64Detour? hook = ref CollectionsMarshal.GetValueRefOrAddDefault(Hooks, key, out bool hookExists);
          if (hookExists) {
            throw new ArgumentException("Specified hook has already been applied to this method!");
          }

          hook = new X64Detour(orig, hookDelegate);
          return hook;
        }

        /// <summary>
        /// Removes a hook from the specified original function, using the specified hook delegate.
        /// </summary>
        /// <param name="orig">The address of the original C++ function.</param>
        /// <param name="hookDelegate">The address of the hook delegate.</param>
        public static void Remove(ulong orig, ulong hookDelegate) {
          if (Hooks.Remove((orig, hookDelegate), out X64Detour? hook)) {
            if (hook.IsHooked()) {
              hook.UnHook();
            }
            hook.Dispose();
          }
        }
      }
      """;

    const string hookSet =
      """
      using PolyHook2.API;

      namespace NativeMod;

      /// <summary>
      /// A set of hooks for a single native function, allowing multiple hooks to be registered and unregistered.
      /// </summary>
      /// <typeparam name="TOrig">
      /// The type of the original native function delegate. Invoking this will either
      /// call the next hook in the chain, or the original function if at the end of the chain.
      /// </typeparam>
      /// <typeparam name="THook">
      /// The hook delegate which mods will register.
      /// </typeparam>
      public sealed class HookSet<TOrig, THook>(ulong orig, ulong toInvoke) : IDisposable
        where TOrig : Delegate
        where THook : Delegate {
        private readonly List<(THook hook, TOrig invokeNext)> _hooks = [];
        private readonly X64Detour _detour = HookManager.Add(orig, toInvoke);

        /// <summary>
        /// Creates a delegate that invoke detour.TrampolineAddress.
        /// The Orig parameter will be null, and must be discarded.
        /// </summary>
        public required Func<ulong, THook> CreateFirstHook { get; init; }

        /// <summary>
        /// Creates a delegate where <c>prev.prevHook</c> invokes <c>prev.orig</c>.
        /// </summary>
        /// <returns>The created delegate.</returns>
        public required Func<(THook prevHook, TOrig orig), TOrig> CreatePrevInvoker { get; init; }

        internal (THook hook, TOrig invokeNext) LastHook => _hooks[^1];

        internal void Activate() {
          if (!_detour.IsHooked()) {
            _detour.Hook();
          }

          if (_hooks.Count == 0) {
            _hooks.Add((CreateFirstHook(_detour.TrampolineAddress), null!));
          }
        }

        internal void Register(THook hook) {
          Activate();
          (THook hook, TOrig _) prev = LastHook;
          _hooks.Add((hook, CreatePrevInvoker(prev)));
        }

        internal void Unregister(THook hook) {
          for (int i = _hooks.Count - 1; i > 0; i--) {
            if (_hooks[i].hook.Equals(hook)) {
              (THook hook, TOrig invokeNext) removed = _hooks[i];
              _hooks.RemoveAt(i);
              if (i < _hooks.Count - 1) {
                // Replace the next hook's invoker, since it now points to a removed hook.
                _hooks[i] = _hooks[i] with { invokeNext = removed.invokeNext };
                return;
              }
            }
          }
        }

        /// <summary>
        /// Disposes the hook set, releasing any resources.
        /// </summary>
        public void Dispose() {
          _detour.Dispose();
        }
      }

      """;

    Directory.CreateDirectory(NativeModPath);
    File.WriteAllText(Path.Combine(NativeModPath, "HookManager.cs"), hookManager);
    File.WriteAllText(Path.Combine(NativeModPath, "HookSet.cs"), hookSet);
  }
}
