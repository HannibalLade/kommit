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

        var tag = $"v{next.Major}.{next.Minor}.{next.Build}";

        git.CreateTag(tag);
        git.PushTag(tag);
        Console.WriteLine(tag);

        return 0;
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
