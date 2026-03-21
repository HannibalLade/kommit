namespace Kommit.Models;

public record CommitMessage(string Type, string? Scope, string Description)
{
    public override string ToString()
    {
        var scope = Scope is not null ? $"({Scope})" : string.Empty;
        return $"{Type}{scope}: {Description}";
    }
}
