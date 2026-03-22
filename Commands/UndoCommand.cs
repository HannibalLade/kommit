using Kommit.Git;

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
            Console.Error.WriteLine("Nothing to undo. No previous kommit command found.");
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
                Console.WriteLine($"Undid commit: {lastMessage}");
                Console.WriteLine("Changes are back in staging.");
                return 0;

            case "tag":
                var tag = detail;
                if (tag is null)
                {
                    Console.Error.WriteLine("Cannot undo tag: missing tag info.");
                    return 1;
                }
                git.DeleteTag(tag);
                git.UndoLastCommit();
                Console.WriteLine($"Undid tag: {tag}");
                Console.WriteLine("Deleted local tag and undid version bump commit.");
                Console.WriteLine("Changes are back in staging.");
                Console.WriteLine();
                Console.WriteLine("If the tag was already pushed, also run:");
                Console.WriteLine($"  git push origin :refs/tags/{tag}");
                return 0;

            case "merge":
                Console.Error.WriteLine("Cannot automatically undo a merge.");
                Console.WriteLine();
                Console.WriteLine("To undo the merge locally:");
                Console.WriteLine("  git reset --hard HEAD~1");
                Console.WriteLine();
                Console.WriteLine("If already pushed, revert it instead:");
                Console.WriteLine("  git revert -m 1 HEAD");
                Console.WriteLine("  git push");
                return 1;

            case "push":
                Console.Error.WriteLine("Cannot automatically undo a push.");
                Console.WriteLine();
                Console.WriteLine("To undo the last pushed commit:");
                Console.WriteLine("  git reset --soft HEAD~1");
                Console.WriteLine("  git push --force-with-lease");
                Console.WriteLine();
                Console.WriteLine("On GitHub/GitLab, you can also revert via the web UI.");
                return 1;

            case "mr":
                Console.Error.WriteLine("Cannot automatically undo a merge request.");
                Console.WriteLine();
                Console.WriteLine("To close the merge request:");
                if (detail is not null)
                    Console.WriteLine($"  Visit: {detail}");
                else
                    Console.WriteLine("  Close it from your GitHub/GitLab web UI.");
                return 1;

            case "pull":
                Console.Error.WriteLine("Cannot automatically undo a pull.");
                Console.WriteLine();
                Console.WriteLine("To undo, reset to your previous position:");
                Console.WriteLine("  git reflog  (find the commit before the pull)");
                Console.WriteLine("  git reset --hard <commit>");
                return 1;

            default:
                Console.Error.WriteLine($"Unknown command '{command}' — cannot undo.");
                return 1;
        }
    }
}
