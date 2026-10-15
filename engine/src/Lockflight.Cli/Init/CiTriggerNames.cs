namespace Lockflight.Cli.Init;

/// <summary>Command-line spellings of <see cref="CiTrigger"/>.</summary>
internal static class CiTriggerNames
{
    public static bool TryParse(string name, out CiTrigger trigger)
    {
        CiTrigger? parsed = name switch
        {
            "pr" => CiTrigger.PullRequest,
            "manual" => CiTrigger.Manual,
            "none" => CiTrigger.None,
            _ => null,
        };
        trigger = parsed ?? CiTrigger.PullRequest;
        return parsed is not null;
    }
}
