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

        var service = new MergeRequestService(config.ApiToken);

        // Get current user for auto-assignee
        Console.WriteLine("Fetching project info...");
        var currentUser = service.GetCurrentUsername(remote).GetAwaiter().GetResult();
        var members = service.GetProjectMembers(remote).GetAwaiter().GetResult();

        // Interactive reviewer selection (includes yourself)
        var selectedReviewers = new List<string>();
        if (members.Count > 0)
        {
            selectedReviewers = PickReviewers(members, currentUser);
        }
        else
        {
            Console.WriteLine("No project members found.");
        }

        // Push branch
        Console.WriteLine($"\nPushing {sourceBranch}...");
        git.PushBranch();

        // Check for conflicts
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

        if (currentUser is not null)
            Console.WriteLine($"Assignee: {currentUser}");
        if (selectedReviewers.Count > 0)
            Console.WriteLine($"Reviewers: {string.Join(", ", selectedReviewers)}");
        else
            Console.WriteLine("No reviewers selected.");

        var result = service.CreateMergeRequest(remote, sourceBranch, targetBranch, title, selectedReviewers, currentUser)
            .GetAwaiter().GetResult();

        if (result is null)
        {
            Console.Error.WriteLine($"Failed to create {platformName}.");
            return 1;
        }

        UndoCommand.RecordCommand("mr", result);
        Console.WriteLine(result);
        return 0;
    }

    private static List<string> PickReviewers(List<ProjectMember> candidates, string? currentUser)
    {
        var selected = new HashSet<int>();
        var cursorIndex = 0;

        Console.WriteLine("\nSelect reviewers (Space to toggle, Enter to confirm):\n");

        string FormatLine(int i)
        {
            var marker = selected.Contains(i) ? "[x]" : "[ ]";
            var arrow = i == cursorIndex ? ">" : " ";
            var you = candidates[i].Username.Equals(currentUser, StringComparison.OrdinalIgnoreCase) ? " (you)" : "";
            var display = candidates[i].Name == candidates[i].Username
                ? candidates[i].Username
                : $"{candidates[i].Name} (@{candidates[i].Username})";
            return $"  {arrow} {marker} {display}{you}";
        }

        void RenderLine(int i, int offset)
        {
            // Move cursor to the right line relative to current position
            if (offset != 0)
                Console.Write(offset > 0 ? $"\x1b[{offset}B" : $"\x1b[{-offset}A");
            Console.Write($"\r\x1b[2K{FormatLine(i)}");
            // Move back
            if (offset != 0)
                Console.Write(offset > 0 ? $"\x1b[{offset}A" : $"\x1b[{-offset}B");
        }

        // Initial render
        for (var i = 0; i < candidates.Count; i++)
            Console.WriteLine(FormatLine(i));

        // Move cursor back to first item
        Console.Write($"\x1b[{candidates.Count}A");

        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.UpArrow && cursorIndex > 0)
            {
                var oldIndex = cursorIndex;
                cursorIndex--;
                // Move cursor up one line in terminal
                Console.Write("\x1b[1A");
                // Update old line (now below us)
                RenderLine(oldIndex, 1);
                // Update current line
                Console.Write($"\r\x1b[2K{FormatLine(cursorIndex)}");
            }
            else if (key.Key == ConsoleKey.DownArrow && cursorIndex < candidates.Count - 1)
            {
                var oldIndex = cursorIndex;
                cursorIndex++;
                // Move cursor down one line in terminal
                Console.Write("\x1b[1B");
                // Update old line (now above us)
                RenderLine(oldIndex, -1);
                // Update current line
                Console.Write($"\r\x1b[2K{FormatLine(cursorIndex)}");
            }
            else if (key.Key == ConsoleKey.Spacebar)
            {
                if (selected.Contains(cursorIndex))
                    selected.Remove(cursorIndex);
                else
                    selected.Add(cursorIndex);
                Console.Write($"\r\x1b[2K{FormatLine(cursorIndex)}");
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // Move to below the list
                var remaining = candidates.Count - 1 - cursorIndex;
                if (remaining > 0)
                    Console.Write($"\x1b[{remaining}B");
                Console.WriteLine();
                Console.WriteLine();
                break;
            }
            else if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
            {
                var remaining = candidates.Count - 1 - cursorIndex;
                if (remaining > 0)
                    Console.Write($"\x1b[{remaining}B");
                Console.WriteLine();
                Console.WriteLine();
                return [];
            }
        }

        return selected.Select(i => candidates[i].Username).ToList();
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
        Console.WriteLine("Token saved to ~/.kommit/config.json\n");

        return config;
    }
}
