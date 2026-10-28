using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>
/// Slash commands of a plugin the repository itself holds (<c>/acme:approve</c> with <c>…/acme/skills/approve/SKILL.md</c> or
/// <c>…/acme/commands/approve.md</c> tracked). A plugin that is only installed is invisible here, so its commands are not checked.
/// </summary>
internal sealed partial class PluginCommandCheck(ILookup<string, string> commandsByPlugin)
{
    public static PluginCommandCheck For(IEnumerable<string> trackedFiles)
    {
        List<(string Plugin, string Command)> commands = [];
        foreach (string path in trackedFiles)
        {
            if (SkillPattern().Match(path) is { Success: true } skill)
            {
                commands.Add((skill.Groups["plugin"].Value, skill.Groups["name"].Value));
            }
            else if (CommandFilePattern().Match(path) is { Success: true } command)
            {
                commands.Add((command.Groups["plugin"].Value, command.Groups["name"].Value));
            }
        }

        return new PluginCommandCheck(commands.ToLookup(entry => entry.Plugin, entry => entry.Command, StringComparer.Ordinal));
    }

    public static bool IsSlashCommand(string span) => SlashCommandPattern().IsMatch(span);

    public DriftFinding? Check(string document, int lineNumber, string span)
    {
        if (SlashCommandPattern().Match(span) is not { Success: true } match)
        {
            return null;
        }

        string plugin = match.Groups["plugin"].Value;
        string name = match.Groups["name"].Value;
        return commandsByPlugin.Contains(plugin) && !commandsByPlugin[plugin].Contains(name, StringComparer.Ordinal)
            ? new DriftFinding(document, lineNumber, $"/{plugin}:{name}", $"plugin '{plugin}' has no skill or command '{name}'", DriftKind.Command)
            : null;
    }

    [GeneratedRegex(@"^/(?<plugin>[a-z0-9][a-z0-9-]*):(?<name>[a-z0-9][a-z0-9-]*)(?:\s|$)")]
    private static partial Regex SlashCommandPattern();

    [GeneratedRegex(@"(?:^|/)(?<plugin>[^/]+)/skills/(?<name>[^/]+)/SKILL\.md$")]
    private static partial Regex SkillPattern();

    [GeneratedRegex(@"(?:^|/)(?<plugin>[^/]+)/commands/(?<name>[^/]+)\.md$")]
    private static partial Regex CommandFilePattern();
}
