using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads what can raise a pull request's class from outside its diff, after the change set (it needs the changed paths to find the
/// covering features): each covering feature's spec class line at head (<see cref="SpecClassLine"/>), each covering feature's
/// recorded class on the base branch (<see cref="FeatureClassRecord"/>), the <c>lockflight:class/&lt;class&gt;</c> labels and the
/// workflow's <c>--raise</c> input. Every raise can only raise: the classifier takes the highest of the computed class and the
/// raises, and a raise below the computed class is shown as ignored.
/// </summary>
internal static class RaiseReader
{
    public const string LabelPrefix = "lockflight:class/";

    private const string SpecFile = "spec.md";

    public static async Task<RaiseReading> ReadAsync(string projectRoot, ChangeSet changeSet, RaiseInputs inputs, CancellationToken cancellationToken)
    {
        List<ClassRaise> raises = [];
        List<string> notes = [];
        IReadOnlyList<string> features = CoveringFeatures.Find(changeSet, inputs.Branch);
        if (await RoutingGit.PrefixAsync(projectRoot, cancellationToken) is not string prefix)
        {
            return new RaiseReading(RaiseReadStatus.GitFailed, [], ["git could not name the project's place in its repository"]);
        }

        IReadOnlyDictionary<string, string>? specs = await HeadTextsAsync(projectRoot, changeSet, prefix, [.. features.Select(feature => $"specs/{feature}/{SpecFile}")], cancellationToken);
        IReadOnlyDictionary<string, byte[]>? records = await RoutingGit.BlobsAsync(
            projectRoot, changeSet.BaseCommit, [.. features.Select(feature => prefix + FeatureClassRecord.PathFor(feature))], cancellationToken);
        if (specs is null || records is null)
        {
            return new RaiseReading(RaiseReadStatus.GitFailed, [], ["git could not read the covering features' specs or records"]);
        }

        foreach (string feature in features)
        {
            if (specs.TryGetValue($"specs/{feature}/{SpecFile}", out string? spec))
            {
                if (SpecClassLine.Read(spec, out string? invalid) is ChangeClass specClass)
                {
                    raises.Add(new ClassRaise($"spec {feature}", specClass));
                }
                else if (invalid is not null)
                {
                    notes.Add($"spec {feature}: '**Class**: {invalid}' is not a class (fix, behaviour, feature, critical); ignored");
                }
            }

            if (records.TryGetValue(prefix + FeatureClassRecord.PathFor(feature), out byte[]? record)
                && RevisionText.Decode(record) is string recordText
                && FeatureClassRecord.Parse(recordText) is ChangeClass recorded)
            {
                raises.Add(new ClassRaise($"feature {feature} on base", recorded));
            }
        }

        foreach (string label in inputs.Labels.Where(label => label.StartsWith(LabelPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (ChangeClassNames.TryParse(label[LabelPrefix.Length..].Trim().ToLowerInvariant(), out ChangeClass labelled))
            {
                raises.Add(new ClassRaise($"label {label}", labelled));
            }
            else
            {
                notes.Add($"label {label} names no class; ignored");
            }
        }

        if (inputs.Raise is string raise)
        {
            if (ChangeClassNames.TryParse(raise.Trim().ToLowerInvariant(), out ChangeClass raised))
            {
                raises.Add(new ClassRaise("workflow --raise", raised));
            }
            else
            {
                notes.Add($"--raise {raise} names no class; ignored");
            }
        }

        return new RaiseReading(RaiseReadStatus.Read, raises, notes);
    }

    /// <summary>Texts at head: from the change set when they changed, else from the head commit, or the working tree locally.</summary>
    private static async Task<IReadOnlyDictionary<string, string>?> HeadTextsAsync(
        string projectRoot, ChangeSet changeSet, string prefix, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        List<string> missing = [];
        foreach (string path in paths)
        {
            if (changeSet.Head.TextOf(path) is string text)
            {
                texts[path] = text;
            }
            else if (changeSet.Head.Contains(path))
            {
                missing.Add(path);
            }
        }

        if (changeSet.HeadCommit is null)
        {
            foreach (string path in missing)
            {
                FileInfo file = new(Path.Combine(projectRoot, path));
                if (file.Exists && file.LinkTarget is null && file.Length <= RevisionText.MaxBytes && RevisionText.Decode(File.ReadAllBytes(file.FullName)) is string text)
                {
                    texts[path] = text;
                }
            }

            return texts;
        }

        if (await RoutingGit.BlobsAsync(projectRoot, changeSet.HeadCommit, [.. missing.Select(path => prefix + path)], cancellationToken) is not IReadOnlyDictionary<string, byte[]> blobs)
        {
            return null;
        }

        foreach (string path in missing)
        {
            if (blobs.TryGetValue(prefix + path, out byte[]? content) && RevisionText.Decode(content) is string text)
            {
                texts[path] = text;
            }
        }

        return texts;
    }
}
