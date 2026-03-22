using Kommit.Analysis;
using Kommit.Models;

namespace Kommit.Tests;

public class CommitAnalyzerTests
{
    private readonly CommitAnalyzer _analyzer = new();

    private static DiffSummary MakeDiff(
        List<string>? files = null,
        int added = 5,
        int deleted = 2,
        string raw = "diff content")
    {
        return new DiffSummary(files ?? ["src/Foo.cs"], added, deleted, raw);
    }

    // ── Branch prefix → type ──

    [Theory]
    [InlineData("feat/add-login", "feat")]
    [InlineData("feature/new-dashboard", "feat")]
    [InlineData("fix/null-ref", "fix")]
    [InlineData("bugfix/crash-on-start", "fix")]
    [InlineData("hotfix/urgent-patch", "fix")]
    [InlineData("docs/update-readme", "docs")]
    [InlineData("doc/api-reference", "docs")]
    [InlineData("style/formatting", "style")]
    [InlineData("refactor/cleanup", "refactor")]
    [InlineData("perf/optimize-query", "perf")]
    [InlineData("test/add-coverage", "test")]
    [InlineData("tests/integration", "test")]
    [InlineData("chore/update-deps", "chore")]
    [InlineData("ci/add-pipeline", "ci")]
    [InlineData("build/docker-setup", "build")]
    [InlineData("revert/bad-merge", "revert")]
    public void Analyze_BranchPrefix_SetsCorrectType(string branch, string expectedType)
    {
        var result = _analyzer.Analyze(branch, MakeDiff());
        Assert.Equal(expectedType, result.Type);
    }

    [Theory]
    [InlineData("FEAT/upper-case", "feat")]
    [InlineData("Fix/Mixed-Case", "fix")]
    public void Analyze_BranchPrefix_IsCaseInsensitive(string branch, string expectedType)
    {
        var result = _analyzer.Analyze(branch, MakeDiff());
        Assert.Equal(expectedType, result.Type);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("my-branch")]
    public void Analyze_NoBranchPrefix_FallsThrough(string branch)
    {
        var result = _analyzer.Analyze(branch, MakeDiff());
        Assert.NotNull(result.Type);
    }

    // ── File-type heuristics → type ──

    [Fact]
    public void Analyze_SingleMarkdownFile_ReturnsDocsType()
    {
        var diff = MakeDiff(files: ["README.md"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("docs", result.Type);
    }

    [Fact]
    public void Analyze_MultipleMarkdownFiles_ReturnsDocsType()
    {
        var diff = MakeDiff(files: ["README.md", "CHANGELOG.md"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("docs", result.Type);
    }

    [Fact]
    public void Analyze_OnlyCsprojFiles_ReturnsBuildType()
    {
        var diff = MakeDiff(files: ["kommit.csproj"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("build", result.Type);
    }

    [Fact]
    public void Analyze_OnlyYamlFiles_ReturnsCiType()
    {
        var diff = MakeDiff(files: [".github/workflows/build.yml"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("ci", result.Type);
    }

    [Fact]
    public void Analyze_MixedFileTypes_DoesNotUseFileHeuristic()
    {
        var diff = MakeDiff(files: ["README.md", "src/Foo.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.NotEqual("docs", result.Type);
    }

    // ── Test file detection ──

    [Theory]
    [InlineData("FooTest.cs")]
    [InlineData("FooTests.cs")]
    [InlineData("Foo.test.js")]
    [InlineData("Foo.spec.ts")]
    [InlineData("TestFoo.cs")]
    [InlineData("src/tests/Foo.cs")]
    [InlineData("src/test/Bar.cs")]
    [InlineData("tests/Foo.cs")]
    [InlineData("test/Bar.cs")]
    public void Analyze_AllTestFiles_ReturnsTestType(string testFile)
    {
        var diff = MakeDiff(files: [testFile]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("test", result.Type);
    }

    // ── Add/delete ratio → type ──

    [Fact]
    public void Analyze_OnlyAdditions_ReturnsFeatType()
    {
        var diff = MakeDiff(added: 20, deleted: 0);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("feat", result.Type);
    }

    [Fact]
    public void Analyze_OnlyDeletions_ReturnsRefactorType()
    {
        var diff = MakeDiff(added: 0, deleted: 15);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("refactor", result.Type);
    }

    [Fact]
    public void Analyze_MixedChanges_DefaultsToChore()
    {
        var diff = MakeDiff(added: 5, deleted: 3);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("chore", result.Type);
    }

    // ── Branch prefix takes priority over file heuristics ──

    [Fact]
    public void Analyze_BranchPrefixOverridesFileHeuristic()
    {
        var diff = MakeDiff(files: ["README.md"]);
        var result = _analyzer.Analyze("fix/typo-in-readme", diff);
        Assert.Equal("fix", result.Type);
    }

    // ── Scope inference ──

    [Fact]
    public void Analyze_AllFilesInSameDirectory_SetsScopeToDirectory()
    {
        var diff = MakeDiff(files: ["Analysis/CommitAnalyzer.cs", "Analysis/Helper.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Equal("Analysis", result.Scope);
    }

    [Fact]
    public void Analyze_FilesInDifferentDirectories_ScopeIsNull()
    {
        var diff = MakeDiff(files: ["Analysis/Foo.cs", "Models/Bar.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Null(result.Scope);
    }

    [Fact]
    public void Analyze_RootLevelFiles_ScopeIsNull()
    {
        var diff = MakeDiff(files: ["Program.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Null(result.Scope);
    }

    // ── Description — single file shows filename ──

    [Fact]
    public void Analyze_SingleFile_DescriptionContainsFileName()
    {
        var diff = MakeDiff(files: ["src/Foo.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Contains("Foo.cs", result.Description);
    }

    // ── Description — 2-3 files lists all filenames ──

    [Fact]
    public void Analyze_TwoOrThreeFiles_DescriptionListsAllFileNames()
    {
        var diff = MakeDiff(files: ["A.cs", "B.cs", "C.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Contains("A.cs", result.Description);
        Assert.Contains("B.cs", result.Description);
        Assert.Contains("C.cs", result.Description);
    }

    // ── Description — many files shows count ──

    [Fact]
    public void Analyze_ManyFiles_DescriptionShowsCount()
    {
        var files = Enumerable.Range(1, 5).Select(i => $"File{i}.cs").ToList();
        var diff = MakeDiff(files: files);
        var result = _analyzer.Analyze("main", diff);
        Assert.Contains("5 files", result.Description);
    }

    // ── Description — same directory shows directory name ──

    [Fact]
    public void Analyze_AllFilesInSameDir_DescriptionShowsDirectory()
    {
        var diff = MakeDiff(files: ["Commands/A.cs", "Commands/B.cs", "Commands/C.cs", "Commands/D.cs"]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Contains("Commands", result.Description);
        Assert.DoesNotContain("files", result.Description);
    }

    // ── Description — action verb from line ratio ──

    [Fact]
    public void Analyze_OnlyAdded_DescriptionStartsWithAdd()
    {
        var diff = MakeDiff(files: ["New.cs"], added: 10, deleted: 0);
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("add", result.Description);
    }

    [Fact]
    public void Analyze_OnlyDeleted_DescriptionStartsWithRemove()
    {
        var diff = MakeDiff(files: ["Old.cs"], added: 0, deleted: 10);
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("remove", result.Description);
    }

    [Fact]
    public void Analyze_MixedChanges_DescriptionStartsWithUpdate()
    {
        var diff = MakeDiff(files: ["Code.cs"], added: 5, deleted: 3);
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("update", result.Description);
    }

    // ── Description — FileChangeKind drives action ──

    [Fact]
    public void Analyze_FileChangeKindAdded_DescriptionStartsWithAdd()
    {
        var diff = new DiffSummary(["NewFile.cs"], 10, 0, "diff content")
        {
            FileChanges = [new FileChange("NewFile.cs", FileChangeKind.Added)]
        };
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("add", result.Description);
    }

    [Fact]
    public void Analyze_FileChangeKindDeleted_DescriptionStartsWithRemove()
    {
        var diff = new DiffSummary(["OldFile.cs"], 0, 10, "diff content")
        {
            FileChanges = [new FileChange("OldFile.cs", FileChangeKind.Deleted)]
        };
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("remove", result.Description);
    }

    [Fact]
    public void Analyze_FileChangeKindRenamed_DescriptionStartsWithRename()
    {
        var diff = new DiffSummary(["NewName.cs"], 1, 1, "diff content")
        {
            FileChanges = [new FileChange("NewName.cs", FileChangeKind.Renamed)]
        };
        var result = _analyzer.Analyze("main", diff);
        Assert.StartsWith("rename", result.Description);
    }

    // ── Description — multiple directories lists them ──

    [Fact]
    public void Analyze_FilesAcrossDirectories_DescriptionListsDirectories()
    {
        var diff = MakeDiff(files: [
            "Commands/A.cs", "Commands/B.cs",
            "Git/C.cs", "Git/D.cs",
            "Config/E.cs"
        ]);
        var result = _analyzer.Analyze("main", diff);
        Assert.Contains("Commands", result.Description);
        Assert.Contains("Git", result.Description);
        Assert.Contains("Config", result.Description);
    }

    // ── CommitMessage.ToString() ──

    [Fact]
    public void CommitMessage_WithScope_FormatsCorrectly()
    {
        var msg = new CommitMessage("feat", "auth", "add login endpoint");
        Assert.Equal("feat(auth): add login endpoint", msg.ToString());
    }

    [Fact]
    public void CommitMessage_WithoutScope_FormatsCorrectly()
    {
        var msg = new CommitMessage("fix", null, "resolve null reference");
        Assert.Equal("fix: resolve null reference", msg.ToString());
    }
}
