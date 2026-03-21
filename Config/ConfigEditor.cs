namespace Kommit.Config;

public class ConfigEditor
{
    private readonly ConfigService _configService;
    private KommitConfig _config;
    private int _selectedIndex;
    private bool _editing;
    private string _editBuffer = "";

    private readonly record struct ConfigItem(string Name, string Description, ConfigItemType Type);

    private enum ConfigItemType { Bool, String, Int, NullableInt }

    private static readonly ConfigItem[] Items =
    {
        new("autoPush", "Auto-push after commit", ConfigItemType.Bool),
        new("autoPull", "Auto-pull before commit", ConfigItemType.Bool),
        new("pullStrategy", "Pull strategy (rebase/merge)", ConfigItemType.String),
        new("pushStrategy", "Push strategy (simple/set-upstream/force-with-lease)", ConfigItemType.String),
        new("defaultScope", "Default commit scope", ConfigItemType.String),
        new("maxCommitLength", "Max commit message length", ConfigItemType.Int),
        new("maxStagedFiles", "File count threshold for split", ConfigItemType.NullableInt),
        new("maxStagedLines", "Line count threshold for split", ConfigItemType.NullableInt),
    };

    public ConfigEditor(ConfigService configService)
    {
        _configService = configService;
        _config = configService.Load();
    }

    public void Run()
    {
        Console.CursorVisible = false;
        Console.Clear();

        try
        {
            Render();

            while (true)
            {
                var key = Console.ReadKey(true);

                if (_editing)
                {
                    HandleEditKey(key);
                }
                else
                {
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                        break;

                    switch (key.Key)
                    {
                        case ConsoleKey.UpArrow or ConsoleKey.K:
                            _selectedIndex = (_selectedIndex - 1 + Items.Length) % Items.Length;
                            break;
                        case ConsoleKey.DownArrow or ConsoleKey.J:
                            _selectedIndex = (_selectedIndex + 1) % Items.Length;
                            break;
                        case ConsoleKey.Enter or ConsoleKey.Spacebar:
                            HandleSelect();
                            break;
                    }
                }

                Render();
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Clear();
        }

        _configService.Save(_config);
        Console.WriteLine("Config saved.");
    }

    private void Render()
    {
        Console.SetCursorPosition(0, 0);
        Console.WriteLine("kommit config");
        Console.WriteLine("Use \u2191\u2193 to navigate, Enter/Space to toggle or edit, Q to save & quit\n");

        for (int i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var isSelected = i == _selectedIndex;
            var prefix = isSelected ? "> " : "  ";
            var value = GetDisplayValue(item);

            if (isSelected && _editing)
            {
                Console.Write($"{prefix}{item.Description,-50} ");
                Console.WriteLine($"[{_editBuffer}_]          ");
            }
            else
            {
                Console.WriteLine($"{prefix}{item.Description,-50} {value,-20}");
            }
        }

        if (_editing)
            Console.WriteLine("\nType a value and press Enter to confirm, Esc to cancel.");
        else
            Console.WriteLine("                                                                        ");
    }

    private string GetDisplayValue(ConfigItem item)
    {
        return item.Name switch
        {
            "autoPush" => _config.AutoPush ? "[x]" : "[ ]",
            "autoPull" => _config.AutoPull ? "[x]" : "[ ]",
            "pullStrategy" => _config.PullStrategy,
            "pushStrategy" => _config.PushStrategy,
            "defaultScope" => _config.DefaultScope ?? "(none)",
            "maxCommitLength" => _config.MaxCommitLength.ToString(),
            "maxStagedFiles" => _config.MaxStagedFiles?.ToString() ?? "(none)",
            "maxStagedLines" => _config.MaxStagedLines?.ToString() ?? "(none)",
            _ => ""
        };
    }

    private void HandleSelect()
    {
        var item = Items[_selectedIndex];

        if (item.Type == ConfigItemType.Bool)
        {
            ToggleBool(item.Name);
        }
        else
        {
            _editing = true;
            _editBuffer = "";
        }
    }

    private void HandleEditKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _editing = false;
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            ApplyEdit(Items[_selectedIndex], _editBuffer.Trim());
            _editing = false;
            return;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_editBuffer.Length > 0)
                _editBuffer = _editBuffer[..^1];
            return;
        }

        if (key.KeyChar >= 32 && key.KeyChar < 127)
            _editBuffer += key.KeyChar;
    }

    private void ToggleBool(string name)
    {
        switch (name)
        {
            case "autoPush": _config.AutoPush = !_config.AutoPush; break;
            case "autoPull": _config.AutoPull = !_config.AutoPull; break;
        }
    }

    private void ApplyEdit(ConfigItem item, string value)
    {
        switch (item.Name)
        {
            case "pullStrategy":
                if (value is "rebase" or "merge")
                    _config.PullStrategy = value;
                break;
            case "pushStrategy":
                if (value is "simple" or "set-upstream" or "force-with-lease")
                    _config.PushStrategy = value;
                break;
            case "defaultScope":
                _config.DefaultScope = string.IsNullOrEmpty(value) ? null : value;
                break;
            case "maxCommitLength":
                if (int.TryParse(value, out var len) && len > 0)
                    _config.MaxCommitLength = len;
                break;
            case "maxStagedFiles":
                if (string.IsNullOrEmpty(value)) _config.MaxStagedFiles = null;
                else if (int.TryParse(value, out var mf) && mf > 0) _config.MaxStagedFiles = mf;
                break;
            case "maxStagedLines":
                if (string.IsNullOrEmpty(value)) _config.MaxStagedLines = null;
                else if (int.TryParse(value, out var ml) && ml > 0) _config.MaxStagedLines = ml;
                break;
        }
    }
}
