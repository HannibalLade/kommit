using Kommit.Config;
using Kommit.Git;

namespace Kommit.Commands;

public static class MergeCommand
{
    public static int Run(string[] args, GitService git, KommitConfig config)
    {
        var useIncoming = args.Contains("-incoming");
        var useCurrent = args.Contains("-current");

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
                UndoCommand.RecordCommand("merge");
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
        UndoCommand.RecordCommand("merge");
        Console.WriteLine("Pushed.");

        return 0;
    }
}
