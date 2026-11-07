using System.Buffers.Binary;
using System.Text;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>A process's executable path, arguments and environment, as macOS gives them (<c>KERN_PROCARGS2</c>).</summary>
internal sealed record ProcessArguments(string Image, IReadOnlyList<string> Arguments, IReadOnlyList<string> Environment)
{
    /// <summary>
    /// Reads the layout: the argument count (a 32-bit integer), the executable path ending in a zero byte, more zero bytes, the
    /// arguments, then the environment, each ending in a zero byte; null when it is too short to hold the count.
    /// </summary>
    public static ProcessArguments? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(int))
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(data);
        ReadOnlySpan<byte> rest = data[sizeof(int)..];
        string image = Next(ref rest);
        while (rest.Length > 0 && rest[0] == 0)
        {
            rest = rest[1..];
        }

        List<string> arguments = [];
        for (int index = 0; index < count && rest.Length > 0; index++)
        {
            arguments.Add(Next(ref rest));
        }

        List<string> environment = [];
        while (rest.Length > 0 && rest[0] != 0)
        {
            environment.Add(Next(ref rest));
        }

        return new ProcessArguments(image, arguments, environment);
    }

    private static string Next(ref ReadOnlySpan<byte> rest)
    {
        int end = rest.IndexOf((byte)0);
        string text = Encoding.UTF8.GetString(end < 0 ? rest : rest[..end]);
        rest = end < 0 ? [] : rest[(end + 1)..];
        return text;
    }
}
