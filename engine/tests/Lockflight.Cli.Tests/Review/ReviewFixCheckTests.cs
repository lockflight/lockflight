using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Review;

/// <summary>
/// A review whose reproduction encodes the reviewer's reading of a requirement that the change's own test contradicts: the
/// reproduction fails on the change, and the reviewer's fix makes it pass but breaks the change's test.
/// </summary>
public sealed class ReviewFixCheckTests : IDisposable
{
    private const string Feature = "001-quotes";
    private const string OwnTest = "Quotes.Tests.QuoteCollectorTests.CallerCancellationDuringACallKeepsTheQuote";

    private const string Spec = """
        # Supplier quotes

        - **FR-001**: The collector MUST ask every supplier for a quote, in order.
        - **FR-002**: A supplier that runs out of time MUST be skipped.
        - **FR-003**: Cancellation by the caller MUST stop the collection and surface as an OperationCanceledException.
        """;

    private const string Collector = """
        namespace Quotes;

        public static class QuoteCollector
        {
            public static async Task<IReadOnlyList<decimal>> CollectAsync(IReadOnlyList<ISupplier> suppliers, CancellationToken cancellationToken)
            {
                List<decimal> quotes = [];
                foreach (ISupplier supplier in suppliers)
                {
                    try
                    {
                        quotes.Add(await supplier.QuoteAsync(cancellationToken));
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }

                return quotes;
            }
        }

        """;

    private const string CollectorTests = """
        using Quotes;

        namespace Quotes.Tests;

        public sealed class QuoteCollectorTests
        {
            [Fact]
            public async Task EverySupplierIsAskedInOrder() =>
                Assert.Equal([1m, 2m], await QuoteCollector.CollectAsync([new Fixed(1m), new Fixed(2m)], TestContext.Current.CancellationToken));

            [Fact]
            public async Task CallerCancellationDuringACallKeepsTheQuote()
            {
                using CancellationTokenSource caller = new();

                IReadOnlyList<decimal> quotes = await QuoteCollector.CollectAsync([new CancelsTheCaller(caller, 5m)], caller.Token);

                Assert.Equal([5m], quotes);
            }

            private sealed class Fixed(decimal quote) : ISupplier
            {
                public Task<decimal> QuoteAsync(CancellationToken cancellationToken) => Task.FromResult(quote);
            }

            private sealed class CancelsTheCaller(CancellationTokenSource caller, decimal quote) : ISupplier
            {
                public Task<decimal> QuoteAsync(CancellationToken cancellationToken)
                {
                    caller.Cancel();
                    return Task.FromResult(quote);
                }
            }
        }

        """;

    private const string Reproduction = """
        [Fact]
        public async Task CallerCancellationSurfacesAsOperationCanceled()
        {
            using CancellationTokenSource caller = new();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QuoteCollector.CollectAsync([new Cancelling(caller)], caller.Token));
        }

        private sealed class Cancelling(CancellationTokenSource caller) : ISupplier
        {
            public Task<decimal> QuoteAsync(CancellationToken cancellationToken)
            {
                caller.Cancel();
                return Task.FromResult(5m);
            }
        }
        """;

    private const string Fix =
        "--- a/src/Quotes/QuoteCollector.cs\n+++ b/src/Quotes/QuoteCollector.cs\n@@ -18,2 +18,3 @@\n \n+        cancellationToken.ThrowIfCancellationRequested();\n         return quotes;\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-fixcheck-");
    private readonly DirectoryInfo tools = Directory.CreateTempSubdirectory("lockflight-fixcheck-path-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeClaude claude = new();

    [Fact]
    public async Task AReproductionTheChangesOwnTestContradictsIsContestedAndNamesThatTest()
    {
        await RepositoryAsync();
        claude.Answers(Finding(Fix));

        (ExitCode exitCode, string output) = await RunAsync(["review"]);

        Assert.True(exitCode == ExitCode.NotEarned, output);
        JsonElement finding = SingleFinding();
        Assert.Equal("contested", finding.GetProperty("outcome").GetString());
        Assert.Equal("failed", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal("breaks-own-test", finding.GetProperty("fixCheck").GetString());
        Assert.False(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Equal([OwnTest], finding.GetProperty("conflictingTests").EnumerateArray().Select(test => test.GetString()!));
        Assert.Equal(2, finding.GetProperty("ownTestsChecked").GetInt32());
        Assert.Equal("open", finding.GetProperty("state").GetString());
        Assert.Contains(OwnTest, output, StringComparison.Ordinal);
        Assert.Equal(Collector, File.ReadAllText(Path.Combine(root.FullName, "src", "Quotes", "QuoteCollector.cs")).ReplaceLineEndings("\n"));
        (_, string status) = await RunAsync(["review", "status"]);
        Assert.Contains("major, contested", status, StringComparison.Ordinal);
        Assert.Contains($"the reviewer's fix breaks the change's own test {OwnTest}", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFixThatLeavesTheReproductionFailingKeepsTheFindingConfirmedWithoutAFixCheck()
    {
        await RepositoryAsync();
        claude.Answers(Finding(Fix.Replace("cancellationToken.ThrowIfCancellationRequested();", "// The caller's token reaches every supplier.", StringComparison.Ordinal)));

        (ExitCode exitCode, string output) = await RunAsync(["review"]);

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding();
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("did-not-fix", finding.GetProperty("fixCheck").GetString());
        Assert.False(finding.GetProperty("fixChecked").GetBoolean());
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(tools);
    }

    private static string Finding(string fix) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["severity"] = "major",
        ["file"] = "src/Quotes/QuoteCollector.cs",
        ["line"] = 16,
        ["requirement"] = "FR-003",
        ["title"] = "Caller cancellation during a supplier call is swallowed",
        ["evidence"] = "When the caller cancels while a supplier answers, the collector returns normally.",
        ["specQuote"] = "Cancellation by the caller MUST stop the collection and surface as an OperationCanceledException.",
        ["reproduction"] = Reproduction,
        ["fix"] = fix,
    });

    private async Task RepositoryAsync()
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", "reviewer@example.com");
        await GitAsync("config", "user.name", "Reviewing Person");
        await GitAsync("config", "commit.gpgsign", "false");
        Write(Manifest.FileName, "version: 1\n");
        Write(".gitignore", "bin/\nobj/\n");
        Write($"specs/{Feature}/spec.md", Spec);
        Write("Quotes.slnx", "<Solution>\n  <Project Path=\"src/Quotes/Quotes.csproj\" />\n  <Project Path=\"tests/Quotes.Tests/Quotes.Tests.csproj\" />\n</Solution>\n");
        Write("src/Quotes/Quotes.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        Write("src/Quotes/ISupplier.cs", "namespace Quotes;\n\npublic interface ISupplier\n{\n    Task<decimal> QuoteAsync(CancellationToken cancellationToken);\n}\n");
        Write("tests/Quotes.Tests/Quotes.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />
                <ProjectReference Include="../../src/Quotes/Quotes.csproj" />
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>
            """);
        Write("tests/Quotes.Tests/SmokeTests.cs", "using Quotes;\n\nnamespace Quotes.Tests;\n\npublic sealed class SmokeTests\n{\n    [Fact]\n    public void TheSupplierContractExists() => Assert.True(typeof(ISupplier).IsInterface);\n}\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "start");
        await GitAsync("switch", "-q", "-c", "quotes");
        Write("src/Quotes/QuoteCollector.cs", Collector);
        Write("tests/Quotes.Tests/QuoteCollectorTests.cs", CollectorTests);
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "collect quotes");
        File.WriteAllText(Path.Combine(tools.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
    }

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content.ReplaceLineEndings("\n"));
    }

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);

    private JsonElement SingleFinding()
    {
        string runFile = Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "features", Feature, "reviews"), "*.json"));
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(runFile));
        return Assert.Single(evidence.RootElement.GetProperty("findings").EnumerateArray().ToArray()).Clone();
    }

    private async Task<(ExitCode ExitCode, string Output)> RunAsync(string[] args)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, time)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.Path] = tools.FullName }),
            Claude = claude.RunAsync,
        };
        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString() + error.ToString());
    }
}
