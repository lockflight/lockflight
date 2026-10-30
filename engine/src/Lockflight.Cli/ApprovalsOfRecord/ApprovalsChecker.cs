using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// Checks the approval facts of one pull request in git, from the base branch's policy: that the base's CODEOWNERS gives every spec
/// path (<c>approvals.specPaths</c>) an owner, so the platform's required code-owner review covers the spec, and, where spec-first
/// applies, that the feature's spec was merged on the base first and the pull request changes no spec. In a unit repository of a
/// workspace shell both are the shell's: its CODEOWNERS and its specs at the clone's commit. Who approved is the platform's to
/// enforce and is never read.
/// </summary>
internal static class ApprovalsChecker
{
    /// <summary>The probe a spec glob stands for when the base has no file it matches: a spec file under a made-up feature.</summary>
    private const string ProbeFeature = "lockflight-probe";

    private const string ProbeFile = "spec.md";

    /// <summary>Where spec-first looks for a feature's spec (<c>specs/&lt;id&gt;/spec.md</c>): the Spec Kit layout, whatever <c>approvals.specPaths</c> says.</summary>
    private const string SpecDirectory = "specs/";

    private const string SpecFile = "/spec.md";

    public static async Task<ApprovalsFacts> CheckAsync(ApprovalsRequest request, CancellationToken cancellationToken)
    {
        CodeownersCoverage coverage = await CoverageAsync(request, cancellationToken);
        SpecFirstFacts specFirst = await SpecFirstAsync(request, cancellationToken);
        List<string> problems = [.. CoverageProblems(request, coverage)];
        if (SpecFirstProblem(specFirst, Branch(request)) is string specProblem)
        {
            problems.Add(specProblem);
        }

        return new ApprovalsFacts(request.Settings.Check, request.Settings.Mode, coverage, specFirst, problems);
    }

    /// <summary>
    /// The CODEOWNERS file the platform reads on the base and which spec globs it gives an owner, probing the spec paths on the base
    /// and those the pull request changes, or, for a glob that matches none of them, a made-up spec file.
    /// </summary>
    private static async Task<CodeownersCoverage> CoverageAsync(ApprovalsRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> specPaths = SpecPaths(request);
        (string root, string baseCommit) = Where(request);
        IReadOnlyList<string> changedPaths = request.Shell is null ? request.ChangedPaths : [];
        if (!await JudgeGit.HasCommitAsync(root, baseCommit, cancellationToken)
            || await JudgeGit.TreeFilesAsync(root, baseCommit, cancellationToken) is not IReadOnlyList<string> baseFiles)
        {
            return new CodeownersCoverage(File: null, baseCommit, [], [.. specPaths], $"git cannot read the {(request.Shell is null ? "base" : "workspace shell's")} commit {CommitIds.Short(baseCommit)} (is the clone deep enough?)");
        }

        foreach (string location in request.Codeowners.Locations.Where(baseFiles.Contains))
        {
            if (await JudgeGit.ShowFileAsync(root, baseCommit, location, cancellationToken) is not string text)
            {
                return new CodeownersCoverage(location, baseCommit, [], [.. specPaths], $"git cannot show {location} at {CommitIds.Short(baseCommit)}");
            }

            CodeownersFile codeowners = CodeownersFile.Parse(location, text, request.Codeowners.Sections);
            List<string> covers = [];
            List<string> missing = [];
            foreach (string glob in specPaths)
            {
                if (Probes(glob, baseFiles, changedPaths).FirstOrDefault(path => codeowners.OwnersOf(path).Count == 0) is string unowned)
                {
                    missing.Add($"{glob} ({unowned} has no owner)");
                }
                else
                {
                    covers.Add(glob);
                }
            }

            return new CodeownersCoverage(location, baseCommit, covers, missing, GitError: null);
        }

        return new CodeownersCoverage(File: null, baseCommit, [], [.. specPaths], GitError: null);
    }

    private static string[] Probes(string glob, IReadOnlyList<string> baseFiles, IReadOnlyList<string> changedPaths)
    {
        string[] real = [.. baseFiles.Concat(changedPaths).Where(path => PathRules.Matches(glob, path)).Distinct(StringComparer.Ordinal)];
        if (real.Length > 0)
        {
            return real;
        }

        string madeUp = glob.Replace("**", $"{ProbeFeature}/{ProbeFile}", StringComparison.Ordinal).Replace("*", ProbeFile, StringComparison.Ordinal).Replace("?", "p", StringComparison.Ordinal);
        return PathRules.Matches(glob, madeUp) ? [madeUp] : [];
    }

    private static async Task<SpecFirstFacts> SpecFirstAsync(ApprovalsRequest request, CancellationToken cancellationToken)
    {
        string[] because = [.. Reasons(request)];
        if (because.Length == 0)
        {
            return SpecFirstFacts.NotApplicable;
        }

        string[] changedSpecs = request.Shell is null
            ? [.. request.ChangedPaths.Where(path => request.Settings.SpecPaths.Any(glob => PathRules.Matches(glob, path))).Distinct(StringComparer.Ordinal)]
            : [];
        if (request.FeatureId is not string feature)
        {
            return new SpecFirstFacts(Applies: true, because, SpecPath: null, SpecOnBase: false, changedSpecs);
        }

        string specPath = SpecDirectory + feature + SpecFile;
        (string root, string baseCommit) = Where(request);
        bool onBase = await JudgeGit.BlobOfAsync(root, baseCommit, specPath, cancellationToken) is not null;
        return new SpecFirstFacts(Applies: true, because, specPath, onBase, changedSpecs);
    }

    /// <summary>Where the specs and the CODEOWNERS that covers them are read: the workspace shell's clone, else the base.</summary>
    private static (string Root, string Commit) Where(ApprovalsRequest request) =>
        request.Shell is ApprovalsShell shell ? (shell.Root, shell.Commit) : (request.Root, request.Base);

    /// <summary>The branch the specs are merged on, as messages name it.</summary>
    private static string Branch(ApprovalsRequest request) => request.Shell is null ? "the base branch" : "the workspace shell's branch";

    private static IReadOnlyList<string> SpecPaths(ApprovalsRequest request) => request.Shell?.SpecPaths ?? request.Settings.SpecPaths;

    /// <summary>Why spec-first applies: the default, the class, a changed path under a spec-first glob; empty when it does not.</summary>
    private static IEnumerable<string> Reasons(ApprovalsRequest request)
    {
        ApprovalSettings settings = request.Settings;
        if (settings.Mode == ApprovalMode.SpecFirst)
        {
            yield return "approvals.default=spec-first";
        }

        if (settings.SpecFirstFrom is ChangeClass from && request.Class >= from)
        {
            yield return $"specFirstFrom={ChangeClassNames.ToName(from)} (class {ChangeClassNames.ToName(request.Class)})";
        }

        foreach (string glob in settings.SpecFirstPaths)
        {
            if (request.ChangedPaths.FirstOrDefault(path => PathRules.Matches(glob, path)) is string matched)
            {
                yield return $"specFirstPaths {glob} ({matched})";
            }
        }
    }

    private static IEnumerable<string> CoverageProblems(ApprovalsRequest request, CodeownersCoverage coverage)
    {
        string globs = string.Join(", ", SpecPaths(request));
        if (coverage.GitError is string gitError)
        {
            yield return $"{gitError}; the CODEOWNERS coverage of {globs} could not be checked";
            yield break;
        }

        if (coverage.File is null)
        {
            yield return request.Codeowners.Locations.Count == 0
                ? $"this platform reads no CODEOWNERS file, so Lockflight cannot check who must approve {globs}; require reviewers for them in the branch policy, and set approvals.check: report"
                : $"{Branch(request)} has no CODEOWNERS file (looked for {string.Join(", ", request.Codeowners.Locations)}); add one that gives {globs} an owner, and require code-owner review on the platform";
            yield break;
        }

        foreach (string missing in coverage.Missing)
        {
            yield return $"{Branch(request)}'s {coverage.File} gives no owner to {missing}; add an owner for it, so the platform's required code-owner review covers the spec";
        }
    }

    private static string? SpecFirstProblem(SpecFirstFacts facts, string branch)
    {
        string why = string.Join("; ", facts.Because);
        return facts switch
        {
            { Satisfied: true } => null,
            { SpecPath: null } => $"spec-first applies ({why}) but no feature spec was found for this pull request; name the branch after the feature (specs/<id>/) and merge its spec first",
            { SpecOnBase: false } => $"spec-first applies ({why}): {facts.SpecPath} is not on {branch}; merge the spec in its own pull request first, then re-run this check",
            _ => $"spec-first applies ({why}): this pull request changes {string.Join(", ", facts.ChangedSpecs)}; a spec changes in its own pull request, approved before the code",
        };
    }
}
