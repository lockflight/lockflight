namespace Lockflight.Cli.Init;

/// <summary>What <c>lockflight init</c> did to one file.</summary>
internal enum FileOutcome
{
    /// <summary>The file already had what Lockflight needs; nothing was written.</summary>
    Unchanged = 0,

    /// <summary>The file did not exist and was created.</summary>
    Created = 1,

    /// <summary>The file existed and Lockflight merged its entries into it, keeping everything else.</summary>
    Updated = 2,
}
