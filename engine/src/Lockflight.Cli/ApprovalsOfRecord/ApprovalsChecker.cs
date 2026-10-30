using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// Checks the approval facts of one pull request in git, from the base branch's policy: that the base's CODEOWNERS gives every spec
/// path (<c>approvals.specPaths</c>) an owner, so the platform's required code-owner review covers the spec, and, where spec-first
/// applies, that the feature's spec was merged on the base first and the pull request changes no spec. Who approved is the
/// platform's to enforce and is never read.
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
        if (SpecFirstProblem(specFirst) is string specProblem)
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
        IReadOnlyList<string> specPaths = request.Settings.SpecPaths;
        if (!await JudgeGit.HasCommitAsync(request.Root, request.Base, cancellationToken)
            || await JudgeGit.TreeFilesAsync(request.Root, request.Base, cancellationToken) is not IReadOnlyList<string> baseFiles)
        {
            return new CodeownersCoverage(File: null, request.Base, [], [.. specPaths], $"git cannot read the base commit {CommitIds.Short(request.Base)} (is the clone deep enough?)");
        }

        foreach (string location in request.Codeowners.Locations.Where(baseFiles.Contains))
        {
            if (await JudgeGit.ShowFileAsync(request.Root, request.Base, location, cancellationToken) is not string text)
            {
                return new CodeownersCoverage(location, request.Base, [], [.. specPaths], $"git cannot show {location} at {CommitIds.Short(request.Base)}");
            }

            CodeownersFile codeowners = CodeownersFile.Parse(location, text, request.Codeowners.Sections);
            List<string> covers = [];
            List<string> missing = [];
            foreach (string glob in specPaths)
            {
                if (Probes(glob, baseFiles, request.ChangedPaths).FirstOrDefault(path => codeowners.OwnersOf(path).Count == 0) is string unowned)
                {
                    missing.Add($"{glob} ({unowned} has no owner)");
                }
                else
                {
                    covers.Add(glob);
                }
            }

            return new CodeownersCoverage(location, request.Base, covers, missing, GitError: null);
        }

        return new CodeownersCoverage(File: null, request.Base, [], [.. specPaths], GitError: null);
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

        string[] changedSpecs = [.. request.ChangedPaths.Where(path => request.Settings.SpecPaths.Any(glob => PathRules.Matches(glob, path))).Distinct(StringComparer.Ordinal)];
        if (request.FeatureId is not string feature)
        {
            return new SpecFirstFacts(Applies: true, because, SpecPath: null, SpecOnBase: false, changedSpecs);
        }

        string specPath = SpecDirectory + feature + SpecFile;
        bool onBase = await JudgeGit.BlobOfAsync(request.Root, request.Base, specPath, cancellationToken) is not null;
        return new SpecFirstFacts(Applies: true, because, specPath, onBase, changedSpecs);
    }

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
        string globs = string.Join(", ", request.Settings.SpecPaths);
        if (coverage.GitError is string gitError)
        {
            yield return $"{gitError}; the CODEOWNERS coverage of {globs} could not be checked";
            yield break;
        }

        if (coverage.File is null)
        {
            yield return request.Codeowners.Locations.Count == 0
                ? $"this platform reads no CODEOWNERS file, so Lockflight cannot check who must approve {globs}; require reviewers for them in the branch policy, and set approvals.check: report"
                : $"the base branch has no CODEOWNERS file (looked for {string.Join(", ", request.Codeowners.Locations)}); add one that gives {globs} an owner, and require code-owner review on the platform";
            yield break;
        }

        foreach (string missing in coverage.Missing)
        {
            yield return $"the base branch's {coverage.File} gives no owner to {missing}; add an owner for it, so the platform's required code-owner review covers the spec";
        }
    }

    private static string? SpecFirstProblem(SpecFirstFacts facts)
    {
        string why = string.Join("; ", facts.Because);
        return facts switch
        {
            { Satisfied: true } => null,
            { SpecPath: null } => $"spec-first applies ({why}) but no feature spec was found for this pull request; name the branch after the feature (specs/<id>/) and merge its spec first",
            { SpecOnBase: false } => $"spec-first applies ({why}): {facts.SpecPath} is not on the base branch; merge the spec in its own pull request first, then re-run this check",
            _ => $"spec-first applies ({why}): this pull request changes {string.Join(", ", facts.ChangedSpecs)}; a spec changes in its own pull request, approved before the code",
        };
    }
}
