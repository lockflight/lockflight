using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>
/// Reads the <c>shell</c> section of a version 2 manifest: the clone URL of the workspace shell this repository is a unit of, its
/// branch, and this repository's unit path there. A URL that carries credentials is refused: they would be committed.
/// </summary>
internal sealed partial class ShellSectionReader(ManifestErrors errors)
{
    private const string Known = "url, ref, unit";

    public ManifestError? Read(YamlNode valueNode, out ShellSettings? shell)
    {
        shell = null;
        if (valueNode is not YamlMappingNode section)
        {
            return errors.At(valueNode.Start, $"'shell' must be a mapping (known keys: {Known})");
        }

        string? url = null;
        string? reference = null;
        string? unit = null;
        foreach ((YamlNode keyNode, YamlNode value) in section.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            string? text = ManifestScalars.Text(value);
            switch (key)
            {
                case "url":
                    if (UrlProblem(text) is string problem)
                    {
                        return errors.At(value.Start, problem);
                    }

                    url = text;
                    break;
                case "ref" when text is not null && RefName().IsMatch(text) && !text.Contains("..", StringComparison.Ordinal):
                    reference = text;
                    break;
                case "ref":
                    return errors.At(value.Start, "'shell.ref' must be a branch name such as main");
                case "unit" when UnitsSectionReader.UnitPath(text) is string path:
                    unit = path;
                    break;
                case "unit":
                    return errors.At(value.Start, "'shell.unit' must be a relative path inside the shell, without '..' (for example services/orders)");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'shell.{key}' (known: {Known})");
            }
        }

        if (url is null || unit is null)
        {
            return errors.At(section.Start, "'shell' needs a 'url' and a 'unit' (this repository's path among the shell's units)");
        }

        shell = new ShellSettings(url, reference, unit);
        return null;
    }

    /// <summary>Why <paramref name="url"/> is not a shell URL; null when it is an <c>https://</c> or SSH URL without credentials.</summary>
    private static string? UrlProblem(string? url)
    {
        const string Credentials =
            "'shell.url' must not carry credentials (they would be committed); use a deploy key or a CI secret for the clone";
        if (string.IsNullOrWhiteSpace(url))
        {
            return "'shell.url' must be the shell's clone URL (https:// or SSH, such as git@github.com:example/shell.git)";
        }

        if (ScpLike().IsMatch(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "ssh") || uri.Host.Length == 0)
        {
            return "'shell.url' must be an https:// or SSH URL (git@host:path or ssh://host/path)";
        }

        bool secretInUser = uri.Scheme == "https" ? uri.UserInfo.Length > 0 : uri.UserInfo.Contains(':', StringComparison.Ordinal);
        return secretInUser || uri.Query.Length > 0 ? Credentials : null;
    }

    /// <summary>The SSH shorthand <c>user@host:path</c> (no password possible in it).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+:[^\s:]+$")]
    private static partial Regex ScpLike();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._/-]*$")]
    private static partial Regex RefName();
}
