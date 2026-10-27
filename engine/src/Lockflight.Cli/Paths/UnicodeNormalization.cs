using System.Text;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Unicode canonical composition (NFC) for path names. The engine runs with invariant globalization, where
/// <see cref="string.Normalize()"/> returns its input unchanged, yet macOS file systems open a name in either form: <c>café</c>
/// typed as <c>e</c> + U+0301 is the same file as one with U+00E9. Both a rule and a path are composed before they are compared.
/// Covers every canonical composition pair and Hangul syllables (<see cref="UnicodeCompositionData"/>); characters NFC would
/// replace without composing (the singleton U+212B ANGSTROM SIGN, composition exclusions such as U+0958) are left as they are.
/// </summary>
internal static class UnicodeNormalization
{
    private const char FirstCombiningMark = '̀';

    private const int HangulSyllableBase = 0xAC00;
    private const int HangulLeadingBase = 0x1100;
    private const int HangulVowelBase = 0x1161;
    private const int HangulTrailingBase = 0x11A7;
    private const int HangulLeadingCount = 19;
    private const int HangulVowelCount = 21;
    private const int HangulTrailingCount = 28;
    private const int HangulSyllablesPerLeading = HangulVowelCount * HangulTrailingCount;
    private const int HangulSyllableCount = HangulLeadingCount * HangulSyllablesPerLeading;

    private static readonly Lazy<(Dictionary<(int, int), int> Compose, Dictionary<int, (int, int)> Decompose)> Tables = new(BuildTables);

    /// <summary>Whether composing could change the text: it holds a character from U+0300 on.</summary>
    public static bool MayChange(ReadOnlySpan<char> text) => text.IndexOfAnyExceptInRange('\0', (char)(FirstCombiningMark - 1)) >= 0;

    /// <summary>The text in NFC; text without a character from U+0300 on is returned as it is.</summary>
    public static string Compose(string text)
    {
        if (!MayChange(text))
        {
            return text;
        }

        List<int> codePoints = [];
        foreach (Rune rune in text.EnumerateRunes())
        {
            Decompose(rune.Value, codePoints);
        }

        SortCombiningMarks(codePoints);
        StringBuilder composed = new(text.Length);
        foreach (int codePoint in ComposePairs(codePoints))
        {
            composed.Append(char.ConvertFromUtf32(codePoint));
        }

        return composed.ToString();
    }

    private static void Decompose(int codePoint, List<int> into)
    {
        int syllable = codePoint - HangulSyllableBase;
        if (syllable is >= 0 and < HangulSyllableCount)
        {
            into.Add(HangulLeadingBase + (syllable / HangulSyllablesPerLeading));
            into.Add(HangulVowelBase + (syllable % HangulSyllablesPerLeading / HangulTrailingCount));
            if (syllable % HangulTrailingCount != 0)
            {
                into.Add(HangulTrailingBase + (syllable % HangulTrailingCount));
            }

            return;
        }

        if (Tables.Value.Decompose.TryGetValue(codePoint, out (int First, int Second) parts))
        {
            Decompose(parts.First, into);
            into.Add(parts.Second);
            return;
        }

        into.Add(codePoint);
    }

    /// <summary>The canonical order: a run of combining marks is sorted by combining class, stably.</summary>
    private static void SortCombiningMarks(List<int> codePoints)
    {
        for (int index = 1; index < codePoints.Count; index++)
        {
            int combiningClass = CombiningClass(codePoints[index]);
            for (int position = index; combiningClass != 0 && position > 0; position--)
            {
                int before = CombiningClass(codePoints[position - 1]);
                if (before <= combiningClass)
                {
                    break;
                }

                (codePoints[position - 1], codePoints[position]) = (codePoints[position], codePoints[position - 1]);
            }
        }
    }

    /// <summary>Canonical composition: a mark joins the last starter unless a mark of the same or a higher class lies between.</summary>
    private static List<int> ComposePairs(List<int> codePoints)
    {
        List<int> composed = [];
        int starter = -1;
        int lastClass = 0;
        foreach (int codePoint in codePoints)
        {
            int combiningClass = CombiningClass(codePoint);
            if (starter >= 0 && (lastClass < combiningClass || lastClass == 0) && Composite(composed[starter], codePoint) is int composite)
            {
                composed[starter] = composite;
                continue;
            }

            if (combiningClass == 0)
            {
                starter = composed.Count;
            }

            composed.Add(codePoint);
            lastClass = combiningClass;
        }

        return composed;
    }

    private static int? Composite(int first, int second)
    {
        int leading = first - HangulLeadingBase;
        int vowel = second - HangulVowelBase;
        if (leading is >= 0 and < HangulLeadingCount && vowel is >= 0 and < HangulVowelCount)
        {
            return HangulSyllableBase + (((leading * HangulVowelCount) + vowel) * HangulTrailingCount);
        }

        int syllable = first - HangulSyllableBase;
        int trailing = second - HangulTrailingBase;
        if (syllable is >= 0 and < HangulSyllableCount && syllable % HangulTrailingCount == 0 && trailing is > 0 and < HangulTrailingCount)
        {
            return first + trailing;
        }

        return Tables.Value.Compose.TryGetValue((first, second), out int composite) ? composite : null;
    }

    private static int CombiningClass(int codePoint)
    {
        ReadOnlySpan<int> ranges = UnicodeCompositionData.CombiningClasses;
        int low = 0;
        int high = (ranges.Length / 3) - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (codePoint < ranges[middle * 3])
            {
                high = middle - 1;
            }
            else if (codePoint > ranges[(middle * 3) + 1])
            {
                low = middle + 1;
            }
            else
            {
                return ranges[(middle * 3) + 2];
            }
        }

        return 0;
    }

    private static (Dictionary<(int, int), int>, Dictionary<int, (int, int)>) BuildTables()
    {
        ReadOnlySpan<int> pairs = UnicodeCompositionData.Compositions;
        Dictionary<(int, int), int> compose = new(pairs.Length / 3);
        Dictionary<int, (int, int)> decompose = new(pairs.Length / 3);
        for (int index = 0; index < pairs.Length; index += 3)
        {
            compose[(pairs[index], pairs[index + 1])] = pairs[index + 2];
            decompose[pairs[index + 2]] = (pairs[index], pairs[index + 1]);
        }

        return (compose, decompose);
    }
}
