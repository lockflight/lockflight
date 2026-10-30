using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Init;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether CODEOWNERS routes the files that decide the CI verdict to a person: the manifest, the engine lock and the vendored
/// install script, the CI files and CODEOWNERS itself. Every platform runs a pipeline definition the pull request can edit, so
/// the CI check is a boundary only when these need a code owner's review; the setting that makes the platform require it is
/// named, since Lockflight cannot set it. Read from the working tree: CI reads the base branch.
/// </summary>
internal static class CodeownersCheck
{
    private const string Name = "codeowners";

    private static readonly string[] Locations = [".github/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS", ".gitlab/CODEOWNERS", ".bitbucket/CODEOWNERS"];

    public static DoctorCheck? Check(string root)
    {
        if (!File.Exists(EngineLockFiles.CommittedPath(root)))
        {
            return null;
        }

        string setting = PlatformSetting(root);
        string? file = Locations.FirstOrDefault(location => File.Exists(Path.Combine(root, location)));
        if (file is null)
        {
            return new(Name, CheckStatus.Warn, "no CODEOWNERS: a pull request can change the engine lock, the manifest or the CI files with any reviewer",
                $"add CODEOWNERS for them (see the CI hardening guide), then {setting}");
        }

        IReadOnlyList<(string Pattern, bool Owned)> rules = Rules(File.ReadAllLines(Path.Combine(root, file)));
        string[] required = [Manifest.FileName, EngineLock.RelativePath, InstallScript.RelativePath, file, .. CiStepMigration.KnownFiles.Where(path => File.Exists(Path.Combine(root, path)))];
        string[] uncovered = [.. required.Where(path => !Owned(rules, path))];
        return uncovered.Length == 0
            ? new(Name, CheckStatus.Pass, $"{file} routes the manifest, the engine lock, the install script and the CI files to a code owner; {setting}", null)
            : new(Name, CheckStatus.Warn, $"{file} gives no owner to {string.Join(", ", uncovered)}", $"add rules for them, then {setting}");
    }

    /// <summary>The platform's setting that makes a code owner's review required: Lockflight cannot set it.</summary>
    private static string PlatformSetting(string root) =>
        File.Exists(Path.Combine(root, ".gitlab-ci.yml")) || Directory.Exists(Path.Combine(root, ".gitlab"))
            ? "make sure GitLab requires code-owner approval on the default branch (a paid-tier setting: without it this is a guardrail)"
            : File.Exists(Path.Combine(root, "bitbucket-pipelines.yml"))
                ? "make sure Bitbucket requires a reviewer from the code owners on these paths (a branch restriction)"
                : File.Exists(Path.Combine(root, ".azure-pipelines", "lockflight-gates.yml"))
                    ? "make sure Azure Repos requires a reviewer on these paths (a branch policy)"
                    : "make sure a ruleset on the default branch has \"Require review from Code Owners\" on";

    /// <summary>Each rule's pattern as a glob, and whether it names an owner (a rule without one removes ownership).</summary>
    private static List<(string Pattern, bool Owned)> Rules(string[] lines)
    {
        List<(string Pattern, bool Owned)> rules = [];
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('['))
            {
                continue;
            }

            string[] words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool owned = words.Skip(1).TakeWhile(word => !word.StartsWith('#')).Any();
            rules.Add((words[0], owned));
        }

        return rules;
    }

    /// <summary>The last matching rule decides, as on GitHub and GitLab.</summary>
    private static bool Owned(IReadOnlyList<(string Pattern, bool Owned)> rules, string path) =>
        rules.LastOrDefault(rule => Matches(rule.Pattern, path)) is { Pattern: not null } match && match.Owned;

    /// <summary>A CODEOWNERS pattern, gitignore-style: a leading or inner slash anchors it, a trailing slash means a directory.</summary>
    private static bool Matches(string pattern, string path)
    {
        bool directory = pattern.EndsWith('/');
        string body = pattern.Trim('/');
        bool anchored = pattern.StartsWith('/') || body.Contains('/', StringComparison.Ordinal);
        string glob = anchored ? body : $"**/{body}";
        return (!directory && PathRules.Matches(glob, path)) || PathRules.Matches($"{glob}/**", path);
    }
}
