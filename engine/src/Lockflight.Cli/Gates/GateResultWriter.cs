using System.Text.Encodings.Web;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Evidence;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Writes the <c>lockflight.result/v1</c> JSON envelope: the verdict, the engine and the commits it judged, and per step its stable
/// <c>id</c>, display <c>name</c>, the unit of a workspace row, and the <c>mode</c> the manifest gives it (<c>fail</c> for a step
/// no key switches).
/// </summary>
internal static class GateResultWriter
{
    public const string Schema = "lockflight.result/v1";

    /// <param name="baseCommit">The commit <c>--base</c> named; null without <c>--base</c>.</param>
    /// <param name="headCommit">The commit HEAD pointed at; null outside a repository.</param>
    public static async Task WriteAsync(
        string path, GateResult result, GateSettings gates, string engineVersion, string? baseCommit, string? headCommit, DateTimeOffset at, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        writer.WriteStartObject();
        writer.WriteString("schema", Schema);
        writer.WriteString("engineVersion", engineVersion);
        writer.WriteString("at", EvidenceTime.Format(at));
        writer.WriteString("target", result.Target);
        writer.WriteString("profile", GateProfileNames.ToName(result.Profile));
        writer.WriteString("base", baseCommit);
        writer.WriteString("head", headCommit);
        writer.WriteString("verdict", VerdictNames.ToContractName(result.Verdict));
        if (result.TestsTotal is int testsTotal)
        {
            writer.WriteNumber("testsTotal", testsTotal);
        }

        writer.WriteStartArray("steps");
        foreach (GateStep step in result.Steps)
        {
            writer.WriteStartObject();
            writer.WriteString("id", step.Id);
            writer.WriteString("name", step.Name);
            writer.WriteString("unit", step.Unit);
            writer.WriteString("mode", GateModeNames.ToName(ModeOf(step.Id, gates)));
            writer.WriteNumber("toolExitCode", step.ToolExitCode);
            writer.WriteString("verdict", VerdictNames.ToContractName(step.Verdict));
            writer.WriteNumber("durationMs", step.DurationMs);
            writer.WriteString("detail", step.Detail);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    /// <summary>The mode the manifest gives a step: the switched steps' keys, <c>fail</c> for every other step.</summary>
    public static GateMode ModeOf(string stepId, GateSettings gates) => stepId switch
    {
        SuppressionsStep.StepName => gates.Suppressions,
        DocsReferencesStep.StepName => gates.DocsReferences,
        EfPendingModelChangesStep.StepName => gates.EfPendingModelChanges,
        _ => GateMode.Fail,
    };
}
