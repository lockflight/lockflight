using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Step P0 of the judge: resolves the merge base, reads the base policy, finds the red commits and their fork points, classifies the
/// change and lays out the build steps — git only, nothing is built or run.
/// </summary>
internal static class JudgePlanner
{
    private const string Shallow =
        "the clone is shallow and does not hold the merge base of the head and the target branch; fetch the full history " +
        "(GitHub: actions/checkout with fetch-depth: 0; Bitbucket: clone: depth: full; GitLab: GIT_DEPTH: \"0\"; Azure: fetchDepth: 0)";

    private static readonly string[] IgnoredVariables =
    [
        EnvironmentVariables.SpecifyFeature, EnvironmentVariables.SpecifyFeatureDirectory, EnvironmentVariables.Debug, EnvironmentVariables.HookLog,
        EnvironmentVariables.PluginApi, EnvironmentVariables.PluginMinimumEngine, EnvironmentVariables.PluginRoot, EnvironmentVariables.Mascot,
    ];

    public static async Task<JudgePlanning> PlanAsync(JudgeInputs inputs, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        string root = inputs.Root;
        string policyCommit = inputs.Base ?? inputs.Head;
        BasePolicyReading policyReading = await BasePolicyReader.ReadAsync(root, policyCommit, cancellationToken);
        if (policyReading.Policy is not BasePolicy policy)
        {
            return JudgePlanning.Failed(ExitCode.Config, policyReading.Error!);
        }

        string? mergeBase = null;
        IReadOnlyList<string> others = [];
        if (inputs.Base is string baseCommit)
        {
            IReadOnlyList<string> mergeBases = await JudgeGit.MergeBasesAsync(root, baseCommit, inputs.Head, cancellationToken);
            if (mergeBases.Count == 0)
            {
                return JudgePlanning.Failed(ExitCode.Config, await JudgeGit.IsShallowAsync(root, cancellationToken)
                    ? Shallow
                    : $"the head {CommitIds.Short(inputs.Head)} and the target branch {CommitIds.Short(baseCommit)} share no history; the pull request cannot be judged against it");
            }

            mergeBase = mergeBases[0];
            others = [.. mergeBases.Skip(1)];
        }

        RedCommitSelection reds = new([], [], [], OverCap: false, LabelsOutOfOrder: false);
        if (inputs.Event == JudgeEvent.PullRequest && mergeBase is not null)
        {
            if (await JudgeGit.HistoryAsync(root, mergeBase, inputs.Head, cancellationToken) is not IReadOnlyList<CommitTrailers> history)
            {
                return JudgePlanning.Failed(ExitCode.Error, $"git could not read the history {CommitIds.Short(mergeBase)}..{CommitIds.Short(inputs.Head)}");
            }

            reds = await WithForkPointsAsync(root, RedCommitSelector.Select(history, policy.Ci.MaxRedCommits), inputs.Base!, cancellationToken);
        }

        if (await ClassifyAsync(root, mergeBase, inputs, policy, cancellationToken) is not (InterimClassification classification, IReadOnlyList<string> changed))
        {
            return JudgePlanning.Failed(ExitCode.Error, $"git could not list the files the pull request changes since {CommitIds.Short(mergeBase ?? inputs.Head)}");
        }

        RequirementSet requirements = InterimRequirements.For(classification.Classification.Class, classification.Shape);
        if (inputs.Event == JudgeEvent.PullRequest && inputs.Base is not null)
        {
            if (await RouteAsync(inputs, policy, cancellationToken) is not (InterimClassification routed, RequirementSet routedRequirements))
            {
                return JudgePlanning.Failed(ExitCode.Error, $"git could not read the change set {CommitIds.Short(inputs.Base)}...{CommitIds.Short(inputs.Head)} for routing");
            }

            (classification, requirements) = (routed, routedRequirements);
        }

        IReadOnlyDictionary<CheckId, CheckLevel> levels = CheckLevels.For(requirements, classification.ProductionChanged, inputs.Event);
        IReadOnlyList<string> ignored = await IgnoredAsync(root, inputs.Head, environment, cancellationToken);
        JudgePlan plan = new(inputs, policy, mergeBase, others, reds, classification, requirements, levels, Steps(inputs, mergeBase, reds, levels), changed, ignored);
        return JudgePlanning.Planned(plan);
    }

    /// <summary>
    /// The change's class from the base branch's routing policy (risk routing): the change set since the merge base, the raises
    /// (a covering feature's class line, its class record), the classifier and the class's requirements; null when git fails.
    /// </summary>
    private static async Task<(InterimClassification, RequirementSet)?> RouteAsync(JudgeInputs inputs, BasePolicy policy, CancellationToken cancellationToken)
    {
        string preset = RoutingPresetNames.ToName((policy.Manifest?.Routing ?? RoutingSettings.Default).Preset);
        RoutingPolicy routing = RoutingPolicy.From(policy.Manifest, new PolicySource(inputs.Base!, policy.Commit, policy.ManifestBlob, preset));
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(inputs.Root, inputs.Base!, inputs.Head, cancellationToken);
        if (reading.ChangeSet is not ChangeSet changeSet)
        {
            return null;
        }

        RaiseReading raises = await RaiseReader.ReadAsync(inputs.Root, changeSet, new RaiseInputs(inputs.Branch, [], Raise: null), cancellationToken);
        ChangeAnalysis analysis = ChangeAnalyzer.Analyze(changeSet, routing.ExcludedGlobs);
        Classification classification = ChangeClassifier.Classify(analysis, routing, raises.Status == RaiseReadStatus.Read ? raises.Raises : []);
        bool productionChanged = analysis.Files.Any(file => file.Role == FileRole.Production && file.Added.Count + file.Removed.Count > 0);
        InterimClassification routed = new(classification, InterimShape.Other) { Source = InterimClassification.RoutingSource, ProductionChanged = productionChanged };
        return (routed, ClassRequirements.For(classification.Class, routing));
    }

    private static async Task<RedCommitSelection> WithForkPointsAsync(string root, RedCommitSelection selection, string baseCommit, CancellationToken cancellationToken)
    {
        List<RedCommit> all = [];
        foreach (RedCommit red in selection.All)
        {
            IReadOnlyList<string> forks = red.IsCycle ? await JudgeGit.MergeBasesAsync(root, red.Commit, baseCommit, cancellationToken) : [];
            all.Add(red with { ForkPoint = forks.Count > 0 ? forks[0] : null });
        }

        return selection with { All = all, Cycles = [.. all.Where(red => red.IsCycle)] };
    }

    private static async Task<(InterimClassification, IReadOnlyList<string>)?> ClassifyAsync(
        string root, string? mergeBase, JudgeInputs inputs, BasePolicy policy, CancellationToken cancellationToken)
    {
        PolicySource source = new(inputs.Base ?? "none", policy.Commit, policy.ManifestBlob, Preset: "interim");
        if (mergeBase is null)
        {
            // A push without a previous commit: nothing to compare, so the change is treated as a behaviour change.
            return (InterimClassifier.Classify(["(no base to compare with)"], policy.Ci.LowRiskPaths, new HashSet<string>(), new HashSet<string>(), null, source), []);
        }

        if (await JudgeGit.ChangedFilesAsync(root, mergeBase, inputs.Head, cancellationToken) is not IReadOnlyList<string> changed
            || await JudgeGit.TreeFilesAsync(root, mergeBase, cancellationToken) is not IReadOnlyList<string> atBase
            || await JudgeGit.TreeFilesAsync(root, inputs.Head, cancellationToken) is not IReadOnlyList<string> atHead)
        {
            return null;
        }

        IReadOnlyList<string> packageFiles = InterimClassifier.PackageFiles(changed);
        string? packageDiff = packageFiles.Count == 0 ? string.Empty : await JudgeGit.DiffHunksAsync(root, mergeBase, inputs.Head, packageFiles, cancellationToken);
        InterimClassification classification = InterimClassifier.Classify(
            changed, policy.Ci.LowRiskPaths, atBase.ToHashSet(StringComparer.Ordinal), atHead.ToHashSet(StringComparer.Ordinal), packageDiff, source);
        return (classification, changed);
    }

    private static List<PlanStep> Steps(JudgeInputs inputs, string? mergeBase, RedCommitSelection reds, IReadOnlyDictionary<CheckId, CheckLevel> levels)
    {
        List<PlanStep> steps =
        [
            new("P0", null, "inputs", "resolve the merge base, the base policy, the red commits and the change class (git only)", 0),
            new("P1", inputs.Head, "head", "build, list and run the whole suite with the base policy laid over", 1),
        ];
        // Green compares the head with the base's inventory: nothing deleted, skipped or made explicit since.
        bool needsBase = mergeBase is not null && levels[CheckId.Green] != CheckLevel.Off;
        if (needsBase)
        {
            steps.Add(new("P2", mergeBase, "base", "build and list the tests (no tests run): the baseline inventory", 1));
            foreach (string forkPoint in reds.Cycles.Select(red => red.ForkPoint).OfType<string>().Where(fork => fork != mergeBase).Distinct(StringComparer.Ordinal))
            {
                steps.Add(new("P2", forkPoint, "fork point", "build and list the tests: the inventory a red commit made before a merge of the target is compared with", 1));
            }
        }

        if (levels[CheckId.Red] != CheckLevel.Off && !reds.OverCap)
        {
            foreach (RedCommit red in reds.Cycles)
            {
                steps.Add(new("P3", red.Commit, $"red {red.Label}", "build, list and run the tests new at this commit, then classify how each failed", 1));
            }

            if (reds.Cycles.Count > 0)
            {
                steps.Add(new("P4", null, "re-check", "only for cycle tests changed since their red: run them on the red commit's code", reds.Cycles.Count));
            }
        }

        steps.Add(new("P5", inputs.Head, "trace and gates", "trace from the spec and the pr gates, reusing the head build", 0));
        return steps;
    }

    private static async Task<IReadOnlyList<string>> IgnoredAsync(string root, string head, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        List<string> ignored = [];
        IReadOnlyList<string> files = await JudgeGit.TreeFilesAsync(root, head, cancellationToken) ?? [];
        if (files.Contains(".specify/feature.json", StringComparer.Ordinal))
        {
            ignored.Add(".specify/feature.json");
        }

        ignored.AddRange(files.Where(IsRecordFile).Select(path => path.EndsWith("/redgreen.json", StringComparison.Ordinal) ? path : path[..(path.IndexOf("/reviews/", StringComparison.Ordinal) + "/reviews/".Length)]).Distinct(StringComparer.Ordinal));
        ignored.AddRange(IgnoredVariables.Where(name => !string.IsNullOrEmpty(environment.Get(name))));
        return ignored;
    }

    /// <summary>A committed red/green record or review run: local evidence the judge recomputes instead of reading.</summary>
    private static bool IsRecordFile(string path) =>
        path.StartsWith(".lockflight/features/", StringComparison.Ordinal)
        && (path.EndsWith("/redgreen.json", StringComparison.Ordinal) || path.Contains("/reviews/", StringComparison.Ordinal));
}
