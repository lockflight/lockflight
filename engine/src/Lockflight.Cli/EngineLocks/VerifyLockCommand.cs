using System.Security.Cryptography;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// <c>lockflight engine verify-lock &lt;file&gt; [--source &lt;feed&gt;] [--base &lt;file&gt;] [--script &lt;file&gt;]</c>: compares an engine lock, its install
/// script's hash included, with the hashes the release of its version published, and the vendored install script, when given,
/// with the lock (exit 0 when they match, 1 when they do not or there is no such release). CI runs it on a pull request's
/// changed lock, so the reviewer reads "matches release vX" instead of judging hashes; it checks integrity against the feed,
/// and GitHub's attestation of the release is verified separately where <c>gh</c> runs. Read only.
/// </summary>
internal sealed class VerifyLockCommand(CliContext context) : ICommand
{
    /// <summary>Also verify GitHub's attestation of the release's hashes with the <c>gh</c> CLI (it fails without one).</summary>
    public const string AttestationFlag = "--attestation";

    /// <summary>The lock the pull request replaces (the base commit's), so a move to an older engine is named.</summary>
    public const string BaseOption = "--base";

    /// <summary>The pull request's vendored install script, which must be the one the lock pins.</summary>
    public const string ScriptOption = "--script";

    /// <summary>The usage line, kept with the command's options.</summary>
    public const string Usage = "engine verify-lock <file> [--source <feed>] [--base <file>] [--script <file>] [--attestation]";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Rest.Count == 0 || arguments.Rest[0].StartsWith('-'))
        {
            await context.Error.WriteLineAsync($"lockflight: engine verify-lock takes the lock file first: {Usage}");
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

        string? baseLock = null;
        if (options.Value(BaseOption) is string basePath)
        {
            string fullBase = Path.GetFullPath(basePath, context.WorkingDirectory);
            if (File.Exists(fullBase))
            {
                baseLock = await File.ReadAllTextAsync(fullBase, cancellationToken);
            }
            else
            {
                await context.Output.WriteLineAsync($"engine lock: warning: no base lock at {fullBase}, so a move to an older engine is not checked");
            }
        }

        string lockText = await File.ReadAllTextAsync(path, cancellationToken);
        EnginePackageFeed feed = options.Value("--source") is string source ? new EnginePackageFeed(source, context.Environment) : EnginePackageFeed.FromEnvironment(context.Environment);
        LockReleaseCheck check = await LockReleaseCheck.RunAsync(
            lockText, baseLock, feed, options.Flag(AttestationFlag) ? context.Processes : null, cancellationToken);
        await context.Output.WriteLineAsync($"engine lock: {check.Detail}");
        if (check.Verdict != LockReleaseVerdict.Matches)
        {
            return ExitCode.Fail;
        }

        if (options.Value(ScriptOption) is not string scriptPath)
        {
            return ExitCode.Pass;
        }

        string fullScript = Path.GetFullPath(scriptPath, context.WorkingDirectory);
        string pinned = EngineLockReader.Read(lockText, allowDevelopment: false, ToolPin.RunningVersion).Lock!.InstallScriptSha256;
        if (!File.Exists(fullScript))
        {
            await context.Output.WriteLineAsync($"engine lock: there is no install script at {fullScript} to compare with the one the lock pins");
            return ExitCode.Fail;
        }

        string vendored = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fullScript, cancellationToken)));
        if (vendored != pinned)
        {
            await context.Output.WriteLineAsync($"engine lock: {InstallScript.RelativePath} is not the script the lock pins (its SHA-256 is {vendored}, the lock's {pinned}); CI would refuse to run it");
            return ExitCode.Fail;
        }

        await context.Output.WriteLineAsync($"engine lock: {InstallScript.RelativePath} is the script the lock pins");
        return ExitCode.Pass;
    }
}
