using Kommit.Config;
using Kommit.Git;
using Kommit.Models;
using Kommit.UI;

namespace Kommit.Analysis;

public class CommitSplitter
{
    private readonly GitService _git;
    private readonly CommitAnalyzer _analyzer;
    private readonly KommitConfig _config;

    public CommitSplitter(GitService git, CommitAnalyzer analyzer, KommitConfig config)
    {
        _git = git;
        _analyzer = analyzer;
        _config = config;
    }

    public bool ShouldSplit(DiffSummary diff)
    {
        if (_config.MaxStagedFiles.HasValue && diff.ChangedFiles.Count > _config.MaxStagedFiles.Value)
            return true;

        if (_config.MaxStagedLines.HasValue)
        {
            var totalLines = diff.LinesAdded + diff.LinesDeleted;
            if (totalLines > _config.MaxStagedLines.Value)
                return true;
        }

        return false;
    }

    public int RunInteractiveSplit(string branch, DiffSummary diff, bool preview)
    {
        var remainingFiles = new List<string>(diff.ChangedFiles);

        Console.WriteLine($"Staged changes exceed configured threshold ({remainingFiles.Count} files).");
        Console.WriteLine("Let's split this into smaller commits.\n");

        int commitCount = 0;

        while (remainingFiles.Count > 0)
        {
            Console.WriteLine("Remaining files:");
            for (int i = 0; i < remainingFiles.Count; i++)
                Console.WriteLine($"  [{i + 1}] {remainingFiles[i]}");

            Console.WriteLine();
            Console.Write("Enter file numbers for this commit (e.g. 1,3,4) or 'all' for remaining: ");
            var input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
                continue;

            List<string> selectedFiles;

            if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                selectedFiles = new List<string>(remainingFiles);
            }
            else
            {
                var indices = ParseIndices(input, remainingFiles.Count);
                if (indices.Count == 0)
                {
                    Console.WriteLine("Invalid selection. Try again.\n");
                    continue;
                }
                selectedFiles = indices.Select(i => remainingFiles[i]).ToList();
            }

            // Unstage everything, then stage only selected files
            _git.UnstageAll();
            _git.StageFiles(selectedFiles);

            var selectedDiff = _git.GetStagedDiff();
            var message = _analyzer.Analyze(branch, selectedDiff);
            var finalMessage = TruncateMessage(message);

            if (preview)
            {
                Console.WriteLine($"[preview] {finalMessage}\n");
            }
            else
            {
                var edited = PromptEditor.Edit("Commit message (enter to accept): ", finalMessage);
                if (edited is null)
                {
                    Console.WriteLine("Skipped.");
                    foreach (var file in selectedFiles)
                        remainingFiles.Remove(file);
                    continue;
                }
                _git.Commit(edited);
                Kommit.Commands.UndoCommand.RecordCommand("commit");
                Console.WriteLine(edited);
                commitCount++;
            }

            foreach (var file in selectedFiles)
                remainingFiles.Remove(file);

            if (remainingFiles.Count > 0)
                Console.WriteLine();
        }

        // Re-stage any remaining files that weren't committed (in dry-run mode)
        if (preview && diff.ChangedFiles.Count > 0)
        {
            _git.StageFiles(diff.ChangedFiles);
        }

        return commitCount;
    }

    private string TruncateMessage(CommitMessage message)
    {
        var full = message.ToString();
        if (_config.MaxCommitLength > 0 && full.Length > _config.MaxCommitLength)
        {
            return full[..(_config.MaxCommitLength - 3)] + "...";
        }
        return full;
    }

    private static List<int> ParseIndices(string input, int max)
    {
        var result = new List<int>();
        foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var num) && num >= 1 && num <= max)
                result.Add(num - 1);
            else
                return new List<int>();
        }
        return result.Distinct().ToList();
    }
}
