using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Linking;

/// <summary>
/// <c>lockflight link</c>: copies the running (pinned, NuGet-restored) binary to <c>.lockflight/bin/</c> in the project, where
/// hooks execute it directly — <c>dotnet tool run</c> costs ~160 ms per hook call.
/// </summary>
internal sealed class LinkCommand(CliContext context)
{
    public const string RelativeBinDirectory = ".lockflight/bin";

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

        string? source = Environment.ProcessPath;
        if (source is null || !File.Exists(source))
        {
            await context.Error.WriteLineAsync("lockflight: cannot locate the running engine binary");
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
