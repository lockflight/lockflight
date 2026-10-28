namespace Lockflight.Cli.Review;

/// <summary>
/// The code change since the review base as git prints it (<see cref="Text"/>), and the changed files it shows as binary although
/// they hold text (<see cref="TextShownAsBinary"/>), whose change the reviewer therefore cannot read.
/// </summary>
internal sealed record CodeDiff(string Text, IReadOnlyList<string> TextShownAsBinary);
