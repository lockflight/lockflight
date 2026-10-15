namespace Lockflight.Cli.Ledger;

/// <summary>Contract spellings of <see cref="GateTrigger"/> in the JSONL ledger.</summary>
internal static class GateTriggerNames
{
    public const string Command = "command";
    public const string StopHook = "stop-hook";

    public static string ToName(GateTrigger trigger) => trigger switch
    {
        GateTrigger.Command => Command,
        GateTrigger.StopHook => StopHook,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown gate trigger."),
    };
}
