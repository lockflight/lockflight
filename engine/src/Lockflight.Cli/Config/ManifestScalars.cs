using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Typed scalar values of the manifest: whole numbers in a range and change classes, read the same way by every section.</summary>
internal static class ManifestScalars
{
    public static string? Text(YamlNode node) => (node as YamlScalarNode)?.Value;

    public static bool TryInteger(YamlNode node, int minimum, int maximum, out int value) =>
        int.TryParse(Text(node), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= minimum && value <= maximum;

    /// <summary>A class of at least <paramref name="lowest"/>, or <c>none</c> (null) when <paramref name="allowNone"/>.</summary>
    public static bool TryClass(YamlNode node, ChangeClass lowest, bool allowNone, out ChangeClass? value)
    {
        string? text = Text(node);
        value = null;
        if (allowNone && text == ChangeClassNames.None)
        {
            return true;
        }

        if (ChangeClassNames.TryParse(text, out ChangeClass parsed) && parsed >= lowest)
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
