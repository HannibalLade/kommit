using Kommit.Analysis;
using Kommit.Git;

namespace Kommit;

class Program
{
    static int Main(string[] args)
    {
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
