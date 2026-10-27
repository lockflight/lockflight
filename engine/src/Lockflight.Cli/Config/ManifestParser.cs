using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>
/// Strict, reflection-free reader of <c>lockflight.yaml</c> (YamlDotNet node tree; Native AOT safe). Unknown keys and
/// wrong types are errors with a line and column, never silently ignored.
/// </summary>
internal sealed class ManifestParser(string root, string path)
{
    private const int SupportedVersion = 1;

    private readonly ManifestErrors errors = new(path);

    public ManifestLoadResult Parse(string text)
    {
        YamlStream stream = new();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException exception)
        {
            return Invalid(exception.Start, $"not valid YAML: {exception.Message}");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // YamlDotNet reports some shapes (duplicate keys in a mapping) outside YamlException; still a manifest error, never a crash.
            return ManifestLoadResult.Invalid(errors.AtStart($"not valid YAML: {exception.Message}"));
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode rootNode)
        {
            return ManifestLoadResult.Invalid(errors.AtStart("the manifest must be a mapping of keys"));
        }

        return ParseRoot(rootNode);
    }

    private ManifestLoadResult ParseRoot(YamlMappingNode rootNode)
    {
        List<string> protectedGlobs = [];
        bool stopGate = false;
        string? stopTarget = null;
        GateSettings gates = GateSettings.Default;
        ReviewSettings review = ReviewSettings.Default;
        List<Unit> units = [];

        foreach ((YamlNode keyNode, YamlNode valueNode) in rootNode.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return ManifestLoadResult.Invalid(errors.NotAName(keyNode));
            }

            ManifestError? error = key switch
            {
                "version" => ReadVersion(valueNode),
                "protected" => ReadStringList(valueNode, key, protectedGlobs),
                "stop" => ReadStop(valueNode, ref stopGate, ref stopTarget),
                "gates" => new GatesSectionReader(errors).Read(valueNode, ref gates),
                "units" => new UnitsSectionReader(errors).Read(valueNode, units),
                "review" => new ReviewSectionReader(errors).Read(valueNode, ref review),
                _ => errors.At(keyNode.Start, $"unknown key '{key}' (known: version, protected, stop, gates, units, review)"),
            };
            if (error is not null)
            {
                return ManifestLoadResult.Invalid(error);
            }
        }

        return ManifestLoadResult.Loaded(new Manifest(root, protectedGlobs, stopGate, stopTarget, gates) { Units = units, Review = review });
    }

    private ManifestError? ReadVersion(YamlNode valueNode) =>
        valueNode is YamlScalarNode { Value: string value } && int.TryParse(value, out int version) && version == SupportedVersion
            ? null
            : errors.At(valueNode.Start, $"'version' must be {SupportedVersion}");

    private ManifestError? ReadStringList(YamlNode valueNode, string key, List<string> target)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return errors.At(valueNode.Start, $"'{key}' must be a list of strings");
        }

        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlScalarNode { Value: string value } || value.Length == 0)
            {
                return errors.At(item.Start, $"every entry of '{key}' must be a non-empty string");
            }

            target.Add(value);
        }

        return null;
    }

    private ManifestError? ReadStop(YamlNode valueNode, ref bool gate, ref string? target)
    {
        if (valueNode is not YamlMappingNode stop)
        {
            return errors.At(valueNode.Start, "'stop' must be a mapping with 'gate' and optional 'target'");
        }

        foreach ((YamlNode keyNode, YamlNode value) in stop.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            switch (key)
            {
                case "gate" when value is YamlScalarNode { Value: "true" or "false" } flag:
                    gate = flag.Value == "true";
                    break;
                case "gate":
                    return errors.At(value.Start, "'stop.gate' must be true or false");
                case "target" when value is YamlScalarNode { Value: string path } && path.Length > 0:
                    target = path;
                    break;
                case "target":
                    return errors.At(value.Start, "'stop.target' must be a non-empty path");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'stop.{key}' (known: gate, target)");
            }
        }

        return null;
    }

    private ManifestLoadResult Invalid(Mark mark, string message) => ManifestLoadResult.Invalid(errors.At(mark, message));
}
