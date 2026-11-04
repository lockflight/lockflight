using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ApprovalOfferTests : IDisposable
{
    private const string FeatureId = "001-orders";
    private const string SpecOffer = "lockflight: Spec ready for your approval: type /lockflight:approve";
    private const string TasksOffer = "lockflight: Tasks ready for your approval: type /lockflight:approve";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-approval-offer-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

    private string SpecDirectory => Path.Combine(root.FullName, "specs", FeatureId);

    private string SpecPath => Path.Combine(SpecDirectory, "spec.md");

    [Fact]
    public async Task AtTheEndOfATurnASpecReadyForApprovalIsOfferedToThePerson()
    {
        await CreateProjectAsync();

        string output = await SessionTyping.EndTurnAsync(root.FullName, time);

        Assert.Equal(SpecOffer, SessionTyping.SystemMessage(output));
    }

    [Fact]
    public async Task TheOfferIsMadeOncePerChangeOfTheSpec()
    {
        await CreateProjectAsync();
        await SessionTyping.EndTurnAsync(root.FullName, time);

        string unchanged = await SessionTyping.EndTurnAsync(root.FullName, time);
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);
        string changed = await SessionTyping.EndTurnAsync(root.FullName, time);

        Assert.Empty(unchanged);
        Assert.Equal(SpecOffer, SessionTyping.SystemMessage(changed));
    }

    [Fact]
    public async Task TasksReadyAreOfferedOnceTheSpecIsApproved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, cancellationToken);
        await ApproveInATerminalAsync("spec");

        string output = await SessionTyping.EndTurnAsync(root.FullName, time);

        Assert.Equal(TasksOffer, SessionTyping.SystemMessage(output));
    }

    [Fact]
    public async Task ASpecThatFailsItsPhaseCheckIsNotOffered()
    {
        await CreateProjectAsync();
        await File.WriteAllTextAsync(SpecPath, "# Orders\n\n### User Story 1 - Place an order\n\n- **FR-001**: Customers place orders\n", TestContext.Current.CancellationToken);

        string output = await SessionTyping.EndTurnAsync(root.FullName, time);

        Assert.Empty(output);
    }

    [Fact]
    public async Task NothingIsOfferedOnceTheSpecIsApprovedAndNoTasksAreWritten()
    {
        await CreateProjectAsync();
        await ApproveInATerminalAsync("spec");

        string output = await SessionTyping.EndTurnAsync(root.FullName, time);

        Assert.Empty(output);
    }

    [Fact]
    public async Task TheStatusLineSaysTheSpecIsReadyUntilItIsApproved()
    {
        await CreateProjectAsync();
        await SessionTyping.EndTurnAsync(root.FullName, time);

        string waiting = await StatusLineAsync();
        await ApproveInATerminalAsync("spec");
        string approved = await StatusLineAsync();

        Assert.Equal("Lockflight · spec ready for your approval: type /lockflight:approve", waiting);
        Assert.Equal("Lockflight · on duty", approved);
    }

    [Fact]
    public async Task TheStatusLineDropsTheOfferWhenTheSpecChangesMidTurn()
    {
        await CreateProjectAsync();
        await SessionTyping.EndTurnAsync(root.FullName, time);
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        Assert.Equal("Lockflight · on duty", await StatusLineAsync());
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.email", "approver@example.com"], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n", cancellationToken);
        Directory.CreateDirectory(SpecDirectory);
        await File.WriteAllTextAsync(SpecPath, ApprovableArtifacts.Spec, cancellationToken);
    }

    private async Task ApproveInATerminalAsync(string phase)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["approve", phase, "--feature", FeatureId], string.Empty, root.FullName);
        Assert.True(exitCode == ExitCode.Pass, error);
    }

    private async Task<string> StatusLineAsync()
    {
        string session = "{\"cwd\":" + System.Text.Json.JsonSerializer.Serialize(root.FullName) + "}";
        (_, string output, _) = await TestConsole.RunAsync(["statusline"], session, root.FullName, new Dictionary<string, string> { [EnvironmentVariables.NoColor] = "1" }, time);
        return output;
    }
}
