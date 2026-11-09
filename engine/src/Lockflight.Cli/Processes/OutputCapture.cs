using System.Text;

namespace Lockflight.Cli.Processes;

/// <summary>
/// Reads one of a child's output streams to its end and keeps what it read, so the runner can stop waiting for the end without
/// losing the output: a process the child started can inherit the stream and hold it open long after the child has exited.
/// </summary>
/// <remarks>
/// The stream is read on a thread of its own, with blocking reads. A read of an anonymous pipe on Windows blocks a thread whichever
/// way it is called, and on the thread pool a busy engine (many processes and tests at once) can leave that read waiting for a thread
/// for seconds after the child wrote and exited; the runner would then give up on output that had already been written.
/// </remarks>
internal sealed class OutputCapture : IDisposable
{
    private const int BufferSize = 4096;

    private const int ReaderStackSize = 256 * 1024;

    private readonly MemoryStream bytes = new();
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long received;
    private bool closed;

    public OutputCapture(Stream stream)
    {
        Thread reader = new(() => Read(stream), ReaderStackSize) { IsBackground = true, Name = "Lockflight output reader" };
        reader.Start();
    }

    /// <summary>Completes when the stream ends or the capture is abandoned; it never faults.</summary>
    public Task Completion => ended.Task;

    /// <summary>How many bytes have arrived so far, kept or not: the runner's drain waits while this still grows.</summary>
    public long Received => Interlocked.Read(ref received);

    /// <summary>
    /// Stops waiting for the stream: <see cref="Completion"/> completes and what was read until now is what <see cref="Bytes"/> returns.
    /// The read itself ends with the stream's last writer.
    /// </summary>
    public void Abandon()
    {
        lock (bytes)
        {
            closed = true;
        }

        ended.TrySetResult();
    }

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
        Abandon();
        lock (bytes)
        {
            bytes.Dispose();
        }
    }

    private void Read(Stream stream)
    {
        byte[] buffer = new byte[BufferSize];
        try
        {
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                lock (bytes)
                {
                    if (closed)
                    {
                        return;
                    }

                    bytes.Write(buffer, 0, count);
                    Interlocked.Add(ref received, count);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The stream was closed under the read: what was read is kept.
        }
        finally
        {
            ended.TrySetResult();
        }
    }
}
