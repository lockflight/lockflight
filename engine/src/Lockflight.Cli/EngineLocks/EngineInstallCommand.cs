using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// <c>lockflight engine install [--source &lt;feed&gt;]</c>, a person's command: installs exactly the engine the project's lock
/// names — its version, and only the executable whose SHA-256 is the lock's entry for this platform — into
/// <c>~/.lockflight/tools/&lt;version&gt;</c> and links it into the project. It changes nothing committed: it is the fix for a clone
/// behind or ahead of the lock (a branch switch, a fresh clone, a teammate's upgrade), where <c>update</c> would move the project
/// to the newest engine. It installs the judge, so the agent never runs it.
/// </summary>
internal sealed class EngineInstallCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        EngineLockReading reading = EngineLockFiles.ReadCommitted(manifest.Root);
        if (reading.Lock is not EngineLock engineLock)
        {
            await context.Error.WriteLineAsync(reading.Problem is string problem
                ? $"lockflight: {problem}"
                : $"lockflight: this project has no {EngineLock.RelativePath}; `{EngineCommand.UserInstall} update` writes one");
            return ExitCode.Config;
        }

        if (EngineRids.Current is not string rid || engineLock.HashFor(rid) is not string expected)
        {
            await context.Error.WriteLineAsync($"lockflight: {EngineLock.RelativePath} names no engine for this platform (released: {string.Join(", ", EngineRids.Released)})");
            return ExitCode.Config;
        }

        if (context.Environment.HomeDirectory() is not string home)
        {
            await context.Error.WriteLineAsync("lockflight: engine install needs the home directory (HOME) for ~/.lockflight/tools");
            return ExitCode.Config;
        }

        EnginePackageFeed feed = options.Value("--source") is string source
            ? new EnginePackageFeed(source, context.Environment)
            : EnginePackageFeed.FromEnvironment(context.Environment);
        await context.Output.WriteLineAsync($"installing Lockflight {engineLock.Version} ({rid}) as {EngineLock.RelativePath} pins it…");
        EngineFetch fetch = await feed.FetchEngineAsync(engineLock.Version, rid, expected, cancellationToken);
        if (fetch.Content is not byte[] engine)
        {
            await context.Error.WriteLineAsync($"lockflight: {fetch.Message}; nothing was installed");
            return fetch.Status == EngineFetchStatus.HashMismatch ? ExitCode.Fail : ExitCode.Error;
        }

        string installed = Install(home, engineLock.Version, rid, engine);
        string linked = LinkCommand.Link(manifest.Root, installed);
        await context.Output.WriteLineAsync($"installed {installed} (sha256 {expected}) and linked it -> {linked}");
        return ExitCode.Pass;
    }

    /// <summary>Writes the verified executable into the user's tools through a temporary file and a rename.</summary>
    private static string Install(string home, string version, string rid, byte[] engine)
    {
        string directory = UserInstall.ToolPath(home, version);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, EngineRids.ExecutableName(rid));
        string temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, engine);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, LinkCommand.ExecutableMode);
        }

        File.Move(temporary, target, overwrite: true);
        return target;
    }
}
