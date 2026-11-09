using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class TraceCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-trace-");

    [Fact]
    public async Task ASpecWithoutRequirementsHasNothingToTrace()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 2\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "001-orders"));
        File.WriteAllText(Path.Combine(root.FullName, "specs", "001-orders", "spec.md"), "# Feature Specification: Orders\n\nNo requirements yet.\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["trace", "--feature", "001-orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("NOT-EARNED", output, StringComparison.Ordinal);
        Assert.Contains("no requirements", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpecWhoseEveryRequirementIsVerifiedOutsideDotNetIsNotApplicableNotAPass()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 2\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "001-orders"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "web"));
        File.WriteAllText(Path.Combine(root.FullName, "web", "card.spec.ts"), "it('FR-001 shows the card', () => {});\n");
        File.WriteAllText(Path.Combine(root.FullName, "specs", "001-orders", "spec.md"), "- **FR-001** [outside .NET: web/card.spec.ts]: The card MUST show the order\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["trace", "--feature", "001-orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("| FR-001 | — | excluded — verified outside .NET (web/card.spec.ts) |", output, StringComparison.Ordinal);
        Assert.Contains("N/A", output, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOutsideMarkerPointingAtAFileThatDoesNotNameTheRequirementStillNeedsRedGreen()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 2\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "001-orders"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "web"));
        File.WriteAllText(Path.Combine(root.FullName, "web", "card.spec.ts"), "it('shows the card', () => {});\n");
        File.WriteAllText(Path.Combine(root.FullName, "specs", "001-orders", "spec.md"), "- **FR-001** [outside .NET: web/card.spec.ts]: The card MUST show the order\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["trace", "--feature", "001-orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("no red/green evidence", output, StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);
}
