using System.Text;

namespace Lockflight.Cli.Processes;

/// <summary>
/// Reads one of a child's output streams to its end and keeps what it read, so the runner can stop waiting for the end without
/// losing the output: a process the child started can inherit the stream and hold it open long after the child has exited.
/// </summary>
internal sealed class OutputCapture : IDisposable
{
    private const int BufferSize = 4096;

    private readonly MemoryStream bytes = new();
    private readonly CancellationTokenSource abandon = new();

    public OutputCapture(Stream stream)
    {
        Completion = ReadAsync(stream);
    }

    /// <summary>Completes when the stream ends or the capture is abandoned; it never faults.</summary>
    public Task Completion { get; }

    /// <summary>Stops reading. A read the platform cannot cancel (an anonymous pipe on Windows) ends with the stream's last writer.</summary>
    public void Abandon() => abandon.Cancel();

    public byte[] Bytes()
    {
        lock (bytes)
        {
            return bytes.ToArray();
        }
    }

    /// <summary>The bytes read so far as text, decoded as the process's own reader would (a byte order mark wins over <paramref name="encoding"/>).</summary>
    public string Text(Encoding encoding)
    {
        using StreamReader reader = new(new MemoryStream(Bytes()), encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Abandons the read; a read still pending finds the capture closed and drops what it reads.</summary>
    public void Dispose()
    {
        abandon.Cancel();
        lock (bytes)
        {
            bytes.Dispose();
        }

        abandon.Dispose();
    }

    private async Task ReadAsync(Stream stream)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, abandon.Token)) > 0)
            {
                lock (bytes)
                {
                    bytes.Write(buffer, 0, count);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Abandoned, or the stream was closed under the read: what was read is kept.
        }
    }
}
