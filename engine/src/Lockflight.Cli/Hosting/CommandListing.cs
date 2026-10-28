namespace Lockflight.Cli.Hosting;

/// <summary>Whether documents may name a command line: what <see cref="CommandCatalog"/> lists.</summary>
internal enum CommandListing
{
    /// <summary>A command documents name, in the catalog and the help text.</summary>
    Listed = 0,

    /// <summary>
    /// A command group's catch-all for verbs it does not know (<c>redgreen xyz</c>, <c>approve xyz</c>): dispatched, so the command
    /// gives its own error, but never a valid command line for a document.
    /// </summary>
    Unlisted = 1,
}
