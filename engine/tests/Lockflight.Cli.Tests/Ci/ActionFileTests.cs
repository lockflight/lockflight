using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The CI judge's composite action (<c>action/action.yml</c>): platform values reach its scripts only through <c>env:</c>, so a
/// branch name such as <c>$(touch pwned)</c> is a string, never shell; and every action it uses is pinned by commit.
/// </summary>
public sealed class ActionFileTests
{
    private static readonly string[] Lines = File.ReadAllLines(Path.Combine(SchemaFiles.RepositoryRoot, "action", "action.yml"));

    [Fact]
    public void NoExpressionIsInterpolatedIntoAScript()
    {
        List<string> interpolated = [];
        int? scriptIndent = null;
        foreach (string line in Lines)
        {
            int indent = line.Length - line.TrimStart().Length;
            if (scriptIndent is int block && line.Trim().Length > 0 && indent <= block)
            {
                scriptIndent = null;
            }

            if (scriptIndent is not null && line.Contains("${{", StringComparison.Ordinal))
            {
                interpolated.Add(line.Trim());
            }

            if (line.TrimStart().StartsWith("run:", StringComparison.Ordinal))
            {
                scriptIndent = line.Trim() == "run: |" ? indent : null;
                if (line.Contains("${{", StringComparison.Ordinal))
                {
                    interpolated.Add(line.Trim());
                }
            }
        }

        Assert.Empty(interpolated);
    }

    [Fact]
    public void EveryActionItUsesIsPinnedByCommit()
    {
        string[] uses = [.. Lines.Select(line => line.Trim()).Where(line => line.StartsWith("uses:", StringComparison.Ordinal))];

        Assert.NotEmpty(uses);
        Assert.All(uses, line => Assert.Matches(@"^uses: [\w.-]+/[\w.-]+@[0-9a-f]{40} # v", line));
    }
}
