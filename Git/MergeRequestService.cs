using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Kommit.Git;

public enum Platform { GitHub, GitLab, Unknown }

public record RemoteInfo(Platform Platform, string Host, string ProjectPath);

public record ProjectMember(int Id, string Username, string Name);

public partial class MergeRequestService
{
    private readonly string _apiToken;

    public MergeRequestService(string apiToken)
    {
        _apiToken = apiToken;
    }

    public async Task<string?> GetCurrentUsername(RemoteInfo remote)
    {
        return remote.Platform switch
        {
            Platform.GitHub => await GetGitHubUsername(),
            Platform.GitLab => await GetGitLabUsername(remote.Host),
            _ => null
        };
    }

    public async Task<List<ProjectMember>> GetProjectMembers(RemoteInfo remote)
    {
        return remote.Platform switch
        {
            Platform.GitHub => await GetGitHubCollaborators(remote),
            Platform.GitLab => await GetGitLabMembers(remote),
            _ => []
        };
    }

    private async Task<string?> GetGitHubUsername()
    {
        using var client = CreateGitHubClient();
        try
        {
            var response = await client.GetAsync("https://api.github.com/user");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("login").GetString();
        }
        catch { return null; }
    }

    private async Task<string?> GetGitLabUsername(string host)
    {
        using var client = CreateGitLabClient();
        try
        {
            var response = await client.GetAsync($"https://{host}/api/v4/user");
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("username").GetString();
        }
        catch { return null; }
    }

    private async Task<List<ProjectMember>> GetGitHubCollaborators(RemoteInfo remote)
    {
        using var client = CreateGitHubClient();
        var members = new List<ProjectMember>();
        try
        {
            var response = await client.GetAsync(
                $"https://api.github.com/repos/{remote.ProjectPath}/collaborators");
            if (!response.IsSuccessStatusCode) return members;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var user in doc.RootElement.EnumerateArray())
            {
                var id = user.GetProperty("id").GetInt32();
                var login = user.GetProperty("login").GetString() ?? "";
                members.Add(new ProjectMember(id, login, login));
            }
        }
        catch { }
        return members;
    }

    private async Task<List<ProjectMember>> GetGitLabMembers(RemoteInfo remote)
    {
        using var client = CreateGitLabClient();
        var members = new List<ProjectMember>();
        var projectId = Uri.EscapeDataString(remote.ProjectPath);
        try
        {
            var response = await client.GetAsync(
                $"https://{remote.Host}/api/v4/projects/{projectId}/members/all?per_page=100");
            if (!response.IsSuccessStatusCode) return members;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var user in doc.RootElement.EnumerateArray())
            {
                var id = user.GetProperty("id").GetInt32();
                var username = user.GetProperty("username").GetString() ?? "";
                var name = user.GetProperty("name").GetString() ?? username;
                members.Add(new ProjectMember(id, username, name));
            }
        }
        catch { }
        return members;
    }

    private HttpClient CreateGitHubClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kommit", "1.0"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private HttpClient CreateGitLabClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", _apiToken);
        return client;
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

    public async Task<string?> CreateMergeRequest(RemoteInfo remote, string sourceBranch, string targetBranch, string title,
        List<string>? reviewerUsernames = null, string? assigneeUsername = null)
    {
        return remote.Platform switch
        {
            Platform.GitHub => await CreateGitHubPr(remote, sourceBranch, targetBranch, title, reviewerUsernames),
            Platform.GitLab => await CreateGitLabMr(remote, sourceBranch, targetBranch, title, reviewerUsernames, assigneeUsername),
            _ => null
        };
    }

    private async Task<string?> CreateGitHubPr(RemoteInfo remote, string sourceBranch, string targetBranch, string title,
        List<string>? reviewerUsernames)
    {
        using var client = CreateGitHubClient();

        var bodyObj = new JsonObject
        {
            ["title"] = title,
            ["head"] = sourceBranch,
            ["base"] = targetBranch
        };

        var response = await client.PostAsync(
            $"https://api.github.com/repos/{remote.ProjectPath}/pulls",
            new StringContent(bodyObj.ToJsonString(), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode == 422)
            {
                var existingUrl = await FindExistingGitHubPr(client, remote, sourceBranch);
                if (existingUrl is not null)
                {
                    Console.WriteLine("A pull request already exists for this branch:");
                    return existingUrl;
                }
            }

            PrintApiError("GitHub", response, errorBody);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var prUrl = doc.RootElement.GetProperty("html_url").GetString();
        var prNumber = doc.RootElement.GetProperty("number").GetInt32();

        // GitHub assigns reviewers via a separate endpoint after PR creation
        if (reviewerUsernames is { Count: > 0 })
        {
            await RequestGitHubReviewers(client, remote, prNumber, reviewerUsernames);
        }

        return prUrl;
    }

    private static async Task RequestGitHubReviewers(HttpClient client, RemoteInfo remote, int prNumber, List<string> reviewers)
    {
        try
        {
            var bodyObj = new JsonObject
            {
                ["reviewers"] = new JsonArray(reviewers.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
            };
            await client.PostAsync(
                $"https://api.github.com/repos/{remote.ProjectPath}/pulls/{prNumber}/requested_reviewers",
                new StringContent(bodyObj.ToJsonString(), Encoding.UTF8, "application/json"));
        }
        catch { }
    }

    private static async Task<string?> FindExistingGitHubPr(HttpClient client, RemoteInfo remote, string sourceBranch)
    {
        try
        {
            var response = await client.GetAsync(
                $"https://api.github.com/repos/{remote.ProjectPath}/pulls?head={sourceBranch}&state=open");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var prs = doc.RootElement;
            if (prs.GetArrayLength() > 0)
                return prs[0].GetProperty("html_url").GetString();
        }
        catch { }
        return null;
    }

    private async Task<string?> CreateGitLabMr(RemoteInfo remote, string sourceBranch, string targetBranch, string title,
        List<string>? reviewerUsernames, string? assigneeUsername)
    {
        using var client = CreateGitLabClient();

        var projectId = Uri.EscapeDataString(remote.ProjectPath);
        var apiBase = $"https://{remote.Host}";

        // Resolve usernames to IDs for GitLab
        int? assigneeId = null;
        List<int>? reviewerIds = null;

        if (assigneeUsername is not null)
        {
            var members = await GetGitLabMembers(remote);
            assigneeId = members.FirstOrDefault(m => m.Username.Equals(assigneeUsername, StringComparison.OrdinalIgnoreCase))?.Id;
            if (reviewerUsernames is { Count: > 0 })
                reviewerIds = members
                    .Where(m => reviewerUsernames.Contains(m.Username, StringComparer.OrdinalIgnoreCase))
                    .Select(m => m.Id)
                    .ToList();
        }

        var bodyObj = new JsonObject
        {
            ["source_branch"] = sourceBranch,
            ["target_branch"] = targetBranch,
            ["title"] = title
        };
        if (assigneeId.HasValue)
            bodyObj["assignee_id"] = assigneeId.Value;
        if (reviewerIds is { Count: > 0 })
            bodyObj["reviewer_ids"] = new JsonArray(reviewerIds.Select(id => (JsonNode)JsonValue.Create(id)).ToArray());

        var response = await client.PostAsync(
            $"{apiBase}/api/v4/projects/{projectId}/merge_requests",
            new StringContent(bodyObj.ToJsonString(), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode == 409)
            {
                var existingUrl = await FindExistingGitLabMr(client, apiBase, projectId, sourceBranch);
                if (existingUrl is not null)
                {
                    Console.WriteLine("A merge request already exists for this branch:");
                    return existingUrl;
                }
            }

            PrintApiError("GitLab", response, errorBody);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("web_url").GetString();
    }

    private static async Task<string?> FindExistingGitLabMr(HttpClient client, string apiBase, string projectId, string sourceBranch)
    {
        try
        {
            var response = await client.GetAsync(
                $"{apiBase}/api/v4/projects/{projectId}/merge_requests?source_branch={sourceBranch}&state=opened");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var mrs = doc.RootElement;
            if (mrs.GetArrayLength() > 0)
                return mrs[0].GetProperty("web_url").GetString();
        }
        catch { }
        return null;
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

    private static void PrintApiError(string platform, HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        var message = status switch
        {
            401 => "Invalid or expired API token. Run 'kommit config' to update it.",
            403 => "Access denied. Your token may not have the required permissions.",
            404 => "Repository not found. Check that the remote URL is correct and your token has access.",
            409 => "A merge request already exists for this branch.",
            422 => ParseValidationError(body) ?? "Invalid request. The merge request could not be created.",
            _ => $"{platform} API error ({status}): {body}"
        };
        Console.Error.WriteLine(message);
    }

    private static string? ParseValidationError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
                return $"Validation error: {errors}";
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return $"Validation error: {msg}";
        }
        catch { }
        return null;
    }

    [GeneratedRegex(@"^git@([^:]+):(.+)$")]
    private static partial Regex SshRemoteRegex();

    [GeneratedRegex(@"^https?://([^/]+)/(.+)$")]
    private static partial Regex HttpsRemoteRegex();
}
