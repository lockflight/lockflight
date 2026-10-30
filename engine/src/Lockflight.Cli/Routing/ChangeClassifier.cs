using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Computes a pull request's change class, as a pure function of its change set, the base branch's policy and the raises from
/// outside the diff: <c>computed = max(routing.floor, every signal)</c>, <c>class = max(computed, every raise)</c>. Nothing
/// subtracts, so every input can only raise the class. A move out of a <c>routing.critical</c> path is a failure of the routing
/// rules (<c>routing.criticalMove</c>), on top of the class.
/// </summary>
internal static class ChangeClassifier
{
    public static Classification Classify(ChangeSet diff, RoutingPolicy policy, IReadOnlyList<ClassRaise> raises) =>
        Classify(ChangeAnalyzer.Analyze(diff, policy.ExcludedGlobs), policy, raises);

    public static Classification Classify(ChangeAnalysis analysis, RoutingPolicy policy, IReadOnlyList<ClassRaise> raises)
    {
        List<ChangeSignal> signals = [.. ChangeSignalSources.All
            .SelectMany(source => source.Find(analysis, policy))
            .Distinct()
            .OrderByDescending(signal => signal.RaisesTo)
            .ThenBy(signal => signal.Id, StringComparer.Ordinal)
            .ThenBy(signal => signal.Where, StringComparer.Ordinal)];
        ChangeClass computed = Max(policy.Settings.Floor, signals.Select(signal => signal.RaisesTo));
        ChangeClass final = Max(computed, raises.Select(raise => raise.To));
        return new Classification(final, computed, signals, raises, CriticalMoves(analysis, policy), policy.Source);
    }

    /// <summary>Exact moves whose old path is critical and whose new path is not: the one move a path boundary can refuse outright.</summary>
    private static List<string> CriticalMoves(ChangeAnalysis analysis, RoutingPolicy policy) =>
        [.. analysis.Moves
            .Where(move => move.Kind == MoveKind.Exact && policy.CriticalGlobOf(move.OldPath) is not null && policy.CriticalGlobOf(move.NewPath) is null)
            .Select(move => $"{SignalIds.CriticalMove}: {move.OldPath} moved to {move.NewPath}, outside routing.critical; add its new path to routing.critical on the base branch first, then move it")];

    private static ChangeClass Max(ChangeClass floor, IEnumerable<ChangeClass> classes) =>
        classes.Aggregate(floor, (highest, next) => next > highest ? next : highest);
}
