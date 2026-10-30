using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>ci</c> section of <c>lockflight.yaml</c>: the CI judge's limits and path lists.</summary>
internal sealed class CiSectionReader(ManifestErrors errors)
{
    private const string Known = "maxRedCommits, timeoutMinutes, flakyRetries, lowRiskPaths, testInputs, redCommits";

    public ManifestError? Read(YamlNode valueNode, ref CiSettings ci)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, $"'ci' must be a mapping (known keys: {Known})");
        }

        CiSettings settings = ci with { FromManifest = true };
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "maxRedCommits" when WholeNumber(text, 1, CiSettings.MaxRedCommitsCeiling) is int count:
                    settings = settings with { MaxRedCommits = count };
                    break;
                case "maxRedCommits":
                    return errors.At(value.Start, $"'ci.maxRedCommits' must be a whole number from 1 to {CiSettings.MaxRedCommitsCeiling} (default {CiSettings.DefaultMaxRedCommits}); split a larger pull request");
                case "timeoutMinutes" when WholeNumber(text, 1, CiSettings.TimeoutMinutesCeiling) is int minutes:
                    settings = settings with { TimeoutMinutes = minutes };
                    break;
                case "timeoutMinutes":
                    return errors.At(value.Start, $"'ci.timeoutMinutes' must be a whole number of minutes from 1 to {CiSettings.TimeoutMinutesCeiling} (default {CiSettings.DefaultTimeoutMinutes})");
                case "flakyRetries" when WholeNumber(text, 0, CiSettings.FlakyRetriesCeiling) is int retries:
                    settings = settings with { FlakyRetries = retries };
                    break;
                case "flakyRetries":
                    return errors.At(value.Start, $"'ci.flakyRetries' must be a whole number from 0 to {CiSettings.FlakyRetriesCeiling} (default {CiSettings.DefaultFlakyRetries})");
                case "lowRiskPaths" or "testInputs":
                    if (ReadGlobs(value) is not List<string> globs)
                    {
                        return errors.At(value.Start, $"'ci.{key}' must be a list of non-empty path globs without duplicates");
                    }

                    settings = key == "lowRiskPaths" ? settings with { LowRiskPaths = globs } : settings with { TestInputs = globs };
                    break;
                case "redCommits" when value is YamlScalarNode { Value: "true" or "false" } flag:
                    settings = settings with { RedCommits = flag.Value == "true" };
                    break;
                case "redCommits":
                    return errors.At(value.Start, "'ci.redCommits' must be true or false");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'ci.{key}' (known: {Known})");
            }
        }

        ci = settings;
        return null;
    }

    private static int? WholeNumber(string? text, int minimum, int maximum) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= minimum && number <= maximum ? number : null;

    private static List<string>? ReadGlobs(YamlNode value)
    {
        if (value is not YamlSequenceNode sequence)
        {
            return null;
        }

        List<string> globs = [];
        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlScalarNode { Value: string glob } || glob.Trim().Length == 0 || globs.Contains(glob, StringComparer.Ordinal))
            {
                return null;
            }

            globs.Add(glob);
        }

        return globs;
    }
}
