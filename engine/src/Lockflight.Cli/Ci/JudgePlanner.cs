using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Workspace;

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

    public static Task<JudgePlanning> PlanAsync(JudgeInputs inputs, EnvironmentReader environment, CancellationToken cancellationToken) =>
        PlanAsync(inputs, environment, shell: null, cancellationToken);

    /// <param name="shell">
    /// The workspace shell's policy for this unit repository, read from its clone; null for a repository that is no unit. Its
    /// routing is merged with the unit's as <c>gate run --shell</c> merges it (<see cref="ShellPolicyOverlay"/>), the stricter
    /// value winning, so the class and its check levels are never lower than the shell's policy makes them.
    /// </param>
    public static async Task<JudgePlanning> PlanAsync(JudgeInputs inputs, EnvironmentReader environment, ShellPolicy? shell, CancellationToken cancellationToken)
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
            JudgeRouting routing = await RouteAsync(inputs, RoutingPolicyOf(inputs, policy, shell), cancellationToken);
            if (routing is not { Class: InterimClassification routed, Requirements: RequirementSet routedRequirements })
            {
                return JudgePlanning.Failed(ExitCode.Error, routing.Error!);
            }

            (classification, requirements) = (routed, routedRequirements);
        }

        IReadOnlyDictionary<CheckId, CheckLevel> levels = CheckLevels.For(requirements, classification.ProductionChanged, inputs.Event);
        IReadOnlyList<string> ignored = await IgnoredAsync(root, inputs.Head, environment, cancellationToken);
        JudgePlan plan = new(inputs, policy, mergeBase, others, reds, classification, requirements, levels, Steps(inputs, mergeBase, reds, levels), changed, ignored);
        if (inputs.Event == JudgeEvent.PullRequest && inputs.Base is not null && policy.Manifest is Manifest baseManifest)
        {
            Dictionary<CheckId, CheckLevel> withPolicy = new(levels) { [CheckId.Policy] = CheckLevel.Required };
            (IReadOnlyList<string> loosened, string? notCompared) = await LoosenedAsync(root, inputs.Head, baseManifest, cancellationToken);
            plan = plan with { Levels = withPolicy, Loosened = loosened, PolicyNotCompared = notCompared };
        }

        return JudgePlanning.Planned(plan);
    }

    /// <summary>
    /// How the head's <c>lockflight.yaml</c> is looser than the base's: removed counts as loosened (later pull requests would be
    /// judged by the defaults). One that does not parse is not compared: it may be written for a newer engine (a new key, a raised
    /// <c>requiresEngine</c>, which is not checked here), so the check is NOT-EARNED, not FAIL.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Loosened, string? NotCompared)> LoosenedAsync(
        string root, string head, Manifest baseManifest, CancellationToken cancellationToken)
    {
        if (await JudgeGit.ShowFileAsync(root, head, Manifest.FileName, cancellationToken) is not string text)
        {
            return ([$"{Manifest.FileName} removed"], null);
        }

        ManifestParser parser = new(root, $"{Manifest.FileName} at {CommitIds.Short(head)}") { Engine = ManifestParser.LocalBuildVersion };
        ManifestLoadResult parsed = parser.Parse(text);
        return parsed.Manifest is Manifest headManifest
            ? (PolicyLoosening.Between(baseManifest, headManifest), null)
            : ([], $"the head's {Manifest.FileName} could not be compared with the base's: {parsed.Error?.Describe()}; a person checks what it changes");
    }

    /// <summary>
    /// The change's class from the base branch's routing policy (risk routing): the change set since the merge base, the raises
    /// (a covering feature's class line, its class record), the classifier and the class's requirements. A git failure reading
    /// the change set or the raises is an error: a raise that could not be read must not leave the class lower than it is.
    /// The pull request's <c>lockflight:class/*</c> labels are not passed in yet: the action has no input for them, so CI does
    /// not read labels (docs/guides/risk-routing.md says so).
    /// </summary>
    private static async Task<JudgeRouting> RouteAsync(JudgeInputs inputs, RoutingPolicy routing, CancellationToken cancellationToken)
    {
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(inputs.Root, inputs.Base!, inputs.Head, cancellationToken);
        if (reading.ChangeSet is not ChangeSet changeSet)
        {
            return JudgeRouting.Failed($"git could not read the change set {CommitIds.Short(inputs.Base!)}...{CommitIds.Short(inputs.Head)} for routing");
        }

        RaiseReading raises = await RaiseReader.ReadAsync(inputs.Root, changeSet, new RaiseInputs(inputs.Branch, Labels: [], Raise: null), cancellationToken);
        return Route(changeSet, routing, raises);
    }

    /// <summary>The base branch's routing policy, made at least as strict as the workspace shell's for a unit repository.</summary>
    private static RoutingPolicy RoutingPolicyOf(JudgeInputs inputs, BasePolicy policy, ShellPolicy? shell)
    {
        Manifest? manifest = shell is not null && policy.Manifest is Manifest unit ? ShellPolicyOverlay.Apply(unit, shell) : policy.Manifest;
        string preset = RoutingPresetNames.ToName((manifest?.Routing ?? RoutingSettings.Default).Preset);
        return RoutingPolicy.From(manifest, new PolicySource(inputs.Base!, policy.Commit, policy.ManifestBlob, preset));
    }

    /// <summary>The routing result for a change set that was read and its raises; an error when the raises could not be read.</summary>
    internal static JudgeRouting Route(ChangeSet changeSet, RoutingPolicy routing, RaiseReading raises)
    {
        if (raises.Status != RaiseReadStatus.Read)
        {
            return JudgeRouting.Failed($"the change's raises could not be read, so its class cannot be computed: {string.Join("; ", raises.Notes)}");
        }

        ChangeAnalysis analysis = ChangeAnalyzer.Analyze(changeSet, routing.ExcludedGlobs);
        Classification classification = ChangeClassifier.Classify(analysis, routing, raises.Raises);
        InterimClassification routed = new(classification, InterimShape.Other) { Source = InterimClassification.RoutingSource, ProductionChanged = ProductionChange.Of(analysis) };
        return JudgeRouting.Routed(routed, ClassRequirements.For(classification.Class, routing));
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
