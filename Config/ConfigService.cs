using System.Text.Json;

namespace Kommit.Config;

public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".kommitconfig"
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public KommitConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new KommitConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<KommitConfig>(json, JsonOptions) ?? new KommitConfig();
    }

    public void Save(KommitConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
