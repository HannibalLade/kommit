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

    [JsonPropertyName("githubToken")]
    public string? GithubToken { get; set; } = null;

    [JsonPropertyName("gitlabToken")]
    public string? GitlabToken { get; set; } = null;

    // Legacy — used as fallback if platform-specific token is not set
    [JsonPropertyName("apiToken")]
    public string? ApiToken { get; set; } = null;

    public string? GetTokenForPlatform(Git.Platform platform) => platform switch
    {
        Git.Platform.GitHub => GithubToken,
        Git.Platform.GitLab => GitlabToken ?? ApiToken,
        _ => null
    };

    public void SetTokenForPlatform(Git.Platform platform, string token)
    {
        switch (platform)
        {
            case Git.Platform.GitHub:
                GithubToken = token;
                break;
            case Git.Platform.GitLab:
                GitlabToken = token;
                break;
        }
    }
}
