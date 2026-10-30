using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lockflight.Cli.Tests.Mascot;

/// <summary>
/// A minimal PNG writer and reader for Levi's badges: 8-bit RGBA, every row unfiltered, one IDAT chunk. The reader takes only what
/// the writer writes (chunk bounds, CRCs and header checked), so a badge edited by hand (or by an optimiser) fails the sync test and is written again from the art.
/// </summary>
internal static class PixelPng
{
    private const int HeaderLength = 13;
    private const byte BitDepth = 8;
    private const byte ColorTypeRgba = 6;
    private const byte NoFilter = 0;

    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = [.. Enumerable.Range(0, 256).Select(index =>
    {
        uint crc = (uint)index;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    })];

    /// <summary>The PNG of <paramref name="rgba"/> (width × height × 4 bytes, row by row).</summary>
    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using MemoryStream raw = new();
        for (int row = 0; row < height; row++)
        {
            raw.WriteByte(NoFilter);
            raw.Write(rgba, row * width * 4, width * 4);
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        byte[] header = new byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = BitDepth;
        header[9] = ColorTypeRgba;
        using MemoryStream png = new();
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The width, height and RGBA bytes of a PNG this writer wrote; null for any other file.</summary>
    public static (int Width, int Height, byte[] Rgba)? Decode(byte[] png)
    {
        if (!png.AsSpan().StartsWith(Signature))
        {
            return null;
        }

        int width = 0;
        int height = 0;
        using MemoryStream data = new();
        for (int offset = Signature.Length; offset < png.Length;)
        {
            if (offset + 12 > png.Length)
            {
                return null;
            }

            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (length < 0 || offset + 12L + length > png.Length)
            {
                return null;
            }

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            ReadOnlySpan<byte> body = png.AsSpan(offset + 8, length);
            if (BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length)) != Crc(png.AsSpan(offset + 4, length + 4).ToArray())
                || (offset == Signature.Length) != (type == "IHDR"))
            {
                return null;
            }

            if (type == "IHDR")
            {
                if (length != HeaderLength || body[8] != BitDepth || body[9] != ColorTypeRgba || body[10] != 0 || body[11] != 0 || body[12] != 0)
                {
                    return null;
                }

                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
            }
            else if (type == "IDAT")
            {
                data.Write(body);
            }

            offset += length + 12;
        }

        data.Position = 0;
        using ZLibStream zlib = new(data, CompressionMode.Decompress);
        using MemoryStream raw = new();
        zlib.CopyTo(raw);
        byte[] rows = raw.ToArray();
        if (width == 0 || rows.Length != height * ((width * 4) + 1))
        {
            return null;
        }

        byte[] rgba = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int start = row * ((width * 4) + 1);
            if (rows[start] != NoFilter)
            {
                return null;
            }

            Array.Copy(rows, start + 1, rgba, row * width * 4, width * 4);
        }

        return (width, height, rgba);
    }

    private static void WriteChunk(Stream png, string type, byte[] body)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        png.Write(number);
        png.Write(typeBytes);
        png.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc([.. typeBytes, .. body]));
        png.Write(number);
    }

    private static uint Crc(byte[] bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
