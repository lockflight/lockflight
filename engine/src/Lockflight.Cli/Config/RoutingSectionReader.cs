using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Config;

/// <summary>
/// Reads the <c>routing</c> section of a version 2 manifest. <c>preset</c> is required; every other key overrides the preset's
/// value, and the keys left out are recorded as filled from the preset. Cross-key rules (a large change is larger than a fix,
/// coverage never decreases with the class, a review key is never required below the review) are checked on the result.
/// </summary>
internal sealed class RoutingSectionReader(ManifestErrors errors)
{
    private const string Known =
        "preset, floor, fix, largeLines, structural, critical, dbPaths, generatedPaths, nonProduction, requiredWorkflows, apiProjects, layers, coverage, require";

    private const int MinLayers = 2;

    private readonly Dictionary<string, Mark> written = new(StringComparer.Ordinal);

    public ManifestError? Read(YamlNode valueNode, out RoutingSettings routing)
    {
        routing = RoutingSettings.Default;
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, $"'routing' must be a mapping (known keys: {Known})");
        }

        YamlNode? presetNode = section.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: "preset" }).Value;
        if (presetNode is null)
        {
            return errors.At(section.Start, $"'routing.preset' is required: {RoutingPresetNames.Choices} (the other keys override its values)");
        }

        if (!RoutingPresetNames.TryParse(ManifestScalars.Text(presetNode), out RoutingPreset preset))
        {
            return errors.At(presetNode.Start, $"'routing.preset' must be {RoutingPresetNames.Choices}");
        }

        RoutingSettings settings = RoutingPresets.For(preset) with { FromManifest = true };
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            if (ReadKey(key, keyNode, value, ref settings) is ManifestError error)
            {
                return error;
            }
        }

        if (CheckAcrossKeys(settings, section.Start) is ManifestError crossError)
        {
            return crossError;
        }

        routing = settings with { FilledFromPreset = [.. RoutingPresets.Keys.Where(key => !written.ContainsKey(key))] };
        return null;
    }

    private ManifestError? ReadKey(string key, YamlNode keyNode, YamlNode value, ref RoutingSettings settings)
    {
        GlobListReader globs = new(errors);
        ManifestError? error = null;
        IReadOnlyList<string> list;
        switch (key)
        {
            case "preset":
                return null;
            case "floor":
                if (!ManifestScalars.TryClass(value, ChangeClass.Fix, allowNone: false, out ChangeClass? floor))
                {
                    return errors.At(value.Start, $"'routing.floor' must be {ChangeClassNames.Choices(ChangeClass.Fix, allowNone: false)}");
                }

                settings = settings with { Floor = floor!.Value };
                break;
            case "fix":
                return ReadFix(value, ref settings);
            case "largeLines":
                if (!ManifestScalars.TryInteger(value, 2, int.MaxValue, out int largeLines))
                {
                    return errors.At(value.Start, "'routing.largeLines' must be a whole number of lines above 'routing.fix.maxLines'");
                }

                settings = settings with { LargeLines = largeLines };
                break;
            case "structural":
                if (!ManifestScalars.TryClass(value, ChangeClass.Feature, allowNone: false, out ChangeClass? structural))
                {
                    return errors.At(value.Start, "'routing.structural' must be feature or critical (a new project is never a fix)");
                }

                settings = settings with { Structural = structural!.Value };
                break;
            case "critical":
                error = globs.Read(value, "routing.critical", out list);
                settings = settings with { Critical = list };
                return error;
            case "dbPaths":
                error = globs.Read(value, "routing.dbPaths", out list);
                settings = settings with { DbPaths = list };
                return error;
            case "generatedPaths":
                error = globs.Read(value, "routing.generatedPaths", out list);
                settings = settings with { GeneratedPaths = list };
                return error;
            case "nonProduction":
                error = globs.Read(value, "routing.nonProduction", out list);
                settings = settings with { NonProduction = list };
                return error;
            case "requiredWorkflows":
                error = globs.Read(value, "routing.requiredWorkflows", out list);
                settings = settings with { RequiredWorkflows = list };
                return error;
            case "apiProjects":
                error = globs.Read(value, "routing.apiProjects", out list);
                settings = settings with { ApiProjects = list };
                return error;
            case "layers":
                return ReadLayers(value, ref settings);
            case "coverage":
                return ReadCoverage(value, ref settings);
            case "require":
                return ReadRequire(value, ref settings);
            default:
                return errors.At(keyNode.Start, $"unknown key 'routing.{key}' (known: {Known})");
        }

        written[key] = value.Start;
        return null;
    }

    private ManifestError? ReadFix(YamlNode valueNode, ref RoutingSettings settings)
    {
        if (valueNode is not YamlMappingNode fix)
        {
            return errors.At(valueNode.Start, "'routing.fix' must be a mapping (known keys: maxLines, maxFiles)");
        }

        foreach ((YamlNode keyNode, YamlNode value) in fix.Children)
        {
            switch ((keyNode as YamlScalarNode)?.Value)
            {
                case "maxLines" when ManifestScalars.TryInteger(value, 1, RoutingSettings.MaxFixLines, out int lines):
                    settings = settings with { FixMaxLines = lines };
                    written["fix.maxLines"] = value.Start;
                    break;
                case "maxLines":
                    return errors.At(value.Start, $"'routing.fix.maxLines' must be a whole number of lines from 1 to {RoutingSettings.MaxFixLines}");
                case "maxFiles" when ManifestScalars.TryInteger(value, 1, RoutingSettings.MaxFixFiles, out int files):
                    settings = settings with { FixMaxFiles = files };
                    written["fix.maxFiles"] = value.Start;
                    break;
                case "maxFiles":
                    return errors.At(value.Start, $"'routing.fix.maxFiles' must be a whole number of files from 1 to {RoutingSettings.MaxFixFiles}");
                case string key:
                    return errors.At(keyNode.Start, $"unknown key 'routing.fix.{key}' (known: maxLines, maxFiles)");
                default:
                    return errors.NotAName(keyNode);
            }
        }

        return null;
    }

    private ManifestError? ReadCoverage(YamlNode valueNode, ref RoutingSettings settings)
    {
        if (valueNode is not YamlMappingNode coverage)
        {
            return errors.At(valueNode.Start, "'routing.coverage' must be a mapping (known keys: fix, behaviour, feature, critical)");
        }

        RoutingCoverage thresholds = settings.Coverage;
        foreach ((YamlNode keyNode, YamlNode value) in coverage.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            if (!ChangeClassNames.TryParse(key, out ChangeClass changeClass))
            {
                return errors.At(keyNode.Start, $"unknown key 'routing.coverage.{key}' (known: fix, behaviour, feature, critical)");
            }

            if (!ManifestScalars.TryInteger(value, 0, 100, out int percent))
            {
                return errors.At(value.Start, $"'routing.coverage.{key}' must be a whole percentage from 0 to 100");
            }

            thresholds = changeClass switch
            {
                ChangeClass.Fix => thresholds with { Fix = percent },
                ChangeClass.Behaviour => thresholds with { Behaviour = percent },
                ChangeClass.Feature => thresholds with { Feature = percent },
                _ => thresholds with { Critical = percent },
            };
            written[$"coverage.{key}"] = value.Start;
        }

        settings = settings with { Coverage = thresholds };
        return null;
    }

    private ManifestError? ReadRequire(YamlNode valueNode, ref RoutingSettings settings)
    {
        const string RequireKeys = "specFrom, reviewFrom, reviewKeyFrom, architectureFrom, architectureDeclaredFrom";
        if (valueNode is not YamlMappingNode require)
        {
            return errors.At(valueNode.Start, $"'routing.require' must be a mapping (known keys: {RequireKeys})");
        }

        RoutingRequirements requirements = settings.Require;
        foreach ((YamlNode keyNode, YamlNode value) in require.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            (ChangeClass lowest, bool allowNone)? allowed = key switch
            {
                "specFrom" => (ChangeClass.Behaviour, false),
                "reviewFrom" => (ChangeClass.Fix, false),
                "reviewKeyFrom" => (ChangeClass.Behaviour, true),
                "architectureFrom" => (ChangeClass.Behaviour, false),
                "architectureDeclaredFrom" => (ChangeClass.Critical, true),
                _ => null,
            };
            if (allowed is not (ChangeClass lowest, bool allowNone))
            {
                return errors.At(keyNode.Start, $"unknown key 'routing.require.{key}' (known: {RequireKeys})");
            }

            if (!ManifestScalars.TryClass(value, lowest, allowNone, out ChangeClass? changeClass))
            {
                return errors.At(value.Start, $"'routing.require.{key}' must be {ChangeClassNames.Choices(lowest, allowNone)}");
            }

            requirements = key switch
            {
                "specFrom" => requirements with { SpecFrom = changeClass!.Value },
                "reviewFrom" => requirements with { ReviewFrom = changeClass!.Value },
                "reviewKeyFrom" => requirements with { ReviewKeyFrom = changeClass },
                "architectureFrom" => requirements with { ArchitectureFrom = changeClass!.Value },
                _ => requirements with { ArchitectureDeclaredFrom = changeClass },
            };
            written[$"require.{key}"] = value.Start;
        }

        settings = settings with { Require = requirements };
        return null;
    }

    private ManifestError? ReadLayers(YamlNode valueNode, ref RoutingSettings settings)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return errors.At(valueNode.Start, "'routing.layers' must be a list of mappings, each with a 'name' and 'paths', inner layer first");
        }

        List<RoutingLayer> layers = [];
        foreach (YamlNode item in sequence.Children)
        {
            if (ReadLayer(item, out RoutingLayer? layer) is ManifestError error)
            {
                return error;
            }

            if (layers.Any(other => other.Name.Equals(layer!.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return errors.At(item.Start, $"'routing.layers' names '{layer!.Name}' twice");
            }

            if (Overlap(layers, layer!) is (string glob, RoutingLayer other))
            {
                return errors.At(item.Start, $"'routing.layers' glob '{glob}' of '{layer!.Name}' overlaps a glob of '{other.Name}'; a file belongs to one layer");
            }

            layers.Add(layer!);
        }

        if (layers.Count < MinLayers)
        {
            return errors.At(valueNode.Start, $"'routing.layers' needs at least {MinLayers} layers (a reference crosses from one to another)");
        }

        settings = settings with { Layers = layers };
        return null;
    }

    private ManifestError? ReadLayer(YamlNode item, out RoutingLayer? layer)
    {
        layer = null;
        if (item is not YamlMappingNode mapping)
        {
            return errors.At(item.Start, "every entry of 'routing.layers' must be a mapping with a 'name' and 'paths'");
        }

        string? name = null;
        IReadOnlyList<string>? paths = null;
        foreach ((YamlNode keyNode, YamlNode value) in mapping.Children)
        {
            switch ((keyNode as YamlScalarNode)?.Value)
            {
                case "name" when ManifestScalars.Text(value) is string text && !string.IsNullOrWhiteSpace(text):
                    name = text;
                    break;
                case "name":
                    return errors.At(value.Start, "'routing.layers[].name' must be a non-empty name");
                case "paths":
                    if (new GlobListReader(errors).Read(value, "routing.layers[].paths", out IReadOnlyList<string> globs) is ManifestError error)
                    {
                        return error;
                    }

                    if (globs.Count == 0)
                    {
                        return errors.At(value.Start, "'routing.layers[].paths' must list at least one glob");
                    }

                    paths = globs;
                    break;
                case string key:
                    return errors.At(keyNode.Start, $"unknown key 'routing.layers[].{key}' (known: name, paths)");
                default:
                    return errors.NotAName(keyNode);
            }
        }

        if (name is null || paths is null)
        {
            return errors.At(item.Start, "every entry of 'routing.layers' needs a 'name' and 'paths'");
        }

        layer = new RoutingLayer(name, paths);
        return null;
    }

    private static (string Glob, RoutingLayer Other)? Overlap(IEnumerable<RoutingLayer> layers, RoutingLayer layer)
    {
        foreach (RoutingLayer other in layers)
        {
            foreach (string glob in layer.Paths)
            {
                if (other.Paths.Any(otherGlob => GlobOverlap.CouldMatch(otherGlob, glob, asTree: false) || GlobOverlap.CouldMatch(glob, otherGlob, asTree: false)))
                {
                    return (glob, other);
                }
            }
        }

        return null;
    }

    private ManifestError? CheckAcrossKeys(RoutingSettings settings, Mark sectionStart)
    {
        if (settings.LargeLines <= settings.FixMaxLines)
        {
            return errors.At(
                MarkOf(sectionStart, "largeLines", "fix.maxLines"),
                $"'routing.largeLines' ({settings.LargeLines}) must be above 'routing.fix.maxLines' ({settings.FixMaxLines})");
        }

        RoutingCoverage coverage = settings.Coverage;
        int[] thresholds = [coverage.Fix, coverage.Behaviour, coverage.Feature, coverage.Critical];
        for (int index = 1; index < thresholds.Length; index++)
        {
            if (thresholds[index] < thresholds[index - 1])
            {
                string lower = ChangeClassNames.ToName((ChangeClass)index);
                string higher = ChangeClassNames.ToName((ChangeClass)(index + 1));
                return errors.At(
                    MarkOf(sectionStart, $"coverage.{higher}", $"coverage.{lower}"),
                    $"'routing.coverage.{higher}' ({thresholds[index]}) is below 'routing.coverage.{lower}' ({thresholds[index - 1]}); "
                    + "coverage never decreases from fix to critical");
            }
        }

        RoutingRequirements require = settings.Require;
        if (require.ReviewKeyFrom is ChangeClass keyFrom && keyFrom < require.ReviewFrom)
        {
            return errors.At(
                MarkOf(sectionStart, "require.reviewKeyFrom", "require.reviewFrom"),
                $"'routing.require.reviewKeyFrom' ({ChangeClassNames.ToName(keyFrom)}) is below 'routing.require.reviewFrom' "
                + $"({ChangeClassNames.ToName(require.ReviewFrom)}): a review key cannot be required where no review runs; raise it or set none");
        }

        if (require.ArchitectureDeclaredFrom is ChangeClass declaredFrom && declaredFrom < require.ArchitectureFrom)
        {
            return errors.At(
                MarkOf(sectionStart, "require.architectureDeclaredFrom", "require.architectureFrom"),
                $"'routing.require.architectureDeclaredFrom' ({ChangeClassNames.ToName(declaredFrom)}) is below 'routing.require.architectureFrom' "
                + $"({ChangeClassNames.ToName(require.ArchitectureFrom)}); raise it or set none");
        }

        return null;
    }

    /// <summary>The position of the first of <paramref name="keys"/> the manifest wrote, else the section's start.</summary>
    private Mark MarkOf(Mark sectionStart, params string[] keys) =>
        keys.Select(key => written.TryGetValue(key, out Mark mark) ? mark : (Mark?)null).FirstOrDefault(mark => mark is not null) ?? sectionStart;
}
