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

    private static readonly Dictionary<string, string[]> TypeAllowedVerbs = new()
    {
        ["feat"] = ["add", "implement", "introduce", "create"],
        ["fix"] = ["fix", "resolve", "handle", "add", "correct"],
        ["refactor"] = ["rename", "replace", "simplify", "clean up", "remove", "restructure", "extract", "move", "refactor", "update"],
        ["docs"] = ["add", "update", "remove", "document"],
        ["perf"] = ["improve", "optimize", "cache", "update"],
        ["test"] = ["add", "update", "remove", "add tests for"],
        ["style"] = ["update", "fix", "adjust"],
        ["chore"] = ["update", "add", "remove", "clean up", "simplify", "implement"],
        ["build"] = ["update", "add", "remove", "configure"],
        ["ci"] = ["update", "add", "remove", "configure"],
        ["revert"] = ["revert"],
    };

    private readonly DiffParser _parser = new();

    private enum TypeSource { Branch, Symbols, Signals, Files, Heuristic }

    public CommitMessage Analyze(string branchName, DiffSummary diff)
    {
        var parsed = _parser.Parse(diff.RawDiff);
        var (type, source) = InferType(branchName, diff, parsed);
        var scope = InferScope(diff.ChangedFiles);
        var description = InferDescription(type, source, diff, parsed);

        return new CommitMessage(type, scope, description);
    }

    private (string type, TypeSource source) InferType(string branchName, DiffSummary diff, ParsedDiff parsed)
    {
        // 1. Branch prefix is highest priority
        var typeFromBranch = GetTypeFromBranch(branchName);
        if (typeFromBranch is not null)
            return (typeFromBranch, TypeSource.Branch);

        // 2. File change kinds (all new files → feat, all deleted → refactor)
        if (diff.FileChanges.Count > 0)
        {
            if (diff.FileChanges.All(f => f.Kind == FileChangeKind.Added))
                return ("feat", TypeSource.Files);
            if (diff.FileChanges.All(f => f.Kind == FileChangeKind.Deleted))
                return ("refactor", TypeSource.Files);
        }

        // 3. Symbol-based inference (new methods/classes take priority)
        var typeFromSymbols = GetTypeFromSymbols(parsed, diff);
        if (typeFromSymbols is not null)
            return (typeFromSymbols, TypeSource.Symbols);

        // 4. Signal-based inference from diff content
        var typeFromSignals = GetTypeFromSignals(parsed, diff);
        if (typeFromSignals is not null)
            return (typeFromSignals, TypeSource.Signals);

        // 5. File-type heuristics
        var typeFromFiles = GetTypeFromFiles(diff.ChangedFiles);
        if (typeFromFiles is not null)
            return (typeFromFiles, TypeSource.Files);

        // 6. All test files
        if (diff.ChangedFiles.All(f => IsTestFile(f)))
            return ("test", TypeSource.Files);

        // 7. Line ratio fallback
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0)
            return ("refactor", TypeSource.Heuristic);

        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0)
            return ("feat", TypeSource.Heuristic);

        // 8. Default
        return ("chore", TypeSource.Heuristic);
    }

    private static string? GetTypeFromSignals(ParsedDiff parsed, DiffSummary diff)
    {
        var signals = parsed.Signals;

        if (signals.HasFlag(DiffSignals.Tests))
            return "test";

        if (signals.HasFlag(DiffSignals.TodoFixed))
            return "fix";

        if (signals.HasFlag(DiffSignals.Security))
            return "fix";

        if (signals.HasFlag(DiffSignals.ErrorHandling) || signals.HasFlag(DiffSignals.NullChecks))
        {
            if (diff.LinesAdded > diff.LinesDeleted)
                return "fix";
        }

        if (signals.HasFlag(DiffSignals.Performance))
            return "perf";

        if (signals.HasFlag(DiffSignals.Documentation) && !signals.HasFlag(DiffSignals.ErrorHandling))
            return "docs";

        if (signals.HasFlag(DiffSignals.Styling))
            return "style";

        if (signals.HasFlag(DiffSignals.DependencyChange))
            return "build";

        if (signals.HasFlag(DiffSignals.ConfigChange))
            return "chore";

        return null;
    }

    private static string? GetTypeFromSymbols(ParsedDiff parsed, DiffSummary diff)
    {
        // Renames detected → refactor
        if (parsed.RenamedSymbols.Count > 0 && parsed.AddedSymbols.Count == 0)
            return "refactor";

        // Only removing symbols → refactor
        if (parsed.RemovedSymbols.Count > 0 && parsed.AddedSymbols.Count == 0)
            return "refactor";

        // Adding new symbols with no removals → feat
        if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count == 0)
            return "feat";

        // Both adding and removing
        if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count > 0)
        {
            if (parsed.RenamedSymbols.Count > 0 && parsed.AddedSymbols.Count <= parsed.RenamedSymbols.Count)
                return "refactor";
            if (parsed.AddedSymbols.Count > parsed.RemovedSymbols.Count)
                return "feat";
            return "refactor";
        }

        return null;
    }

    private static string? GetTypeFromBranch(string branchName)
    {
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

    private static string InferDescription(string type, TypeSource source, DiffSummary diff, ParsedDiff parsed)
    {
        // For multi-file changes (>3 files), skip symbol-based descriptions — they produce noise
        var useSymbols = diff.ChangedFiles.Count <= 3;

        // Symbol-based descriptions for small changesets
        if (useSymbols)
        {
            if (parsed.RenamedSymbols.Count > 0)
            {
                if (parsed.RenamedSymbols.Count == 1)
                    return $"rename {parsed.RenamedSymbols[0]}";
                return $"rename {parsed.RenamedSymbols.Count} symbols";
            }

            if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count == 0)
            {
                var action = type == "test" ? "add tests for" : "add";
                return $"{action} {DescribeSymbols(parsed.AddedTypes, parsed.AddedMembers)}";
            }

            if (parsed.RemovedSymbols.Count > 0 && parsed.AddedSymbols.Count == 0)
            {
                return $"remove {DescribeSymbols(parsed.RemovedTypes, parsed.RemovedMembers)}";
            }

            if (parsed.AddedSymbols.Count > 0 && parsed.RemovedSymbols.Count > 0)
            {
                if (parsed.AddedSymbols.Count == 1 && parsed.RemovedSymbols.Count == 1)
                    return $"replace {parsed.RemovedSymbols[0]} with {parsed.AddedSymbols[0]}";
                if (parsed.AddedTypes.Count > 0)
                    return $"add {DescribeSymbols(parsed.AddedTypes, [])}";
                // Fall through to file-based instead of "refactor N symbols"
            }
        }

        // Signal-based descriptions when signals determined the type
        if (source == TypeSource.Signals)
        {
            if (parsed.Signals.HasFlag(DiffSignals.ErrorHandling))
                return DescribeWithFiles("add error handling", diff.ChangedFiles);

            if (parsed.Signals.HasFlag(DiffSignals.NullChecks))
                return DescribeWithFiles("add null safety checks", diff.ChangedFiles);

            if (parsed.Signals.HasFlag(DiffSignals.TodoFixed))
                return DescribeWithFiles("resolve TODO items", diff.ChangedFiles);

            if (parsed.Signals.HasFlag(DiffSignals.Performance))
                return DescribeWithFiles("improve performance", diff.ChangedFiles);

            if (parsed.Signals.HasFlag(DiffSignals.Security))
                return DescribeWithFiles("improve security", diff.ChangedFiles);

            if (parsed.Signals.HasFlag(DiffSignals.Logging))
                return DescribeWithFiles("update logging", diff.ChangedFiles);
        }

        // File-based description
        var description = DescribeFromFiles(diff);

        // Consistency check: ensure the description verb doesn't contradict the type
        return EnsureConsistency(type, description, diff);
    }

    private static string EnsureConsistency(string type, string description, DiffSummary diff)
    {
        if (!TypeAllowedVerbs.TryGetValue(type, out var allowed))
            return description;

        // Check if description starts with an allowed verb for this type
        if (allowed.Any(v => description.StartsWith(v, StringComparison.OrdinalIgnoreCase)))
            return description;

        // Rewrite with a type-appropriate verb
        var verb = GetVerbForType(type, diff);

        // Try to keep the object of the description (everything after the first verb)
        var firstSpace = description.IndexOf(' ');
        if (firstSpace > 0)
        {
            var rest = description[firstSpace..];
            return $"{verb}{rest}";
        }

        return $"{verb} {description}";
    }

    private static string GetVerbForType(string type, DiffSummary diff)
    {
        return type switch
        {
            "feat" => diff.LinesAdded > diff.LinesDeleted * 3 ? "implement" : "add",
            "fix" => "fix",
            "refactor" => diff.LinesDeleted > diff.LinesAdded ? "simplify" : "restructure",
            "docs" => "update",
            "perf" => "improve",
            "test" => "add tests for",
            "style" => "update",
            "chore" => "update",
            "build" => "update",
            "ci" => "update",
            _ => "update",
        };
    }

    private static string DescribeWithFiles(string action, IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            var purpose = InferFilePurpose(files[0]);
            if (purpose is not null)
                return $"{action} in {purpose}";
            return $"{action} in {Path.GetFileName(files[0])}";
        }
        if (files.Count <= 3)
        {
            var purposes = files.Select(InferFilePurpose).Where(p => p is not null).Distinct().ToList();
            if (purposes.Count == 1)
                return $"{action} in {purposes[0]}";
        }
        return action;
    }

    private static string DescribeFromFiles(DiffSummary diff)
    {
        var files = diff.ChangedFiles;
        var action = GetActionFromFileChanges(diff);

        if (files.Count == 1)
        {
            var fileName = Path.GetFileName(files[0]);
            var purpose = InferFilePurpose(files[0]);
            return purpose is not null ? $"{action} {purpose}" : $"{action} {fileName}";
        }

        // Check if all files share a common purpose/area
        var purposes = files.Select(InferFilePurpose).Where(p => p is not null).Distinct().ToList();
        if (purposes.Count == 1)
            return $"{action} {purposes[0]}";

        // Find deepest common directory
        var commonDir = GetDeepestCommonDirectory(files);
        if (commonDir is not null)
            return $"{action} {commonDir}";

        if (files.Count <= 3)
        {
            var fileNames = string.Join(", ", files.Select(Path.GetFileName));
            return $"{action} {fileNames}";
        }

        // Last resort: use top-level directory grouping for a better description
        var topDirs = files
            .Select(f => f.Replace('\\', '/'))
            .Select(f => { var i = f.IndexOf('/'); return i > 0 ? f[..i] : Path.GetFileName(f); })
            .Distinct()
            .ToList();

        if (topDirs.Count == 1)
            return $"{action} {topDirs[0]}";

        if (topDirs.Count <= 3)
            return $"{action} {string.Join(", ", topDirs)}";

        return $"{action} {files.Count} files across {topDirs.Count} modules";
    }

    private static string GetActionFromFileChanges(DiffSummary diff)
    {
        // Use FileChangeKind when available for more accurate verbs
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
                    _ => GetAction(diff),
                };
            }

            // Mostly new files
            var addedCount = diff.FileChanges.Count(f => f.Kind == FileChangeKind.Added);
            if (addedCount > diff.FileChanges.Count / 2)
                return "add";

            var deletedCount = diff.FileChanges.Count(f => f.Kind == FileChangeKind.Deleted);
            if (deletedCount > diff.FileChanges.Count / 2)
                return "remove";
        }

        return GetAction(diff);
    }

    private static string? GetDeepestCommonDirectory(IReadOnlyList<string> files)
    {
        if (files.Count < 2) return null;

        var paths = files.Select(f => f.Replace('\\', '/')).ToList();

        // Split each path into segments
        var segments = paths
            .Select(p => p.Split('/'))
            .ToList();

        // Find common prefix segments
        var minLen = segments.Min(s => s.Length);
        var commonDepth = 0;

        for (var i = 0; i < minLen - 1; i++) // -1 to exclude filename
        {
            var seg = segments[0][i];
            if (segments.All(s => s[i] == seg))
                commonDepth = i + 1;
            else
                break;
        }

        if (commonDepth == 0) return null;

        var commonPath = string.Join("/", segments[0].Take(commonDepth));

        // Use the deepest directory name for readability
        var dirName = segments[0][commonDepth - 1];
        return dirName;
    }

    private static string? InferFilePurpose(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var dir = path.Replace('\\', '/').ToLowerInvariant();

        if (name.Contains("controller") || dir.Contains("/controllers/")) return "API controller";
        if (name.Contains("middleware")) return "middleware";
        if (name.Contains("service") || dir.Contains("/services/")) return "service layer";
        if (name.Contains("repository") || name.Contains("repo") || dir.Contains("/repositories/")) return "data access layer";
        if (name.Contains("model") || dir.Contains("/models/") || dir.Contains("/entities/")) return "data model";
        if (name.Contains("migration") || dir.Contains("/migrations/")) return "database migration";
        if (name.Contains("component") || dir.Contains("/components/")) return "UI component";
        if (name.Contains("route") || name.Contains("router") || dir.Contains("/routes/")) return "routing";
        if (name.Contains("hook") || dir.Contains("/hooks/")) return "React hook";
        if (name.Contains("util") || name.Contains("helper") || dir.Contains("/utils/") || dir.Contains("/helpers/")) return "utility functions";
        if (name.Contains("config") || name.Contains("setting")) return "configuration";
        if (name == "readme" || name == "changelog" || name == "contributing") return "documentation";
        if (name == "dockerfile" || name.Contains("docker-compose")) return "Docker configuration";
        if (name == "makefile" || name == "rakefile" || name == "taskfile") return "build scripts";

        return null;
    }

    private static string DescribeSymbols(List<string> types, List<string> members)
    {
        if (types.Count > 0)
        {
            if (types.Count == 1 && members.Count == 0)
                return types[0];
            if (types.Count == 1)
                return types[0];
            if (types.Count == 2)
                return $"{types[0]} and {types[1]}";
            return $"{types[0]} and {types.Count - 1} others";
        }

        if (members.Count == 1)
            return members[0];
        if (members.Count == 2)
            return $"{members[0]} and {members[1]}";
        if (members.Count <= 4)
            return string.Join(", ", members);
        return $"{members[0]} and {members.Count - 1} others";
    }

    private static string GetAction(DiffSummary diff)
    {
        if (diff.LinesAdded > 0 && diff.LinesDeleted == 0) return "add";
        if (diff.LinesAdded == 0 && diff.LinesDeleted > 0) return "remove";
        if (diff.LinesDeleted > diff.LinesAdded * 2) return "simplify";
        if (diff.LinesAdded > diff.LinesDeleted * 3) return "implement";
        if (diff.LinesDeleted > diff.LinesAdded) return "clean up";
        return "update";
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
