using System.Text;

namespace Kommit.UI;

public static class PromptEditor
{
    public static string? Edit(string label, string prefilled)
    {
        if (Console.IsInputRedirected)
            return prefilled;

        Console.Write(label);
        Console.Write(prefilled);

        var buffer = new StringBuilder(prefilled);
        var cursor = buffer.Length;
        var labelLen = label.Length;

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    var result = buffer.ToString().Trim();
                    return result.Length == 0 ? null : result;

                case ConsoleKey.Escape:
                    Console.WriteLine();
                    return null;

                case ConsoleKey.Backspace:
                    if (cursor > 0)
                    {
                        buffer.Remove(cursor - 1, 1);
                        cursor--;
                        Redraw(label, buffer, cursor);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursor < buffer.Length)
                    {
                        buffer.Remove(cursor, 1);
                        Redraw(label, buffer, cursor);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursor > 0)
                    {
                        cursor--;
                        Console.SetCursorPosition(labelLen + cursor, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length)
                    {
                        cursor++;
                        Console.SetCursorPosition(labelLen + cursor, Console.CursorTop);
                    }
                    break;

                case ConsoleKey.Home:
                    cursor = 0;
                    Console.SetCursorPosition(labelLen, Console.CursorTop);
                    break;

                case ConsoleKey.End:
                    cursor = buffer.Length;
                    Console.SetCursorPosition(labelLen + cursor, Console.CursorTop);
                    break;

                default:
                    if (key.KeyChar >= 32) // printable character
                    {
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                        Redraw(label, buffer, cursor);
                    }
                    break;
            }
        }
    }

    private static void Redraw(string label, StringBuilder buffer, int cursor)
    {
        var top = Console.CursorTop;
        Console.SetCursorPosition(0, top);
        Console.Write(label);
        Console.Write(buffer);
        // Clear any leftover characters from previous longer text
        Console.Write("  ");
        Console.SetCursorPosition(label.Length + cursor, top);
    }
}
