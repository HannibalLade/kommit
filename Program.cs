using Kommit.Analysis;
using Kommit.Commands;
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

        if (!GitService.IsGitInstalled())
        {
            Console.Error.WriteLine("Git is not installed or not in your PATH.");
            Console.Error.WriteLine("Install git: https://git-scm.com/downloads");
            return 1;
        }

        if (!GitService.IsGitRepo())
        {
            Console.Error.WriteLine("Not a git repository. Run 'git init' to create one.");
            return 1;
        }

        try
        {
            return Run(args);
        }
        catch (GitException ex)
        {
            Console.Error.WriteLine($"Git error: {ex.Message}");
            return 1;
        }
    }

    static int Run(string[] args)
    {
        var configService = new ConfigService();
        var config = configService.Load();
        var git = new GitService();

        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "config":
                    var editor = new ConfigEditor(configService);
                    editor.Run();
                    return 0;
                case "pull":
                    git.Pull(config.PullStrategy);
                    Console.WriteLine("Pull complete.");
                    return 0;
                case "push":
                    git.Push(config.PushStrategy);
                    Console.WriteLine("Push complete.");
                    return 0;
                case "tag":
                    return TagCommand.Run(args, git);
                case "merge":
                    return MergeCommand.Run(args, git, config);
                case "mr":
                    return MrCommand.Run(args, git, config, configService);
                case "undo":
                    return UndoCommand.Run(git);
                case "status":
                    return StatusCommand.Run(git, config);
            }
        }

        var dryRun = args.Contains("--dry-run");

        // Detached HEAD warning
        var branch = git.GetBranchName();
        if (branch == "HEAD")
        {
            Console.WriteLine("Warning: You are in detached HEAD state. Branch-based type inference won't work.");
            Console.Write("Continue anyway? [Y/n] ");
            var answer = Console.ReadLine()?.Trim();
            if (answer?.Equals("n", StringComparison.OrdinalIgnoreCase) == true)
                return 1;
        }

        if (config.AutoPull && !dryRun)
        {
            var needsStash = git.HasUnstagedChanges();
            if (needsStash)
                git.Stash();

            Console.WriteLine("Pulling latest changes...");
            git.Pull(config.PullStrategy);

            if (needsStash)
                git.StashPop();
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

        var diff = git.GetStagedDiff();
        var analyzer = new CommitAnalyzer();

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
        Console.WriteLine("  mr <branch>     Create a merge request/pull request to target branch");
        Console.WriteLine("  push            Push changes using configured strategy");
        Console.WriteLine("  pull            Pull changes using configured strategy");
        Console.WriteLine("  status          Preview commit message and staged file list");
        Console.WriteLine("  tag             Bump minor version and push tag");
        Console.WriteLine("    -major          Bump major version instead");
        Console.WriteLine("    -patch          Bump patch version instead");
        Console.WriteLine("  undo            Undo the last commit (keeps changes staged)");
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
