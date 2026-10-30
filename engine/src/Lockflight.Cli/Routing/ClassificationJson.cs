using System.Text.Json;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The evidence's <c>changeClass</c> block: the final and computed class, where the policy was read (base ref, commit, manifest
/// blob id, preset), every signal (id, the class it raises to, strength, place, detail), the raises, the routing failures, and the
/// engine version. The lists are already sorted and there is no timestamp, so the same inputs and engine give the same bytes.
/// </summary>
internal static class ClassificationJson
{
    public static void Write(Utf8JsonWriter writer, Classification classification, string engineVersion)
    {
        writer.WriteStartObject();
        writer.WriteString("class", ChangeClassNames.ToName(classification.Class));
        writer.WriteString("computed", ChangeClassNames.ToName(classification.Computed));
        writer.WriteStartObject("policy");
        writer.WriteString("base", classification.Policy.BaseRef);
        writer.WriteString("commit", classification.Policy.Commit);
        if (classification.Policy.ManifestBlob is string blob)
        {
            writer.WriteString("manifest", blob);
        }
        else
        {
            writer.WriteNull("manifest");
        }

        writer.WriteString("preset", classification.Policy.Preset);
        writer.WriteEndObject();
        writer.WriteStartArray("signals");
        foreach (ChangeSignal signal in classification.Signals)
        {
            writer.WriteStartObject();
            writer.WriteString("id", signal.Id);
            writer.WriteString("raisesTo", ChangeClassNames.ToName(signal.RaisesTo));
            writer.WriteString("strength", SignalStrengthNames.ToName(signal.Strength));
            writer.WriteString("where", signal.Where);
            writer.WriteString("detail", signal.Detail);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("raises");
        foreach (ClassRaise raise in classification.Raises)
        {
            writer.WriteStartObject();
            writer.WriteString("source", raise.Source);
            writer.WriteString("to", ChangeClassNames.ToName(raise.To));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("failures");
        foreach (string failure in classification.Failures)
        {
            writer.WriteStringValue(failure);
        }

        writer.WriteEndArray();
        writer.WriteString("engine", engineVersion);
        writer.WriteEndObject();
    }
}
