using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Evidence;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// Hash-bound approval records in <c>.lockflight/features/&lt;id&gt;/approvals/&lt;phase&gt;.json</c> (protected; written only by
/// <c>lockflight approve</c>). <c>approvedBy</c> is the git user name — a label, not an identity. A <c>.sig</c> file a 0.2 engine wrote next to a
/// record is ignored. Writes <c>lockflight.approval/v2</c> (<c>state: person</c>); reads v1 records as <c>person</c> too. A
/// <c>skipped-by-policy</c> record (the autonomous mode's, not written by this engine) and an unknown state approve nothing.
/// </summary>
internal sealed class ApprovalStore(Feature feature)
{
    public const string Schema = "lockflight.approval/v2";
    public const string LegacySchema = "lockflight.approval/v1";
    public const string PersonState = "person";
    public const string SkippedByPolicyState = "skipped-by-policy";

    public string RecordPath(ApprovalPhase phase) => Path.Combine(feature.RecordDirectory, "approvals", $"{ApprovalPhaseNames.ToName(phase)}.json");

    /// <summary>Writes the record over <paramref name="artifacts"/> (the hashes the person approved).</summary>
    public string Save(ApprovalPhase phase, IReadOnlyList<(string Name, string Hash)> artifacts, string approvedBy, DateTimeOffset at, string engineVersion)
    {
        string path = RecordPath(phase);
        JsonFiles.Write(path, Render(phase, artifacts, approvedBy, at, engineVersion));
        return path;
    }

    /// <summary>The approval record's content.</summary>
    private JsonObject Render(ApprovalPhase phase, IReadOnlyList<(string Name, string Hash)> artifacts, string approvedBy, DateTimeOffset at, string engineVersion)
    {
        JsonObject hashes = [];
        foreach ((string name, string hash) in artifacts)
        {
            hashes[name] = hash;
        }

        return new JsonObject
        {
            ["schema"] = Schema,
            ["feature"] = feature.Id,
            ["phase"] = ApprovalPhaseNames.ToName(phase),
            ["artifacts"] = hashes,
            ["state"] = PersonState,
            ["approvedBy"] = approvedBy,
            ["at"] = EvidenceTime.Format(at),
            ["engineVersion"] = engineVersion,
            ["rule"] = null,
        };
    }

    /// <summary>
    /// Compares the record with the artifacts on disk. The record must name this feature and phase: a record copied from another
    /// feature (whose artifacts may hash the same) or saved as another phase approves nothing here.
    /// </summary>
    public ApprovalCheck Check(ApprovalPhase phase) => Check(phase, [.. CurrentHashes(phase)]);

    /// <summary>As <see cref="Check(ApprovalPhase)"/>, against hashes already taken (<paramref name="current"/>) instead of reading the files again.</summary>
    public ApprovalCheck Check(ApprovalPhase phase, IReadOnlyList<(string Name, string Hash)> current)
    {
        string path = RecordPath(phase);
        if (!File.Exists(path))
        {
            return new ApprovalCheck(phase, ApprovalState.Missing, null, []);
        }

        if (Read(path) is not JsonObject record || record["artifacts"] is not JsonObject approved)
        {
            return Invalid(phase, $"{Path.GetFileName(path)} is not a readable approval record");
        }

        if (StateProblem(record) is string stateProblem)
        {
            return Invalid(phase, stateProblem);
        }

        string expectedPhase = ApprovalPhaseNames.ToName(phase);
        if (Text(record["feature"]) is not string recordedFeature || recordedFeature != feature.Id)
        {
            return Invalid(phase, $"it was recorded for feature '{Text(record["feature"]) ?? "(none)"}', not '{feature.Id}'");
        }

        if (Text(record["phase"]) is not string recordedPhase || recordedPhase != expectedPhase)
        {
            return Invalid(phase, $"it was recorded for phase '{Text(record["phase"]) ?? "(none)"}', not '{expectedPhase}'");
        }

        string[] changed = [.. current
            .Where(artifact => Text(approved[artifact.Name]) != artifact.Hash)
            .Select(artifact => artifact.Name)];
        return new ApprovalCheck(phase, changed.Length == 0 ? ApprovalState.Valid : ApprovalState.Stale, Text(record["approvedBy"]), changed);
    }

    /// <summary>Each artifact the phase binds with the hash of its current content, or <c>absent</c> when it does not exist.</summary>
    public IEnumerable<(string Name, string Hash)> CurrentHashes(ApprovalPhase phase) =>
        Snapshot(phase).Select(artifact => (artifact.Name, artifact.Hash));

    /// <summary>Each artifact the phase binds, read once, in the order <see cref="ApprovalPhaseNames.ArtifactsOf"/> names them.</summary>
    public IReadOnlyList<ArtifactSnapshot> Snapshot(ApprovalPhase phase) =>
        [.. ApprovalPhaseNames.ArtifactsOf(phase).Select(name =>
        {
            string path = Path.Combine(feature.SpecDirectory, name);
            return new ArtifactSnapshot(name, File.Exists(path) ? File.ReadAllBytes(path) : null);
        })];

    /// <summary>
    /// Why the record's schema and state approve nothing; null for a person's approval (a v1 record, or v2 with <c>state: person</c>).
    /// An unknown schema or state is named as it is and fails closed.
    /// </summary>
    private static string? StateProblem(JsonObject record)
    {
        string? schema = Text(record["schema"]);
        if (schema == LegacySchema)
        {
            return null;
        }

        if (schema != Schema)
        {
            return $"its schema is '{schema ?? "(none)"}', which this engine does not read as an approval";
        }

        return Text(record["state"]) switch
        {
            PersonState => null,
            null => "it has no 'state'",
            SkippedByPolicyState => "it records a step skipped by policy, which approves nothing in this engine version",
            string unknown => $"its state '{unknown}' is not one this engine knows",
        };
    }

    private static ApprovalCheck Invalid(ApprovalPhase phase, string problem) => new(phase, ApprovalState.Invalid, null, [], problem);

    private static JsonNode? Read(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string value; null for anything else (absent, a number, an object), so a malformed record never throws.</summary>
    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
