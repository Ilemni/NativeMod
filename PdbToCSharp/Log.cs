namespace PdbToCSharp;

public static class Log {
  public static void Step(string s) {
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[STEP] {s}");
    Console.ResetColor();
  }

  public static void Info(string s) {
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[INFO] {s}");
    Console.ResetColor();
  }

  public static void Warn(string message) {
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[WARN] {message}");
    Console.ResetColor();
  }

  public static void Error(string message) {
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] {message}");
    Console.ResetColor();
  }
}
