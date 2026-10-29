using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the reserved <c>autonomy</c> section of a version 2 manifest: every key optional, every present value range-checked.</summary>
internal sealed class AutonomySectionReader(ManifestErrors errors)
{
    private const string Known = "maxClass, budgetUsd, timeMinutes";

    public ManifestError? Read(YamlNode valueNode, out AutonomySettings autonomy)
    {
        autonomy = new AutonomySettings(MaxClass: null, BudgetUsd: null, TimeMinutes: null);
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, $"'autonomy' must be a mapping (known keys: {Known})");
        }

        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = ManifestScalars.Text(value);
            switch (key)
            {
                case "maxClass" when ManifestScalars.TryClass(value, ChangeClass.Fix, allowNone: false, out ChangeClass? maxClass):
                    autonomy = autonomy with { MaxClass = maxClass };
                    break;
                case "maxClass":
                    return errors.At(value.Start, $"'autonomy.maxClass' must be {ChangeClassNames.Choices(ChangeClass.Fix, allowNone: false)}");
                case "budgetUsd" when decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal budget)
                    && budget is > 0 and <= AutonomySettings.MaxBudgetUsd:
                    autonomy = autonomy with { BudgetUsd = budget };
                    break;
                case "budgetUsd":
                    return errors.At(value.Start, $"'autonomy.budgetUsd' must be an amount in US dollars above 0 and at most {AutonomySettings.MaxBudgetUsd}, such as 5.00");
                case "timeMinutes" when ManifestScalars.TryInteger(value, 1, AutonomySettings.MaxTimeMinutes, out int minutes):
                    autonomy = autonomy with { TimeMinutes = minutes };
                    break;
                case "timeMinutes":
                    return errors.At(value.Start, $"'autonomy.timeMinutes' must be a whole number of minutes from 1 to {AutonomySettings.MaxTimeMinutes}");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'autonomy.{key}' (known: {Known})");
            }
        }

        return null;
    }
}
