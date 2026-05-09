using Kommit.Config;
using Kommit.Git;
using Kommit.UI;

namespace Kommit.Commands;

public static class MrCommand
{
    private static readonly string KommitDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".kommit"
    );

    private static readonly string PendingMrPath = Path.Combine(KommitDir, "pending-mr");

    public static int Run(string[] args, GitService git, KommitConfig config, ConfigService configService)
    {
        var targetBranch = args.Length > 1 ? args.Skip(1).FirstOrDefault(a => !a.StartsWith("-")) : null;
        var autoIncoming = args.Any(a => a is "-incoming" or "--incoming");
        var autoCurrent = args.Any(a => a is "-current" or "--current");
        if (targetBranch is null)
        {
            Out.Error("Usage: kommit mr <target-branch> [-incoming | -current]");
            Out.Error("Example: kommit mr develop");
            Out.Error("         kommit mr develop -incoming  (auto-accept incoming on conflicts)");
            Out.Error("         kommit mr develop -current   (auto-keep current on conflicts)");
            return 1;
        }

        var sourceBranch = git.GetBranchName();
        var remoteUrl = git.GetRemoteUrl();
        var remote = MergeRequestService.ParseRemoteUrl(remoteUrl);

        if (remote.Platform == Platform.Unknown)
        {
            Out.Error($"Could not detect platform from remote: {remoteUrl}");
            return 1;
        }

        var platformName = remote.Platform == Platform.GitHub ? "pull request" : "merge request";

        var token = config.GetTokenForPlatform(remote.Platform);
        if (string.IsNullOrEmpty(token))
        {
            config = PromptForApiToken(config, configService, remote.Platform);
            token = config.GetTokenForPlatform(remote.Platform);
            if (string.IsNullOrEmpty(token))
                return 1;
        }

        var service = new MergeRequestService(token);

        // Get current user for auto-assignee
        Out.Muted("Fetching project info...");
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
            Out.Muted("No project members found.");
        }

        // Push branch
        Out.Muted($"\nPushing {sourceBranch}...");
        git.PushBranch();

        // Check for conflicts
        Out.Muted($"Checking for conflicts with {targetBranch}...");
        git.Fetch();
        var hasConflicts = git.StartMerge($"origin/{targetBranch}");

        if (hasConflicts)
        {
            var conflicts = git.GetConflictedFiles();
            Out.Warn($"\nConflicts detected with {targetBranch} ({conflicts.Count} file(s)):");
            foreach (var file in conflicts)
            {
                var line = GitService.GetFirstConflictLine(file);
                Out.Warn($"  - {file}:{line}");
            }

            if (autoIncoming || autoCurrent)
            {
                // Auto-resolve with the specified strategy
                if (autoIncoming)
                    git.AcceptIncoming(conflicts);
                else
                    git.AcceptCurrent(conflicts);

                git.StageFiles(conflicts);
                Out.Success(autoIncoming ? "Accepted all incoming changes." : "Kept all current changes.");
                CommitMergeAndPush(git, config, sourceBranch, targetBranch);
            }
            else
            {
                Out.Warn($"\nTo create the {platformName}, these conflicts need to be resolved first.");
                Out.Warn($"This merges {targetBranch} into {sourceBranch} so your branch is up to date.\n");

                Console.Write("Resolve now? [Y/n] ");
                var resolveAnswer = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(resolveAnswer) || resolveAnswer.Equals("y", StringComparison.OrdinalIgnoreCase))
                {
                    // Merge is already in progress from the check above — resolve conflicts
                    var resolved = ResolveConflicts(git, conflicts);

                    if (resolved)
                    {
                        // All conflicts resolved inline — commit merge and push
                        CommitMergeAndPush(git, config, sourceBranch, targetBranch);
                    }
                    else
                    {
                        // User chose VS Code or skipped — save state for kommit continue
                        SavePendingMr(targetBranch, selectedReviewers, currentUser, remote);
                        Out.Warn($"\nAfter resolving, run 'kommit continue' — it will finish the merge and create the {platformName}.");
                        return 1;
                    }
                }
                else
                {
                    git.AbortMerge();

                    Console.Write($"\nCreate {platformName} anyway? Conflicts will be visible on the remote. [y/N] ");
                    var answer = Console.ReadLine()?.Trim();
                    if (!answer?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Out.Warn("Aborted.");
                        return 1;
                    }
                }
            }
        }
        else
        {
            git.AbortMerge();
            Out.Success("No conflicts.");
        }

        return CreateMr(git, config, service, remote, sourceBranch, targetBranch, selectedReviewers, currentUser);
    }

    public static int CreateMr(GitService git, KommitConfig config, MergeRequestService service,
        RemoteInfo remote, string sourceBranch, string targetBranch,
        List<string> selectedReviewers, string? currentUser)
    {
        var platformName = remote.Platform == Platform.GitHub ? "pull request" : "merge request";
        var title = MergeRequestService.GenerateTitle(sourceBranch);
        Out.Muted($"Creating {platformName}: \"{title}\"...");

        if (currentUser is not null)
            Out.Muted($"Assignee: {currentUser}");
        if (selectedReviewers.Count > 0)
            Out.Muted($"Reviewers: {string.Join(", ", selectedReviewers)}");
        else
            Out.Muted("No reviewers selected.");

        var result = service.CreateMergeRequest(remote, sourceBranch, targetBranch, title, selectedReviewers, currentUser)
            .GetAwaiter().GetResult();

        if (result is null)
        {
            Out.Error($"Failed to create {platformName}.");
            return 1;
        }

        UndoCommand.RecordCommand("mr", result);
        Out.Info(result);
        return 0;
    }

    /// <summary>
    /// Resume MR creation after conflict resolution via kommit continue.
    /// Returns true if a pending MR was found and handled.
    /// </summary>
    public static bool TryResumePendingMr(GitService git, KommitConfig config, ConfigService configService)
    {
        if (!File.Exists(PendingMrPath))
            return false;

        var pending = LoadPendingMr();
        File.Delete(PendingMrPath);

        if (pending is null)
            return false;

        var (targetBranch, reviewers, currentUser, remote) = pending.Value;
        var sourceBranch = git.GetBranchName();
        var platformName = remote.Platform == Platform.GitHub ? "pull request" : "merge request";

        Console.Write($"\nConflicts resolved. Create the {platformName} to {targetBranch}? [Y/n] ");
        var answer = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(answer) && !answer.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            Out.Muted($"Skipped. You can create it later with 'kommit mr {targetBranch}'.");
            return true;
        }

        var token = config.GetTokenForPlatform(remote.Platform);
        if (string.IsNullOrEmpty(token))
        {
            config = PromptForApiToken(config, configService, remote.Platform);
            token = config.GetTokenForPlatform(remote.Platform);
            if (string.IsNullOrEmpty(token))
                return true;
        }

        var service = new MergeRequestService(token);
        CreateMr(git, config, service, remote, sourceBranch, targetBranch, reviewers, currentUser);
        return true;
    }

    private static bool ResolveConflicts(GitService git, List<string> conflicts)
    {
        var useIncoming = false;

        Out.Warn($"How do you want to resolve all {conflicts.Count} conflict(s)?\n");
        Console.Write("[i]ncoming / [c]urrent / [v]scode / [o]ne-by-one? ");

        while (true)
        {
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (input is "i" or "incoming")
            {
                useIncoming = true;
                break;
            }
            if (input is "c" or "current")
            {
                break;
            }
            if (input is "v" or "vscode")
            {
                OpenConflictsInVSCode(conflicts);
                Out.Warn("Fix the conflicts in VS Code, then run 'kommit continue'.");
                return false;
            }
            if (input is "o" or "one-by-one")
            {
                return ResolveOneByOne(git, conflicts);
            }

            Console.Write("[i]ncoming / [c]urrent / [v]scode / [o]ne-by-one? ");
        }

        if (useIncoming)
            git.AcceptIncoming(conflicts);
        else
            git.AcceptCurrent(conflicts);

        git.StageFiles(conflicts);
        Out.Success(useIncoming ? "Accepted all incoming changes." : "Kept all current changes.");
        return true;
    }

    private static bool ResolveOneByOne(GitService git, List<string> conflicts)
    {
        foreach (var file in conflicts)
        {
            var line = GitService.GetFirstConflictLine(file);
            Out.Warn($"\n  {file}:{line}");
            Console.Write("  [i]ncoming / [c]urrent / [v]scode / [s]kip? ");

            while (true)
            {
                var input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (input is "i" or "incoming")
                {
                    git.AcceptIncoming(new[] { file });
                    git.StageFiles(new[] { file });
                    Out.Muted("  -> accepted incoming");
                    break;
                }
                if (input is "c" or "current")
                {
                    git.AcceptCurrent(new[] { file });
                    git.StageFiles(new[] { file });
                    Out.Muted("  -> kept current");
                    break;
                }
                if (input is "v" or "vscode")
                {
                    OpenConflictsInVSCode(new[] { file });
                    Out.Muted("  -> opened in VS Code");
                    Out.Warn("  Fix the conflicts, then run 'kommit continue'.");
                    return false;
                }
                if (input is "s" or "skip")
                {
                    Out.Muted("  -> skipped");
                    break;
                }
                Console.Write("  [i]ncoming / [c]urrent / [v]scode / [s]kip? ");
            }
        }

        // Check if all conflicts are resolved
        var remaining = git.GetConflictedFiles();
        if (remaining.Count > 0)
        {
            Out.Warn($"\n{remaining.Count} conflict(s) still unresolved. Run 'kommit continue' after resolving.");
            return false;
        }

        return true;
    }

    private static void CommitMergeAndPush(GitService git, KommitConfig config, string sourceBranch, string targetBranch)
    {
        var message = $"merge {targetBranch} into {sourceBranch}";
        git.Commit(message);
        Out.Info($"\nCommitted: {message}");

        Out.Muted($"Pushing {sourceBranch}...");
        git.Push(config.PushStrategy);
    }

    private static void SavePendingMr(string targetBranch, List<string> reviewers, string? currentUser, RemoteInfo remote)
    {
        Directory.CreateDirectory(KommitDir);
        // Format: targetBranch|reviewers|currentUser|platform|host|projectPath
        var reviewerStr = string.Join(",", reviewers);
        var content = $"{targetBranch}|{reviewerStr}|{currentUser ?? ""}|{(int)remote.Platform}|{remote.Host}|{remote.ProjectPath}";
        File.WriteAllText(PendingMrPath, content);
    }

    private static (string targetBranch, List<string> reviewers, string? currentUser, RemoteInfo remote)? LoadPendingMr()
    {
        try
        {
            var parts = File.ReadAllText(PendingMrPath).Trim().Split('|');
            if (parts.Length < 6) return null;

            var targetBranch = parts[0];
            var reviewers = string.IsNullOrEmpty(parts[1]) ? new List<string>() : parts[1].Split(',').ToList();
            var currentUser = string.IsNullOrEmpty(parts[2]) ? null : parts[2];
            var platform = (Platform)int.Parse(parts[3]);
            var host = parts[4];
            var projectPath = parts[5];
            var remote = new RemoteInfo(platform, host, projectPath);

            return (targetBranch, reviewers, currentUser, remote);
        }
        catch
        {
            return null;
        }
    }

    private static void OpenConflictsInVSCode(IEnumerable<string> files)
    {
        var filesWithLines = files.Select(f =>
        {
            var line = GitService.GetFirstConflictLine(f);
            return $"{f}:{line}";
        });
        GitService.OpenInVSCode(filesWithLines);
    }

    private static List<string> PickReviewers(List<ProjectMember> candidates, string? currentUser)
    {
        var selected = new HashSet<int>();
        var cursorIndex = 0;

        Out.Info("\nSelect reviewers (Space to toggle, Enter to confirm):\n");

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
        Out.Warn("No API token configured.\n");

        if (platform == Platform.GitHub)
        {
            Console.WriteLine("To create a GitHub Personal Access Token:");
            Out.Muted("  1. Go to GitHub → Settings → Developer settings → Personal access tokens → Fine-grained tokens");
            Out.Muted("  2. Click 'Generate new token'");
            Out.Muted("  3. Give it a name (e.g. 'kommit')");
            Out.Muted("  4. Under 'Repository permissions', set 'Pull requests' to 'Read and write'");
            Out.Muted("  5. Click 'Generate token' and copy it");
        }
        else
        {
            Console.WriteLine("To create a GitLab Personal Access Token:");
            Out.Muted("  1. Go to GitLab → Settings → Access Tokens");
            Out.Muted("  2. Give it a name (e.g. 'kommit')");
            Out.Muted("  3. Select the 'api' scope");
            Out.Muted("  4. Click 'Create personal access token' and copy it");
        }

        Console.Write("\nPaste your token here (or press Enter to cancel): ");
        var token = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            Out.Warn("Cancelled.");
            return config;
        }

        config.SetTokenForPlatform(platform, token);
        configService.Save(config);
        Out.Success("Token saved to ~/.kommit/config.json\n");

        return config;
    }
}
