namespace Lockflight.Cli.Init;

/// <summary>
/// Adds Lockflight's <c>.gitattributes</c> rules: the standard profile's, so feature records are checked out byte-exact
/// (<c>-text</c>) and the evidence a person reviews and CI reads is the same bytes on every OS despite <c>core.autocrlf</c>; and
/// the native level's, for its hook shim.
/// </summary>
internal static class GitAttributesUpdater
{
    public const string Rule = ".lockflight/features/** -text";

    private const string BlockHeader = "# Lockflight evidence: byte-exact on every OS";

    /// <summary>The native level's rule: the vendored hook shim keeps LF endings, which <c>sh</c> needs, despite <c>core.autocrlf</c>.</summary>
    public const string HookShimRule = NativeHookShim.RelativePath + " text eol=lf";

    private const string HookShimHeader = "# Lockflight hook shim: run by sh, LF on every OS";

    public static (string Content, bool Changed) Update(string? existing) => Add(existing, BlockHeader, Rule);

    public static (string Content, bool Changed) UpdateHookShim(string? existing) => Add(existing, HookShimHeader, HookShimRule);

    private static (string Content, bool Changed) Add(string? existing, string header, string rule)
    {
        string content = existing ?? string.Empty;
        if (content.Split('\n').Any(line => line.Trim() == rule))
        {
            return (content, false);
        }

        string separator = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        return (content + separator + (content.Length == 0 ? string.Empty : "\n") + header + "\n" + rule + "\n", true);
    }
}
