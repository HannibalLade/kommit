namespace Kommit.Models;

public enum FileChangeKind { Added, Modified, Deleted, Renamed }

public record FileChange(string Path, FileChangeKind Kind);

public record DiffSummary(
    IReadOnlyList<string> ChangedFiles,
    int LinesAdded,
    int LinesDeleted,
    string RawDiff
)
{
    public IReadOnlyList<FileChange> FileChanges { get; init; } = [];
}
