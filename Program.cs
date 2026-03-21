using Kommit.Analysis;
using Kommit.Config;
using Kommit.Git;
using Kommit.Update;

namespace Kommit;

class Program
{
    static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 0;
        }

        if (args.Contains("--version"))
        {
            var version = UpdateService.GetCurrentVersion();
            Console.WriteLine($"kommit {version.Major}.{version.Minor}.{version.Build}");
            return 0;
        }

        if (args.Length > 0 && args[0] == "update")
        {
            var updateService = new UpdateService();
            return updateService.RunUpdateAsync().GetAwaiter().GetResult();
        }

        var configService = new ConfigService();
        var config = configService.Load();
        var git = new GitService();

        if (args.Length > 0 && args[0] == "config")
        {
            var editor = new ConfigEditor(configService);
            editor.Run();
            return 0;
        }

        if (args.Length > 0 && args[0] == "pull")
        {
            git.Pull(config.PullStrategy);
            Console.WriteLine("Pull complete.");
            return 0;
        }

        if (args.Length > 0 && args[0] == "push")
        {
            git.Push(config.PushStrategy);
            Console.WriteLine("Push complete.");
            return 0;
        }

        if (args.Length > 0 && args[0] == "tag")
        {
            return HandleTag(args, git);
        }

        if (args.Length > 0 && args[0] == "merge")
        {
            return HandleMerge(args, git, config);
        }

        var dryRun = args.Contains("--dry-run");

        // Auto-pull before commit
        if (config.AutoPull && !dryRun)
        {
            Console.WriteLine("Pulling latest changes...");
            git.Pull(config.PullStrategy);
        }

        if (!git.HasStagedChanges() && config.AutoAdd && !dryRun)
        {
            Console.Write("No staged changes. Stage all files? [Y/n] ");
            var answer = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer) || answer.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                git.StageAll();
            }
        }

        if (!git.HasStagedChanges())
        {
            Console.Error.WriteLine("No staged changes found. Stage your changes with 'git add' first.");
            return 1;
        }

        var branch = git.GetBranchName();
        var diff = git.GetStagedDiff();
        var analyzer = new CommitAnalyzer();

        // Check if we should split
        var splitter = new CommitSplitter(git, analyzer, config);
        if (splitter.ShouldSplit(diff))
        {
            splitter.RunInteractiveSplit(branch, diff, dryRun);

            if (!dryRun && config.AutoPush)
            {
                git.Push(config.PushStrategy);
                Console.WriteLine("Pushed.");
            }

            return 0;
        }

        var message = analyzer.Analyze(branch, diff);

        // Apply default scope from config if analyzer didn't infer one
        if (config.DefaultScope is not null && message.Scope is null)
            message = message with { Scope = config.DefaultScope };

        var finalMessage = TruncateMessage(message.ToString(), config.MaxCommitLength);

        if (dryRun)
        {
            Console.WriteLine(finalMessage);
            return 0;
        }

        git.Commit(finalMessage);
        Console.WriteLine(finalMessage);

        if (config.AutoPush)
        {
            git.Push(config.PushStrategy);
            Console.WriteLine("Pushed.");
        }

        return 0;
    }

    private static int HandleMerge(string[] args, GitService git, KommitConfig config)
    {
        var useIncoming = args.Contains("-incoming");
        var useCurrent = args.Contains("-current");

        // If a branch name is provided, start the merge
        var branchArg = args.Skip(1).FirstOrDefault(a => !a.StartsWith("-"));

        if (!git.IsMergeInProgress())
        {
            if (branchArg is null)
            {
                Console.Error.WriteLine("No merge in progress. Start one with: kommit merge <branch>");
                return 1;
            }

            Console.WriteLine($"Fetching latest from origin...");
            git.Fetch();

            Console.WriteLine($"Merging {branchArg} into {git.GetBranchName()}...");
            var hasConflicts = git.StartMerge(branchArg);

            if (!hasConflicts)
            {
                Console.WriteLine("Merged cleanly.");
                git.Push(config.PushStrategy);
                Console.WriteLine("Pushed.");
                return 0;
            }

            Console.WriteLine("Merge has conflicts.\n");
        }

        var conflicts = git.GetConflictedFiles();
        if (conflicts.Count == 0)
        {
            Console.WriteLine("No conflicted files found. You can commit the merge.");
            return 0;
        }

        // Bulk resolve with -incoming or -current
        if (useIncoming || useCurrent)
        {
            var strategy = useIncoming ? "incoming" : "current";
            Console.WriteLine($"WARNING: This will resolve all {conflicts.Count} conflict(s) by accepting {strategy} changes:");
            foreach (var file in conflicts)
                Console.WriteLine($"  - {file}");

            Console.Write("\nAre you sure? [y/N] ");
            var answer = Console.ReadLine()?.Trim();
            if (!answer?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine("Aborted.");
                return 1;
            }

            if (useIncoming)
                git.AcceptIncoming(conflicts);
            else
                git.AcceptCurrent(conflicts);

            git.StageFiles(conflicts);
            return CommitAndPushMerge(git, config, conflicts);
        }

        // Interactive per-file resolution
        Console.WriteLine($"{conflicts.Count} conflicted file(s):\n");

        foreach (var file in conflicts)
        {
            Console.WriteLine($"  {file}");
            Console.Write("  [i]ncoming / [c]urrent / [s]kip? ");

            while (true)
            {
                var input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (input is "i" or "incoming")
                {
                    git.AcceptIncoming(new[] { file });
                    git.StageFiles(new[] { file });
                    Console.WriteLine("  -> accepted incoming\n");
                    break;
                }
                if (input is "c" or "current")
                {
                    git.AcceptCurrent(new[] { file });
                    git.StageFiles(new[] { file });
                    Console.WriteLine("  -> kept current\n");
                    break;
                }
                if (input is "s" or "skip")
                {
                    Console.WriteLine("  -> skipped\n");
                    break;
                }
                Console.Write("  [i]ncoming / [c]urrent / [s]kip? ");
            }
        }

        // Check if there are still unresolved conflicts
        var remaining = git.GetConflictedFiles();
        if (remaining.Count > 0)
        {
            Console.WriteLine($"{remaining.Count} conflict(s) still unresolved:");
            foreach (var file in remaining)
                Console.WriteLine($"  - {file}");
            Console.WriteLine("\nResolve them manually or run 'kommit merge' again.");
            return 1;
        }

        return CommitAndPushMerge(git, config, conflicts);
    }

    private static int CommitAndPushMerge(GitService git, KommitConfig config, List<string> conflicts)
    {
        var description = conflicts.Count == 1
            ? $"resolve merge conflict in {Path.GetFileName(conflicts[0])}"
            : $"resolve merge conflicts in {conflicts.Count} files";
        var message = $"fix: {description}";

        git.Commit(message);
        Console.WriteLine(message);

        git.Push(config.PushStrategy);
        Console.WriteLine("Pushed.");

        return 0;
    }

    private static int HandleTag(string[] args, GitService git)
    {
        var bump = "minor";
        if (args.Contains("-major")) bump = "major";
        else if (args.Contains("-patch")) bump = "patch";

        var latest = git.GetLatestTag();
        Version current;

        if (latest is not null && latest.StartsWith("v") &&
            Version.TryParse(latest[1..], out var parsed))
        {
            current = parsed;
        }
        else
        {
            current = new Version(0, 0, 0);
        }

        var next = bump switch
        {
            "major" => new Version(current.Major + 1, 0, 0),
            "patch" => new Version(current.Major, current.Minor, current.Build + 1),
            _ => new Version(current.Major, current.Minor + 1, 0),
        };

        var versionString = $"{next.Major}.{next.Minor}.{next.Build}";
        var tag = $"v{versionString}";

        // Update .csproj version so the built binary reports the correct version
        UpdateCsprojVersion(versionString);

        git.StageAll();
        git.Commit($"chore: bump version to {tag}");
        git.CreateTag(tag);
        git.Push("simple");
        git.PushTag(tag);
        Console.WriteLine(tag);

        return 0;
    }

    private static void UpdateCsprojVersion(string version)
    {
        var csprojFiles = Directory.GetFiles(".", "*.csproj", SearchOption.TopDirectoryOnly);
        if (csprojFiles.Length == 0) return;

        var csproj = csprojFiles[0];
        var content = File.ReadAllText(csproj);
        var updated = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"<Version>[^<]*</Version>",
            $"<Version>{version}</Version>");
        File.WriteAllText(csproj, updated);
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (maxLength > 0 && message.Length > maxLength)
            return message[..(maxLength - 3)] + "...";
        return message;
    }

    private static void PrintHelp()
    {
        var version = UpdateService.GetCurrentVersion();
        Console.WriteLine($"kommit {version.Major}.{version.Minor}.{version.Build}");
        Console.WriteLine("A lightweight CLI tool that generates Conventional Commits messages from your staged changes.");
        Console.WriteLine();
        Console.WriteLine("Usage: kommit [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  config          Open interactive config editor");
        Console.WriteLine("  merge <branch>  Merge a branch into current branch");
        Console.WriteLine("    -incoming       Accept all incoming changes (skip interactive)");
        Console.WriteLine("    -current        Keep all current changes (skip interactive)");
        Console.WriteLine("  push            Push changes using configured strategy");
        Console.WriteLine("  pull            Pull changes using configured strategy");
        Console.WriteLine("  tag             Bump minor version and push tag");
        Console.WriteLine("    -major          Bump major version instead");
        Console.WriteLine("    -patch          Bump patch version instead");
        Console.WriteLine("  update          Check for and install the latest version");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --dry-run       Preview the commit message without committing");
        Console.WriteLine("  --version       Show the current version");
        Console.WriteLine("  -h, --help      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Config: ~/.kommitconfig (JSON)");
        Console.WriteLine("  autoAdd         Prompt to stage all files if none staged (default: false)");
        Console.WriteLine("  autoPush        Auto-push after commit (default: false)");
        Console.WriteLine("  autoPull        Auto-pull before commit (default: false)");
        Console.WriteLine("  pullStrategy    \"rebase\" or \"merge\" (default: rebase)");
        Console.WriteLine("  pushStrategy    \"simple\", \"set-upstream\", or \"force-with-lease\" (default: simple)");
        Console.WriteLine("  defaultScope    Default scope for commit messages (default: null)");
        Console.WriteLine("  maxCommitLength Max commit message length (default: 72)");
        Console.WriteLine("  maxStagedFiles  File count threshold for interactive split (default: null)");
        Console.WriteLine("  maxStagedLines  Line count threshold for interactive split (default: null)");
    }
}
