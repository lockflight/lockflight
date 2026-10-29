using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Facts;

/// <summary>
/// Renders <see cref="ProjectFacts"/> as <c>lockflight.facts/v1</c> JSON (documented in docs/guides/commands.md#facts). Property
/// order is fixed and every list is sorted, so the same tree gives the same bytes. Additions keep the id; a removed, renamed or
/// re-meant property is a new id.
/// </summary>
internal static class FactsJson
{
    public const string Schema = "lockflight.facts/v1";

    public static string Render(ProjectFacts facts, string engineVersion)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("engineVersion", engineVersion);
            WriteManifest(writer, facts.Manifest);
            WriteStrings(writer, "protectedPaths", facts.ProtectedPaths);
            WriteGates(writer, facts);
            WriteStrings(writer, "instructionFiles", facts.InstructionFiles);
            writer.WriteStartArray("analyzers");
            foreach (AnalyzerSetting setting in facts.Analyzers)
            {
                writer.WriteStartObject();
                writer.WriteString("id", setting.Id);
                writer.WriteString("severity", setting.Severity);
                writer.WriteBoolean("reachesBuild", setting.ReachesTheBuild);
                writer.WriteString("file", setting.File);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("bannedSymbols");
            foreach (BannedSymbolsFile file in facts.BannedSymbols)
            {
                writer.WriteStartObject();
                writer.WriteString("file", file.File);
                WriteStrings(writer, "symbols", file.Symbols);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("codeMetrics");
            foreach (MetricThreshold threshold in facts.MetricThresholds)
            {
                writer.WriteStartObject();
                writer.WriteString("rule", threshold.Rule);
                if (threshold.Target is null)
                {
                    writer.WriteNull("target");
                }
                else
                {
                    writer.WriteString("target", threshold.Target);
                }

                writer.WriteNumber("threshold", threshold.Threshold);
                writer.WriteString("file", threshold.File);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("architectureTests");
            writer.WriteBoolean("present", facts.ArchitectureTests.Count > 0);
            WriteStrings(writer, "files", facts.ArchitectureTests);
            writer.WriteEndObject();
            WriteStrings(writer, "humanOnlyCommands", facts.HumanOnlyCommands);
            writer.WriteStartObject("ledger");
            writer.WriteString("directory", LedgerWriter.RelativeDirectory);
            writer.WriteString("schema", LedgerWriter.Schema);
            WriteStrings(writer, "files", facts.LedgerFiles);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteManifest(Utf8JsonWriter writer, Manifest? manifest)
    {
        if (manifest is null)
        {
            writer.WriteNull("manifest");
            return;
        }

        writer.WriteStartObject("manifest");
        writer.WriteString("file", Manifest.FileName);
        writer.WriteBoolean("stopGate", manifest.StopGate);
        WriteOptional(writer, "stopTarget", manifest.StopTarget);
        writer.WriteStartArray("units");
        foreach (Unit unit in manifest.Units)
        {
            writer.WriteStartObject();
            writer.WriteString("path", unit.Path);
            writer.WriteString("kind", unit.Kind switch { UnitKind.Dotnet => "dotnet", UnitKind.None => "none", _ => "auto" });
            WriteOptional(writer, "build", unit.Build);
            WriteOptional(writer, "testFramework", unit.TestFramework);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (manifest.Shell is ShellSettings shell)
        {
            writer.WriteStartObject("shell");
            writer.WriteString("url", shell.Url);
            WriteOptional(writer, "ref", shell.Ref);
            WriteOptional(writer, "unit", shell.Unit);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("shell");
        }

        writer.WriteStartObject("review");
        writer.WriteString("model", manifest.Review.Model);
        writer.WriteNumber("budgetUsd", manifest.Review.BudgetUsd);
        WriteOptional(writer, "target", manifest.Review.Target);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteGates(Utf8JsonWriter writer, ProjectFacts facts)
    {
        GateSettings settings = facts.Manifest?.Gates ?? GateSettings.Default;
        writer.WriteStartObject("gates");
        writer.WriteNumber("timeoutSeconds", settings.TimeoutSeconds);
        WriteOptional(writer, "build", settings.BuildFile);
        WriteOptional(writer, "testFramework", settings.TestFramework);
        writer.WriteStartObject("coverage");
        writer.WriteString("collector", settings.Coverage.Collector switch { CoverageCollector.Coverlet => "coverlet", CoverageCollector.Microsoft => "microsoft", _ => "none" });
        writer.WriteNumber("patchThreshold", settings.Coverage.PatchThreshold);
        writer.WriteNumber("minChangedLines", settings.Coverage.MinChangedLines);
        writer.WriteEndObject();
        writer.WriteStartArray("profiles");
        foreach (GateProfileFact profile in facts.Profiles)
        {
            writer.WriteStartObject();
            writer.WriteString("name", profile.Name);
            writer.WriteStartArray("steps");
            foreach (GateStepFact step in profile.Steps)
            {
                writer.WriteStartObject();
                writer.WriteString("name", step.Name);
                writer.WriteString("scope", step.Scope == StepScope.Unit ? "unit" : "root");
                writer.WriteString("mode", GateModeNames.ToName(step.Mode));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartArray(name);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }
}
