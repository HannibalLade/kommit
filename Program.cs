using Kommit.Analysis;
using Kommit.Git;
using Kommit.Update;

namespace Kommit;

class Program
{
    static int Main(string[] args)
    {
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

        var dryRun = args.Contains("--dry-run");

        var git = new GitService();

        if (!git.HasStagedChanges())
        {
            Console.Error.WriteLine("No staged changes found. Stage your changes with 'git add' first.");
            return 1;
        }

        var branch = git.GetBranchName();
        var diff = git.GetStagedDiff();
        var analyzer = new CommitAnalyzer();
        var message = analyzer.Analyze(branch, diff);

        if (dryRun)
        {
            Console.WriteLine(message);
            return 0;
        }

        Console.WriteLine($"Committing: {message}");
        git.Commit(message.ToString());
        return 0;
    }
}
