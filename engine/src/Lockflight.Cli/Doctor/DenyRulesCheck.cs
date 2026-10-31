using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether Claude Code itself would refuse its file tools on the protected paths: the <c>Edit(…)</c> rules <c>init</c> lists in
/// <c>permissions.deny</c> of <c>.claude/settings.json</c>, against the rules <c>init</c> would write for the project as it is now.
/// A rule the team removed, or a protected file created since <c>init</c>, is named with the fix.
/// </summary>
internal static class DenyRulesCheck
{
    private const string Name = "deny rules";
    private const int MaxListed = 5;

    /// <summary>Null when the project's settings cannot be read as JSON: the <c>claude settings</c> check reports that.</summary>
    public static DoctorCheck? Check(string root)
    {
        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? text = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        if (ClaudeSettingsMerger.Problem(text, nativeHooks: false) is not null)
        {
            return null;
        }

        IReadOnlyList<string> listed = PermissionDenyRules.Entries(text is null ? null : JsonNode.Parse(text));
        Manifest? manifest = ManifestLocator.Discover(root).Manifest;
        IReadOnlyList<string> expected = PermissionDenyRules.For(root, manifest is null ? DefaultProtectedPaths.Globs : DefaultProtectedPaths.For(manifest));
        string[] missing = [.. expected.Except(listed, StringComparer.Ordinal)];
        if (missing.Length > 0 && missing.Length == expected.Count)
        {
            return new(Name, CheckStatus.Warn, $"{ClaudeSettingsMerger.RelativePath} lists no Edit deny rules for the protected paths, so only the guard stops Claude Code's file tools there", $"{EngineCommand.Name} init");
        }

        if (missing.Length > 0)
        {
            string names = string.Join(", ", missing.Take(MaxListed)) + (missing.Length > MaxListed ? ", …" : string.Empty);
            string install = missing.Contains(PermissionDenyRules.UserInstallRule, StringComparer.Ordinal) ? " (the user's Lockflight install among them)" : string.Empty;
            return new(Name, CheckStatus.Warn, $"{missing.Length} protected paths{install} have no Edit deny rule: {names}", $"{EngineCommand.Name} init (it adds them)");
        }

        return new(Name, CheckStatus.Pass, $"Claude Code refuses its file tools on the protected paths ({expected.Count} Edit deny rules)", null);
    }
}
