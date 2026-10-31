using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Linking;

/// <summary>
/// <c>lockflight link</c>: copies the running (pinned, NuGet-restored) binary to <c>.lockflight/bin/</c> in the project, where
/// hooks execute it directly — <c>dotnet tool run</c> costs ~160 ms per hook call. Only a single-file (Native AOT) binary is
/// copied: a framework-dependent build cannot run from a copy of its launcher. Its hash and platform are recorded beside it
/// (<see cref="EngineRecord"/>), so an engine replaced afterwards is not run. When the project has an engine lock, only the
/// engine whose hash is the lock's entry for this platform is linked. <c>link --installed</c> links, instead of the running
/// engine, the locked version already installed in <c>~/.lockflight/tools/&lt;version&gt;</c>, only when its hash is the lock's:
/// the one recovery an agent may run after a branch switch moved the lock, since it installs nothing.
/// </summary>
internal sealed class LinkCommand(CliContext context) : ICommand
{
    public const string RelativeBinDirectory = ".lockflight/bin";
    public const string InstalledFlag = "--installed";

    /// <summary>The platforms with a Native AOT engine package (the tool's runtime identifiers).</summary>
    public const string SupportedPlatforms = "osx-arm64, osx-x64, linux-x64, linux-arm64 and win-x64";

    public const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>The linked engine of the project at <paramref name="root"/> (<c>.exe</c> on Windows).</summary>
    public static string BinaryPath(string root) =>
        Path.Combine(root, RelativeBinDirectory, OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight");

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        return options.Flag(InstalledFlag) ? await LinkInstalledAsync(cancellationToken) : await RunAsync(cancellationToken);
    }

    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        if (context.Engine.Path is not string source || !File.Exists(source))
        {
            await context.Error.WriteLineAsync("lockflight: cannot locate the running engine binary");
            return ExitCode.Error;
        }

        // A framework-dependent build's launcher fails on its own (it looks for the assembly next to it), and the hooks block
        // every tool call, recovery commands included, when the linked engine fails. An unlinked project stays in recovery
        // mode instead, where restore and link still run.
        if (!context.Engine.IsSingleFile)
        {
            await context.Error.WriteLineAsync(
                $"lockflight: this engine ({source}) is not a single-file native build, and a copy of it cannot run from "
                + $"{RelativeBinDirectory}, so nothing was linked. Lockflight ships native engines for {SupportedPlatforms}: install "
                + "the package on one of those (scripts/dev-install.sh builds one from source). Until a native engine is "
                + "linked, the project stays in recovery mode: the hooks allow reading and the restore and link commands, and block "
                + "other tool calls.");
            return ExitCode.Error;
        }

        if (LockProblem(manifest.Root, EngineRecord.HashOf(source)) is string problem)
        {
            await context.Error.WriteLineAsync($"lockflight: {problem}; nothing was linked");
            return ExitCode.Config;
        }

        string target = Link(manifest.Root, source);
        await context.Output.WriteLineAsync($"linked lockflight {ToolPin.RunningVersion} -> {target}");
        return ExitCode.Pass;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into the project and records its hash and this platform. Copied to a temporary name and
    /// renamed, so a concurrently running hook never sees a half-written binary.
    /// </summary>
    public static string Link(string root, string source)
    {
        string target = BinaryPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp";
        File.Copy(source, temporary, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, ExecutableMode);
        }

        string hash = EngineRecord.HashOf(temporary);
        ExecutableReplacement.Replace(temporary, target);
        EngineRecord.Write(root, hash, EngineRids.Current);
        return target;
    }

    /// <summary>
    /// Why an engine with <paramref name="hash"/> may not be linked into the project at <paramref name="root"/>: the lock cannot be
    /// read, has no entry for this platform, or names another engine. Null when it may (or the project has no lock).
    /// </summary>
    public static string? LockProblem(string root, string hash)
    {
        EngineLockReading reading = EngineLockFiles.ReadForClone(root);
        if (reading.Problem is string problem)
        {
            return $"the engine lock cannot be read: {problem}";
        }

        if (reading.Lock is not EngineLock engineLock)
        {
            return null;
        }

        string rid = EngineRids.Current ?? "this platform";
        return engineLock.HashFor(rid) switch
        {
            null => $"the engine lock names no engine for {rid}",
            string locked when locked == hash => null,
            _ => $"this engine ({ToolPin.RunningVersion}) is not the one {EngineLock.RelativePath} pins ({engineLock.Version}); ask the user to "
                + HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} engine install"),
        };
    }

    private async Task<ExitCode> LinkInstalledAsync(CancellationToken cancellationToken)
    {
        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        // The agent may run this command, so it trusts only the committed lock, which a person reviewed; the git-ignored local lock
        // of a development build is a person's to link (`lockflight link` with that build).
        string install = $"ask the user to {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} engine install")}";
        if (File.Exists(EngineLockFiles.LocalPath(manifest.Root)))
        {
            await context.Error.WriteLineAsync(
                $"lockflight: link {InstalledFlag} links only the engine the committed {EngineLock.RelativePath} pins, and this clone has a development lock ({EngineLock.LocalRelativePath}) that the hooks read first; ask the user to link the development build with `lockflight link`, or to remove {EngineLock.LocalRelativePath} and {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.UserInstall} engine install")}");
            return ExitCode.Config;
        }

        EngineLockReading reading = EngineLockFiles.ReadCommitted(manifest.Root);
        if (reading.Lock is not EngineLock engineLock || EngineRids.Current is not string rid || engineLock.HashFor(rid) is not string locked)
        {
            await context.Error.WriteLineAsync($"lockflight: link {InstalledFlag} needs an engine lock with an entry for this platform ({reading.Problem ?? "none"}); {install}");
            return ExitCode.Config;
        }

        if (context.Environment.HomeDirectory() is not string home
            || InstalledEngines(home, engineLock.Version, rid).FirstOrDefault(path => EngineRecord.HashOf(path) == locked) is not string installed)
        {
            await context.Error.WriteLineAsync($"lockflight: Lockflight {engineLock.Version} with the locked hash is not installed in ~/.lockflight/tools/{engineLock.Version}; {install}");
            return ExitCode.Fail;
        }

        string target = Link(manifest.Root, installed);
        await context.Output.WriteLineAsync($"linked the installed lockflight {engineLock.Version} -> {target}");
        return ExitCode.Pass;
    }

    /// <summary>The executables of <paramref name="version"/> under the user's tools: the one <c>engine install</c> writes, then a <c>dotnet tool install</c>'s.</summary>
    private static IEnumerable<string> InstalledEngines(string home, string version, string rid)
    {
        string toolPath = UserInstall.ToolPath(home, version);
        string direct = Path.Combine(toolPath, EngineRids.ExecutableName(rid));
        if (File.Exists(direct))
        {
            yield return direct;
        }

        string store = Path.Combine(toolPath, ".store");
        if (!Directory.Exists(store))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(store, EngineRids.ExecutableName(rid), SearchOption.AllDirectories))
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) == rid)
            {
                yield return file;
            }
        }
    }
}
