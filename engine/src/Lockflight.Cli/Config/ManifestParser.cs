using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>
/// Strict, reflection-free reader of <c>lockflight.yaml</c> (YamlDotNet node tree; Native AOT safe). Unknown keys and
/// wrong types are errors with a line and column, never silently ignored.
/// </summary>
internal sealed class ManifestParser(string root, string path)
{
    private const string KnownLegacyKeys = "version, protected, stop, gates, units, review";
    private const string KnownKeys = "version, requiresEngine, protected, stop, gates, routing, approvals, autonomy, units, shell, review";

    /// <summary>The version a local build reports (<c>0.0.0-local</c>), which is not held to <c>requiresEngine</c>.</summary>
    private const string LocalBuildVersion = "0.0.0-local";

    private readonly ManifestErrors errors = new(path);

    /// <summary>The engine version reading the manifest, named in errors and compared with <c>requiresEngine</c>.</summary>
    public string Engine { get; init; } = ToolPin.RunningVersion;

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
            return ManifestLoadResult.Invalid(errors.AtStart($"not valid YAML: {exception.Message}"));
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode rootNode)
        {
            return ManifestLoadResult.Invalid(errors.AtStart("the manifest must be a mapping of keys"));
        }

        return ParseRoot(rootNode);
    }

    private ManifestLoadResult ParseRoot(YamlMappingNode rootNode)
    {
        if (ReadVersion(rootNode, out int version) is ManifestError versionError)
        {
            return ManifestLoadResult.Invalid(versionError);
        }

        if (ReadRequiresEngine(rootNode, version, out string? requiresEngine) is ManifestError engineError)
        {
            return ManifestLoadResult.Invalid(engineError);
        }

        ManifestLoadResult result = ParseSections(rootNode, version, requiresEngine);
        return result.Error is ManifestError error ? ManifestLoadResult.Invalid(WithEngineNote(error)) : result;
    }

    private ManifestLoadResult ParseSections(YamlMappingNode rootNode, int version, string? requiresEngine)
    {
        bool current = version == ManifestVersions.Current;
        List<string> protectedGlobs = [];
        bool stopGate = false;
        string? stopTarget = null;
        // Checks a version 2 manifest leaves out judge; in version 1 they only report (an engine upgrade turns nothing red).
        GateSettings gates = current ? GateSettings.Default with { Suppressions = GateMode.Fail } : GateSettings.Default;
        ReviewSettings review = ReviewSettings.Default;
        RoutingSettings routing = RoutingSettings.Default;
        ApprovalSettings approvals = current ? ApprovalSettings.Default : ApprovalSettings.ForVersion1;
        bool specFirstFromWritten = false;
        AutonomySettings? autonomy = null;
        ShellSettings? shell = null;
        List<Unit> units = [];
        YamlNode? unitsKey = null;
        YamlNode? shellKey = null;

        foreach ((YamlNode keyNode, YamlNode valueNode) in rootNode.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return ManifestLoadResult.Invalid(errors.NotAName(keyNode));
            }

            if (!current && ManifestVersions.CurrentOnlyKeys.Contains(key, StringComparer.Ordinal))
            {
                return ManifestLoadResult.Invalid(errors.At(keyNode.Start, $"'{key}' needs 'version: {ManifestVersions.Current}' (this manifest is version {ManifestVersions.Legacy})"));
            }

            ManifestError? error = key switch
            {
                "version" or "requiresEngine" => null,
                "protected" when current => new GlobListReader(errors).Read(valueNode, key, out IReadOnlyList<string> globs) ?? AddAll(protectedGlobs, globs),
                "protected" => ReadStringList(valueNode, key, protectedGlobs),
                "stop" => ReadStop(valueNode, ref stopGate, ref stopTarget),
                "gates" => new GatesSectionReader(errors).Read(valueNode, ref gates),
                "units" => new UnitsSectionReader(errors).Read(valueNode, units),
                "review" => new ReviewSectionReader(errors).Read(valueNode, ref review),
                "routing" => new RoutingSectionReader(errors).Read(valueNode, out routing),
                "approvals" => new ApprovalsSectionReader(errors).Read(valueNode, ref approvals, out specFirstFromWritten),
                "autonomy" => ReadAutonomy(valueNode, out autonomy),
                "shell" => new ShellSectionReader(errors).Read(valueNode, out shell),
                _ => errors.At(keyNode.Start, $"unknown key '{key}' (known: {(current ? KnownKeys : KnownLegacyKeys)})"),
            };
            if (error is not null)
            {
                return ManifestLoadResult.Invalid(error);
            }

            unitsKey = key == "units" ? keyNode : unitsKey;
            shellKey = key == "shell" ? keyNode : shellKey;
        }

        if (unitsKey is not null && shellKey is not null)
        {
            return ManifestLoadResult.Invalid(errors.At(
                shellKey.Start, "'shell' and 'units' cannot both be set: a repository is either a shell or monorepo with units, or a unit of a shell"));
        }

        if (!specFirstFromWritten)
        {
            approvals = approvals with { SpecFirstFrom = RoutingPresets.SpecFirstFrom(routing.Preset) };
        }

        return ManifestLoadResult.Loaded(new Manifest(root, protectedGlobs, stopGate, stopTarget, gates)
        {
            Units = units,
            Review = review,
            Version = version,
            RequiresEngine = requiresEngine,
            Routing = routing,
            Approvals = approvals,
            Autonomy = autonomy,
            Shell = shell,
        });
    }

    /// <summary>
    /// The manifest's version, read before any other key so that every later error can say which version it judged: absent is 1,
    /// otherwise 1 or 2.
    /// </summary>
    private ManifestError? ReadVersion(YamlMappingNode rootNode, out int version)
    {
        version = ManifestVersions.Legacy;
        if (Value(rootNode, "version") is not YamlNode valueNode)
        {
            return null;
        }

        if (valueNode is YamlScalarNode { Value: string text }
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int read)
            && read is ManifestVersions.Legacy or ManifestVersions.Current)
        {
            version = read;
            return null;
        }

        return errors.At(
            valueNode.Start,
            $"'version' must be {ManifestVersions.Legacy} or {ManifestVersions.Current} — this engine is {Engine}; a newer engine may read a later version");
    }

    /// <summary>
    /// <c>requiresEngine</c>, checked before the other keys: a manifest written for a newer engine says so instead of failing on
    /// a key this engine does not know. A development build is not held to it.
    /// </summary>
    private ManifestError? ReadRequiresEngine(YamlMappingNode rootNode, int version, out string? requiresEngine)
    {
        requiresEngine = null;
        if (version != ManifestVersions.Current || Value(rootNode, "requiresEngine") is not YamlNode valueNode)
        {
            return null;
        }

        if (valueNode is not YamlScalarNode { Value: string text } || EngineVersion.Parse(text) is not EngineVersion required || text.Contains('+', StringComparison.Ordinal))
        {
            return errors.At(valueNode.Start, "'requiresEngine' must be an engine version such as 0.3.0");
        }

        requiresEngine = text;
        EngineVersion? running = EngineVersion.Parse(Engine);
        bool development = running is null || running.IsDevelopment || running.ToString().StartsWith(LocalBuildVersion, StringComparison.OrdinalIgnoreCase);
        return development || running!.CompareTo(required) >= 0
            ? null
            : errors.At(valueNode.Start, $"this manifest needs Lockflight >= {text}; this engine is {Engine} (ask the user to update the engine)");
    }

    /// <summary>An unknown key may be one a newer engine knows: the error names this engine's version.</summary>
    private ManifestError WithEngineNote(ManifestError error) =>
        error.Message.StartsWith("unknown key", StringComparison.Ordinal)
            ? error with { Message = $"{error.Message} — this engine is {Engine}; a newer engine may know it" }
            : error;

    private ManifestError? ReadAutonomy(YamlNode valueNode, out AutonomySettings? autonomy)
    {
        ManifestError? error = new AutonomySectionReader(errors).Read(valueNode, out AutonomySettings read);
        autonomy = read;
        return error;
    }

    private static ManifestError? AddAll(List<string> target, IReadOnlyList<string> items)
    {
        target.AddRange(items);
        return null;
    }

    private static YamlNode? Value(YamlMappingNode mapping, string key) =>
        mapping.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: string name } && name == key).Value;

    private ManifestError? ReadStringList(YamlNode valueNode, string key, List<string> target)
    {
        if (valueNode is not YamlSequenceNode sequence)
        {
            return errors.At(valueNode.Start, $"'{key}' must be a list of strings");
        }

        foreach (YamlNode item in sequence.Children)
        {
            if (item is not YamlScalarNode { Value: string value } || value.Length == 0)
            {
                return errors.At(item.Start, $"every entry of '{key}' must be a non-empty string");
            }

            target.Add(value);
        }

        return null;
    }

    private ManifestError? ReadStop(YamlNode valueNode, ref bool gate, ref string? target)
    {
        if (valueNode is not YamlMappingNode stop)
        {
            return errors.At(valueNode.Start, "'stop' must be a mapping with 'gate' and optional 'target'");
        }

        foreach ((YamlNode keyNode, YamlNode value) in stop.Children)
        {
            if (keyNode is not YamlScalarNode { Value: string key })
            {
                return errors.NotAName(keyNode);
            }

            switch (key)
            {
                case "gate" when value is YamlScalarNode { Value: "true" or "false" } flag:
                    gate = flag.Value == "true";
                    break;
                case "gate":
                    return errors.At(value.Start, "'stop.gate' must be true or false");
                case "target" when value is YamlScalarNode { Value: string path } && path.Length > 0:
                    target = path;
                    break;
                case "target":
                    return errors.At(value.Start, "'stop.target' must be a non-empty path");
                default:
                    return errors.At(keyNode.Start, $"unknown key 'stop.{key}' (known: gate, target)");
            }
        }

        return null;
    }

    private ManifestLoadResult Invalid(Mark mark, string message) => ManifestLoadResult.Invalid(errors.At(mark, message));
}
