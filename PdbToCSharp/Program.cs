using System.Globalization;

namespace PdbToCSharp;

internal static class Program {
  public static void Main(string[] args) {
    // Dissect.PdbDissect.DissectPdb();
    // return;

    string? pdbPath = null;
    string? namespaceName = null;
    string? outputPath = null;

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
      }
    }

    if (pdbPath is null) {
      // Find the one .pdb file in the current directory
      // Disallow multiple .pdb files, as this is ambiguous
      string[] pdbFiles = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.pdb");
      int numPdbs = pdbFiles.Length;
      if (pdbFiles.Any(s => s.EndsWith("PdbToCSharp.pdb"))) {
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
      namespaceName = Path.GetFileNameWithoutExtension(pdbPath);
      TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
      namespaceName = textInfo.ToTitleCase(namespaceName)
        .Replace(" ", "")
        .Replace("-", "_");
    }

    outputPath ??= Path.Combine(Directory.GetCurrentDirectory(), "output", namespaceName);

    using SourceGen sourceGen = new(pdbPath, namespaceName, outputPath);
    sourceGen.PdbToCSharp();
  }

  private static void PrintHelp() {
    Console.WriteLine("Usage: PdbToCSharp [-pdb <path>] [-namespace <name>] [-output <path>]");
    Console.WriteLine("  -pdb <path>       Path to the .pdb file to process.");
    Console.WriteLine("                      If not specified, the program will look for exactly one .pdb file in the current directory.");
    Console.WriteLine("  -namespace <name> Namespace name to use for the generated C# code.");
    Console.WriteLine("                      If not specified, the program will use the .pdb file name,");
    Console.WriteLine("                      formatted to PascalCase and invalid characters replaced with underscores.");
    Console.WriteLine("  -output <path>    Output directory for the generated C# code.");
    Console.WriteLine("                      If not specified, the program will create an 'output' directory in the current directory.");
  }
}
