using System.Globalization;
using System.Xml;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Reads VSTest TRX files (<c>--logger trx</c>): the <c>&lt;Counters&gt;</c> of each run summed, and the <c>testName</c> of every
/// failed <c>&lt;UnitTestResult&gt;</c> — one per theory row, so the names account for the counted failures. A run that did not
/// complete is reported as incomplete: an abnormal outcome, abnormal counters no result accounts for, or a run that "failed" without
/// a failed or abnormal result (a crashed test host drops its results and leaves only a message, which gives the reason).
/// </summary>
internal static class TrxReports
{
    private const int MaxReasonLength = 200;

    private static readonly string[] AbnormalCounters = ["error", "timeout", "aborted", "disconnected", "passedButRunAborted"];

    public static TrxSummary Read(IEnumerable<string> paths)
    {
        int total = 0;
        int executed = 0;
        int failed = 0;
        List<string> failedTests = [];
        List<string> abnormalTests = [];
        List<string> incomplete = [];
        foreach (string path in paths)
        {
            string? runOutcome = null;
            int reportFailed = 0;
            int reportAbnormalCounted = 0;
            int reportAbnormalNamed = 0;
            int reportFailedNamed = 0;
            string? abortReason = null;
            bool inRunInfoText = false;
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Text && inRunInfoText)
                {
                    abortReason ??= AbortReason(reader.Value);
                    continue;
                }

                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "Text")
                {
                    inRunInfoText = false;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "ResultSummary")
                {
                    runOutcome = reader.GetAttribute("outcome");
                }
                else if (reader.LocalName == "Counters")
                {
                    total += Count(reader, "total");
                    executed += Count(reader, "executed");
                    reportFailed = Count(reader, "failed");
                    failed += reportFailed;
                    reportAbnormalCounted = AbnormalCounters.Sum(counter => Count(reader, counter));
                }
                else if (reader.LocalName == "Text" && !reader.IsEmptyElement)
                {
                    inRunInfoText = true;
                }
                else if (reader.LocalName == "UnitTestResult" && reader.GetAttribute("testName") is string name)
                {
                    string? outcome = reader.GetAttribute("outcome");
                    if (outcome == "Failed")
                    {
                        failedTests.Add(name);
                        reportFailedNamed++;
                    }
                    else if (TrxOutcomes.IsAbnormal(outcome))
                    {
                        abnormalTests.Add($"{name} ({outcome})");
                        reportAbnormalNamed++;
                    }
                }
            }

            string report = Path.GetFileName(path);
            if (TrxOutcomes.IsAbnormal(runOutcome))
            {
                incomplete.Add(abortReason is null ? $"{report}: the run's outcome is {runOutcome}" : $"{report}: {abortReason}");
            }
            else if (reportAbnormalCounted > reportAbnormalNamed
                || (runOutcome == "Failed" && reportFailed == 0 && reportFailedNamed == 0 && reportAbnormalNamed == 0))
            {
                incomplete.Add($"{report}: {abortReason ?? "the run failed without a failed test"}");
            }
        }

        return new TrxSummary(total, executed, failed, failedTests) { Incomplete = incomplete, AbnormalTests = abnormalTests };
    }

    /// <summary>The first line of a run message that says the run was aborted, shortened; null for any other message.</summary>
    private static string? AbortReason(string text)
    {
        if (!text.Contains("aborted", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string line = text.Split('\n')[0].Trim();
        return line.Length <= MaxReasonLength ? line : line[..MaxReasonLength] + "…";
    }

    private static int Count(XmlReader reader, string attribute) =>
        int.TryParse(reader.GetAttribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
