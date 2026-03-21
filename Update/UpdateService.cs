using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kommit.Update;

public class UpdateService
{
    private const string RepoOwner = "HannibalLade";
    private const string RepoName = "kommit";
    private const string ReleasesUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(0, 0, 0);
    }

    public static string GetRuntimeIdentifier()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : throw new PlatformNotSupportedException("Unsupported operating system.");

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };

        return $"{os}-{arch}";
    }

    public async Task<int> RunUpdateAsync()
    {
        var currentVersion = GetCurrentVersion();
        Console.WriteLine($"Current version: {currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kommit", currentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(ReleasesUrl);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Failed to check for updates: {ex.Message}");
            return 1;
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine("No releases found. Publish a release on GitHub first.");
                return 1;
            }
            Console.Error.WriteLine($"GitHub API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
            return 1;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var latestVersionStr = tagName.TrimStart('v');

        if (!Version.TryParse(latestVersionStr, out var latestVersion))
        {
            Console.Error.WriteLine($"Could not parse version from tag: {tagName}");
            return 1;
        }

        var latestDisplay = $"{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build}";

        if (latestVersion <= currentVersion)
        {
            Console.WriteLine($"Already up to date (v{latestDisplay}).");
            return 0;
        }

        Console.WriteLine($"New version available: v{latestDisplay}");

        var rid = GetRuntimeIdentifier();
        var assetName = $"kommit-{rid}";

        string? downloadUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (downloadUrl is null)
        {
            Console.Error.WriteLine($"No binary found for your platform ({rid}).");
            Console.Error.WriteLine("Available assets:");
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                Console.Error.WriteLine($"  - {asset.GetProperty("name").GetString()}");
            }
            return 1;
        }

        Console.WriteLine($"Downloading {assetName}...");

        byte[] binary;
        try
        {
            binary = await client.GetByteArrayAsync(downloadUrl);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Download failed: {ex.Message}");
            return 1;
        }

        var currentBinary = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentBinary))
        {
            Console.Error.WriteLine("Could not determine current binary path.");
            return 1;
        }

        var backupPath = currentBinary + ".bak";
        try
        {
            // Move current binary to backup, write new one, delete backup
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(currentBinary, backupPath);
            await File.WriteAllBytesAsync(currentBinary, binary);

            // Preserve executable permission on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(currentBinary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.Delete(backupPath);
        }
        catch (UnauthorizedAccessException)
        {
            // Restore backup if we failed
            if (File.Exists(backupPath) && !File.Exists(currentBinary))
                File.Move(backupPath, currentBinary);

            Console.Error.WriteLine($"Permission denied writing to {currentBinary}.");
            Console.Error.WriteLine("Try running with sudo or moving kommit to a user-writable location.");
            return 1;
        }

        Console.WriteLine($"Updated to v{latestDisplay}!");
        return 0;
    }
}
