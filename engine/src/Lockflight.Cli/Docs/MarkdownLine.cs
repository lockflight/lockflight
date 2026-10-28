namespace Lockflight.Cli.Docs;

/// <summary>One line of a Markdown document: its 1-based number, text, kind and, inside a fence, the fence's language (lower case, empty when untagged).</summary>
internal sealed record MarkdownLine(int Number, string Text, MarkdownLineKind Kind, string FenceLanguage);
