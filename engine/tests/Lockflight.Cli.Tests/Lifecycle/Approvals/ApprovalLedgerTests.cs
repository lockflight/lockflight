using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ApprovalLedgerTests : IDisposable
{
    private const string ApproverEmail = "approver@example.com";
    private const string ApproverName = "Approving Person";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-approval-ledger-");

    [Fact]
    public async Task ApprovingASpecRecordsAnApprovalWithoutWhoApprovedIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.email", ApproverEmail], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.name", ApproverName], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n", cancellationToken);
        string specDirectory = Path.Combine(root.FullName, "specs", "001-orders");
        Directory.CreateDirectory(specDirectory);
        string spec = Path.Combine(specDirectory, "spec.md");
        await File.WriteAllTextAsync(spec, "- **FR-001**: A\n", cancellationToken);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["approve", "spec", "--feature", "001-orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        string line = Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory)))));
        Assert.DoesNotContain(ApproverEmail, line, StringComparison.Ordinal);
        Assert.DoesNotContain(ApproverName, line, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement record = document.RootElement;
        Assert.Equal("approval.recorded", record.GetProperty("type").GetString());
        Assert.Equal("approve:spec", record.GetProperty("signature").GetString());
        Assert.Equal("001-orders", record.GetProperty("feature").GetString());
        Assert.Equal("spec", record.GetProperty("phase").GetString());
        Assert.Equal($"spec.md={ArtifactHash.Of(spec)[..12]}", record.GetProperty("artifacts").GetString());
        Assert.False(record.GetProperty("signed").GetBoolean());
        Assert.Equal("terminal", record.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task ASigningFailureOnTheCommandLineRecordsNothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n", cancellationToken);
        string specDirectory = Path.Combine(root.FullName, "specs", "001-orders");
        Directory.CreateDirectory(specDirectory);
        await File.WriteAllTextAsync(Path.Combine(specDirectory, "spec.md"), "- **FR-001**: A\n", cancellationToken);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["approve", "spec", "--feature", "001-orders", "--sign", "--key", Path.Combine(root.FullName, "missing-key")], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("signing failed, so nothing was recorded", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.FullName, ".lockflight", "features", "001-orders", "approvals", "spec.json")));
        Assert.False(Directory.Exists(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory)));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
