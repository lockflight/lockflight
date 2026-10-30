using System.Runtime.CompilerServices;

namespace Lockflight.Cli.Tests.Routing;

/// <summary>
/// A routing fixture: <c>Routing/Fixtures/&lt;name&gt;.case</c>, read from the source tree. The file is a list of sections, each
/// starting with a line <c>=== &lt;operation&gt; &lt;path&gt; [&lt;path&gt;]</c>:
/// <c>base</c> writes a file of the base commit (its content follows), <c>head</c> writes or overwrites a file of the pull request,
/// <c>delete</c> removes one, <c>move</c> moves one, and <c>bytes</c> writes a pull request file from space-separated hex bytes.
/// Lines before the first section describe the case. The expected classification is the golden next to it,
/// <c>&lt;name&gt;.expected.txt</c>, rewritten with <c>LOCKFLIGHT_UPDATE_GOLDENS=1</c>.
/// </summary>
internal sealed record RoutingCase(string Name, IReadOnlyDictionary<string, string> BaseFiles, IReadOnlyList<RoutingCaseStep> Steps)
{
    private const string SectionMarker = "=== ";

    public static string FixturesDirectory => Path.Combine(Path.GetDirectoryName(ThisFile())!, "Fixtures");

    public static IEnumerable<string> Names() =>
        Directory.EnumerateFiles(FixturesDirectory, "*.case").Select(path => Path.GetFileNameWithoutExtension(path)).Order(StringComparer.Ordinal);

    public static RoutingCase Load(string name)
    {
        Dictionary<string, string> baseFiles = new(StringComparer.Ordinal);
        List<RoutingCaseStep> steps = [];
        string[] header = [];
        List<string> body = [];
        foreach (string line in File.ReadAllText(Path.Combine(FixturesDirectory, name + ".case")).ReplaceLineEndings("\n").Split('\n').Append(SectionMarker + "end"))
        {
            if (!line.StartsWith(SectionMarker, StringComparison.Ordinal))
            {
                body.Add(line);
                continue;
            }

            if (header.Length > 0)
            {
                string content = string.Join('\n', body).TrimEnd('\n') + "\n";
                if (header[0] == "base")
                {
                    baseFiles[header[1]] = content;
                }
                else
                {
                    steps.Add(new RoutingCaseStep(header[0], header[1], header.Length > 2 ? header[2] : null, content));
                }
            }

            header = line[SectionMarker.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            body.Clear();
        }

        return new RoutingCase(name, baseFiles, steps);
    }

    public void Apply(RoutingRepository repository)
    {
        foreach (RoutingCaseStep step in Steps)
        {
            _ = step.Operation switch
            {
                "head" => repository.Write(step.Path, step.Content),
                "delete" => repository.Delete(step.Path),
                "move" => repository.Move(step.Path, step.Target!),
                "bytes" => repository.WriteBytes(step.Path, Convert.FromHexString(step.Content.Replace(" ", string.Empty, StringComparison.Ordinal).Trim())),
                _ => throw new InvalidDataException($"{Name}.case: unknown operation '{step.Operation}'"),
            };
        }
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
