using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// A YAML document as the JSON a schema validator sees: plain scalars become numbers, booleans and null as YAML 1.2's core
/// schema reads them, quoted scalars stay strings.
/// </summary>
internal static class YamlJson
{
    public static JsonNode? Convert(string yaml)
    {
        YamlStream stream = new();
        stream.Load(new StringReader(yaml));
        return Convert(stream.Documents[0].RootNode);
    }

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode mapping => new JsonObject(mapping.Children.Select(pair =>
            KeyValuePair.Create(((YamlScalarNode)pair.Key).Value!, Convert(pair.Value)))),
        YamlSequenceNode sequence => new JsonArray([.. sequence.Children.Select(Convert)]),
        YamlScalarNode scalar => Scalar(scalar),
        _ => throw new InvalidDataException($"unsupported YAML node at {node.Start}"),
    };

    private static JsonValue? Scalar(YamlScalarNode scalar)
    {
        string text = scalar.Value ?? string.Empty;
        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted)
        {
            return JsonValue.Create(text);
        }

        if (text is "" or "~" or "null")
        {
            return null;
        }

        if (text is "true" or "false")
        {
            return JsonValue.Create(text == "true");
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
        {
            return JsonValue.Create(integer);
        }

        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)
            ? JsonValue.Create(number)
            : JsonValue.Create(text);
    }
}
