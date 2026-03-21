using Kommit.Config;
using Kommit.Git;

namespace Kommit.Commands;

public static class MrCommand
{
    public static int Run(string[] args, GitService git, KommitConfig config, ConfigService configService)
    {
        var targetBranch = args.Length > 1 ? args.Skip(1).FirstOrDefault(a => !a.StartsWith("-")) : null;
        if (targetBranch is null)
        {
            Console.Error.WriteLine("Usage: kommit mr <target-branch>");
            Console.Error.WriteLine("Example: kommit mr develop");
            return 1;
        }

        var sourceBranch = git.GetBranchName();
        var remoteUrl = git.GetRemoteUrl();
        var remote = MergeRequestService.ParseRemoteUrl(remoteUrl);

        if (remote.Platform == Platform.Unknown)
        {
            Console.Error.WriteLine($"Could not detect platform from remote: {remoteUrl}");
            return 1;
        }

        var platformName = remote.Platform == Platform.GitHub ? "pull request" : "merge request";

        if (string.IsNullOrEmpty(config.ApiToken))
        {
            config = PromptForApiToken(config, configService, remote.Platform);
            if (string.IsNullOrEmpty(config.ApiToken))
                return 1;
        }

        Console.WriteLine($"Pushing {sourceBranch}...");
        git.PushBranch();

        Console.WriteLine($"Checking for conflicts with {targetBranch}...");
        git.Fetch();
        var hasConflicts = git.StartMerge($"origin/{targetBranch}");

        if (hasConflicts)
        {
            var conflicts = git.GetConflictedFiles();
            Console.WriteLine($"\nConflicts detected with {targetBranch} ({conflicts.Count} file(s)):");
            foreach (var file in conflicts)
                Console.WriteLine($"  - {file}");

            git.AbortMerge();

            Console.Write($"\nCreate {platformName} anyway? Conflicts will be visible on the remote. [y/N] ");
            var answer = Console.ReadLine()?.Trim();
            if (!answer?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine("Aborted. Use 'kommit merge' to resolve conflicts locally first.");
                return 1;
            }
        }
        else
        {
            git.AbortMerge();
            Console.WriteLine("No conflicts.");
        }

        var title = MergeRequestService.GenerateTitle(sourceBranch);
        Console.WriteLine($"Creating {platformName}: \"{title}\"...");

        var service = new MergeRequestService(config.ApiToken);
        var result = service.CreateMergeRequest(remote, sourceBranch, targetBranch, title)
            .GetAwaiter().GetResult();

        if (result is null)
        {
            Console.Error.WriteLine($"Failed to create {platformName}.");
            return 1;
        }

        Console.WriteLine(result);
        return 0;
    }

    private static KommitConfig PromptForApiToken(KommitConfig config, ConfigService configService, Platform platform)
    {
        Console.WriteLine("No API token configured.\n");

        if (platform == Platform.GitHub)
        {
            Console.WriteLine("To create a GitHub Personal Access Token:");
            Console.WriteLine("  1. Go to GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens");
            Console.WriteLine("  2. Click 'Generate new token'");
            Console.WriteLine("  3. Give it a name (e.g. 'kommit')");
            Console.WriteLine("  4. Under 'Repository permissions', set 'Pull requests' to 'Read and write'");
            Console.WriteLine("  5. Click 'Generate token' and copy it");
        }
        else
        {
            Console.WriteLine("To create a GitLab Personal Access Token:");
            Console.WriteLine("  1. Go to GitLab → Settings → Access Tokens");
            Console.WriteLine("  2. Give it a name (e.g. 'kommit')");
            Console.WriteLine("  3. Select the 'api' scope");
            Console.WriteLine("  4. Click 'Create personal access token' and copy it");
        }

        Console.Write("\nPaste your token here (or press Enter to cancel): ");
        var token = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Cancelled.");
            return config;
        }

        config.ApiToken = token;
        configService.Save(config);
        Console.WriteLine("Token saved to ~/.kommitconfig\n");

        return config;
    }
}
