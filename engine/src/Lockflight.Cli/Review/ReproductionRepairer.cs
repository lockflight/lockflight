namespace Lockflight.Cli.Review;

/// <summary>
/// Asks the reviewer, once per finding, to fix a reproduction that did not compile. Every call counts toward the run's budget:
/// it gets what is left after the review and the earlier repairs as its own cap, and none is made once less than
/// <see cref="MinimumBudgetUsd"/> is left. The calls' cost, time and tokens are added to the run's.
/// </summary>
internal sealed class ReproductionRepairer(Func<string, decimal, CancellationToken, Task<ReviewOutcome>> ask, decimal budgetUsd)
{
    public const decimal MinimumBudgetUsd = 0.05m;

    public int Calls { get; private set; }

    public decimal CostUsd { get; private set; }

    public long DurationMs { get; private set; }

    public ReviewUsage Usage { get; private set; } = ReviewUsage.Empty;

    public async Task<ReproductionRepair> RepairAsync(ReproductionRepairRequest request, CancellationToken cancellationToken)
    {
        decimal left = Math.Floor((budgetUsd - CostUsd) * 100m) / 100m;
        if (left < MinimumBudgetUsd)
        {
            return ReproductionRepair.NotAsked;
        }

        Calls++;
        ReviewOutcome reply = await ask(ReviewPrompt.Repair(request), left, cancellationToken);
        CostUsd += reply.CostUsd;
        DurationMs += reply.DurationMs;
        Usage = new ReviewUsage(
            Usage.InputTokens + reply.Usage.InputTokens,
            Usage.OutputTokens + reply.Usage.OutputTokens,
            Usage.CacheReadInputTokens + reply.Usage.CacheReadInputTokens,
            Usage.CacheCreationInputTokens + reply.Usage.CacheCreationInputTokens);
        string? reproduction = reply.Error is null
            ? reply.Findings.Select(finding => finding.Reproduction).FirstOrDefault(candidate => candidate is not null && Reproductions.MethodName(candidate) is not null)
            : null;
        return new ReproductionRepair(true, reproduction);
    }
}
