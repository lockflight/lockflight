namespace Lockflight.Cli.Hooks.Requests;

/// <summary>The tool a call uses: the client's own name (for messages and the ledger) and what it does.</summary>
/// <param name="Name">The client's tool name (<c>Edit</c>); null when the event has no tool.</param>
/// <param name="Shell">Which shell's quoting a <see cref="ToolKind.Shell"/> command follows; null for other kinds.</param>
/// <param name="WritesFiles">
/// Whether the call writes the files it names: true for edit, write and notebook tools, and for an <see cref="ToolKind.Other"/>
/// tool whose input names a file path. Rules on file writes test this flag, never a list of kinds.
/// </param>
internal sealed record HookTool(string? Name, ToolKind Kind, ShellDialect? Shell, bool WritesFiles)
{
    public static HookTool None { get; } = new(Name: null, ToolKind.Other, Shell: null, WritesFiles: false);

    /// <summary>Whether the tool only reads (read or search): what lockdown and recovery allow.</summary>
    public bool OnlyReads => Kind is ToolKind.Read or ToolKind.Search;
}
