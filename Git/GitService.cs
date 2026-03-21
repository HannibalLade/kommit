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

    public void Pull(string strategy = "rebase")
    {
        var args = strategy switch
        {
            "rebase" => "pull --rebase",
            "merge" => "pull",
            _ => "pull --rebase"
        };
        RunGit(args);
    }

    public void Push(string strategy = "simple")
    {
        var args = strategy switch
        {
            "set-upstream" => HasUpstream()
                ? "push"
                : $"push --set-upstream origin {GetBranchName()}",
            "force-with-lease" => "push --force-with-lease",
            _ => "push"
        };
        RunGit(args);
    }

    public void StageFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
            RunGit($"add \"{file}\"");
    }

    public void StageAll()
    {
        RunGit("add -A");
    }

    public void UnstageAll()
    {
        RunGit("reset HEAD");
    }

    public int GetStagedLineCount()
    {
        var stat = RunGit("diff --cached --numstat");
        int total = 0;
        foreach (var line in stat.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0], out var a)) total += a;
                if (int.TryParse(parts[1], out var d)) total += d;
            }
        }
        return total;
    }

    public string? GetLatestTag()
    {
        var output = RunGit("tag -l v* --sort=-v:refname").Trim();
        if (string.IsNullOrEmpty(output))
            return null;
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public List<string> GetConflictedFiles()
    {
        var output = RunGit("diff --name-only --diff-filter=U");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public bool IsMergeInProgress()
    {
        var output = RunGit("rev-parse --git-dir").Trim();
        return File.Exists(Path.Combine(output, "MERGE_HEAD"));
    }

    public void AcceptIncoming(IEnumerable<string> files)
    {
        foreach (var file in files)
            RunGit($"checkout --theirs \"{file}\"");
    }

    public void AcceptCurrent(IEnumerable<string> files)
    {
        foreach (var file in files)
            RunGit($"checkout --ours \"{file}\"");
    }

    public void CreateTag(string tag)
    {
        RunGit($"tag {tag}");
    }

    public void PushTag(string tag)
    {
        RunGit($"push origin {tag}");
    }

    private bool HasUpstream()
    {
        var result = RunGit("rev-parse --abbrev-ref --symbolic-full-name @{u}");
        return !string.IsNullOrWhiteSpace(result);
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
