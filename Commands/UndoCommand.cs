using Kommit.Git;
using Kommit.UI;

namespace Kommit.Commands;

public static class UndoCommand
{
    private static readonly string KommitDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".kommit"
    );

    private static readonly string LastCommandPath = Path.Combine(KommitDir, "last-command");

    public static void RecordCommand(string command, string? detail = null)
    {
        Directory.CreateDirectory(KommitDir);
        var content = detail is not null ? $"{command}|{detail}" : command;
        File.WriteAllText(LastCommandPath, content);
    }

    public static int Run(GitService git)
    {
        if (!File.Exists(LastCommandPath))
        {
            Out.Error("Nothing to undo. No previous kommit command found.");
            return 1;
        }

        var parts = File.ReadAllText(LastCommandPath).Trim().Split('|', 2);
        var command = parts[0];
        var detail = parts.Length > 1 ? parts[1] : null;

        File.Delete(LastCommandPath);

        switch (command)
        {
            case "commit":
                var lastMessage = git.GetLastCommitMessage();
                git.UndoLastCommit();
                Out.Success($"Undid commit: {lastMessage}");
                Out.Success("Changes are back in staging.");
                return 0;

            case "tag":
                var tag = detail;
                if (tag is null)
                {
                    Out.Error("Cannot undo tag: missing tag info.");
                    return 1;
                }
                git.DeleteTag(tag);
                git.UndoLastCommit();
                Out.Success($"Undid tag: {tag}");
                Out.Success("Deleted local tag and undid version bump commit.");
                Out.Success("Changes are back in staging.");
                Console.WriteLine();
                Console.WriteLine("If the tag was already pushed, also run:");
                Out.Info($"  git push origin :refs/tags/{tag}");
                return 0;

            case "merge":
                Out.Error("Cannot automatically undo a merge.");
                Console.WriteLine();
                Console.WriteLine("To undo the merge locally:");
                Out.Info("  git reset --hard HEAD~1");
                Console.WriteLine();
                Console.WriteLine("If already pushed, revert it instead:");
                Out.Info("  git revert -m 1 HEAD");
                Out.Info("  git push");
                return 1;

            case "push":
                Out.Error("Cannot automatically undo a push.");
                Console.WriteLine();
                Console.WriteLine("To undo the last pushed commit:");
                Out.Info("  git reset --soft HEAD~1");
                Out.Info("  git push --force-with-lease");
                Console.WriteLine();
                Console.WriteLine("On GitHub/GitLab, you can also revert via the web UI.");
                return 1;

            case "mr":
                Out.Error("Cannot automatically undo a merge request.");
                Console.WriteLine();
                Console.WriteLine("To close the merge request:");
                if (detail is not null)
                    Out.Info($"  Visit: {detail}");
                else
                    Console.WriteLine("  Close it from your GitHub/GitLab web UI.");
                return 1;

            case "pull":
                Out.Error("Cannot automatically undo a pull.");
                Console.WriteLine();
                Console.WriteLine("To undo, reset to your previous position:");
                Out.Info("  git reflog  (find the commit before the pull)");
                Out.Info("  git reset --hard <commit>");
                return 1;

            default:
                Out.Error($"Unknown command '{command}' — cannot undo.");
                return 1;
        }
    }
}
