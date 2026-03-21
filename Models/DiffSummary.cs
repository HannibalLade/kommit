namespace Kommit.Models;

public record DiffSummary(
    IReadOnlyList<string> ChangedFiles,
    int LinesAdded,
    int LinesDeleted,
    string RawDiff
);
