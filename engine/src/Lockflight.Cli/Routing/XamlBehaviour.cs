using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Whether a change to a XAML view touches behaviour rather than looks: a changed line inside an <c>x:Code</c> block, or one that
/// sets an event handler (<c>Click="OnClick"</c>, any attribute named like an event whose value is a method name), a command
/// (<c>Command=</c>) or a converter (<c>Converter=</c>). Read from the added and removed lines alone, so a margin edit in a window
/// that wires a handler elsewhere stays visual. A heuristic: the names are the frameworks' conventions, not a XAML parse.
/// </summary>
internal static partial class XamlBehaviour
{
    private const string CodeBlockOpen = "<x:Code";
    private const string CodeBlockClose = "</x:Code>";

    /// <summary>What the change wires (<c>an event handler (Click)</c>), or null when no changed line touches behaviour.</summary>
    public static string? Wired(RoutedFile file, ChangeSet changeSet)
    {
        if (file.Change.Binary)
        {
            return null;
        }

        return Wired(file.Added, changeSet.Head.TextOf(file.Path)) ?? Wired(file.Removed, changeSet.Base.TextOf(file.Path));
    }

    private static string? Wired(IReadOnlyList<RoutedLine> lines, string? fileText)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        List<(int First, int Last)> codeBlocks = CodeBlocks(fileText);
        foreach (RoutedLine line in lines)
        {
            if (line.Text.Contains(CodeBlockOpen, StringComparison.Ordinal) || line.Text.Contains(CodeBlockClose, StringComparison.Ordinal)
                || codeBlocks.Any(block => line.Line >= block.First && line.Line <= block.Last))
            {
                return "an x:Code block";
            }

            if (CommandAttribute().Match(line.Text) is { Success: true } command)
            {
                return $"a command ({command.Groups["name"].Value})";
            }

            if (ConverterAttribute().IsMatch(line.Text))
            {
                return "a converter";
            }

            Match handler = HandlerAttribute().Matches(line.Text).FirstOrDefault(match => !NotAHandler(match)) ?? Match.Empty;
            if (handler.Success)
            {
                return $"an event handler ({handler.Groups["name"].Value})";
            }
        }

        return null;
    }

    private static bool NotAHandler(Match match) =>
        (match.Groups["name"].Value.StartsWith("Is", StringComparison.Ordinal) && char.IsUpper(match.Groups["name"].Value.ElementAtOrDefault(2)))
        || BooleanValue().IsMatch(match.Groups["value"].Value);

    private static List<(int First, int Last)> CodeBlocks(string? fileText)
    {
        List<(int First, int Last)> blocks = [];
        if (fileText is null || !fileText.Contains(CodeBlockOpen, StringComparison.Ordinal))
        {
            return blocks;
        }

        string[] lines = fileText.Split('\n');
        int? open = null;
        for (int index = 0; index < lines.Length; index++)
        {
            if (open is null && lines[index].Contains(CodeBlockOpen, StringComparison.Ordinal))
            {
                open = index + 1;
            }

            if (open is int first && lines[index].Contains(CodeBlockClose, StringComparison.Ordinal))
            {
                blocks.Add((first, index + 1));
                open = null;
            }
        }

        if (open is int unclosed)
        {
            blocks.Add((unclosed, lines.Length));
        }

        return blocks;
    }

    [GeneratedRegex(@"\b(?<name>\w*Command)\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex CommandAttribute();

    [GeneratedRegex(@"\bConverter\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex ConverterAttribute();

    // An attribute named like an event (Click, Loaded, SelectionChanged, KeyDown, …) or an EventSetter's Handler, set to a plain
    // method name rather than a value or a markup extension.
    [GeneratedRegex(
        @"\b(?<name>\w*(?:Click|Clicked|Loaded|Unloaded|Changed|Changing|Opened|Opening|Closed|Closing|Pressed|Released|Tapped|Toggled|Checked|Unchecked|Selected|Unselected|Activated|Deactivated|Initialized|Down|Up|Enter|Leave|Moved|Move|Wheel|GotFocus|LostFocus|Focused|Unfocused|Drop|DragOver|DragStarting|Invoked|Requested|Started|Starting|Completed|Executed|Execute|Appearing|Disappearing|Swiped|Scrolled|ScrollChanged|Handler))\s*=\s*""(?<value>[A-Za-z_][\w]*)""",
        RegexOptions.CultureInvariant)]
    private static partial Regex HandlerAttribute();

    [GeneratedRegex(@"^(?:true|false)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BooleanValue();
}
