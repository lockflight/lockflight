using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>review</c> section of <c>lockflight.yaml</c>: model, budget, target branch and diff limit.</summary>
internal sealed partial class ReviewSectionReader(ManifestErrors errors)
{
    public ManifestError? Read(YamlNode valueNode, ref ReviewSettings review)
    {
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, "'review' must be a mapping (known keys: model, maxUsd, target, maxDiffCharacters)");
        }

        ReviewSettings settings = review with { FromManifest = true };
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = (value as YamlScalarNode)?.Value;
            switch (key)
            {
                case "model" when text is not null && ModelName().IsMatch(text):
                    settings = settings with { Model = text };
                    break;
                case "model":
                    return errors.At(value.Start, "'review.model' must be a Claude Code model name such as opus or sonnet");
                case "maxUsd" when decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal budget)
                    && budget is > 0 and <= ReviewSettings.MaxBudgetUsd:
                    settings = settings with { BudgetUsd = budget };
                    break;
                case "target" when text is not null && TargetName().IsMatch(text):
                    settings = settings with { Target = text };
                    break;
                case "target":
                    return errors.At(value.Start, "'review.target' must be a branch name such as main or origin/main");
                case "maxDiffCharacters" when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int limit)
                    && limit is >= ReviewSettings.MinDiffCharacters and <= ReviewSettings.DiffCharactersCeiling:
                    settings = settings with { MaxDiffCharacters = limit };
                    break;
                case "maxDiffCharacters":
                    return errors.At(
                        value.Start,
                        $"'review.maxDiffCharacters' must be a whole number of characters from {ReviewSettings.MinDiffCharacters} to {ReviewSettings.DiffCharactersCeiling} "
                        + $"(the engine's ceiling; default {ReviewSettings.DefaultMaxDiffCharacters}), such as 200000; split a larger change into smaller pull requests");
                case "maxUsd":
                    return errors.At(value.Start, $"'review.maxUsd' must be an amount in US dollars above 0 and at most {ReviewSettings.MaxBudgetUsd}, such as 3.00");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'review.{key}' (known: model, maxUsd, target, maxDiffCharacters)");
            }
        }

        review = settings;
        return null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\[\]-]*$")]
    private static partial Regex ModelName();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._/-]*$")]
    private static partial Regex TargetName();
}
