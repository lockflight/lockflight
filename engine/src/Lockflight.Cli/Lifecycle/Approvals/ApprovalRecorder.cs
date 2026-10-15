using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// Records a person's approval, whichever way they gave it (<c>lockflight approve</c> in a terminal or <c>/lockflight:approve</c> in
/// a session), so both write the same record, signature and ledger line. A signed approval is signed before it replaces the
/// record: when signing fails nothing is recorded, and an existing approval stays as it was.
/// </summary>
internal sealed class ApprovalRecorder(CliContext context, string root)
{
    private const string UnknownApprover = "unknown";
    private const string SignatureSuffix = ".sig";
    private const string UnsignedSuffix = ".unsigned";
    private const int HashPrefixLength = 12;

    /// <summary>
    /// A hook has no terminal for a passphrase prompt, so a key that is not in an agent and needs no touch would wait forever; the
    /// hook's own time limit is longer.
    /// </summary>
    private static readonly TimeSpan SessionSigningTimeLimit = TimeSpan.FromSeconds(60);

    /// <param name="artifacts">The artifact hashes the person approved (the record binds exactly these).</param>
    /// <param name="sign">Whether to SSH-sign the record with <paramref name="keyOption"/>, else <c>git config user.signingkey</c>.</param>
    public async Task<ApprovalRecording> RecordAsync(
        Feature feature,
        ApprovalPhase phase,
        IReadOnlyList<(string Name, string Hash)> artifacts,
        bool sign,
        string? keyOption,
        ApprovalChannel channel,
        CancellationToken cancellationToken)
    {
        string approvedBy = await Git.ConfigValueAsync(root, "user.email", cancellationToken) ?? UnknownApprover;
        ApprovalStore store = new(feature);
        DateTimeOffset now = context.Time.GetUtcNow();
        string record;
        if (sign)
        {
            string? key = keyOption ?? await Git.ConfigValueAsync(root, "user.signingkey", cancellationToken);
            if (key is null)
            {
                return ApprovalRecording.Failed(
                    ExitCode.Config, "--sign needs --key <file> or git config user.signingkey (an SSH key; prefer one that needs a touch to sign)");
            }

            if (await SaveSignedAsync(store, phase, artifacts, approvedBy, now, key, channel, cancellationToken) is ApprovalRecording failure)
            {
                return failure;
            }

            record = store.RecordPath(phase);
        }
        else
        {
            record = store.Save(phase, artifacts, approvedBy, now, ToolPin.RunningVersion);
        }

        // The approver stays in the approval record; the ledger is readable by the agent and needs only what was approved.
        string phaseName = ApprovalPhaseNames.ToName(phase);
        string summary = string.Join(',', artifacts.Select(artifact => $"{artifact.Name}={artifact.Hash[..Math.Min(HashPrefixLength, artifact.Hash.Length)]}"));
        LedgerEvent approval = new(LedgerEventType.ApprovalRecorded, LedgerSignatures.ApprovePrefix + phaseName, $"{feature.Id} {phaseName} approved", null)
        {
            Approval = new LedgerApproval(feature.Id, phaseName, summary, sign, channel),
        };
        await new LedgerWriter(root, context.Time).AppendAsync(approval, cancellationToken);
        return ApprovalRecording.Done(record, approvedBy, sign, summary);
    }

    /// <summary>
    /// Writes the record next to its final place, signs that file, then moves the signature and the record into place. Null when
    /// signed; else the failure, with the existing record and signature left untouched.
    /// </summary>
    private async Task<ApprovalRecording?> SaveSignedAsync(
        ApprovalStore store,
        ApprovalPhase phase,
        IReadOnlyList<(string Name, string Hash)> artifacts,
        string approvedBy,
        DateTimeOffset at,
        string key,
        ApprovalChannel channel,
        CancellationToken cancellationToken)
    {
        string record = store.RecordPath(phase);
        string unsigned = record + UnsignedSuffix;
        JsonFiles.Write(unsigned, store.Render(phase, artifacts, approvedBy, at, ToolPin.RunningVersion));
        File.Delete(unsigned + SignatureSuffix);
        try
        {
            // ssh-keygen asks the agent or the key's owner (Touch ID / security-key touch) here; the engine never sees the private key.
            TimeSpan timeLimit = channel == ApprovalChannel.Session ? SessionSigningTimeLimit : Timeout.InfiniteTimeSpan;
            ProcessResult signing = await ProcessRunner.RunAsync(
                "ssh-keygen", ["-Y", "sign", "-f", ExpandHome(key), "-n", ApprovalStore.SignatureNamespace, unsigned], root, timeLimit, cancellationToken);
            if (signing.ExitCode != 0 || !File.Exists(unsigned + SignatureSuffix))
            {
                string why = signing.TimedOut
                    ? $"ssh-keygen did not finish within {timeLimit.TotalSeconds:0} seconds (a key that asks for a passphrase needs to be in an SSH agent here)"
                    : signing.Combined.Trim();
                return ApprovalRecording.Failed(ExitCode.Error, $"signing failed, so nothing was recorded: {why}");
            }

            File.Move(unsigned, record, overwrite: true);
            File.Move(unsigned + SignatureSuffix, record + SignatureSuffix, overwrite: true);
            return null;
        }
        catch (ExecutableNotFoundException exception)
        {
            return ApprovalRecording.Failed(ExitCode.Error, $"signing failed, so nothing was recorded: {exception.Message}");
        }
        finally
        {
            File.Delete(unsigned);
            File.Delete(unsigned + SignatureSuffix);
        }
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path;
}
