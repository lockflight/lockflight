namespace Lockflight.Cli.Review;

/// <summary>A production file as a fix would change it: the file's full path, the full path of the project that compiles it, and the fixed text.</summary>
internal sealed record FixedFile(string Path, string Project, string Text);
