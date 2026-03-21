using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kommit.Git;

public enum Platform { GitHub, GitLab, Unknown }

public record RemoteInfo(Platform Platform, string Host, string ProjectPath);

public partial class MergeRequestService
{
    private readonly string _apiToken;

    public MergeRequestService(string apiToken)
    {
        _apiToken = apiToken;
    }

    public static RemoteInfo ParseRemoteUrl(string url)
    {
        // SSH: git@gitlab.com:user/repo.git
        var sshMatch = SshRemoteRegex().Match(url);
        if (sshMatch.Success)
        {
            var host = sshMatch.Groups[1].Value;
            var path = sshMatch.Groups[2].Value.TrimEnd('/');
            if (path.EndsWith(".git")) path = path[..^4];
            var platform = DetectPlatform(host);
            return new RemoteInfo(platform, host, path);
        }

        // HTTPS: https://github.com/user/repo.git
        var httpsMatch = HttpsRemoteRegex().Match(url);
        if (httpsMatch.Success)
        {
            var host = httpsMatch.Groups[1].Value;
            var path = httpsMatch.Groups[2].Value.TrimEnd('/');
            if (path.EndsWith(".git")) path = path[..^4];
            var platform = DetectPlatform(host);
            return new RemoteInfo(platform, host, path);
        }

        return new RemoteInfo(Platform.Unknown, "", "");
    }

    private static Platform DetectPlatform(string host)
    {
        if (host.Contains("github", StringComparison.OrdinalIgnoreCase))
            return Platform.GitHub;
        if (host.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
            return Platform.GitLab;
        // Self-hosted — default to GitLab since that's more common for self-hosted
        return Platform.GitLab;
    }

    public async Task<string?> CreateMergeRequest(RemoteInfo remote, string sourceBranch, string targetBranch, string title)
    {
        return remote.Platform switch
        {
            Platform.GitHub => await CreateGitHubPr(remote, sourceBranch, targetBranch, title),
            Platform.GitLab => await CreateGitLabMr(remote, sourceBranch, targetBranch, title),
            _ => null
        };
    }

    private async Task<string?> CreateGitHubPr(RemoteInfo remote, string sourceBranch, string targetBranch, string title)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kommit", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var body = JsonSerializer.Serialize(new
        {
            title,
            head = sourceBranch,
            @base = targetBranch
        });

        var response = await client.PostAsync(
            $"https://api.github.com/repos/{remote.ProjectPath}/pulls",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"GitHub API error ({(int)response.StatusCode}): {error}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("html_url").GetString();
    }

    private async Task<string?> CreateGitLabMr(RemoteInfo remote, string sourceBranch, string targetBranch, string title)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _apiToken);

        var projectId = Uri.EscapeDataString(remote.ProjectPath);

        var body = JsonSerializer.Serialize(new
        {
            source_branch = sourceBranch,
            target_branch = targetBranch,
            title
        });

        var apiBase = $"https://{remote.Host}";

        var response = await client.PostAsync(
            $"{apiBase}/api/v4/projects/{projectId}/merge_requests",
            new StringContent(body, Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"GitLab API error ({(int)response.StatusCode}): {error}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("web_url").GetString();
    }

    public static string GenerateTitle(string branchName)
    {
        // feature/add-map → "Add map"
        // fix/login-bug → "Login bug"
        var slashIndex = branchName.IndexOf('/');
        var name = slashIndex >= 0 ? branchName[(slashIndex + 1)..] : branchName;

        // Replace hyphens/underscores with spaces
        name = name.Replace('-', ' ').Replace('_', ' ').Trim();

        if (name.Length == 0) return branchName;

        // Capitalize first letter
        return char.ToUpper(name[0]) + name[1..];
    }

    [GeneratedRegex(@"^git@([^:]+):(.+)$")]
    private static partial Regex SshRemoteRegex();

    [GeneratedRegex(@"^https?://([^/]+)/(.+)$")]
    private static partial Regex HttpsRemoteRegex();
}
