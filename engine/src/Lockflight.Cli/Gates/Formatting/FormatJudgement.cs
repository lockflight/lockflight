namespace Lockflight.Cli.Gates.Formatting;

/// <summary>
/// What a change is answerable for. <see cref="Attributed"/> are changed lines whose formatting a diagnostic would change (or the
/// diagnostic's own line in a file that could only be judged whole, listed in <see cref="WholeFiles"/>); <see cref="EndOfLineOnly"/>
/// counts diagnostics that differ from the file only in line endings; <see cref="Untouched"/> those on lines the change did not touch;
/// <see cref="CharsetOnly"/> encoding (byte order mark) diagnostics on files whose encoding the change left as it was.
/// </summary>
internal sealed record FormatJudgement(IReadOnlyList<FormatFinding> Attributed, int EndOfLineOnly, int Untouched, IReadOnlyList<string> WholeFiles, int CharsetOnly);
