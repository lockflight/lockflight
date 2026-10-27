namespace Lockflight.Cli.Paths;

/// <summary>
/// The directory reads one guard decision makes: listings started and entries seen. Tests bound the guard's work with it instead
/// of timing it, which shared machines make noisy.
/// </summary>
internal sealed class DirectoryReads
{
    /// <summary>Directory listings started; a recursive walk counts once.</summary>
    public int Listings { get; private set; }

    /// <summary>Directory entries (files and directories) the listings returned or a walk looked at.</summary>
    public long Entries { get; private set; }

    public void StartListing() => Listings++;

    public void SeeEntry() => Entries++;

    public void SeeEntries(int count) => Entries += count;
}
