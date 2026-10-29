using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>
/// Reads a list of manifest globs: each a non-empty path pattern relative to the project root (not absolute, no <c>..</c>
/// segment), listed once. A duplicate is always a typo, so it is an error.
/// </summary>
internal sealed class GlobListReader(ManifestErrors errors)
{
    public ManifestError? Read(YamlNode valueNode, string key, out IReadOnlyList<string> globs)
    {
        globs = [];
        if (valueNode is not YamlSequenceNode sequence)
        {
            return errors.At(valueNode.Start, $"'{key}' must be a list of globs");
        }

        List<string> read = [];
        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlScalarNode { Value: string glob } || glob.Length == 0)
            {
                return errors.At(item.Start, $"every entry of '{key}' must be a non-empty glob");
            }

            if (!IsRelative(glob))
            {
                return errors.At(item.Start, $"'{key}' entry '{glob}' can never match: globs are relative to the project root, without '..'");
            }

            if (read.Contains(glob, StringComparer.Ordinal))
            {
                return errors.At(item.Start, $"'{key}' lists '{glob}' twice");
            }

            read.Add(glob);
        }

        globs = read;
        return null;
    }

    private static bool IsRelative(string glob)
    {
        string path = glob.Replace('\\', '/');
        bool absolute = path.StartsWith('/') || (path.Length >= 2 && path[1] == ':');
        return !absolute && !path.Split('/').Contains("..", StringComparer.Ordinal);
    }
}
