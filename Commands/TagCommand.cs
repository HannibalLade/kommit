using System.Text.RegularExpressions;
using Kommit.Git;

namespace Kommit.Commands;

public static class TagCommand
{
    public static int Run(string[] args, GitService git, bool preview = false)
    {
        var bump = "minor";
        if (args.Contains("-major")) bump = "major";
        else if (args.Contains("-patch")) bump = "patch";

        var latest = git.GetLatestTag();
        Version current;

        if (latest is not null && latest.StartsWith("v") &&
            Version.TryParse(latest[1..], out var parsed))
        {
            current = parsed;
        }
        else
        {
            current = new Version(0, 0, 0);
        }

        var next = bump switch
        {
            "major" => new Version(current.Major + 1, 0, 0),
            "patch" => new Version(current.Major, current.Minor, current.Build + 1),
            _ => new Version(current.Major, current.Minor + 1, 0),
        };

        var versionString = $"{next.Major}.{next.Minor}.{next.Build}";
        var tag = $"v{versionString}";

        var projectFile = DetectProjectFile();

        if (preview)
        {
            Console.WriteLine($"[preview] Would bump {bump} version: {latest ?? "v0.0.0"} -> {tag}");
            if (projectFile is not null)
                Console.WriteLine($"[preview] Update version in {Path.GetFileName(projectFile)}");
            else
                Console.WriteLine("[preview] No project file found — tag only, no version file update");
            Console.WriteLine($"[preview] Create tag {tag}");
            Console.WriteLine($"[preview] Push tag to origin");
            return 0;
        }

        Console.WriteLine($"Bumping {bump} version: {latest ?? "v0.0.0"} -> {tag}");

        if (projectFile is not null)
        {
            Console.WriteLine($"Updating version in {Path.GetFileName(projectFile)}...");
            UpdateVersionFile(projectFile, versionString);

            Console.WriteLine("Staging changes...");
            git.StageAll();

            Console.WriteLine($"Committing: chore: bump version to {tag}");
            git.Commit($"chore: bump version to {tag}");

            Console.WriteLine("Pushing commit...");
            git.Push("simple");
        }

        Console.WriteLine($"Creating tag {tag}...");
        git.CreateTag(tag);

        Console.WriteLine($"Pushing tag {tag}...");
        git.PushTag(tag);

        UndoCommand.RecordCommand("tag", tag);
        Console.WriteLine($"Done. Released {tag}");

        return 0;
    }

    private static string? DetectProjectFile()
    {
        // C# / .NET
        var csproj = Directory.GetFiles(".", "*.csproj", SearchOption.TopDirectoryOnly);
        if (csproj.Length > 0) return csproj[0];

        // Node.js / JavaScript / TypeScript
        if (File.Exists("package.json")) return "package.json";

        // Python
        if (File.Exists("pyproject.toml")) return "pyproject.toml";
        if (File.Exists("setup.py")) return "setup.py";
        if (File.Exists("setup.cfg")) return "setup.cfg";

        // Rust
        if (File.Exists("Cargo.toml")) return "Cargo.toml";

        // Go
        if (File.Exists("go.mod")) return "go.mod";

        // Java / Kotlin (Gradle)
        if (File.Exists("build.gradle")) return "build.gradle";
        if (File.Exists("build.gradle.kts")) return "build.gradle.kts";

        // Java (Maven)
        if (File.Exists("pom.xml")) return "pom.xml";

        return null;
    }

    private static void UpdateVersionFile(string filePath, string version)
    {
        var fileName = Path.GetFileName(filePath);
        var content = File.ReadAllText(filePath);

        var updated = fileName switch
        {
            _ when fileName.EndsWith(".csproj") =>
                Regex.Replace(content, @"<Version>[^<]*</Version>", $"<Version>{version}</Version>"),

            "package.json" =>
                Regex.Replace(content, @"""version"":\s*""[^""]*""", $"\"version\": \"{version}\""),

            "pyproject.toml" =>
                Regex.Replace(content, @"version\s*=\s*""[^""]*""", $"version = \"{version}\""),

            "setup.py" =>
                Regex.Replace(content, @"version\s*=\s*['""][^'""]*['""]", $"version=\"{version}\""),

            "setup.cfg" =>
                Regex.Replace(content, @"version\s*=\s*\S+", $"version = {version}"),

            "Cargo.toml" =>
                ReplaceFirstOccurrence(content, @"version\s*=\s*""[^""]*""", $"version = \"{version}\""),

            "go.mod" => content, // Go uses tags directly, no version field to update

            "build.gradle" =>
                Regex.Replace(content, @"version\s*=\s*['""][^'""]*['""]", $"version = '{version}'"),

            "build.gradle.kts" =>
                Regex.Replace(content, @"version\s*=\s*""[^""]*""", $"version = \"{version}\""),

            "pom.xml" =>
                Regex.Replace(content, @"<version>[^<]*</version>", $"<version>{version}</version>",
                    RegexOptions.None, TimeSpan.FromSeconds(1)),

            _ => content,
        };

        if (updated != content)
            File.WriteAllText(filePath, updated);
    }

    private static string ReplaceFirstOccurrence(string content, string pattern, string replacement)
    {
        var match = Regex.Match(content, pattern);
        if (!match.Success) return content;
        return string.Concat(content.AsSpan(0, match.Index), replacement, content.AsSpan(match.Index + match.Length));
    }
}
