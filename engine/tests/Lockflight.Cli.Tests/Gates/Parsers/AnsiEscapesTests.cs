using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class AnsiEscapesTests
{
    // What dotnet test prints for Microsoft.Testing.Platform when GITHUB_ACTIONS=true: colours even with redirected output.
    private const string GitHubActionsOutput =
        "\u001b[[32m+0\u001b[m/\u001b[31mx0\u001b[m/\u001b[33m?0\u001b[m] Billing.Tests.dll (net10.0|x64)(0s)\n\n"
        + "\u001b[31mfailed\u001b[m Billing.Tests.ChargePolicyTests.RetriesBelowTheLimit\u001b[90m \u001b[90m(0ms)\u001b[m\n"
        + "\u001b[31m  Assert.True() Failure\n"
        + "\u001b[31mTest run summary: Failed!\n\u001b[m  total: 2\n\u001b[31m  failed: 1\n\u001b[m  succeeded: 1\n  skipped: 0\n";

    [Fact]
    public void ColouredTestingPlatformOutputStillNamesItsFailuresAndCountsThem()
    {
        string plain = AnsiEscapes.Strip(GitHubActionsOutput);
        HashSet<string> known = new(StringComparer.Ordinal) { "Billing.Tests.ChargePolicyTests.RetriesBelowTheLimit" };

        GateStep step = TestStep.JudgeAgainstBaseline(new ProcessResult(2, plain, string.Empty, 0, TimedOut: false), 2, FailedTests.Find(plain), known);

        Assert.Equal(["Billing.Tests.ChargePolicyTests.RetriesBelowTheLimit"], FailedTests.Find(plain));
        Assert.Equal(Verdict.Pass, step.Verdict);
    }

    [Fact]
    public void TextWithoutEscapesIsUnchanged()
    {
        Assert.Equal("failed A.B (1ms)\n  total: 2\n", AnsiEscapes.Strip("failed A.B (1ms)\n  total: 2\n"));
    }
}
