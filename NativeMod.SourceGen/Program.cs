using System.Diagnostics;
using System.Globalization;
using JetBrains.Annotations;
using NativeMod.SourceGen.Lang;
using SharpPdb.Native;

namespace NativeMod.SourceGen;

internal static class Program {
  public static void Main(string[] args) {
    // Dissect.PdbDissect.DissectPdb();
    // return;

    string? pdbPath = null;
    string? namespaceName = null;
    string? outputPath = null;
    string? lang = null;

    if (args is ["help", ..]) {
      PrintHelp();
      return;
    }

    for (int i = 0; i < args.Length - 1; i++) {
      string arg = args[i];
      switch (arg) {
        case "-pdb":
          pdbPath = args[++i];
          break;
        case "-namespace":
          namespaceName = args[++i];
          break;
        case "-output":
          outputPath = args[++i];
          break;
        case "-lang":
          lang = args[++i];
          break;
      }
    }

    if (pdbPath is null) {
      // Find the one .pdb file in the current directory
      // Disallow multiple .pdb files, as this is ambiguous
      string[] pdbFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.pdb");
      int numPdbs = pdbFiles.Length;
      if (pdbFiles.Any(s => s.EndsWith("NativeMod.SourceGen.pdb"))) {
        --numPdbs;
      }

      switch (numPdbs) {
        case 0:
          Console.WriteLine(
            "No .pdb file found in the current directory. Please specify a .pdb file using the -pdb argument.");
          return;
        case > 1:
          Console.WriteLine(
            "Multiple .pdb files found in the current directory. Please specify a .pdb file using the -pdb argument.");
          return;
        default:
          pdbPath = pdbFiles[0];
          break;
      }
    }

    if (namespaceName is null) {
      string pdbName = Path.GetFileNameWithoutExtension(pdbPath);
      namespaceName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(pdbName)
        .Replace(" ", "")
        .Replace("-", "_");
      if (!namespaceName.EndsWith("Game", StringComparison.OrdinalIgnoreCase)) {
        namespaceName += "Game";
      }
    }

    outputPath = @"C:\Users\Twili\source\repos\MioBinds\MioBinds";
    outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), "output");
    string bindsPath = Path.Combine(outputPath, namespaceName);
    string nativeModPath = Path.Combine(outputPath, "NativeMod");
    EmptyDirectory(bindsPath);
    EmptyDirectory(nativeModPath);
    lang ??= "cs";

    namespaceName = namespaceName.KeywordToVerbatim();
    PdbFileReader reader = new(pdbPath);

    Stopwatch sw = Stopwatch.StartNew();
    LangGen gen = CreateLangGen(lang, reader, namespaceName, bindsPath, nativeModPath);
    Log.Step("Pre-processing PDB");
    gen.PreProcess();
    Log.Step($"Pre-processing complete in {sw.Elapsed.TotalSeconds:F2} seconds. Found {gen.ProcCache.Count} procedures and {gen.Types?.Length ?? 0} types.");
    double dt = sw.Elapsed.TotalSeconds;

    Log.Step("Writing all files");
    gen.WriteAll();
    Log.Step($"Writing complete in {sw.Elapsed.TotalSeconds - dt:F2} seconds.");

    Log.Step("Cleaning up.");
    gen.Dispose();

    Log.Step($"Done. Elapsed time: {sw.Elapsed.TotalSeconds:F2} seconds.");
    return;

    static void EmptyDirectory(string path) {
      if (Directory.Exists(path)) {
        Directory.Delete(path, true);
      }

      Directory.CreateDirectory(path);
    }
  }

  [MustDisposeResource]
  private static LangGen CreateLangGen(string lang, PdbFileReader reader, string ns, string bindsPath,
    string nativeModPath) {
    Func<PdbFileReader, string, string, string, LangGen> genFunc = lang.ToLowerInvariant() switch {
      "cs" or "csharp" => Lang.Cs.CsGen.CreateGen,
      _ => throw new NotSupportedException($"Language '{lang}' is not supported.")
    };

    LangGen result = genFunc(reader, ns, bindsPath, nativeModPath);
    Log.Info($"Using Source Generator type: {result.GetType().Name}");
    return result;
  }

  private static void PrintHelp() {
    Console.WriteLine(
      """
      Usage: NativeMod.SourceGen [-pdb <path>] [-namespace <name>] [-output <path>] [-lang <language>]
      -pdb <path>       Path to the .pdb file to process.
                          If not specified, the program will look for
                          exactly one .pdb file in the current directory.
      -namespace <name> Namespace name to use for the generated code.
                          If not specified, the program will use
                          the .pdb file name formatted to PascalCase, with
                          invalid characters replaced with underscores,
                          and appended with \"Game\" if not already present.
                          For example, \"foobar.pdb\" becomes FoobarGame
      -output <path>    Output directory for the generated code.
                          If not specified, the program will create an 
                          'output' directory in the current directory.
      -lang <path>      Language of the generated code.
                          If not specified, the language will be C#.
                          Currently, only C# code generation is supported
      """
    );
  }
}
