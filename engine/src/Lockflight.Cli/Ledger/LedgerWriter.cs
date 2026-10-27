using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Lockflight.Cli.Ledger;

/// <summary>
/// Appends events to <c>.lockflight/ledger/events-YYYY-MM.jsonl</c> under the project root (git-ignored, per developer).
/// Fail-open by design: a ledger that cannot be written never blocks work; it is not an audit log.
/// </summary>
internal sealed class LedgerWriter(string projectRoot, TimeProvider time)
{
    public const string RelativeDirectory = ".lockflight/ledger";

    private const string Schema = "lockflight.ledger/v1";
    private const int MaxDetailLength = 500;

    public async Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
    {
        try
        {
            DateTimeOffset now = time.GetUtcNow();
            string directory = Path.Combine(projectRoot, RelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"events-{now:yyyy-MM}.jsonl");
            await File.AppendAllTextAsync(path, ToJsonLine(ledgerEvent, now) + "\n", cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Fail-open: losing a ledger line is acceptable, blocking the agent because of it is not.
        }
    }

    private static string ToJsonLine(LedgerEvent ledgerEvent, DateTimeOffset now)
    {
        using MemoryStream stream = new();
        // Relaxed escaping keeps ledger lines readable ('+' in timestamps, quotes in reasons); they are never embedded in HTML.
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("id", Guid.CreateVersion7(now).ToString("N"));
            writer.WriteString("ts", now.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("type", LedgerEventTypeNames.ToName(ledgerEvent.Type));
            writer.WriteString("signature", ledgerEvent.Signature);
            writer.WriteString("summary", ledgerEvent.Summary);
            if (ledgerEvent.Detail is string detail)
            {
                writer.WriteString("detail", detail.Length > MaxDetailLength ? detail[..MaxDetailLength] : detail);
            }

            if (ledgerEvent.Context is LedgerEventContext eventContext)
            {
                WriteIfPresent(writer, "tool", eventContext.Tool);
                WriteIfPresent(writer, "call", eventContext.Call is string call ? CallRedaction.Shorten(call) : null);
                WriteIfPresent(writer, "session", eventContext.Session);
                WriteIfPresent(writer, "agent", eventContext.Agent);
            }

            if (ledgerEvent.GateRun is LedgerGateRun gateRun)
            {
                writer.WriteString("trigger", GateTriggerNames.ToName(gateRun.Trigger));
                writer.WriteNumber("durationMs", gateRun.DurationMs);
                writer.WriteString("cache", StopCacheResultNames.ToName(gateRun.Cache));
            }

            if (ledgerEvent.Approval is LedgerApproval approval)
            {
                writer.WriteString("feature", approval.Feature);
                writer.WriteString("phase", approval.Phase);
                writer.WriteString("artifacts", approval.Artifacts);
                writer.WriteBoolean("signed", approval.Signed);
                writer.WriteString("channel", ApprovalChannelNames.ToName(approval.Channel));
            }

            if (ledgerEvent.Review is LedgerReview review)
            {
                writer.WriteString("feature", review.Feature);
                writer.WriteString("run", review.Run);
                writer.WriteBoolean("ofRecord", review.OfRecord);
                writer.WriteNumber("findings", review.Findings);
                writer.WriteNumber("openConfirmed", review.OpenConfirmed);
                writer.WriteNumber("openUnverified", review.OpenUnverified);
                writer.WriteNumber("fixed", review.Fixed);
                writer.WriteNumber("inputTokens", review.InputTokens);
                writer.WriteNumber("outputTokens", review.OutputTokens);
                writer.WriteNumber("costUsd", review.EstimatedCostUsd);
            }

            if (ledgerEvent.Finding is LedgerFinding finding)
            {
                writer.WriteString("feature", finding.Feature);
                writer.WriteString("finding", finding.Id);
                writer.WriteString("severity", finding.Severity);
                writer.WriteString("dimension", finding.Dimension);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteIfPresent(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }
}
