namespace Kommit.UI;

public static class Out
{
    public static void Success(string message) => Write(message, ConsoleColor.Green);
    public static void Error(string message) => WriteErr(message, ConsoleColor.Red);
    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);
    public static void Info(string message) => Write(message, ConsoleColor.Cyan);
    public static void Muted(string message) => Write(message, ConsoleColor.DarkGray);

    private static void Write(string message, ConsoleColor color)
    {
        if (!Console.IsOutputRedirected)
            Console.ForegroundColor = color;
        Console.WriteLine(message);
        if (!Console.IsOutputRedirected)
            Console.ResetColor();
    }

    private static void WriteErr(string message, ConsoleColor color)
    {
        if (!Console.IsErrorRedirected)
            Console.ForegroundColor = color;
        Console.Error.WriteLine(message);
        if (!Console.IsErrorRedirected)
            Console.ResetColor();
    }
}
