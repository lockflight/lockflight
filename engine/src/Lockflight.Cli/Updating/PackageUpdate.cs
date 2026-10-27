using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Updating;

/// <summary>
/// Updates the user install from a package feed: installs the newest <c>Lockflight.Cli</c> into a fresh tool path, moves it to
/// <c>~/.lockflight/tools/&lt;version&gt;</c> and points <c>~/.lockflight/bin/lockflight</c> at it as <paramref name="layout"/> says
/// (a link, or on Windows a copy, which replaces the old one even while that runs). The SDK runs in <c>~/.lockflight</c>, outside
/// any project, so a project's <c>global.json</c> pinning an SDK older than 10 (which cannot install the platform-specific
/// package) is never in the way. Older versions stay installed: projects keep their own copies anyway.
/// </summary>
internal sealed class PackageUpdate(RunProcess processes, TimeProvider time, EngineLayout layout)
{
    public const string PackageId = "Lockflight.Cli";

    private const string StagingPrefix = ".staging-";

    /// <param name="feed">The package source that replaces the configured ones (a URL or a local folder); null for the NuGet configuration's sources.</param>
    public async Task<PackageUpdateResult> RunAsync(string home, string? feed, CancellationToken cancellationToken)
    {
        string? installed = UserInstall.InstalledVersion(home);
        string lockflightHome = UserInstall.Directory(home);
        string staging = Path.Combine(lockflightHome, "tools", StagingPrefix + time.GetUtcNow().ToUnixTimeMilliseconds());
        Directory.CreateDirectory(lockflightHome);
        DeleteDirectory(staging);
        try
        {
            ProcessResult install = await processes("dotnet", InstallArguments(staging, installed, feed), lockflightHome, cancellationToken);
            if (install.ExitCode != 0)
            {
                return PackageUpdateResult.Failure($"dotnet tool install {PackageId} failed: {FirstLines(install)}");
            }

            if (UserInstall.VersionIn(staging) is not string version)
            {
                return PackageUpdateResult.Failure($"dotnet tool install left no {PackageId} package in {staging}");
            }

            if (installed is not null && EngineVersion.Parse(installed) is EngineVersion current
                && EngineVersion.Parse(version) is EngineVersion found && found.CompareTo(current) < 0)
            {
                return new PackageUpdateResult(PackageUpdateStatus.KeptNewer, installed, null);
            }

            string target = UserInstall.ToolPath(home, version);
            if (Directory.Exists(target))
            {
                // Linked from tools/<version> already; a package installed straight into bin is moved into that layout.
                if (string.Equals(installed, version, StringComparison.OrdinalIgnoreCase))
                {
                    return new PackageUpdateResult(PackageUpdateStatus.UpToDate, version, null);
                }
            }
            else
            {
                Directory.Move(staging, target);
            }

            UserInstall.PointAt(home, version, layout);
            ProcessResult check = await processes(UserInstall.Engine(home), ["version"], lockflightHome, cancellationToken);
            return check.ExitCode == 0
                ? new PackageUpdateResult(PackageUpdateStatus.Installed, version, null)
                : PackageUpdateResult.Failure($"the installed engine {version} does not run: {FirstLines(check)}");
        }
        catch (ExecutableNotFoundException)
        {
            return PackageUpdateResult.Failure("the .NET SDK (dotnet) is not on PATH; install the .NET 10 SDK");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PackageUpdateResult.Failure($"the engine could not be put in place in {lockflightHome}: {exception.Message}");
        }
        finally
        {
            DeleteDirectory(staging);
        }
    }

    /// <summary>
    /// A prerelease install keeps following prereleases (and so does a first install, while only prereleases exist); a release
    /// install moves to releases only.
    /// </summary>
    private static List<string> InstallArguments(string toolPath, string? installed, string? feed)
    {
        List<string> arguments = ["tool", "install", PackageId, "--tool-path", toolPath];
        if (installed is null || EngineVersion.Parse(installed) is not { Prerelease.Length: 0 })
        {
            arguments.Add("--prerelease");
        }

        if (feed is not null)
        {
            arguments.AddRange(["--source", feed]);
        }

        return arguments;
    }

    private static string FirstLines(ProcessResult result)
    {
        string text = (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim();
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? $"exit {result.ExitCode}" : string.Join(" ", lines.Take(3));
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
