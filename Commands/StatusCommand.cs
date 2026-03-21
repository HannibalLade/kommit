using Kommit.Analysis;
using Kommit.Config;
using Kommit.Git;

namespace Kommit.Commands;

public static class StatusCommand
{
    public static int Run(GitService git, KommitConfig config)
    {
        if (!git.HasStagedChanges())
        {
            Console.Error.WriteLine("No staged changes.");
            return 1;
        }

        var branch = git.GetBranchName();
        var diff = git.GetStagedDiff();
        var analyzer = new CommitAnalyzer();
        var message = analyzer.Analyze(branch, diff);

        if (config.DefaultScope is not null && message.Scope is null)
            message = message with { Scope = config.DefaultScope };

        var finalMessage = message.ToString();
        if (config.MaxCommitLength > 0 && finalMessage.Length > config.MaxCommitLength)
            finalMessage = finalMessage[..(config.MaxCommitLength - 3)] + "...";

        Console.WriteLine($"Message: {finalMessage}");
        Console.WriteLine($"\n{diff.ChangedFiles.Count} staged file(s):");
        foreach (var file in diff.ChangedFiles)
            Console.WriteLine($"  {file}");
        Console.WriteLine($"\n+{diff.LinesAdded} -{diff.LinesDeleted} lines");

        return 0;
    }
}
