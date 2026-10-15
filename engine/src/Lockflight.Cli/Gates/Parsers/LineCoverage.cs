using System.Xml;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Hits per coverable line, merged from one or more Cobertura reports (several test projects can cover the same file; the highest
/// hit count wins). Files are keyed by their path relative to the project root.
/// </summary>
internal sealed class LineCoverage
{
    private readonly Dictionary<string, Dictionary<int, int>> hitsByFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether any report instrumented the file at all.</summary>
    public bool Covers(string relativeFile) => hitsByFile.ContainsKey(relativeFile);

    /// <summary>Whether any report instrumented a file under the directory (relative, '/'-separated; "" is the root).</summary>
    public bool CoversAnyUnder(string relativeDirectory) =>
        relativeDirectory.Length == 0 ? hitsByFile.Count > 0 : hitsByFile.Keys.Any(file => file.StartsWith(relativeDirectory.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>Hits for a line; null when the line is not coverable (blank, comment, declaration) or the file was not instrumented.</summary>
    public int? HitsAt(string relativeFile, int line) =>
        hitsByFile.TryGetValue(relativeFile, out Dictionary<int, int>? lines) && lines.TryGetValue(line, out int hits) ? hits : null;

    public void AddCobertura(string reportPath, string projectRoot)
    {
        using XmlReader reader = XmlReader.Create(reportPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        List<string> sources = [];
        Dictionary<int, int>? currentLines = null;
        int methodDepth = 0;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "method")
            {
                methodDepth--;
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            switch (reader.Name)
            {
                case "source":
                    sources.Add(reader.ReadElementContentAsString().Trim());
                    break;
                case "class":
                    currentLines = LinesFor(reader.GetAttribute("filename") ?? string.Empty, sources, projectRoot);
                    break;
                case "method" when !reader.IsEmptyElement:
                    methodDepth++;
                    break;
                // Class-level <lines> repeat the method-level ones; count each line once, from the class level.
                case "line" when currentLines is not null && methodDepth == 0
                    && int.TryParse(reader.GetAttribute("number"), out int number)
                    && int.TryParse(reader.GetAttribute("hits"), out int hits):
                    currentLines[number] = Math.Max(currentLines.GetValueOrDefault(number), hits);
                    break;
            }
        }
    }

    private Dictionary<int, int>? LinesFor(string filename, List<string> sources, string projectRoot)
    {
        string absolute = Path.IsPathRooted(filename) || sources.Count == 0 ? filename : Path.Combine(sources[0], filename);
        if (PathRules.ToRelative(projectRoot, absolute) is not string relative)
        {
            return null;
        }

        if (!hitsByFile.TryGetValue(relative, out Dictionary<int, int>? lines))
        {
            lines = [];
            hitsByFile[relative] = lines;
        }

        return lines;
    }
}
