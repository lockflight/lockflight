namespace Lockflight.Cli.Config;

/// <summary>Why a manifest is invalid, with the 1-based position so the fix is one jump away.</summary>
internal sealed record ManifestError(string Path, int Line, int Column, string Message)
{
    /// <summary>Compiler-style location prefix, e.g. <c>/repo/lockflight.yaml:7:3: unknown key 'gats'</c>.</summary>
    public string Describe() => $"{Path}:{Line}:{Column}: {Message}";
}
