using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kommit.Config;

[JsonSerializable(typeof(KommitConfig))]
internal partial class KommitConfigContext : JsonSerializerContext { }

public class ConfigService
{
    private static readonly string KommitDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".kommit"
    );

    private static readonly string ConfigPath = Path.Combine(KommitDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = KommitConfigContext.Default
    };

    private static readonly string LegacyConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".kommitconfig"
    );

    public KommitConfig Load()
    {
        if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
        {
            Directory.CreateDirectory(KommitDir);
            File.Move(LegacyConfigPath, ConfigPath);
        }

        if (!File.Exists(ConfigPath))
            return new KommitConfig();

        var json = File.ReadAllText(ConfigPath);
        return JsonSerializer.Deserialize<KommitConfig>(json, JsonOptions) ?? new KommitConfig();
    }

    public void Save(KommitConfig config)
    {
        Directory.CreateDirectory(KommitDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
