using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The spec from the head's tree, found in this order: the branch name as Spec Kit names feature branches (its last segment as
/// the directory under <c>specs/</c>, else the one directory with its number), the only feature directory the pull request
/// changes, the only one at head. <c>.specify/feature.json</c> and <c>SPECIFY_FEATURE</c> are never read. The branch is the
/// agent's to name, so "no spec" is not an easy way out: when <c>specs/</c> exists and a cycle test names a requirement, a red
/// commit's subject names an existing feature, or the class is feature or critical, a missing spec cannot be vouched for; and the
/// branch and a red commit's subject naming different features cannot either.
/// </summary>
internal sealed partial class TreeSpecSource : ISpecSource
{
    private const string Specs = "specs/";

    public async Task<SpecResolution> ResolveAsync(JudgePlan plan, string? branch, string? shellPath, CancellationToken cancellationToken) =>
        await ResolveAsync(plan, branch, [], cancellationToken);

    /// <summary>As the interface, with the requirements the accepted cycle tests name (they make a missing spec suspicious).</summary>
    public static async Task<SpecResolution> ResolveAsync(JudgePlan plan, string? branch, IReadOnlyList<string> testRequirements, CancellationToken cancellationToken)
    {
        string root = plan.Inputs.Root;
        string head = plan.Inputs.Head;
        IReadOnlyList<string> files = await JudgeGit.TreeFilesAsync(root, head, cancellationToken) ?? [];
        string[] features = [.. files.Where(path => path.StartsWith(Specs, StringComparison.Ordinal)).Select(path => path.Split('/')).Where(parts => parts.Length >= 3).Select(parts => parts[1]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string[] named = [.. plan.Reds.Cycles.Select(red => SubjectFeature().Match(red.Subject)).Where(match => match.Success).Select(match => match.Groups["id"].Value).Where(features.Contains).Distinct(StringComparer.Ordinal)];

        (string? id, string source) = ByBranch(branch, features) is string fromBranch ? (fromBranch, "branch")
            : ChangedFeature(plan.ChangedFiles, features) is string changed ? (changed, "specs-diff")
            : features.Length == 1 ? (features[0], "single")
            : ((string?)null, "none");
        if (id is null)
        {
            bool suspicious = features.Length > 0
                && (testRequirements.Count > 0 || named.Length > 0 || plan.Class.Classification.Class >= ChangeClass.Feature);
            return SpecResolution.None(suspicious
                ? "no spec was found for this pull request, but its tests name requirements, a red commit names a feature, or its class needs a spec; name the branch after the feature (specs/<id>/)"
                : null);
        }

        if (named.FirstOrDefault(other => other != id) is string disagreeing)
        {
            return new SpecResolution(id, source, null, null, null, SpecOrigin.Head, $"the branch and the tests disagree on the feature: {id} by the {source}, {disagreeing} by a red commit's subject");
        }

        string specPath = $"{Specs}{id}/spec.md";
        string? spec = await JudgeGit.ShowFileAsync(root, head, specPath, cancellationToken);
        string? tasks = await JudgeGit.ShowFileAsync(root, head, $"{Specs}{id}/tasks.md", cancellationToken);
        return new SpecResolution(id, source, specPath, spec, tasks, SpecOrigin.Head, spec is null ? $"{specPath} does not exist at head" : null);
    }

    private static string? ByBranch(string? branch, string[] features)
    {
        if (branch is null)
        {
            return null;
        }

        string segment = branch[(branch.LastIndexOf('/') + 1)..];
        if (features.Contains(segment, StringComparer.Ordinal))
        {
            return segment;
        }

        Match number = FeatureNumber().Match(segment);
        string[] matches = number.Success ? [.. features.Where(feature => feature.StartsWith(number.Groups["number"].Value + "-", StringComparison.Ordinal))] : [];
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? ChangedFeature(IReadOnlyList<string> changed, string[] features)
    {
        string?[] touched = [.. changed.Select(FeatureLocator.FeatureIdOf).Where(id => id is not null && features.Contains(id)).Distinct(StringComparer.Ordinal)];
        return touched.Length == 1 ? touched[0] : null;
    }

    [GeneratedRegex(@"^[a-z]+\((?<id>[^)\s]+)\)!?:")]
    private static partial Regex SubjectFeature();

    [GeneratedRegex(@"^(?<number>[0-9]{8}-[0-9]{6}|[0-9]{3,})-")]
    private static partial Regex FeatureNumber();
}
