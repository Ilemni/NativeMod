namespace NativeMod.SourceGen;

public static class Log {
  public static void Step(string message) {
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[STEP] {message}");
    Console.ResetColor();
  }

  public static void Info(string message) {
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"[INFO] {message}");
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
