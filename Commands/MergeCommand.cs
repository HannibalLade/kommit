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

            var currentBranch = git.GetBranchName();
            Console.WriteLine($"Fetching latest from origin...");
            git.Fetch();

            Console.WriteLine($"Merging {branchArg} into {currentBranch}...");
            Console.WriteLine($"(This brings {branchArg} changes into your branch. If you have an MR, this resolves its conflicts.)\n");
            var hasConflicts = git.StartMerge(branchArg);

            if (!hasConflicts)
            {
                Console.WriteLine("Merged cleanly — no conflicts.");
                Console.WriteLine($"Pushing {currentBranch}...");
                git.Push(config.PushStrategy);
                UndoCommand.RecordCommand("merge");
                Console.WriteLine("Done. If you had an MR, it should now be conflict-free.");
                return 0;
            }

            Console.WriteLine("Merge has conflicts.\n");
        }

        return ResolveConflicts(git, config, useIncoming, useCurrent);
    }

    public static int Continue(GitService git, KommitConfig config)
    {
        if (!git.IsMergeInProgress())
        {
            Console.Error.WriteLine("No merge in progress.");
            return 1;
        }

        // Auto-stage files that were resolved (no more conflict markers)
        var conflicts = git.GetConflictedFiles();
        var autoStaged = new List<string>();
        foreach (var file in conflicts)
        {
            if (!FileHasConflictMarkers(file))
            {
                git.StageFiles(new[] { file });
                autoStaged.Add(file);
            }
        }
        if (autoStaged.Count > 0)
        {
            foreach (var file in autoStaged)
                Console.WriteLine($"Staged resolved file: {file}");
            Console.WriteLine();
        }

        // Re-check after staging
        conflicts = git.GetConflictedFiles();
        if (conflicts.Count == 0)
        {
            Console.WriteLine("All conflicts resolved.");
            return CommitAndPush(git, config);
        }

        Console.WriteLine($"{conflicts.Count} conflict(s) still unresolved:\n");
        foreach (var file in conflicts)
        {
            var line = GitService.GetFirstConflictLine(file);
            Console.WriteLine($"  {file}:{line}");
        }

        Console.Write("\nOpen in VS Code? [Y/n] ");
        var answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer) || answer.Equals("y", StringComparison.OrdinalIgnoreCase))
        {
            OpenConflictsInVSCode(conflicts);
            Console.WriteLine("Fix the conflicts in VS Code, then run 'kommit continue' again.");
        }
        else
        {
            Console.WriteLine("Fix the conflicts manually, then run 'kommit continue' again.");
        }

        return 1;
    }

    private static int ResolveConflicts(GitService git, KommitConfig config, bool useIncoming, bool useCurrent)
    {
        var conflicts = git.GetConflictedFiles();
        if (conflicts.Count == 0)
        {
            Console.WriteLine("No conflicted files found.");
            return CommitAndPush(git, config);
        }

        // Bulk resolve with -incoming or -current
        if (useIncoming || useCurrent)
        {
            var strategy = useIncoming ? "incoming" : "current";
            Console.WriteLine($"Resolving all {conflicts.Count} conflict(s) by accepting {strategy} changes:");
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
            Console.WriteLine(useIncoming ? "Accepted all incoming changes." : "Kept all current changes.");
            return CommitAndPush(git, config);
        }

        // Interactive per-file resolution
        Console.WriteLine($"{conflicts.Count} conflicted file(s):\n");

        foreach (var file in conflicts)
        {
            var line = GitService.GetFirstConflictLine(file);
            Console.WriteLine($"  {file}:{line}");
            Console.Write("  [i]ncoming / [c]urrent / [v]scode / [s]kip? ");

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
                if (input is "v" or "vscode")
                {
                    OpenConflictsInVSCode(new[] { file });
                    Console.WriteLine("  -> opened in VS Code");
                    Console.WriteLine("  Fix the conflict, then run 'kommit continue'.\n");
                    var remaining = git.GetConflictedFiles();
                    if (remaining.Count > 0)
                    {
                        Console.WriteLine($"{remaining.Count} conflict(s) remaining. Run 'kommit continue' after resolving.");
                    }
                    return 1;
                }
                if (input is "s" or "skip")
                {
                    Console.WriteLine("  -> skipped\n");
                    break;
                }
                Console.Write("  [i]ncoming / [c]urrent / [v]scode / [s]kip? ");
            }
        }

        var stillUnresolved = git.GetConflictedFiles();
        if (stillUnresolved.Count > 0)
        {
            Console.WriteLine($"{stillUnresolved.Count} conflict(s) still unresolved.");
            Console.WriteLine("Resolve them manually or run 'kommit continue'.");
            return 1;
        }

        return CommitAndPush(git, config);
    }

    private static bool FileHasConflictMarkers(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        foreach (var line in File.ReadLines(filePath))
        {
            if (line.StartsWith("<<<<<<<") || line.StartsWith(">>>>>>>"))
                return true;
        }
        return false;
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

    private static int CommitAndPush(GitService git, KommitConfig config)
    {
        var currentBranch = git.GetBranchName();
        var stagedFiles = git.GetStagedFileNames();
        var description = stagedFiles.Count == 1
            ? $"resolve merge conflict in {Path.GetFileName(stagedFiles[0])}"
            : $"resolve merge conflicts in {stagedFiles.Count} files";
        var message = $"fix: {description}";

        git.Commit(message);
        Console.WriteLine(message);

        Console.WriteLine($"Pushing {currentBranch}...");
        git.Push(config.PushStrategy);
        UndoCommand.RecordCommand("merge");
        Console.WriteLine("Done. If you had an MR, it should now be conflict-free.");

        // Check if there's a pending MR to resume
        MrCommand.TryResumePendingMr(git, config);

        return 0;
    }
}
