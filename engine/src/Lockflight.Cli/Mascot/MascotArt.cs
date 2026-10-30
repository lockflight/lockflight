using System.Text;

namespace Lockflight.Cli.Mascot;

/// <summary>
/// Levi as pixel art: a 16 × 14 body plus one overlay per <see cref="MascotPose"/> (eyes and a prop) on a 22 × 16 grid,
/// drawn in a terminal with half-block characters and 24-bit colors, two pixel rows per text line. The same pixels as
/// <c>docs/assets/levi-*.svg</c> and the CI judge's PNG badges (<c>docs/assets/badges/</c>).
/// </summary>
internal static class MascotArt
{
    public const int Width = 22;

    public const int Height = 16;

    private static readonly string[] Body =
    [
        "......CCCC......",
        "....CCCAACCC....",
        "...CCCCCCCCCC...",
        "..DBBBBBBBBBBD..",
        "..BBBBBBBBBBBB..",
        "..BBBBBBBBBBBB..",
        "..BBBBBBBBBBBB..",
        "..BBBBBKKBBBBB..",
        "..BBBBLWWLBBBB..",
        "..BBBBLWWLBBBB..",
        ".BBBBLLLLLLBBBB.",
        ".BBBLLLLLLLLBBDD",
        "..BBLLLLLLLLBDDD",
        "...DD......DD...",
    ];

    private static readonly string[] Face =
    [
        "..CCAACC..",
        ".BBKBBKBB.",
        ".BBBKKBBB.",
        "..BBWWBB..",
    ];

    private static readonly Dictionary<char, (byte R, byte G, byte B)> Palette = new()
    {
        ['C'] = (0x14, 0x80, 0x8F),
        ['A'] = (0xF2, 0xA3, 0x3A),
        ['D'] = (0x7A, 0x3F, 0x1A),
        ['B'] = (0xC0, 0x68, 0x2F),
        ['K'] = (0x14, 0x14, 0x14),
        ['W'] = (0xFF, 0xFF, 0xFF),
        ['L'] = (0xF0, 0xC0, 0x8E),
        ['P'] = (0xC9, 0xA0, 0x6A),
        ['G'] = (0x2F, 0x8F, 0x5B),
        ['R'] = (0xC9, 0x48, 0x3B),
        ['Q'] = (0x3A, 0x24, 0x10),
        ['S'] = (0x5B, 0x6B, 0x75),
    };

    /// <summary>The pose's pixels: one string per row, '.' transparent, other characters <see cref="Palette"/> keys.</summary>
    public static string[] Pixels(MascotPose pose)
    {
        char[][] grid = [.. Enumerable.Range(0, Height).Select(_ => Enumerable.Repeat('.', Width).ToArray())];
        for (int row = 0; row < Body.Length; row++)
        {
            for (int column = 0; column < Body[row].Length; column++)
            {
                grid[row + 2][column] = Body[row][column];
            }
        }

        foreach ((int x, int y, int width, int height, char color) in Overlay(pose))
        {
            for (int row = y; row < y + height; row++)
            {
                for (int column = x; column < x + width; column++)
                {
                    grid[row][column] = color;
                }
            }
        }

        return [.. grid.Select(row => new string(row))];
    }

    /// <summary>The pose drawn for a terminal: <see cref="Height"/> / 2 lines.</summary>
    public static IReadOnlyList<string> Render(MascotPose pose) => HalfBlocks(Pixels(pose));

    /// <summary>Levi's face in two lines, for a status line.</summary>
    public static IReadOnlyList<string> RenderFace() => HalfBlocks(Face);

    /// <summary>The color of a palette key; null for a transparent pixel ('.') or a key the palette does not have.</summary>
    public static (byte R, byte G, byte B)? ColorOf(char key) => Palette.TryGetValue(key, out (byte R, byte G, byte B) color) ? color : null;

    /// <summary>An ANSI 24-bit foreground color sequence for a palette key.</summary>
    public static string Foreground(char key) => Palette.TryGetValue(key, out (byte R, byte G, byte B) color) ? $"\u001b[38;2;{color.R};{color.G};{color.B}m" : string.Empty;

    public const string Reset = "\u001b[0m";

    private static List<string> HalfBlocks(string[] pixels)
    {
        List<string> lines = [];
        for (int row = 0; row < pixels.Length; row += 2)
        {
            string top = pixels[row];
            string bottom = row + 1 < pixels.Length ? pixels[row + 1] : new string('.', top.Length);
            StringBuilder line = new();
            for (int column = 0; column < top.Length; column++)
            {
                line.Append(Cell(top[column], bottom[column]));
            }

            lines.Add(line.Append(Reset).ToString().TrimEnd());
        }

        return lines;
    }

    private static string Cell(char top, char bottom) => (top, bottom) switch
    {
        ('.', '.') => $"{Reset} ",
        (_, '.') => $"{Reset}{Foreground(top)}▀",
        ('.', _) => $"{Reset}{Foreground(bottom)}▄",
        _ => $"{Foreground(top)}{Background(bottom)}▀",
    };

    private static string Background(char key) => Palette.TryGetValue(key, out (byte R, byte G, byte B) color) ? $"\u001b[48;2;{color.R};{color.G};{color.B}m" : string.Empty;

    private static (int X, int Y, int Width, int Height, char Color)[] Overlay(MascotPose pose)
    {
        (int, int, int, int, char)[] openEyes = [(4, 7, 1, 1, 'W'), (5, 7, 1, 1, 'K'), (4, 8, 2, 1, 'K'), (10, 7, 1, 1, 'W'), (11, 7, 1, 1, 'K'), (10, 8, 2, 1, 'K')];
        (int, int, int, int, char)[] flag = [(17, 1, 1, 12, 'P'), (15, 10, 2, 1, 'B')];
        return pose switch
        {
            MascotPose.Pass =>
            [
                (5, 7, 1, 1, 'K'), (4, 8, 1, 1, 'K'), (6, 8, 1, 1, 'K'), (10, 7, 1, 1, 'K'), (9, 8, 1, 1, 'K'), (11, 8, 1, 1, 'K'),
                .. flag, (18, 1, 4, 5, 'G'), (18, 3, 1, 1, 'W'), (19, 4, 1, 1, 'W'), (20, 3, 1, 1, 'W'), (21, 2, 1, 1, 'W'),
            ],
            MascotPose.NotEarned =>
            [
                (4, 8, 2, 1, 'K'), (10, 8, 2, 1, 'K'), (11, 6, 2, 1, 'D'),
                (16, 0, 6, 6, 'A'), (16, 6, 1, 1, 'A'), (15, 7, 1, 1, 'A'),
                (18, 1, 2, 1, 'Q'), (20, 2, 1, 1, 'Q'), (19, 3, 1, 1, 'Q'), (19, 5, 1, 1, 'Q'),
            ],
            MascotPose.Fail =>
            [
                (3, 6, 1, 1, 'K'), (4, 7, 1, 1, 'K'), (4, 8, 2, 1, 'K'), (12, 6, 1, 1, 'K'), (11, 7, 1, 1, 'K'), (10, 8, 2, 1, 'K'),
                .. flag, (18, 1, 4, 5, 'R'), (18, 2, 1, 1, 'W'), (20, 2, 1, 1, 'W'), (19, 3, 1, 1, 'W'), (18, 4, 1, 1, 'W'), (20, 4, 1, 1, 'W'),
            ],
            MascotPose.Blocked =>
            [
                .. openEyes,
                .. Enumerable.Range(0, 10).Select(stripe => (stripe * 2, 11, 2, 2, stripe % 2 == 0 ? 'R' : 'W')),
                (20, 9, 2, 7, 'S'),
            ],
            _ => openEyes,
        };
    }
}
