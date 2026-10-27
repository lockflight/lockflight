using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Gates;

public sealed class SessionBaselineTests : IDisposable
{
    private const string Code = "k7m2qx";
    private const string SecondCode = "p3w9ha";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-baseline-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly RandomSource codes = RandomSource.WithCodes(Code, SecondCode);

    private string BaselinePath => Path.Combine(root.FullName, ".lockflight", "baselines", DiagnosticsBaseline.FileName);

    [Fact]
    public async Task TheFirstStepShowsTheWarningsItWouldAcceptAndTheSecondWritesExactlyThem()
    {
        await CreateProjectAsync();

        (ExitCode shownExit, _, string digest) = await TypeAsync("build");

        Assert.Equal(ExitCode.HookBlock, shownExit);
        Assert.StartsWith("Step 1 of 2 — nothing has been accepted yet. Check the 1 build warning you are accepting as the build baseline of the repository", digest, StringComparison.Ordinal);
        Assert.Contains("Shop.cs: The field 'Shop.unused' is never used", digest, StringComparison.Ordinal);
        Assert.Contains("Compared with the current baseline: 1 new, 0 no longer listed", digest, StringComparison.Ordinal);
        Assert.Contains($"Type within 10 minutes: /lockflight:baseline build {Code}", digest, StringComparison.Ordinal);
        Assert.False(File.Exists(BaselinePath));

        (ExitCode acceptedExit, string output, string error) = await TypeAsync($"build {Code}");

        Assert.True(acceptedExit == ExitCode.Pass, error);
        Assert.StartsWith("Step 2 of 2 — accepted: baseline: 1 warnings accepted -> .lockflight/baselines/build-diagnostics.json", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.Contains("CS0169|", File.ReadAllText(BaselinePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileChangedBetweenTheStepsAcceptsNothing()
    {
        await CreateProjectAsync();
        await TypeAsync("build");
        File.WriteAllText(Path.Combine(root.FullName, "Shop.cs"), "public class Shop { private int unused; private int another; }\n");

        (ExitCode exitCode, _, string error) = await TypeAsync($"build {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was accepted: files changed since the engine took the baseline you saw", error, StringComparison.Ordinal);
        Assert.False(File.Exists(BaselinePath));
    }

    [Fact]
    public async Task ACodeShownForOneKindDoesNotAcceptAnother()
    {
        await CreateProjectAsync();
        await TypeAsync("build");

        (ExitCode exitCode, _, string error) = await TypeAsync($"tests {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("no code is waiting for the tests baseline", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideAGitRepositoryNothingIsShown()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, _, string error) = await TypeAsync("build");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("needs a git repository", error, StringComparison.Ordinal);
        Assert.Contains("lockflight baseline build", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build --path services/none")]
    [InlineData("build --path ../elsewhere")]
    public async Task APathThatIsNotAUnitIsAUsageRefusal(string arguments)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("--path takes a unit listed in lockflight.yaml", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("everything")]
    public async Task AMissingOrUnknownKindIsAUsageRefusal(string arguments)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("type /lockflight:baseline build, tests or deps", error, StringComparison.Ordinal);
        Assert.Contains("lockflight baseline build|tests|deps", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCommandAloneWhileACodeWaitsSaysWhichLineToType()
    {
        await CreateProjectAsync();
        await TypeAsync("build");

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Step 2 of 2 — type: /lockflight:baseline build <the code shown with it>", error, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(string arguments) =>
        SessionTyping.TypeAsync(root.FullName, time, codes, SessionBaseline.CommandName, arguments);

    private async Task CreateProjectAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        File.WriteAllText(Path.Combine(root.FullName, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
        File.WriteAllText(Path.Combine(root.FullName, "Shop.cs"), "public class Shop { private int unused; }\n");
        File.WriteAllText(Path.Combine(root.FullName, ".gitignore"), "bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n");
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["add", "-A"], root.FullName, cancellationToken);
    }
}
