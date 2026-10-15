using System.Text;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// The engine's standard output and error as a person's terminal receives them: everything written passes through
/// <see cref="TerminalText.Clean(string)"/>, so text from the project cannot reach the terminal as control characters or escape
/// sequences whichever command prints it. Line ends come from the wrapped writer. The engine's own colours (Levi, the status
/// line) are written to <see cref="CliContext.RawOutput"/> instead.
/// </summary>
internal sealed class TerminalSafeWriter(TextWriter terminal) : TextWriter(terminal.FormatProvider)
{
    public override Encoding Encoding => terminal.Encoding;

    public override void Write(char value) => terminal.Write(TerminalText.Clean([value]));

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(ReadOnlySpan<char> buffer) => terminal.Write(TerminalText.Clean(buffer));

    public override void Write(string? value) => terminal.Write(value is null ? null : TerminalText.Clean(value));

    public override void WriteLine() => terminal.WriteLine();

    public override void WriteLine(char[] buffer, int index, int count) => WriteLine(buffer.AsSpan(index, count));

    public override void WriteLine(ReadOnlySpan<char> buffer) => terminal.WriteLine(TerminalText.Clean(buffer));

    public override void WriteLine(string? value) => terminal.WriteLine(value is null ? null : TerminalText.Clean(value));

    public override Task WriteAsync(char value) => terminal.WriteAsync(TerminalText.Clean([value]));

    public override Task WriteAsync(string? value) => terminal.WriteAsync(value is null ? null : TerminalText.Clean(value));

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        terminal.WriteAsync(TerminalText.Clean(buffer.Span).AsMemory(), cancellationToken);

    public override Task WriteLineAsync() => terminal.WriteLineAsync();

    public override Task WriteLineAsync(char value) => terminal.WriteLineAsync(TerminalText.Clean([value]));

    public override Task WriteLineAsync(string? value) => terminal.WriteLineAsync(value is null ? null : TerminalText.Clean(value));

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default) =>
        terminal.WriteLineAsync(TerminalText.Clean(buffer.Span).AsMemory(), cancellationToken);

    public override void Flush() => terminal.Flush();

    public override Task FlushAsync() => terminal.FlushAsync();

    public override Task FlushAsync(CancellationToken cancellationToken) => terminal.FlushAsync(cancellationToken);
}
