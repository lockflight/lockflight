namespace Lockflight.Cli.Init;

/// <summary>
/// The manifest text <c>init</c> would write (<see cref="Content"/>; the file's own text when nothing changes) and the lines a
/// person has to add by hand because a list could not be extended in place.
/// </summary>
internal sealed record ManifestPlan(string Content, bool Changed, IReadOnlyList<string> Manual);
