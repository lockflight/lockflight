using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>gates</c> section of <c>lockflight.yaml</c>: timeout, build file, test framework, coverage and the switched steps.</summary>
internal sealed class GatesSectionReader(ManifestErrors errors)
{
    public ManifestError? Read(YamlNode valueNode, ref GateSettings gates)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, "'gates' must be a mapping (known keys: timeoutSeconds, build, test, coverage, quality, docs)");
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
                    if (ReadMode(value, "quality", "suppressions", out GateMode? suppressions) is ManifestError qualityError)
                    {
                        return qualityError;
                    }

                    gates = gates with { Suppressions = suppressions ?? gates.Suppressions };
                    break;
                case "docs":
                    if (ReadMode(value, "docs", "references", out GateMode? references) is ManifestError docsError)
                    {
                        return docsError;
                    }

                    gates = gates with { DocsReferences = references ?? gates.DocsReferences };
                    break;
                default:
                    return errors.At(keyNode.Start, $"unknown key 'gates.{key}' (known: timeoutSeconds, build, test, coverage, quality, docs)");
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

    /// <summary>Reads a section of one switched step, <c>gates.&lt;section&gt;.&lt;step&gt;: fail|report|off</c>; null when the key is absent.</summary>
    private ManifestError? ReadMode(YamlNode valueNode, string section, string step, out GateMode? mode)
    {
        mode = null;
        if (valueNode is not YamlMappingNode mapping)
        {
            return errors.At(valueNode.Start, $"'gates.{section}' must be a mapping (known keys: {step})");
        }

        foreach ((YamlNode keyNode, YamlNode value) in mapping.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            if (key != step)
            {
                return errors.At(keyNode.Start, $"unknown key 'gates.{section}.{key}' (known: {step})");
            }

            if (!GateModeNames.TryParse((value as YamlScalarNode)?.Value, out GateMode parsed))
            {
                return errors.At(value.Start, $"'gates.{section}.{step}' must be {GateModeNames.Choices}");
            }

            mode = parsed;
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
