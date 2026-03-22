using System.Text.Json.Serialization;

namespace Kommit.Config;

public class KommitConfig
{
    [JsonPropertyName("autoGenerate")]
    public bool AutoGenerate { get; set; } = true;

    [JsonPropertyName("autoPush")]
    public bool AutoPush { get; set; } = false;

    [JsonPropertyName("autoPull")]
    public bool AutoPull { get; set; } = false;

    [JsonPropertyName("autoAdd")]
    public bool AutoAdd { get; set; } = false;

    [JsonPropertyName("pullStrategy")]
    public string PullStrategy { get; set; } = "rebase";

    [JsonPropertyName("pushStrategy")]
    public string PushStrategy { get; set; } = "simple";

    [JsonPropertyName("defaultScope")]
    public string? DefaultScope { get; set; } = null;

    [JsonPropertyName("maxCommitLength")]
    public int MaxCommitLength { get; set; } = 72;

    [JsonPropertyName("maxStagedFiles")]
    public int? MaxStagedFiles { get; set; } = null;

    [JsonPropertyName("maxStagedLines")]
    public int? MaxStagedLines { get; set; } = null;

    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; set; } = null;
}
