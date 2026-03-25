using Kommit.Git;

namespace Kommit.Commands;

public static class CheckoutCommand
{
    public static int Run(string[] args, GitService git)
    {
        if (args.Length < 2)
        {
            var branches = git.ListBranches();
            foreach (var branch in branches)
                Console.WriteLine(branch);
            return 0;
        }

        var createNew = args.Contains("-b");

        if (createNew)
        {
            var idx = Array.IndexOf(args, "-b");
            if (idx + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: kommit checkout -b <branch-name>");
                return 1;
            }
            var branchName = args[idx + 1];
            git.CheckoutNewBranch(branchName);
            Console.WriteLine($"Switched to new branch '{branchName}'");
            return 0;
        }

        var target = args[1];
        git.Checkout(target);
        Console.WriteLine($"Switched to branch '{target}'");
        return 0;
    }
}
