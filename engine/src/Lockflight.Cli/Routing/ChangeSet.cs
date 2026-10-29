namespace Lockflight.Cli.Routing;

/// <summary>
/// Everything routing reads about a pull request's change, read once by <see cref="ChangeSetReader"/> so that the classifier is a
/// pure function of it: the base ref as given and the commit it named, the merge base the diff starts from, the head commit
/// (null when the working tree is the head, locally), the changed files sorted by path, and both sides' path listings and texts.
/// </summary>
internal sealed record ChangeSet(
    string BaseRef,
    string BaseCommit,
    string MergeBase,
    string? HeadCommit,
    IReadOnlyList<ChangedFile> Files,
    RevisionSnapshot Base,
    RevisionSnapshot Head);
