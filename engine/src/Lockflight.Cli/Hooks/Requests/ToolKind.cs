namespace Lockflight.Cli.Hooks.Requests;

/// <summary>
/// What a tool call does, independent of the client's tool names. The guard decides on the kind (and on
/// <see cref="HookTool.WritesFiles"/>), never on a name, so a client's new tool falls into <see cref="Other"/> and is judged
/// like any unknown tool.
/// </summary>
internal enum ToolKind
{
    /// <summary>Reads a file or lists a directory (<c>read</c>).</summary>
    Read = 1,

    /// <summary>Searches file names or contents (<c>search</c>).</summary>
    Search = 2,

    /// <summary>Changes an existing file (<c>edit</c>).</summary>
    Edit = 3,

    /// <summary>Creates or replaces a file (<c>write</c>).</summary>
    Write = 4,

    /// <summary>Runs a shell command (<c>shell</c>); <see cref="HookTool.Shell"/> says which shell.</summary>
    Shell = 5,

    /// <summary>Edits a notebook (<c>notebook</c>).</summary>
    Notebook = 6,

    /// <summary>Fetches or searches the web (<c>fetch</c>).</summary>
    Fetch = 7,

    /// <summary>Starts a subagent (<c>agent</c>).</summary>
    Agent = 8,

    /// <summary>Any other tool, or no tool at all (<c>other</c>).</summary>
    Other = 9,
}
