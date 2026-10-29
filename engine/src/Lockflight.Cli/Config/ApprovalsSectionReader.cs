using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Reads the <c>approvals</c> section of a version 2 manifest: the check mode, where specs are approved, and which paths need spec-first.</summary>
internal sealed class ApprovalsSectionReader(ManifestErrors errors)
{
    private const string Known = "check, default, specFirstFrom, specFirstPaths, specPaths";

    /// <param name="specFirstFromWritten">Whether the manifest set <c>specFirstFrom</c>; when it did not, the routing preset seeds it.</param>
    public ManifestError? Read(YamlNode valueNode, ref ApprovalSettings approvals, out bool specFirstFromWritten)
    {
        specFirstFromWritten = false;
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, $"'approvals' must be a mapping (known keys: {Known})");
        }

        GlobListReader globs = new(errors);
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = ManifestScalars.Text(value);
            switch (key)
            {
                case "check" when text is "fail" or "report":
                    approvals = approvals with { Check = text == "fail" ? ApprovalCheckMode.Fail : ApprovalCheckMode.Report };
                    break;
                case "check":
                    return errors.At(value.Start, "'approvals.check' must be fail or report");
                case "default" when text is "same-pr" or "spec-first":
                    approvals = approvals with { Mode = text == "same-pr" ? ApprovalMode.SamePr : ApprovalMode.SpecFirst };
                    break;
                case "default":
                    return errors.At(value.Start, "'approvals.default' must be same-pr or spec-first");
                case "specFirstFrom" when text == ChangeClassNames.None || text is "feature" or "critical":
                    ManifestScalars.TryClass(value, ChangeClass.Feature, allowNone: true, out ChangeClass? from);
                    approvals = approvals with { SpecFirstFrom = from };
                    specFirstFromWritten = true;
                    break;
                case "specFirstFrom":
                    return errors.At(value.Start, "'approvals.specFirstFrom' must be none, feature or critical");
                case "specFirstPaths":
                    if (globs.Read(value, "approvals.specFirstPaths", out IReadOnlyList<string> specFirstPaths) is ManifestError specFirstError)
                    {
                        return specFirstError;
                    }

                    approvals = approvals with { SpecFirstPaths = specFirstPaths };
                    break;
                case "specPaths":
                    if (globs.Read(value, "approvals.specPaths", out IReadOnlyList<string> specPaths) is ManifestError specPathsError)
                    {
                        return specPathsError;
                    }

                    if (specPaths.Count == 0)
                    {
                        return errors.At(value.Start, $"'approvals.specPaths' must list at least one glob (default [\"{ApprovalSettings.DefaultSpecPath}\"])");
                    }

                    approvals = approvals with { SpecPaths = specPaths };
                    break;
                default:
                    return errors.At(keyNode.Start, $"unknown key 'approvals.{key}' (known: {Known})");
            }
        }

        return null;
    }
}
