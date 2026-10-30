namespace Lockflight.Cli.Ci;

/// <summary>
/// The judge job's time budget (<c>ci.timeoutMinutes</c> on the base). Before each build the judge checks that more than the
/// reserve is left; each build or test process is bounded by the gates' per-process limit and by the time left minus the reserve,
/// so no process outlives the budget and the evidence is always written before the platform's own limit kills the job.
/// </summary>
internal sealed class JudgeBudget(TimeSpan limit, TimeProvider time)
{
    /// <summary>Kept back for writing the evidence after the last step.</summary>
    public static readonly TimeSpan Reserve = TimeSpan.FromMinutes(3);

    /// <summary>How <see cref="Why"/> starts, so the checks the budget left undecided can be told apart.</summary>
    public const string WhyStart = "the CI budget (";

    private readonly long started = time.GetTimestamp();
    private readonly List<string> notRun = [];

    public TimeSpan Limit => limit;

    public TimeSpan Used => time.GetElapsedTime(started);

    public bool Exhausted => notRun.Count > 0;

    /// <summary>The steps skipped because the budget ran out, in order.</summary>
    public IReadOnlyList<string> NotRun => notRun;

    /// <summary>Whether a step may start; when it may not, it is recorded as not run.</summary>
    public bool TryStart(string step)
    {
        if (limit - Used > Reserve)
        {
            return true;
        }

        notRun.Add(step);
        return false;
    }

    /// <summary>The per-process limit: the smaller of the gates' limit and the time left before the reserve (at least one second).</summary>
    public TimeSpan ProcessLimit(TimeSpan gatesLimit)
    {
        TimeSpan left = limit - Used - Reserve;
        return left < gatesLimit ? (left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1)) : gatesLimit;
    }

    /// <summary>Why the checks not yet decided are NOT-EARNED.</summary>
    public string Why(int builds) =>
        $"{WhyStart}{(int)limit.TotalMinutes} min, ci.timeoutMinutes on the target branch) ran out after {builds} builds; raise it on the target branch or split the pull request";
}
