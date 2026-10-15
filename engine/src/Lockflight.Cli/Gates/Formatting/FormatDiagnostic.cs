namespace Lockflight.Cli.Gates.Formatting;

/// <summary>
/// One change <c>dotnet format --verify-no-changes</c> would make: the file (relative to the project root, '/'-separated), where the
/// replaced span starts (1-based line and column), the diagnostic ID (<c>WHITESPACE</c>, <c>ENDOFLINE</c>, …) and its description,
/// which names the replacement (<c>Replace 3 characters with '\r\n\s\s'.</c>).
/// </summary>
internal sealed record FormatDiagnostic(string File, int Line, int Column, string DiagnosticId, string Description);
