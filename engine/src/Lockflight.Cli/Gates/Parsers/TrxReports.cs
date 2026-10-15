using System.Globalization;
using System.Xml;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Reads VSTest TRX files (<c>--logger trx</c>): the <c>&lt;Counters&gt;</c> of each run summed, and the <c>testName</c> of every
/// failed <c>&lt;UnitTestResult&gt;</c> — one per theory row, so the names account for the counted failures.
/// </summary>
internal static class TrxReports
{
    public static TrxSummary Read(IEnumerable<string> paths)
    {
        int total = 0;
        int executed = 0;
        int failed = 0;
        List<string> failedTests = [];
        foreach (string path in paths)
        {
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "Counters")
                {
                    total += Count(reader, "total");
                    executed += Count(reader, "executed");
                    failed += Count(reader, "failed");
                }
                else if (reader.LocalName == "UnitTestResult" && reader.GetAttribute("outcome") == "Failed" && reader.GetAttribute("testName") is string name)
                {
                    failedTests.Add(name);
                }
            }
        }

        return new TrxSummary(total, executed, failed, failedTests);
    }

    private static int Count(XmlReader reader, string attribute) =>
        int.TryParse(reader.GetAttribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
}
