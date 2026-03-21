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

    public CommitMessage Analyze(string branchName, DiffSummary diff)
    {
        var type = InferType(branchName, diff);
        var scope = InferScope(diff.ChangedFiles);
        var description = InferDescription(type, diff);

        return new CommitMessage(type, scope, description);
    }

    private string InferType(string branchName, DiffSummary diff)
    {
        // 1. Try branch prefix
        var typeFromBranch = GetTypeFromBranch(branchName);
        if (typeFromBranch is not null)
            return typeFromBranch;

        // 2. Try file-type heuristics
        var typeFromFiles = GetTypeFromFiles(diff.ChangedFiles);
        if (typeFromFiles is not null)
            return typeFromFiles;

        // 3. Check if test files
        if (diff.ChangedFiles.All(f => IsTestFile(f)))
            return "test";

        // 4. Check add/delete ratio
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0)
            return "refactor";

        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0)
            return "feat";

        // 5. Default
        return "chore";
    }

    private static string? GetTypeFromBranch(string branchName)
    {
        // Match patterns like "feat/something", "fix/issue-123", "feature/add-login"
        var slashIndex = branchName.IndexOf('/');
        if (slashIndex <= 0) return null;

        var prefix = branchName[..slashIndex];
        return BranchPrefixMap.GetValueOrDefault(prefix);
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

        // If all files share the same mapped type, use it
        var mappedTypes = extensions
            .Select(e => ExtensionTypeMap.GetValueOrDefault(e))
            .Where(t => t is not null)
            .Distinct()
            .ToList();

        if (mappedTypes.Count == 1 && mappedTypes[0] is not null && files.All(f => ExtensionTypeMap.ContainsKey(Path.GetExtension(f))))
            return mappedTypes[0];

        // CI-specific files
        if (files.All(f => IsCiFile(f)))
            return "ci";

        return null;
    }

    private static string? InferScope(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return null;

        // If all files are in the same top-level directory, use that as scope
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

    private static string InferDescription(string type, DiffSummary diff)
    {
        var files = diff.ChangedFiles;

        if (files.Count == 1)
        {
            var fileName = Path.GetFileName(files[0]);
            var action = GetAction(diff);
            return $"{action} {fileName}";
        }

        if (files.Count <= 3)
        {
            var fileNames = string.Join(", ", files.Select(Path.GetFileName));
            var action = GetAction(diff);
            return $"{action} {fileNames}";
        }

        var commonExt = files
            .Select(Path.GetExtension)
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        var action2 = GetAction(diff);
        return $"{action2} {files.Count} files";
    }

    private static string GetAction(DiffSummary diff)
    {
        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0) return "add";
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0) return "remove";
        if (diff.LinesDeleted > diff.LinesAdded) return "simplify";
        return "update";
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
