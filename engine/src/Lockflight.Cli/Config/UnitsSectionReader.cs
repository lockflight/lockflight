using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>units</c> list of <c>lockflight.yaml</c>; units are relative paths that never nest.</summary>
internal sealed class UnitsSectionReader(ManifestErrors errors)
{
    public ManifestError? Read(YamlNode valueNode, List<Unit> units)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return errors.At(valueNode.Start, "'units' must be a list of mappings, each with a 'path'");
        }

        foreach (YamlNode item in sequence.Children)
        {
            if (ReadUnit(item, out Unit? unit) is ManifestError error)
            {
                return error;
            }

            Unit read = unit!;
            if (units.FirstOrDefault(other => Overlaps(other.Path, read.Path)) is Unit clash)
            {
                return errors.At(item.Start, clash.Path.Equals(read.Path, StringComparison.OrdinalIgnoreCase)
                    ? $"unit '{read.Path}' is listed twice"
                    : $"units '{clash.Path}' and '{read.Path}' are nested; a unit cannot contain another");
            }

            units.Add(read);
        }

        return null;
    }

    private ManifestError? ReadUnit(YamlNode item, out Unit? unit)
    {
        unit = null;
        if (item is not YamlMappingNode mapping)
        {
            return errors.At(item.Start, "every entry of 'units' must be a mapping with a 'path'");
        }

        string? path = null;
        string? build = null;
        UnitKind kind = UnitKind.Auto;
        string? framework = null;
        string? stopFilter = null;
        foreach ((YamlNode keyNode, YamlNode value) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "path":
                    if (UnitPath(text) is not string normalized)
                    {
                        return errors.At(value.Start, "'units[].path' must be a relative path inside the project, without '..' (for example services/orders)");
                    }

                    path = normalized;
                    break;
                case "build" when !string.IsNullOrWhiteSpace(text):
                    build = text;
                    break;
                case "build":
                    return errors.At(value.Start, "'units[].build' must be a solution or project file name");
                case "kind" when text is "auto" or "dotnet" or "none":
                    kind = text switch { "dotnet" => UnitKind.Dotnet, "none" => UnitKind.None, _ => UnitKind.Auto };
                    break;
                case "kind":
                    return errors.At(value.Start, "'units[].kind' must be auto, dotnet or none");
                case "test":
                    if (ReadUnitTest(value, ref framework, ref stopFilter) is ManifestError testError)
                    {
                        return testError;
                    }

                    break;
                default:
                    return errors.At(keyNode.Start, $"unknown key 'units[].{key}' (known: path, build, kind, test)");
            }
        }

        if (path is null)
        {
            return errors.At(item.Start, "every entry of 'units' needs a 'path'");
        }

        unit = new Unit(path, build, kind, framework, stopFilter);
        return null;
    }

    private ManifestError? ReadUnitTest(YamlNode valueNode, ref string? framework, ref string? stopFilter)
    {
        if (valueNode is not YamlMappingNode test)
        {
            return errors.At(valueNode.Start, "'units[].test' must be a mapping (known keys: framework, stopFilter)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in test.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "framework" when text is not null && text.StartsWith("net", StringComparison.Ordinal):
                    framework = text;
                    break;
                case "framework":
                    return errors.At(value.Start, "'units[].test.framework' must be a target framework moniker such as net8.0");
                case "stopFilter" when !string.IsNullOrWhiteSpace(text):
                    stopFilter = text;
                    break;
                case "stopFilter":
                    return errors.At(value.Start, "'units[].test.stopFilter' must be a non-empty test filter");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'units[].test.{key}' (known: framework, stopFilter)");
            }
        }

        return null;
    }

    /// <summary>A unit path with forward slashes and no trailing slash, or null when it is empty, absolute or leaves the root.</summary>
    public static string? UnitPath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string path = text.Replace('\\', '/').TrimEnd('/');
        bool absolute = path.StartsWith('/') || (path.Length >= 2 && path[1] == ':');
        string[] segments = path.Split('/');
        return absolute || segments.Any(segment => segment is "" or "." or "..") ? null : path;
    }

    private static bool Overlaps(string first, string second) =>
        first.Equals(second, StringComparison.OrdinalIgnoreCase)
        || first.StartsWith(second + "/", StringComparison.OrdinalIgnoreCase)
        || second.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase);
}
