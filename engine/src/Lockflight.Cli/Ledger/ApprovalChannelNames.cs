namespace Lockflight.Cli.Ledger;

/// <summary>Contract spellings of <see cref="ApprovalChannel"/> in the JSONL ledger.</summary>
internal static class ApprovalChannelNames
{
    public const string Terminal = "terminal";
    public const string Session = "session";

    public static string ToName(ApprovalChannel channel) => channel switch
    {
        ApprovalChannel.Terminal => Terminal,
        ApprovalChannel.Session => Session,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown approval channel."),
    };
}
