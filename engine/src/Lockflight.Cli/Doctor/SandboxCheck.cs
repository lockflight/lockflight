using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether the Claude Code sandbox would refuse shell writes to the protected paths: the write denials <c>init</c> lists in
/// <c>.claude/settings.json</c>, whether they cover the protected files that exist now, and whether the sandbox is on. The
/// sandbox state is read from the settings files Claude Code would use, highest first: managed, local, project, user.
/// </summary>
internal static class SandboxCheck
{
    private const string Name = "sandbox";
    private const int MaxListed = 5;
    private const string LocalSettings = ".claude/settings.local.json";

    /// <summary>Null when the project's settings cannot be read as JSON: the <c>claude settings</c> check reports that.</summary>
    public static DoctorCheck? Check(string root, string? userHome)
    {
        string settingsPath = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? text = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        if (ClaudeSettingsMerger.Problem(text) is not null)
        {
            return null;
        }

        JsonNode? project = text is null ? null : JsonNode.Parse(text);
        IReadOnlyList<string> denied = SandboxSettings.Entries(project, SandboxSettings.DenyWriteKey);
        if (denied.Count == 0)
        {
            return new(Name, CheckStatus.Warn, $"{ClaudeSettingsMerger.RelativePath} lists no sandbox write denials for the protected paths", $"{EngineCommand.Name} init");
        }

        Manifest? manifest = ManifestLocator.Discover(root).Manifest;
        IReadOnlyList<string> expected = SandboxSettings.DenyWrite(root, manifest is null ? DefaultProtectedPaths.Globs : DefaultProtectedPaths.For(manifest));
        string[] missing = [.. expected.Except(denied, StringComparer.Ordinal)];
        if (missing.Length > 0)
        {
            string listed = string.Join(", ", missing.Take(MaxListed)) + (missing.Length > MaxListed ? ", …" : string.Empty);
            return new(Name, CheckStatus.Warn, $"{missing.Length} protected paths are not in the sandbox write denials: {listed}", $"{EngineCommand.Name} init (it adds them)");
        }

        if (OperatingSystem.IsWindows())
        {
            return new(Name, CheckStatus.Pass, $"write denials listed for {denied.Count} paths, but native Windows has no Claude Code sandbox: shell writes are checked by the guard only (WSL2 has the sandbox)", null);
        }

        return Enabled(root, userHome)
            ? new(Name, CheckStatus.Pass, $"on: the operating system refuses shell writes to {denied.Count} protected paths", null)
            : new(Name, CheckStatus.Pass, $"off: write denials listed for {denied.Count} paths take effect once the sandbox is on (/sandbox, or sandbox.enabled in your settings)", null);
    }

    /// <summary>The first <c>sandbox.enabled</c> set in managed, local, project or user settings; false when none sets it.</summary>
    private static bool Enabled(string root, string? userHome)
    {
        string?[] files =
        [
            ManagedSettingsPath(),
            Path.Combine(root, LocalSettings),
            Path.Combine(root, ClaudeSettingsMerger.RelativePath),
            userHome is null ? null : Path.Combine(userHome, ClaudeSettingsMerger.RelativePath),
        ];
        foreach (string file in files.OfType<string>().Where(File.Exists))
        {
            if (EnabledIn(file) is bool enabled)
            {
                return enabled;
            }
        }

        return false;
    }

    private static bool? EnabledIn(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file))?[SandboxSettings.SandboxKey]?[SandboxSettings.EnabledKey]?.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ManagedSettingsPath() =>
        OperatingSystem.IsMacOS() ? "/Library/Application Support/ClaudeCode/managed-settings.json"
        : OperatingSystem.IsLinux() ? "/etc/claude-code/managed-settings.json"
        : null;
}
