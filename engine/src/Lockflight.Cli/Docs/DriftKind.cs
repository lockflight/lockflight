namespace Lockflight.Cli.Docs;

/// <summary>What kind of reference in a document does not resolve.</summary>
internal enum DriftKind
{
    /// <summary>A Markdown link or image whose target file or directory does not exist.</summary>
    Link = 0,

    /// <summary>A link to a heading that the target document does not have.</summary>
    Anchor = 1,

    /// <summary>A path in an inline code span that was deleted or renamed, or that lives elsewhere.</summary>
    Path = 2,

    /// <summary>A command line of the engine or of a tracked plugin that the command does not accept.</summary>
    Command = 3,

    /// <summary>An instruction file longer than Claude Code recommends.</summary>
    Budget = 4,
}
