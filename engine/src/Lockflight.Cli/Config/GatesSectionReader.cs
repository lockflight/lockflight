using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>gates</c> section of <c>lockflight.yaml</c>: timeout, build file, test framework, coverage and the switched steps.</summary>
internal sealed class GatesSectionReader(ManifestErrors errors)
{
    public ManifestError? Read(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, "'gates' must be a mapping (known keys: timeoutSeconds, build, test, coverage, quality)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            switch (key)
            {
                case "timeoutSeconds" when value is YamlScalarNode { Value: string text }
                    && int.TryParse(text, out int seconds) && seconds is > 0 and <= GateSettings.MaxTimeoutSeconds:
                    gates = gates with { TimeoutSeconds = seconds };
                    break;
                case "timeoutSeconds":
                    return errors.At(value.Start, $"'gates.timeoutSeconds' must be a whole number from 1 to {GateSettings.MaxTimeoutSeconds}");
                case "build" when value is YamlScalarNode { Value: string buildFile } && !string.IsNullOrWhiteSpace(buildFile):
                    gates = gates with { BuildFile = buildFile };
                    break;
                case "build":
                    return errors.At(value.Start, "'gates.build' must be a solution or project file name");
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
                case "quality":
                    if (ReadQuality(value, ref gates) is ManifestError qualityError)
                    {
                        return qualityError;
                    }

                    break;
                default:
                    return errors.At(keyNode.Start, $"unknown key 'gates.{key}' (known: timeoutSeconds, build, test, coverage, quality)");
            }
        }

        return null;
    }

    private ManifestError? ReadCoverage(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode coverage)
        {
            return errors.At(valueNode.Start, "'gates.coverage' must be a mapping (known keys: collector, patchThreshold, minChangedLines)");
        }

        CoverageSettings settings = gates.Coverage;
        foreach ((YamlNode keyNode, YamlNode value) in coverage.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "collector" when text is "coverlet" or "microsoft" or "none":
                    settings = settings with { Collector = text switch { "coverlet" => CoverageCollector.Coverlet, "microsoft" => CoverageCollector.Microsoft, _ => CoverageCollector.None } };
                    break;
                case "collector":
                    return errors.At(value.Start, "'gates.coverage.collector' must be coverlet, microsoft or none");
                case "patchThreshold" when int.TryParse(text, out int threshold) && threshold is >= 0 and <= 100:
                    settings = settings with { PatchThreshold = threshold };
                    break;
                case "patchThreshold":
                    return errors.At(value.Start, "'gates.coverage.patchThreshold' must be a whole percentage from 0 to 100");
                case "minChangedLines" when int.TryParse(text, out int minimum) && minimum >= 0:
                    settings = settings with { MinChangedLines = minimum };
                    break;
                case "minChangedLines":
                    return errors.At(value.Start, "'gates.coverage.minChangedLines' must be a whole number of lines");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'gates.coverage.{key}' (known: collector, patchThreshold, minChangedLines)");
            }
        }

        gates = gates with { Coverage = settings };
        return null;
    }

    private ManifestError? ReadQuality(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode quality)
        {
            return errors.At(valueNode.Start, "'gates.quality' must be a mapping (known keys: suppressions)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in quality.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            if (key != "suppressions")
            {
                return errors.At(keyNode.Start, $"unknown key 'gates.quality.{key}' (known: suppressions)");
            }

            if (!GateModeNames.TryParse((value as YamlScalarNode)?.Value, out GateMode mode))
            {
                return errors.At(value.Start, $"'gates.quality.suppressions' must be {GateModeNames.Choices}");
            }

            gates = gates with { Suppressions = mode };
        }

        return null;
    }

    private ManifestError? ReadTestGate(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode test)
        {
            return errors.At(valueNode.Start, "'gates.test' must be a mapping (known keys: framework)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in test.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            if (key != "framework")
            {
                return errors.At(keyNode.Start, $"unknown key 'gates.test.{key}' (known: framework)");
            }

            if (value is not YamlScalarNode { Value: string framework } || !framework.StartsWith("net", StringComparison.Ordinal))
            {
                return errors.At(value.Start, "'gates.test.framework' must be a target framework moniker such as net10.0");
            }

            gates = gates with { TestFramework = framework };
        }

        return null;
    }
}
