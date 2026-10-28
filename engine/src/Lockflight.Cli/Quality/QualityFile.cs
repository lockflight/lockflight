namespace Lockflight.Cli.Quality;

/// <summary>
/// A file of the quality pack and its proposed content, relative to the project root. <see cref="Existing"/> says whether the
/// file is already there: a new file is written, an existing one is only proposed.
/// </summary>
internal sealed record QualityFile(string RelativePath, string Content, bool Existing);
