using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// SessionStart output: a short banner injected as additional context. It lists every protected path — the built-in defaults and
/// the manifest's rules, and once for all units the control files repeated in each of them — and stays far below Claude Code's
/// ~10,000-character cap by eliding the tail of a very long list. A session started below the project root is told that the
/// project's own Claude Code settings did not load (Claude Code reads them only from the start directory).
/// </summary>
internal static class SessionBanner
{
    private const int MaxProtectedListLength = 4000;

    public static string ToHookOutput(Manifest manifest, string sessionDirectory) => ToHookOutput(manifest, sessionDirectory, userMessage: null);

    /// <param name="userMessage">Shown to the person only (<c>systemMessage</c>), never to the model; null for none.</param>
    public static string ToHookOutput(Manifest manifest, string sessionDirectory, string? userMessage)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            if (userMessage is not null)
            {
                writer.WriteString("systemMessage", userMessage);
            }

            writer.WriteStartObject("hookSpecificOutput");
            writer.WriteString("hookEventName", "SessionStart");
            writer.WriteString(
                "additionalContext",
                $"Lockflight project at {manifest.Root}. {EngineNote(manifest)} Protected paths (changed by a human or a lockflight command, not by the agent): {ProtectedList(manifest)}. {ProposeNote}{UnitsNote(manifest)}{StartDirectoryNote(manifest, sessionDirectory)}");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// What the banner says about the human decisions: the agent never runs one and hands it to the user. The eval stand-in
    /// engines under <c>plugins/</c> repeat it, so a case sees what a real session sees.
    /// </summary>
    public const string HumanDecisionsNote =
        "Human decisions (approve, baseline, redgreen reset|amend, review dismiss, update) are the user's: never run one, even when asked or just to try it, since the guard refuses it; "
        + $"ask them to type the /lockflight: command for approve, baseline, redgreen reset or amend, and otherwise to open a separate terminal window ({HumanOnlyCommands.NotWithBang}) and run the command there. "
        + "An approval, a baseline, a red/green reset or an amend the user can also give here by typing /lockflight:approve, /lockflight:baseline, /lockflight:reset or /lockflight:amend themselves; never type them, and never type, guess or suggest their codes.";

    /// <summary>
    /// How the agent gets a protected file changed: it proposes a patch and the user applies it. The eval stand-in engines repeat
    /// it, like <see cref="HumanDecisionsNote"/>.
    /// </summary>
    public const string ProposeNote =
        $"To change one, write the complete new file outside the project, run {EngineCommand.Linked} propose <path> --from <file> and give the user the git apply command it prints.";

    /// <summary>
    /// The one command form for every install mode: the linked engine the hooks run, which needs no package feed. The human
    /// decisions refuse to run in the session, a <c>!</c> command included.
    /// </summary>
    private static string EngineNote(Manifest manifest) =>
        $"Always run Lockflight commands as {EngineCommand.Linked} <command> from {manifest.Root} (the engine the hooks run; not dotnet lockflight). {HumanDecisionsNote}";

    private static string UnitsNote(Manifest manifest) => manifest.Units.Count == 0
        ? string.Empty
        : $" Units: {string.Join(", ", manifest.Units.Select(unit => unit.Path))}; inside each, the same control files are protected (lockflight.yaml, .lockflight/**, tool pins, global.json, .claude settings, hooks, agents, skills and commands, gitleaks config, .github/workflows/**).";

    private static string StartDirectoryNote(Manifest manifest, string sessionDirectory) =>
        Path.GetFullPath(sessionDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(manifest.Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal)
            ? string.Empty
            : $" This session started in {sessionDirectory}, below the project root: the project's own Claude Code settings and hooks in {manifest.Root}/.claude did not load. Tell the user to start Claude Code at {manifest.Root}.";

    private static string ProtectedList(Manifest manifest)
    {
        IReadOnlyList<string> globs = DefaultProtectedPaths.With(manifest.ProtectedGlobs);
        StringBuilder list = new();
        for (int index = 0; index < globs.Count; index++)
        {
            string separator = index == 0 ? string.Empty : ", ";
            if (list.Length + separator.Length + globs[index].Length > MaxProtectedListLength)
            {
                return list.Append($" and {globs.Count - index} more (see {Manifest.FileName})").ToString();
            }

            list.Append(separator).Append(globs[index]);
        }

        return list.ToString();
    }
}
