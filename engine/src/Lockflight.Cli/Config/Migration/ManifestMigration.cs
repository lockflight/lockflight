using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config.Migration;

/// <summary>
/// Migrates <c>lockflight.yaml</c> from version 1 to 2 by editing its text, never by rewriting the tree, so the person's comments,
/// key order and formatting stay: the <c>version</c> line becomes 2 (or is inserted first), the profile comments an older
/// <c>init</c> wrote are removed when they are exactly the template's, and the sections version 2 added are appended with
/// migration values (the new checks only report; routing spells out the default preset). The edited text must parse to the
/// settings version 1 had; otherwise nothing is written and the person gets the snippet. A byte-order mark and the file's line
/// endings are kept; a file with more than one YAML document is left for a person.
/// </summary>
internal static partial class ManifestMigration
{
    private const string Header =
        "# Manifest version 2, written by `lockflight update`. The checks version 2 added only report here, so this upgrade changes\n"
        + "# no verdict; set each to fail when the team is ready (configuration guide, \"Manifest version 2\").\n";

    /// <summary>The stop-gate comments of the lite and standard <c>init</c> profiles, removed only when they match exactly.</summary>
    private static readonly string[][] ProfileComments =
    [
        ["# Off in the lite profile; turn it on once you know how long your build and tests take."],
        [
            "# Off at first in the standard profile too: turn it on once you know how long your build and tests take and the",
            "# known-failing tests are accepted (a person runs '.lockflight/bin/lockflight baseline tests').",
        ],
    ];

    /// <summary>The switched gate steps, each with its migration value and how to make it judge.</summary>
    private static readonly GateToggle[] GateToggles =
    [
        new("quality", "suppressions", "report", "fail: new suppressions fail the pr profile"),
        new("docs", "references", "report", "fail: references the change breaks fail the pr profile"),
        new("ef", "pendingModelChanges", "auto", "auto: fails when the repository pins dotnet-ef; fail forces it"),
    ];

    public static ManifestMigrationResult Migrate(string text, string root, string path)
    {
        bool byteOrderMark = text.StartsWith('﻿');
        string body = byteOrderMark ? text[1..] : text;
        string newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string lf = body.Replace("\r\n", "\n", StringComparison.Ordinal);

        ManifestLoadResult loaded = new ManifestParser(root, path).Parse(lf);
        if (loaded.Error is ManifestError error)
        {
            return new ManifestMigrationResult(ManifestMigrationStatus.Invalid, null, $"{error.Describe()}; fix it first, nothing was changed");
        }

        Manifest before = loaded.Manifest!;
        if (before.Version == ManifestVersions.Current)
        {
            return new ManifestMigrationResult(ManifestMigrationStatus.AlreadyCurrent, null, $"{Manifest.FileName} is version 2 already");
        }

        YamlStream stream = new();
        stream.Load(new StringReader(lf));
        if (stream.Documents.Count > 1)
        {
            return Manual("it holds more than one YAML document", gatesPresent: false, GateToggles);
        }

        YamlMappingNode? rootNode = stream.Documents.Count == 1 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        YamlMappingNode? gates = Child(rootNode, "gates") as YamlMappingNode;
        List<string> lines = [.. lf.Split('\n')];
        SetVersion(lines);
        RemoveProfileComments(lines);

        GateToggle[] missing = [.. GateToggles.Where(toggle => Child(Child(gates, toggle.Section), toggle.Key) is null)];
        if (missing.FirstOrDefault(toggle => Child(gates, toggle.Section) is not null) is GateToggle halfSet)
        {
            return Manual($"'gates.{halfSet.Section}' is there without '{halfSet.Key}'", gates is not null, missing);
        }

        if (gates is not null && missing.Length > 0 && InsertIntoGates(lines, missing) is false)
        {
            return Manual("its 'gates' section is not a block mapping the migration can add keys to", gatesPresent: true, missing);
        }

        string appended = Header
            + (gates is null && Child(rootNode, "gates") is null ? GatesBlock(missing) : string.Empty)
            + ManifestYaml.Approvals(ApprovalCheckMode.Report, RoutingPreset.Default) + "\n"
            + ManifestYaml.Routing(RoutingPreset.Default);
        string edited = string.Join('\n', lines).TrimEnd('\n') + "\n\n" + appended.TrimEnd('\n') + "\n";

        if (Problem(before, edited, root, path) is string problem)
        {
            return Manual($"the edited file would not read as the same settings ({problem})", gates is not null, missing);
        }

        string written = (byteOrderMark ? "﻿" : string.Empty) + edited.Replace("\n", newline, StringComparison.Ordinal);
        List<string> checks = [.. missing.Select(toggle => $"gates.{toggle.Section}.{toggle.Key}: {toggle.Value} ({toggle.Note})")];
        checks.Add("approvals.check: report (fail: a missing CODEOWNERS coverage or spec-first violation fails the CI check)");
        checks.Add("routing: preset default with every value written down (the CI judge routes by it)");
        return new ManifestMigrationResult(ManifestMigrationStatus.Migrated, written, $"{Manifest.FileName}: version 1 → 2") { NewChecks = checks };
    }

    /// <summary>Replaces <c>version: 1</c>, or inserts <c>version: 2</c> as the first key after the leading comments.</summary>
    private static void SetVersion(List<string> lines)
    {
        int existing = lines.FindIndex(line => VersionLine().IsMatch(line));
        if (existing >= 0)
        {
            lines[existing] = VersionLine().Replace(lines[existing], "version: 2${rest}");
            return;
        }

        int first = lines.FindIndex(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#') && line.Trim() != "---");
        lines.Insert(first < 0 ? lines.Count : first, "version: 2");
    }

    private static void RemoveProfileComments(List<string> lines)
    {
        foreach (string[] comment in ProfileComments)
        {
            for (int index = 0; index + comment.Length <= lines.Count; index++)
            {
                if (comment.Select((line, offset) => lines[index + offset] == line).All(same => same))
                {
                    lines.RemoveRange(index, comment.Length);
                    break;
                }
            }
        }
    }

    /// <summary>Adds the missing toggles at the end of a block-style top-level <c>gates:</c>; false when the section is not one.</summary>
    private static bool InsertIntoGates(List<string> lines, GateToggle[] missing)
    {
        int start = lines.FindIndex(line => GatesLine().IsMatch(line));
        if (start < 0)
        {
            return false;
        }

        int end = start + 1;
        int last = start;
        string? indent = null;
        for (; end < lines.Count; end++)
        {
            string line = lines[end];
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                break;
            }

            indent ??= line[..(line.Length - line.TrimStart().Length)];
            last = end;
        }

        if (indent is null || indent.Contains('\t', StringComparison.Ordinal))
        {
            return false;
        }

        lines.InsertRange(last + 1, missing.SelectMany(toggle => new[]
        {
            $"{indent}{toggle.Section}:",
            $"{indent}{indent}{toggle.Key}: {toggle.Value}    # {toggle.Note}",
        }));
        return true;
    }

    private static string GatesBlock(GateToggle[] toggles) =>
        toggles.Length == 0
            ? string.Empty
            : "gates:\n" + string.Concat(toggles.Select(toggle => $"  {toggle.Section}:\n    {toggle.Key}: {toggle.Value}    # {toggle.Note}\n")) + "\n";

    /// <summary>Why <paramref name="edited"/> does not read as <paramref name="before"/> plus the migration values; null when it does.</summary>
    private static string? Problem(Manifest before, string edited, string root, string path)
    {
        ManifestLoadResult after = new ManifestParser(root, path).Parse(edited);
        if (after.Error is ManifestError error)
        {
            return error.Message;
        }

        return Settings(after.Manifest!) == Settings(before, migrated: true) ? null : "a setting changed";
    }

    /// <summary>Every setting the migration must keep, written out so two manifests compare by value.</summary>
    private static string Settings(Manifest manifest, bool migrated = false)
    {
        StringBuilder text = new();
        text.AppendLine($"protected={string.Join('|', manifest.ProtectedGlobs)}");
        text.AppendLine($"stop={manifest.StopGate}|{manifest.StopTarget}");
        text.AppendLine($"gates={manifest.Gates}");
        text.AppendLine($"units={string.Join('|', manifest.Units)}");
        text.AppendLine($"review={manifest.Review}");
        ApprovalSettings approvals = migrated ? ApprovalSettings.ForVersion1 : manifest.Approvals;
        text.AppendLine($"approvals={approvals.Check}|{approvals.Mode}|{approvals.SpecFirstFrom}|{string.Join('|', approvals.SpecFirstPaths)}|{string.Join('|', approvals.SpecPaths)}");
        RoutingSettings routing = migrated ? RoutingPresets.For(RoutingPreset.Default) : manifest.Routing;
        text.AppendLine($"routing={routing.Preset}|{routing.Floor}|{routing.FixMaxLines}|{routing.FixMaxFiles}|{routing.LargeLines}|{routing.Structural}|{routing.Coverage}|{routing.Require}");
        text.AppendLine($"routing.lists={string.Join('|', routing.Critical)};{string.Join('|', routing.DbPaths)};{routing.Layers.Count}");
        text.AppendLine($"autonomy={manifest.Autonomy}|shell={manifest.Shell}");
        return text.ToString();
    }

    private static ManifestMigrationResult Manual(string reason, bool gatesPresent, GateToggle[] missing)
    {
        string gatesHint = missing.Length == 0
            ? string.Empty
            : gatesPresent
                ? "# under gates:\n" + string.Concat(missing.Select(toggle => $"#   {toggle.Section}:\n#     {toggle.Key}: {toggle.Value}\n"))
                : GatesBlock(missing);
        string snippet = "# change 'version: 1' to 'version: 2' (or add it as the first key), then add:\n" + gatesHint
            + ManifestYaml.Approvals(ApprovalCheckMode.Report, RoutingPreset.Default) + "\n" + ManifestYaml.Routing(RoutingPreset.Default);
        return new ManifestMigrationResult(ManifestMigrationStatus.ManualEdit, null, $"{Manifest.FileName} was left as it is: {reason}") { Snippet = snippet };
    }

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode mapping
            ? mapping.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: string name } && name == key).Value
            : null;

    [GeneratedRegex(@"^version:[ \t]*1(?<rest>[ \t]*(#.*)?)$")]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^gates:[ \t]*(#.*)?$")]
    private static partial Regex GatesLine();
}
