namespace Lockflight.Cli.Init;

/// <summary>Command-line spellings of <see cref="InitProfile"/>.</summary>
internal static class InitProfileNames
{
    public static bool TryParse(string name, out InitProfile profile)
    {
        InitProfile? parsed = name switch
        {
            "lite" => InitProfile.Lite,
            "standard" => InitProfile.Standard,
            _ => null,
        };
        profile = parsed ?? InitProfile.Lite;
        return parsed is not null;
    }
}
