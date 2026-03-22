using Kommit.Models;

namespace Kommit.Analysis;

public class CommitAnalyzer
{
    private static readonly Dictionary<string, string> BranchPrefixMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["feat"] = "feat",
        ["feature"] = "feat",
        ["fix"] = "fix",
        ["bugfix"] = "fix",
        ["hotfix"] = "fix",
        ["docs"] = "docs",
        ["doc"] = "docs",
        ["style"] = "style",
        ["refactor"] = "refactor",
        ["perf"] = "perf",
        ["test"] = "test",
        ["tests"] = "test",
        ["chore"] = "chore",
        ["ci"] = "ci",
        ["build"] = "build",
        ["revert"] = "revert"
    };

    private static readonly Dictionary<string, string> ExtensionTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "docs",
        [".txt"] = "docs",
        [".yml"] = "ci",
        [".yaml"] = "ci",
        [".dockerfile"] = "build",
        [".dockerignore"] = "build",
        [".csproj"] = "build",
        [".sln"] = "build",
        [".props"] = "build",
        [".targets"] = "build",
    };

    private readonly DiffParser _parser = new();

    public CommitMessage Analyze(string branchName, DiffSummary diff)
    {
        var parsed = _parser.Parse(diff.RawDiff);
        var type = InferType(branchName, diff, parsed);
        var scope = InferScope(diff.ChangedFiles);
        var description = InferDescription(type, diff);

        return new CommitMessage(type, scope, description);
    }

    private string InferType(string branchName, DiffSummary diff, ParsedDiff parsed)
    {
        // 1. Branch prefix
        var typeFromBranch = GetTypeFromBranch(branchName);
        if (typeFromBranch is not null)
            return typeFromBranch;

        // 2. File change kinds
        if (diff.FileChanges.Count > 0)
        {
            if (diff.FileChanges.All(f => f.Kind == FileChangeKind.Added))
                return "feat";
            if (diff.FileChanges.All(f => f.Kind == FileChangeKind.Deleted))
                return "refactor";
        }

        // 3. Symbol-based
        if (parsed.RenamedSymbols.Count > 0 && parsed.AddedSymbols.Count == 0)
            return "refactor";
        if (parsed.RemovedSymbols.Count > 0 && parsed.AddedSymbols.Count == 0)
            return "refactor";
        if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count == 0)
            return "feat";
        if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count > 0)
        {
            if (parsed.RenamedSymbols.Count > 0 && parsed.AddedSymbols.Count <= parsed.RenamedSymbols.Count)
                return "refactor";
            if (parsed.AddedSymbols.Count > parsed.RemovedSymbols.Count)
                return "feat";
            return "refactor";
        }

        // 4. Signal-based
        var signals = parsed.Signals;
        if (signals.HasFlag(DiffSignals.Tests)) return "test";
        if (signals.HasFlag(DiffSignals.TodoFixed)) return "fix";
        if (signals.HasFlag(DiffSignals.Security)) return "fix";
        if ((signals.HasFlag(DiffSignals.ErrorHandling) || signals.HasFlag(DiffSignals.NullChecks)) && diff.LinesAdded > diff.LinesDeleted)
            return "fix";
        if (signals.HasFlag(DiffSignals.Performance)) return "perf";
        if (signals.HasFlag(DiffSignals.Documentation) && !signals.HasFlag(DiffSignals.ErrorHandling)) return "docs";
        if (signals.HasFlag(DiffSignals.Styling)) return "style";
        if (signals.HasFlag(DiffSignals.DependencyChange)) return "build";
        if (signals.HasFlag(DiffSignals.ConfigChange)) return "chore";

        // 5. File-type heuristics
        var typeFromFiles = GetTypeFromFiles(diff.ChangedFiles);
        if (typeFromFiles is not null) return typeFromFiles;

        if (diff.ChangedFiles.All(f => IsTestFile(f)))
            return "test";

        // 6. Line ratio
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0) return "refactor";
        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0) return "feat";

        return "chore";
    }

    private static string InferDescription(string type, DiffSummary diff)
    {
        var files = diff.ChangedFiles;
        var action = GetAction(type, diff);

        if (files.Count == 1)
            return $"{action} {Path.GetFileName(files[0])}";

        // All files in same directory
        var commonDir = GetCommonDirectory(files);
        if (commonDir is not null)
            return $"{action} {commonDir}";

        if (files.Count <= 3)
            return $"{action} {string.Join(", ", files.Select(Path.GetFileName))}";

        // Group by top-level directories
        var dirs = files
            .Select(f => f.Replace('\\', '/'))
            .Select(f => { var i = f.IndexOf('/'); return i > 0 ? f[..i] : Path.GetFileName(f); })
            .Distinct()
            .ToList();

        if (dirs.Count == 1)
            return $"{action} {dirs[0]}";
        if (dirs.Count <= 3)
            return $"{action} {string.Join(", ", dirs)}";

        return $"{action} {files.Count} files";
    }

    private static string GetAction(string type, DiffSummary diff)
    {
        // Use file change kinds when available
        if (diff.FileChanges.Count > 0)
        {
            var kinds = diff.FileChanges.Select(f => f.Kind).Distinct().ToList();
            if (kinds.Count == 1)
            {
                return kinds[0] switch
                {
                    FileChangeKind.Added => "add",
                    FileChangeKind.Deleted => "remove",
                    FileChangeKind.Renamed => "rename",
                    _ => "update",
                };
            }
        }

        // Line ratio fallback
        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0) return "add";
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0) return "remove";

        return "update";
    }

    private static string? GetCommonDirectory(IReadOnlyList<string> files)
    {
        if (files.Count < 2) return null;

        var segments = files
            .Select(f => f.Replace('\\', '/').Split('/'))
            .ToList();

        var minLen = segments.Min(s => s.Length);
        var commonDepth = 0;

        for (var i = 0; i < minLen - 1; i++)
        {
            if (segments.All(s => s[i] == segments[0][i]))
                commonDepth = i + 1;
            else
                break;
        }

        if (commonDepth == 0) return null;
        return segments[0][commonDepth - 1];
    }

    private static string? GetTypeFromBranch(string branchName)
    {
        var slashIndex = branchName.IndexOf('/');
        if (slashIndex <= 0) return null;
        return BranchPrefixMap.GetValueOrDefault(branchName[..slashIndex]);
    }

    private static string? GetTypeFromFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return null;

        var extensions = files
            .Select(f => Path.GetExtension(f))
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct()
            .ToList();

        if (extensions.Count == 0) return null;

        var mappedTypes = extensions
            .Select(e => ExtensionTypeMap.GetValueOrDefault(e))
            .Where(t => t is not null)
            .Distinct()
            .ToList();

        if (mappedTypes.Count == 1 && mappedTypes[0] is not null && files.All(f => ExtensionTypeMap.ContainsKey(Path.GetExtension(f))))
            return mappedTypes[0];

        if (files.All(f => IsCiFile(f)))
            return "ci";

        return null;
    }

    private static string? InferScope(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return null;

        var directories = files
            .Select(f => f.Replace('\\', '/'))
            .Select(f =>
            {
                var slashIndex = f.IndexOf('/');
                return slashIndex > 0 ? f[..slashIndex] : null;
            })
            .Where(d => d is not null)
            .Distinct()
            .ToList();

        if (directories.Count == 1)
            return directories[0];

        return null;
    }

    private static bool IsTestFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".spec", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Test", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/test/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCiFile(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains(".github/")
            || normalized.Contains(".gitlab-ci")
            || normalized.Contains("jenkinsfile")
            || normalized.Contains(".circleci/")
            || normalized.Contains("azure-pipelines")
            || normalized.EndsWith(".yml")
            || normalized.EndsWith(".yaml");
    }
}
