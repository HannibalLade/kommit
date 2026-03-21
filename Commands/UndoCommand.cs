using Kommit.Git;

namespace Kommit.Commands;

public static class UndoCommand
{
    public static int Run(GitService git)
    {
        var lastMessage = git.GetLastCommitMessage();
        git.UndoLastCommit();
        Console.WriteLine($"Undid commit: {lastMessage}");
        Console.WriteLine("Changes are back in staging.");
        return 0;
    }
}
