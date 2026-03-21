using System.Diagnostics;
using Kommit.Models;

namespace Kommit.Git;

public class GitService
{
    public string GetBranchName()
    {
        return RunGit("rev-parse --abbrev-ref HEAD").Trim();
    }

    public DiffSummary GetStagedDiff()
    {
        var diff = RunGit("diff --cached");
        var files = RunGit("diff --cached --name-only")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var stat = RunGit("diff --cached --numstat");
        int added = 0, deleted = 0;

        foreach (var line in stat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out var a)) added += a;
                if (int.TryParse(parts[1], out var d)) deleted += d;
            }
        }

        return new DiffSummary(files, added, deleted, diff);
    }

    public bool HasStagedChanges()
    {
        var output = RunGit("diff --cached --name-only");
        return !string.IsNullOrWhiteSpace(output);
    }

    public void Commit(string message)
    {
        RunGit($"commit -m \"{message.Replace("\"", "\\\"")}\"");
    }

    private static string RunGit(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}
