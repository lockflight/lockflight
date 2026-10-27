using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Linking;

/// <summary>
/// <c>lockflight link</c>: copies the running (pinned, NuGet-restored) binary to <c>.lockflight/bin/</c> in the project, where
/// hooks execute it directly — <c>dotnet tool run</c> costs ~160 ms per hook call. Only a single-file (Native AOT) binary is
/// copied: a framework-dependent build cannot run from a copy of its launcher.
/// </summary>
internal sealed class LinkCommand(CliContext context)
{
    public const string RelativeBinDirectory = ".lockflight/bin";

    /// <summary>The platforms with a Native AOT engine package (the tool's runtime identifiers).</summary>
    public const string SupportedPlatforms = "osx-arm64, osx-x64, linux-x64, linux-arm64 and win-x64";

    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>The linked engine of the project at <paramref name="root"/> (<c>.exe</c> on Windows).</summary>
    public static string BinaryPath(string root) =>
        Path.Combine(root, RelativeBinDirectory, OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight");

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

        string target = BinaryPath(manifest.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Copy to a temporary name and rename, so a concurrently running hook never sees a half-written binary.
        string temporary = target + ".tmp";
        File.Copy(source, temporary, overwrite: true);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, ExecutableMode);
        }

        File.Move(temporary, target, overwrite: true);
        await context.Output.WriteLineAsync($"linked lockflight {ToolPin.RunningVersion} -> {target}");
        return ExitCode.Pass;
    }
}
