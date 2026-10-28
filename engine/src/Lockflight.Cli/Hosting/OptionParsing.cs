namespace Lockflight.Cli.Hosting;

/// <summary>How a command reads its options.</summary>
internal enum OptionParsing
{
    /// <summary>With <see cref="CommandOptions"/>, against the descriptor's option lists: an unknown option lists the accepted ones.</summary>
    Shared = 0,

    /// <summary>With its own parser (<c>init</c>, <c>update</c>, <c>propose</c>) or none at all; the descriptor's lists document them.</summary>
    Own = 1,
}
