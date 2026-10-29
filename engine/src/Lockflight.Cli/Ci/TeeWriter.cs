using System.Text;

namespace Lockflight.Cli.Ci;

/// <summary>Writes everything to two writers: the console a person reads, and a copy a command reads its own lines back from.</summary>
internal sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
{
    public override Encoding Encoding => first.Encoding;

    public override void Write(char value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override void Write(string? value)
    {
        first.Write(value);
        second.Write(value);
    }

    public override Task WriteAsync(string? value) => Task.WhenAll(first.WriteAsync(value), second.WriteAsync(value));

    public override Task WriteLineAsync(string? value) => Task.WhenAll(first.WriteLineAsync(value), second.WriteLineAsync(value));
}
