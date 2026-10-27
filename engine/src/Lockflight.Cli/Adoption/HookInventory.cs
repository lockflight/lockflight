using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Reads the command hooks of a Claude Code settings file (<c>hooks.&lt;Event&gt;[].hooks[]</c>). The script a command runs is
/// resolved when its first word (or the word after an interpreter) is a path, with <c>$CLAUDE_PROJECT_DIR</c> standing for the
/// project the settings belong to and <c>~</c> for the user's home.
/// An unreadable file yields no hooks; the plan says so through its absence, it never fails the inventory.
/// </summary>
internal static class HookInventory
{
    public static IReadOnlyList<ClaudeHook> Read(string settingsPath, string scope, string projectDirectory)
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        JsonObject? settings;
        try
        {
            settings = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return [];
        }

        List<ClaudeHook> hooks = [];
        if (settings?["hooks"] is not JsonObject events)
        {
            return hooks;
        }

        foreach ((string eventName, JsonNode? groups) in events)
        {
            foreach (JsonObject group in (groups as JsonArray ?? []).OfType<JsonObject>())
            {
                string? matcher = group["matcher"]?.GetValueKind() == JsonValueKind.String ? group["matcher"]!.GetValue<string>() : null;
                foreach (JsonObject hook in (group["hooks"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if (hook["command"]?.GetValueKind() == JsonValueKind.String)
                    {
                        string command = hook["command"]!.GetValue<string>();
                        hooks.Add(new ClaudeHook(scope, eventName, matcher, command, ScriptOf(command, projectDirectory)));
                    }
                }
            }
        }

        return hooks;
    }

    private static readonly string[] Interpreters = ["sh", "bash", "zsh", "python", "python3", "node", "pwsh"];

    /// <summary>
    /// The file a command runs, if it exists: its first shell word, or the second after an interpreter (<c>bash x.sh</c>). Quotes
    /// may wrap the whole word or part of it (<c>"$CLAUDE_PROJECT_DIR"/.claude/hooks/x.sh</c>, needed in a path with a space).
    /// </summary>
    private static string? ScriptOf(string command, string projectDirectory)
    {
        List<string> words = Words(command, count: 2);
        string first = words.Count > 1 && Interpreters.Contains(Path.GetFileName(words[0]), StringComparer.Ordinal) ? words[1] : words.FirstOrDefault() ?? string.Empty;
        string path = first
            .Replace("${CLAUDE_PROJECT_DIR}", projectDirectory, StringComparison.Ordinal)
            .Replace("$CLAUDE_PROJECT_DIR", projectDirectory, StringComparison.Ordinal);
        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }

        return path.Length > 0 && Path.IsPathRooted(path) && File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    /// <summary>Up to <paramref name="count"/> leading shell words, with quotes removed (variables are left as written).</summary>
    private static List<string> Words(string command, int count)
    {
        List<string> words = [];
        System.Text.StringBuilder word = new();
        bool inWord = false;
        char quote = '\0';
        foreach (char character in command)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(character);
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                inWord = true;
            }
            else if (char.IsWhiteSpace(character) || character is ';' or '|' or '&')
            {
                if (inWord)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                    if (words.Count == count)
                    {
                        return words;
                    }
                }

                if (!char.IsWhiteSpace(character))
                {
                    return words;
                }
            }
            else
            {
                word.Append(character);
                inWord = true;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }
}
