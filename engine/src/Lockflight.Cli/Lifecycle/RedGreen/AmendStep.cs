using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen amend &lt;test&gt; --reason "…"</c> (human-only): a person allows one test of the open red cycle to change
/// after its red was accepted, because its expectation was wrong or the API it uses changed. The guard then lets writes through to
/// the file that declares the test, and only to it, until the next <c>redgreen red</c> re-checks the amended test; green is refused
/// until then. The amend records who (git <c>user.email</c>), when and why, in the evidence and the ledger, and status, trace and the
/// review's pull request summary show it. A guardrail like the other human decisions.
/// </summary>
internal sealed class AmendStep(RedGreenSession session)
{
    public const string Usage = "lockflight redgreen amend <test> --reason \"<why the test must change>\" [--feature <id>]";

    private const string UnknownPerson = "unknown";

    public async Task<ExitCode> RunAsync(RedGreenRecord? record, string test, string reason, CancellationToken cancellationToken)
    {
        AmendTargetLookup lookup = RedGreenAmends.Find(session.Manifest.Root, record, test);
        if (lookup.Target is not AmendTarget target)
        {
            return await session.FailAsync($"nothing was amended: {lookup.Problem}", cancellationToken);
        }

        string person = await Git.ConfigValueAsync(session.Manifest.Root, "user.email", cancellationToken) ?? UnknownPerson;
        await RecordAsync(session, record!, target, person, reason, cancellationToken);
        await session.Output.WriteLineAsync($"Amended {target.Test.MethodFilter} in red cycle {target.Cycle.Number}: {RedGreenAmends.Unlocks(target)}");
        return ExitCode.Pass;
    }

    /// <summary>Adds the amend to the evidence and the ledger, whichever way the person gave it.</summary>
    public static async Task RecordAsync(
        RedGreenSession session, RedGreenRecord record, AmendTarget target, string person, string reason, CancellationToken cancellationToken)
    {
        session.Save(RedGreenAmends.Add(record, target, person, session.Context.Time.GetUtcNow(), reason));
        await session.RecordAsync(
            LedgerSignatures.RedGreenAmended,
            $"{session.Feature.Id}: cycle {target.Cycle.Number}, {target.Test.MethodFilter} amended ({string.Join(", ", target.Files)})",
            $"by {person}: {reason}",
            cancellationToken);
    }
}
