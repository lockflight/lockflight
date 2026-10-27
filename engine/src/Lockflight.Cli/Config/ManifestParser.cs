using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>
/// Strict, reflection-free reader of <c>lockflight.yaml</c> (YamlDotNet node tree; Native AOT safe). Unknown keys and
/// wrong types are errors with a line and column, never silently ignored.
/// </summary>
internal sealed partial class ManifestParser(string root, string path)
{
    private const int SupportedVersion = 1;

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
            return ManifestLoadResult.Invalid(new ManifestError(path, 1, 1, $"not valid YAML: {exception.Message}"));
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode rootNode)
        {
            return ManifestLoadResult.Invalid(new ManifestError(path, 1, 1, "the manifest must be a mapping of keys"));
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
                return ManifestLoadResult.Invalid(NotAName(keyNode));
            }

            ManifestError? error = key switch
            {
                "version" => ReadVersion(valueNode),
                "protected" => ReadStringList(valueNode, key, protectedGlobs),
                "stop" => ReadStop(valueNode, ref stopGate, ref stopTarget),
                "gates" => ReadGates(valueNode, ref gates),
                "units" => ReadUnits(valueNode, units),
                "review" => ReadReview(valueNode, ref review),
                _ => Error(keyNode.Start, $"unknown key '{key}' (known: version, protected, stop, gates, units, review)"),
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
            : Error(valueNode.Start, $"'version' must be {SupportedVersion}");

    private ManifestError? ReadStringList(YamlNode valueNode, string key, List<string> target)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return Error(valueNode.Start, $"'{key}' must be a list of strings");
        }

        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlScalarNode { Value: string value } || value.Length == 0)
            {
                return Error(item.Start, $"every entry of '{key}' must be a non-empty string");
            }

            target.Add(value);
        }

        return null;
    }

    private ManifestError? ReadStop(YamlNode valueNode, ref bool gate, ref string? target)
    {
        if (valueNode is not YamlMappingNode stop)
        {
            return Error(valueNode.Start, "'stop' must be a mapping with 'gate' and optional 'target'");
        }

        foreach ((YamlNode keyNode, YamlNode value) in stop.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            switch (key)
            {
                case "gate" when value is YamlScalarNode { Value: "true" or "false" } flag:
                    gate = flag.Value == "true";
                    break;
                case "gate":
                    return Error(value.Start, "'stop.gate' must be true or false");
                case "target" when value is YamlScalarNode { Value: string path } && path.Length > 0:
                    target = path;
                    break;
                case "target":
                    return Error(value.Start, "'stop.target' must be a non-empty path");
                default:
                    return Error(keyNode.Start, $"unknown key 'stop.{key}' (known: gate, target)");
            }
        }

        return null;
    }

    private ManifestError? ReadGates(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return Error(valueNode.Start, "'gates' must be a mapping (known keys: timeoutSeconds, build, test, coverage)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            switch (key)
            {
                case "timeoutSeconds" when value is YamlScalarNode { Value: string text }
                    && int.TryParse(text, out int seconds) && seconds is > 0 and <= GateSettings.MaxTimeoutSeconds:
                    gates = gates with { TimeoutSeconds = seconds };
                    break;
                case "timeoutSeconds":
                    return Error(value.Start, $"'gates.timeoutSeconds' must be a whole number from 1 to {GateSettings.MaxTimeoutSeconds}");
                case "build" when value is YamlScalarNode { Value: string buildFile } && !string.IsNullOrWhiteSpace(buildFile):
                    gates = gates with { BuildFile = buildFile };
                    break;
                case "build":
                    return Error(value.Start, "'gates.build' must be a solution or project file name");
                case "test":
                    if (ReadTestGate(value, ref gates) is ManifestError testError)
                    {
                        return testError;
                    }

                    break;
                case "coverage":
                    if (ReadCoverage(value, ref gates) is ManifestError coverageError)
                    {
                        return coverageError;
                    }

                    break;
                default:
                    return Error(keyNode.Start, $"unknown key 'gates.{key}' (known: timeoutSeconds, build, test, coverage)");
            }
        }

        return null;
    }

    private ManifestError? ReadCoverage(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode coverage)
        {
            return Error(valueNode.Start, "'gates.coverage' must be a mapping (known keys: collector, patchThreshold, minChangedLines)");
        }

        CoverageSettings settings = gates.Coverage;
        foreach ((YamlNode keyNode, YamlNode value) in coverage.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "collector" when text is "coverlet" or "microsoft" or "none":
                    settings = settings with { Collector = text switch { "coverlet" => CoverageCollector.Coverlet, "microsoft" => CoverageCollector.Microsoft, _ => CoverageCollector.None } };
                    break;
                case "collector":
                    return Error(value.Start, "'gates.coverage.collector' must be coverlet, microsoft or none");
                case "patchThreshold" when int.TryParse(text, out int threshold) && threshold is >= 0 and <= 100:
                    settings = settings with { PatchThreshold = threshold };
                    break;
                case "patchThreshold":
                    return Error(value.Start, "'gates.coverage.patchThreshold' must be a whole percentage from 0 to 100");
                case "minChangedLines" when int.TryParse(text, out int minimum) && minimum >= 0:
                    settings = settings with { MinChangedLines = minimum };
                    break;
                case "minChangedLines":
                    return Error(value.Start, "'gates.coverage.minChangedLines' must be a whole number of lines");
                default:
                    return Error(keyNode.Start, $"unknown key 'gates.coverage.{key}' (known: collector, patchThreshold, minChangedLines)");
            }
        }

        gates = gates with { Coverage = settings };
        return null;
    }

    private ManifestError? ReadTestGate(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode test)
        {
            return Error(valueNode.Start, "'gates.test' must be a mapping (known keys: framework)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in test.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            if (key != "framework")
            {
                return Error(keyNode.Start, $"unknown key 'gates.test.{key}' (known: framework)");
            }

            if (value is not YamlScalarNode { Value: string framework } || !framework.StartsWith("net", StringComparison.Ordinal))
            {
                return Error(value.Start, "'gates.test.framework' must be a target framework moniker such as net10.0");
            }

            gates = gates with { TestFramework = framework };
        }

        return null;
    }

    private ManifestError? ReadReview(YamlNode valueNode, ref ReviewSettings review)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return Error(valueNode.Start, "'review' must be a mapping (known keys: model, maxUsd, target)");
        }

        ReviewSettings settings = review with { FromManifest = true };
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "model" when text is not null && ModelName().IsMatch(text):
                    settings = settings with { Model = text };
                    break;
                case "model":
                    return Error(value.Start, "'review.model' must be a Claude Code model name such as opus or sonnet");
                case "maxUsd" when decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal budget)
                    && budget is > 0 and <= ReviewSettings.MaxBudgetUsd:
                    settings = settings with { BudgetUsd = budget };
                    break;
                case "target" when text is not null && TargetName().IsMatch(text):
                    settings = settings with { Target = text };
                    break;
                case "target":
                    return Error(value.Start, "'review.target' must be a branch name such as main or origin/main");
                case "maxUsd":
                    return Error(value.Start, $"'review.maxUsd' must be an amount in US dollars above 0 and at most {ReviewSettings.MaxBudgetUsd}, such as 3.00");
                default:
                    return Error(keyNode.Start, $"unknown key 'review.{key}' (known: model, maxUsd, target)");
            }
        }

        review = settings;
        return null;
    }

    private ManifestError? ReadUnits(YamlNode valueNode, List<Unit> units)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return Error(valueNode.Start, "'units' must be a list of mappings, each with a 'path'");
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
                return Error(item.Start, clash.Path.Equals(read.Path, StringComparison.OrdinalIgnoreCase)
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
            return Error(item.Start, "every entry of 'units' must be a mapping with a 'path'");
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
                return NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "path":
                    if (UnitPath(text) is not string normalized)
                    {
                        return Error(value.Start, "'units[].path' must be a relative path inside the project, without '..' (for example services/orders)");
                    }

                    path = normalized;
                    break;
                case "build" when !string.IsNullOrWhiteSpace(text):
                    build = text;
                    break;
                case "build":
                    return Error(value.Start, "'units[].build' must be a solution or project file name");
                case "kind" when text is "auto" or "dotnet" or "none":
                    kind = text switch { "dotnet" => UnitKind.Dotnet, "none" => UnitKind.None, _ => UnitKind.Auto };
                    break;
                case "kind":
                    return Error(value.Start, "'units[].kind' must be auto, dotnet or none");
                case "test":
                    if (ReadUnitTest(value, ref framework, ref stopFilter) is ManifestError testError)
                    {
                        return testError;
                    }

                    break;
                default:
                    return Error(keyNode.Start, $"unknown key 'units[].{key}' (known: path, build, kind, test)");
            }
        }

        if (path is null)
        {
            return Error(item.Start, "every entry of 'units' needs a 'path'");
        }

        unit = new Unit(path, build, kind, framework, stopFilter);
        return null;
    }

    private ManifestError? ReadUnitTest(YamlNode valueNode, ref string? framework, ref string? stopFilter)
    {
        if (valueNode is not YamlMappingNode test)
        {
            return Error(valueNode.Start, "'units[].test' must be a mapping (known keys: framework, stopFilter)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in test.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "framework" when text is not null && text.StartsWith("net", StringComparison.Ordinal):
                    framework = text;
                    break;
                case "framework":
                    return Error(value.Start, "'units[].test.framework' must be a target framework moniker such as net8.0");
                case "stopFilter" when !string.IsNullOrWhiteSpace(text):
                    stopFilter = text;
                    break;
                case "stopFilter":
                    return Error(value.Start, "'units[].test.stopFilter' must be a non-empty test filter");
                default:
                    return Error(keyNode.Start, $"unknown key 'units[].test.{key}' (known: framework, stopFilter)");
            }
        }

        return null;
    }

    /// <summary>A unit path with forward slashes and no trailing slash, or null when it is empty, absolute or leaves the root.</summary>
    private static string? UnitPath(string? text)
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

    private ManifestError NotAName(YamlNode keyNode) => Error(keyNode.Start, "every key must be a plain name, not a list or a mapping");

    private ManifestLoadResult Invalid(Mark mark, string message) => ManifestLoadResult.Invalid(Error(mark, message));

    private ManifestError Error(Mark mark, string message) => new(path, (int)mark.Line, (int)mark.Column, message);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\[\]-]*$")]
    private static partial Regex ModelName();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._/-]*$")]
    private static partial Regex TargetName();
}
