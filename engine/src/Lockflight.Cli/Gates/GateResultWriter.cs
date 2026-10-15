using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lockflight.Cli.Gates;

/// <summary>Writes the <c>lockflight.result/v0</c> JSON envelope.</summary>
internal static class GateResultWriter
{
    private const string Schema = "lockflight.result/v0";

    public static async Task WriteAsync(string path, GateResult result, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        writer.WriteStartObject();
        writer.WriteString("schema", Schema);
        writer.WriteString("target", result.Target);
        writer.WriteString("profile", GateProfileNames.ToName(result.Profile));
        writer.WriteString("verdict", VerdictNames.ToContractName(result.Verdict));
        if (result.TestsTotal is int testsTotal)
        {
            writer.WriteNumber("testsTotal", testsTotal);
        }

        writer.WriteStartArray("steps");
        foreach (GateStep step in result.Steps)
        {
            writer.WriteStartObject();
            writer.WriteString("name", step.Name);
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
}
