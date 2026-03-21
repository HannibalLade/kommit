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

        var dryRun = args.Contains("--dry-run");

        // Auto-pull before commit
        if (config.AutoPull && !dryRun)
        {
            Console.WriteLine("Pulling latest changes...");
            git.Pull(config.PullStrategy);
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
                Console.WriteLine("Pushing changes...");
                git.Push(config.PushStrategy);
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

        Console.WriteLine($"Committing: {finalMessage}");
        git.Commit(finalMessage);

        // Auto-push after commit
        if (config.AutoPush)
        {
            Console.WriteLine("Pushing changes...");
            git.Push(config.PushStrategy);
        }

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
        Console.WriteLine("  push            Push changes using configured strategy");
        Console.WriteLine("  pull            Pull changes using configured strategy");
        Console.WriteLine("  update          Check for and install the latest version");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --dry-run       Preview the commit message without committing");
        Console.WriteLine("  --version       Show the current version");
        Console.WriteLine("  -h, --help      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Config: ~/.kommitconfig (JSON)");
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
