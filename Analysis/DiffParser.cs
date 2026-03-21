using System.Text.RegularExpressions;

namespace Kommit.Analysis;

public record ParsedDiff(
    List<string> AddedTypes,
    List<string> AddedMembers,
    List<string> RemovedTypes,
    List<string> RemovedMembers,
    List<string> RenamedSymbols,
    List<string> AddedImports,
    List<string> RemovedImports,
    DiffSignals Signals
)
{
    public List<string> AddedSymbols => [..AddedTypes, ..AddedMembers];
    public List<string> RemovedSymbols => [..RemovedTypes, ..RemovedMembers];
};

[Flags]
public enum DiffSignals
{
    None = 0,
    ErrorHandling = 1 << 0,
    NullChecks = 1 << 1,
    TodoFixed = 1 << 2,
    Performance = 1 << 3,
    Logging = 1 << 4,
    Tests = 1 << 5,
    ConfigChange = 1 << 6,
    DependencyChange = 1 << 7,
    Documentation = 1 << 8,
    Styling = 1 << 9,
    Security = 1 << 10,
}

public partial class DiffParser
{
    // --- Symbol patterns per language ---

    // C# / Java
    [GeneratedRegex(@"^\+\s*(?:public|private|protected|internal)?\s*(?:static\s+)?(?:partial\s+)?(?:class|record|struct|interface|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex CSharpClassRegex();

    [GeneratedRegex(@"^\+\s*(?:public|private|protected|internal)?\s*(?:static\s+)?(?:async\s+)?(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex CSharpMethodRegex();

    [GeneratedRegex(@"^-\s*(?:public|private|protected|internal)?\s*(?:static\s+)?(?:partial\s+)?(?:class|record|struct|interface|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex CSharpRemovedClassRegex();

    [GeneratedRegex(@"^-\s*(?:public|private|protected|internal)?\s*(?:static\s+)?(?:async\s+)?(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex CSharpRemovedMethodRegex();

    // JS / TS
    [GeneratedRegex(@"^\+\s*(?:export\s+)?(?:default\s+)?(?:class|interface)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex JsClassRegex();

    [GeneratedRegex(@"^\+\s*(?:export\s+)?(?:async\s+)?function\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex JsFunctionRegex();

    [GeneratedRegex(@"^\+\s*(?:const|let|var)\s+(\w+)\s*=\s*(?:async\s+)?\(.*\)\s*=>", RegexOptions.Multiline)]
    private static partial Regex JsArrowFunctionRegex();

    [GeneratedRegex(@"^-\s*(?:export\s+)?(?:default\s+)?(?:class|interface)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex JsRemovedClassRegex();

    [GeneratedRegex(@"^-\s*(?:export\s+)?(?:async\s+)?function\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex JsRemovedFunctionRegex();

    // Python
    [GeneratedRegex(@"^\+\s*class\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex PyClassRegex();

    [GeneratedRegex(@"^\+\s*(?:async\s+)?def\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex PyFunctionRegex();

    [GeneratedRegex(@"^-\s*class\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex PyRemovedClassRegex();

    [GeneratedRegex(@"^-\s*(?:async\s+)?def\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex PyRemovedFunctionRegex();

    // Go
    [GeneratedRegex(@"^\+\s*func\s+(?:\(\w+\s+\*?\w+\)\s+)?(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex GoFunctionRegex();

    [GeneratedRegex(@"^\+\s*type\s+(\w+)\s+(?:struct|interface)", RegexOptions.Multiline)]
    private static partial Regex GoTypeRegex();

    [GeneratedRegex(@"^-\s*func\s+(?:\(\w+\s+\*?\w+\)\s+)?(\w+)\s*\(", RegexOptions.Multiline)]
    private static partial Regex GoRemovedFunctionRegex();

    [GeneratedRegex(@"^-\s*type\s+(\w+)\s+(?:struct|interface)", RegexOptions.Multiline)]
    private static partial Regex GoRemovedTypeRegex();

    // Rust
    [GeneratedRegex(@"^\+\s*(?:pub\s+)?(?:async\s+)?fn\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex RustFunctionRegex();

    [GeneratedRegex(@"^\+\s*(?:pub\s+)?(?:struct|enum|trait)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex RustTypeRegex();

    [GeneratedRegex(@"^-\s*(?:pub\s+)?(?:async\s+)?fn\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex RustRemovedFunctionRegex();

    [GeneratedRegex(@"^-\s*(?:pub\s+)?(?:struct|enum|trait)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex RustRemovedTypeRegex();

    // --- Import patterns ---
    [GeneratedRegex(@"^\+\s*(?:using|import|from|require|#include)\s+(.+?)[\s;]*$", RegexOptions.Multiline)]
    private static partial Regex AddedImportRegex();

    [GeneratedRegex(@"^-\s*(?:using|import|from|require|#include)\s+(.+?)[\s;]*$", RegexOptions.Multiline)]
    private static partial Regex RemovedImportRegex();

    // --- Signal patterns ---
    [GeneratedRegex(@"^\+.*(?:try\s*\{|catch\s*\(|\.catch\(|except\s|rescue\b|on\s+\w+Error)", RegexOptions.Multiline)]
    private static partial Regex ErrorHandlingRegex();

    [GeneratedRegex(@"^\+.*(?:\?\?|is\s+not\s+null|is\s+null|\?\.|!=\s*null|==\s*null|!= ?nil|== ?nil|is\s+None|is\s+not\s+None)", RegexOptions.Multiline)]
    private static partial Regex NullCheckRegex();

    [GeneratedRegex(@"^-\s*.*(?://\s*(?:TODO|FIXME|HACK|XXX|BUG)|#\s*(?:TODO|FIXME|HACK|XXX|BUG))", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex TodoFixedRegex();

    [GeneratedRegex(@"^\+.*(?:cache|memoiz|\.AsParallel|async\s+|await\s+|Parallel\.|\.AsNoTracking|lazy|buffer|batch|pool|throttl|debounce)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PerformanceRegex();

    [GeneratedRegex(@"^\+.*(?:Console\.Write|Logger\.|_logger\.|log\.|logging\.|print\(|fmt\.Print|println!|slog\.|ILogger)", RegexOptions.Multiline)]
    private static partial Regex LoggingRegex();

    [GeneratedRegex(@"^\+\s*(?:\[Test\]|\[Fact\]|\[Theory\]|\[TestMethod\]|describe\(['""]|(?:^|\s)it\(['""]|(?:^|\s)test\(['""]|def\s+test_|func\s+Test|#\[test\]|@Test\b|@pytest)", RegexOptions.Multiline)]
    private static partial Regex TestRegex();

    [GeneratedRegex(@"^\+.*(?:appsettings|\.env|config\.|\.config|environment\.|settings\.|\.properties|\.toml|\.ini)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ConfigRegex();

    [GeneratedRegex(@"^\+.*(?:PackageReference|dependencies|devDependencies|require\s|gem\s|pip\s|cargo\s|go\s+get)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DependencyRegex();

    [GeneratedRegex(@"^\+.*(?:///|/\*\*|\*\s+@param|:param|Args:|Returns:|Raises:|##)", RegexOptions.Multiline)]
    private static partial Regex DocRegex();

    [GeneratedRegex(@"^\+.*(?:margin|padding|font-|color:|background|display:|flex|grid|border|width:|height:|@media|className|style=)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"^\+.*(?:authenticat|authoriz|password|secret_?key|encrypt|decrypt|hash(?:ing|ed)|salt|csrf|cors|sanitize|escape_?html|validate.*input|HttpOnly|SecurePolicy|bearer|jwt|oauth)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SecurityRegex();

    public ParsedDiff Parse(string rawDiff)
    {
        var addedTypes = new List<string>();
        var addedMembers = new List<string>();
        var removedTypes = new List<string>();
        var removedMembers = new List<string>();
        var addedImports = new List<string>();
        var removedImports = new List<string>();
        var signals = DiffSignals.None;

        // Extract added types (classes, structs, enums, interfaces, records)
        ExtractMatches(rawDiff, addedTypes,
            CSharpClassRegex(), JsClassRegex(),
            PyClassRegex(), GoTypeRegex(), RustTypeRegex());

        // Extract added members (methods, functions)
        ExtractMatches(rawDiff, addedMembers,
            CSharpMethodRegex(), JsFunctionRegex(), JsArrowFunctionRegex(),
            PyFunctionRegex(), GoFunctionRegex(), RustFunctionRegex());

        // Extract removed types
        ExtractMatches(rawDiff, removedTypes,
            CSharpRemovedClassRegex(), JsRemovedClassRegex(),
            PyRemovedClassRegex(), GoRemovedTypeRegex(), RustRemovedTypeRegex());

        // Extract removed members
        ExtractMatches(rawDiff, removedMembers,
            CSharpRemovedMethodRegex(), JsRemovedFunctionRegex(),
            PyRemovedFunctionRegex(), GoRemovedFunctionRegex(), RustRemovedFunctionRegex());

        // Imports
        ExtractMatches(rawDiff, addedImports, AddedImportRegex());
        ExtractMatches(rawDiff, removedImports, RemovedImportRegex());

        // Detect renames from all symbols combined
        var allAdded = addedTypes.Concat(addedMembers).ToList();
        var allRemoved = removedTypes.Concat(removedMembers).ToList();
        var renamed = DetectRenames(allAdded, allRemoved);

        // Rebuild lists after rename detection removed matched items
        addedTypes = addedTypes.Where(allAdded.Contains).ToList();
        addedMembers = addedMembers.Where(allAdded.Contains).ToList();
        removedTypes = removedTypes.Where(allRemoved.Contains).ToList();
        removedMembers = removedMembers.Where(allRemoved.Contains).ToList();

        // Detect signals
        if (ErrorHandlingRegex().IsMatch(rawDiff)) signals |= DiffSignals.ErrorHandling;
        if (NullCheckRegex().IsMatch(rawDiff)) signals |= DiffSignals.NullChecks;
        if (TodoFixedRegex().IsMatch(rawDiff)) signals |= DiffSignals.TodoFixed;
        if (PerformanceRegex().IsMatch(rawDiff)) signals |= DiffSignals.Performance;
        if (LoggingRegex().IsMatch(rawDiff)) signals |= DiffSignals.Logging;
        if (TestRegex().IsMatch(rawDiff)) signals |= DiffSignals.Tests;
        if (ConfigRegex().IsMatch(rawDiff)) signals |= DiffSignals.ConfigChange;
        if (DependencyRegex().IsMatch(rawDiff)) signals |= DiffSignals.DependencyChange;
        if (DocRegex().IsMatch(rawDiff)) signals |= DiffSignals.Documentation;
        if (StyleRegex().IsMatch(rawDiff)) signals |= DiffSignals.Styling;
        if (SecurityRegex().IsMatch(rawDiff)) signals |= DiffSignals.Security;

        return new ParsedDiff(addedTypes, addedMembers, removedTypes, removedMembers,
            renamed, addedImports, removedImports, signals);
    }

    private static void ExtractMatches(string text, List<string> results, params Regex[] patterns)
    {
        foreach (var pattern in patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (!results.Contains(name))
                    results.Add(name);
            }
        }
    }

    private static List<string> DetectRenames(List<string> added, List<string> removed)
    {
        var renamed = new List<string>();

        foreach (var old in removed.ToList())
        {
            // Find a matching added symbol with similar name (e.g. case change, prefix/suffix change)
            var match = added.FirstOrDefault(a =>
                !a.Equals(old, StringComparison.Ordinal) &&
                (a.Contains(old, StringComparison.OrdinalIgnoreCase) ||
                 old.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                 LevenshteinSimilar(a, old)));

            if (match is not null)
            {
                renamed.Add($"{old} -> {match}");
                added.Remove(match);
                removed.Remove(old);
            }
        }

        return renamed;
    }

    private static bool LevenshteinSimilar(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 5) return false;

        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return true;

        var distance = LevenshteinDistance(a, b);
        return (double)(maxLen - distance) / maxLen >= 0.6;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }
}
