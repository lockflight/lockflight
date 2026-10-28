namespace Lockflight.Cli.Docs;

/// <summary>Where a line of a Markdown document sits, which decides what the drift check reads in it.</summary>
internal enum MarkdownLineKind
{
    /// <summary>Text, list items and tables: links, code spans and commands are read.</summary>
    Prose = 0,

    /// <summary>An ATX heading (<c>## Title</c>): an anchor target, and read like prose.</summary>
    Heading = 1,

    /// <summary>A line inside a fenced code block: only command lines of shell fences are read.</summary>
    Fenced = 2,

    /// <summary>The opening or closing line of a fenced code block.</summary>
    FenceMarker = 3,

    /// <summary>A line the author marked with the drift-ignore comment: never read.</summary>
    Ignored = 4,
}
