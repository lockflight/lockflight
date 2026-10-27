namespace Lockflight.Cli.Review;

/// <summary>One compiler or build error: the file name (no directory; empty when the error has no location), line, code and message.</summary>
internal sealed record CompileError(string File, int Line, int Column, string Code, string Message)
{
    public override string ToString() => File.Length == 0 ? $"{Code}: {Message}" : $"{File}({Line},{Column}): {Code}: {Message}";
}
