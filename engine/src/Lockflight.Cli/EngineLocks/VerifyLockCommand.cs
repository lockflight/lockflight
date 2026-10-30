using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// <c>lockflight engine verify-lock &lt;file&gt; [--source &lt;feed&gt;]</c>: compares an engine lock with the hashes the release of its
/// version published (exit 0 when they match, 1 when they do not or there is no such release). CI runs it on a pull request's
/// changed lock, so the reviewer reads "matches release vX" instead of judging hashes; it checks integrity against the feed,
/// and GitHub's attestation of the release is verified separately where <c>gh</c> runs. Read only.
/// </summary>
internal sealed class VerifyLockCommand(CliContext context) : ICommand
{
    /// <summary>Also verify GitHub's attestation of the release's hashes with the <c>gh</c> CLI (it fails without one).</summary>
    public const string AttestationFlag = "--attestation";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Rest.Count == 0 || arguments.Rest[0].StartsWith('-'))
        {
            await context.Error.WriteLineAsync("lockflight: engine verify-lock takes the lock file first: engine verify-lock <file> [--source <feed>] [--attestation]");
            return ExitCode.Config;
        }

        if (await CommandOptions.ParseOrReportAsync(arguments.Command.Name, [.. arguments.Rest.Skip(1)], arguments.Command.ValueOptions, arguments.Command.FlagOptions, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        string path = Path.GetFullPath(arguments.Rest[0], context.WorkingDirectory);
        if (!File.Exists(path))
        {
            await context.Error.WriteLineAsync($"lockflight: no file at {path}");
            return ExitCode.Config;
        }

        EnginePackageFeed feed = options.Value("--source") is string source ? new EnginePackageFeed(source, context.Environment) : EnginePackageFeed.FromEnvironment(context.Environment);
        LockReleaseCheck check = await LockReleaseCheck.RunAsync(
            await File.ReadAllTextAsync(path, cancellationToken), feed, options.Flag(AttestationFlag) ? context.Processes : null, cancellationToken);
        await context.Output.WriteLineAsync($"engine lock: {check.Detail}");
        return check.Verdict == LockReleaseVerdict.Matches ? ExitCode.Pass : ExitCode.Fail;
    }
}
