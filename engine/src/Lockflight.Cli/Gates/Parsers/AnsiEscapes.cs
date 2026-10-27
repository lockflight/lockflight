using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Removes terminal control sequences (colours, cursor moves) from a tool's captured output. <c>dotnet test</c> colours
/// Microsoft.Testing.Platform's output in some environments even when it is redirected (for example with <c>GITHUB_ACTIONS=true</c>),
/// and a colour code at the start of a line hides the summary's figures and the failed tests' names from the parsers.
/// </summary>
internal static partial class AnsiEscapes
{
    public static string Strip(string output) => output.Contains('\u001b') ? ControlSequence().Replace(output, string.Empty) : output;

    // CSI sequences (ESC [ parameters intermediates final), and the two-character escapes such as ESC ( B.
    [GeneratedRegex(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|[()][A-Za-z0-9]|[@-Z\\-_])")]
    private static partial Regex ControlSequence();
}
