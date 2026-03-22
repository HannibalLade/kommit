using System.Diagnostics;
using Kommit.Models;

namespace Kommit.Git;

public class GitException : Exception
{
    public GitException(string message) : base(message) { }
}

public class GitService
{
    public static bool IsGitInstalled()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsGitRepo()
    {
        var (output, _, exitCode) = RunGitRaw("rev-parse --is-inside-work-tree");
        return exitCode == 0 && output.Trim() == "true";
    }

    public bool HasUnstagedChanges()
    {
        var (_, _, exitCode) = RunGitRaw("diff --quiet");
        return exitCode != 0;
    }

    public void Stash()
    {
        RunGit("stash");
    }

    public void StashPop()
    {
        RunGit("stash pop");
    }

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
        var (output, _, _) = RunGitRaw("diff --cached --name-only");
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
        var needsUpstream = !HasUpstream();
        var args = strategy switch
        {
            "force-with-lease" => needsUpstream
                ? $"push --force-with-lease --set-upstream origin {GetBranchName()}"
                : "push --force-with-lease",
            _ => needsUpstream
                ? $"push --set-upstream origin {GetBranchName()}"
                : "push"
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
        var (output, _, _) = RunGitRaw("tag -l v* --sort=-v:refname");
        output = output.Trim();
        if (string.IsNullOrEmpty(output))
            return null;
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public List<string> GetConflictedFiles()
    {
        var (output, _, _) = RunGitRaw("diff --name-only --diff-filter=U");
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

    public bool StartMerge(string branch)
    {
        var (_, _, exitCode) = RunGitRaw($"merge {branch}");
        return exitCode != 0;
    }

    public void Fetch()
    {
        RunGit("fetch origin");
    }

    public void AbortMerge()
    {
        RunGitRaw("merge --abort");
    }

    public string GetRemoteUrl()
    {
        return RunGit("remote get-url origin").Trim();
    }

    public void PushBranch()
    {
        var branch = GetBranchName();
        if (!HasUpstream())
            RunGit($"push --set-upstream origin {branch}");
        else
            RunGit("push");
    }

    public string GetLastCommitMessage()
    {
        return RunGit("log -1 --pretty=%s").Trim();
    }

    public void UndoLastCommit()
    {
        RunGit("reset --soft HEAD~1");
    }

    public void CreateTag(string tag)
    {
        RunGit($"tag {tag}");
    }

    public void DeleteTag(string tag)
    {
        RunGitRaw($"tag -d {tag}");
    }

    public void PushTag(string tag)
    {
        RunGit($"push origin {tag}");
    }

    public bool HasUpstream()
    {
        var (output, _, exitCode) = RunGitRaw("rev-parse --abbrev-ref --symbolic-full-name @{u}");
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }

    private static string RunGit(string arguments)
    {
        var (output, error, exitCode) = RunGitRaw(arguments);

        if (exitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? $"git {arguments} failed" : error.Trim();
            throw new GitException(message);
        }

        return output;
    }

    private static (string output, string error, int exitCode) RunGitRaw(string arguments)
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
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (output, error, process.ExitCode);
    }
}
