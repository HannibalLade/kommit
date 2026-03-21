using Kommit.Git;

namespace Kommit.Commands;

public static class TagCommand
{
    public static int Run(string[] args, GitService git)
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

        UpdateCsprojVersion(versionString);

        git.StageAll();
        git.Commit($"chore: bump version to {tag}");
        git.CreateTag(tag);
        git.Push("simple");
        git.PushTag(tag);
        Console.WriteLine(tag);

        return 0;
    }

    private static void UpdateCsprojVersion(string version)
    {
        var csprojFiles = Directory.GetFiles(".", "*.csproj", SearchOption.TopDirectoryOnly);
        if (csprojFiles.Length == 0) return;

        var csproj = csprojFiles[0];
        var content = File.ReadAllText(csproj);
        var updated = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"<Version>[^<]*</Version>",
            $"<Version>{version}</Version>");
        File.WriteAllText(csproj, updated);
    }
}
