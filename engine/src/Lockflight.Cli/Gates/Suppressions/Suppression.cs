namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>
/// One suppression a change adds: the file (relative to the project root, '/'-separated), the 1-based line in the working tree, the
/// rule it silences (a diagnostic id such as <c>CA1822</c>, <c>category-Design</c>, <c>all</c> for every warning, <c>nullable</c>,
/// <c>ExcludeFromCodeCoverage</c>) and how.
/// </summary>
internal sealed record Suppression(string File, int Line, string Rule, SuppressionKind Kind)
{
    /// <summary>What the suppressions baseline accepts: this rule in this file, at any line.</summary>
    public string Key => KeyOf(File, Rule);

    public static string KeyOf(string file, string rule) => $"{file}|{rule}";

    public string Describe() => $"{File}:{Line} {Rule} ({SuppressionKindNames.ToDisplay(Kind)})";
}
