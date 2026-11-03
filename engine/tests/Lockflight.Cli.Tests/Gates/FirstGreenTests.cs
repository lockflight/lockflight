using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

/// <summary>The first PASS after init says how long it took, once; a second init does not restart the clock.</summary>
public sealed class FirstGreenTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-first-green-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task TheFirstPassAfterInitSaysHowLongItTookOnce()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FirstGreen firstGreen = new(root.FullName, time);
        await firstGreen.RecordInitAsync(cancellationToken);
        time.Advance(TimeSpan.FromMinutes(12.5));

        Assert.Equal("First green in 12 min after init.", await firstGreen.ReportPassAsync(cancellationToken));
        Assert.Null(await firstGreen.ReportPassAsync(cancellationToken));
    }

    [Fact]
    public async Task ASecondInitKeepsTheFirstOnesTime()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FirstGreen firstGreen = new(root.FullName, time);
        await firstGreen.RecordInitAsync(cancellationToken);
        time.Advance(TimeSpan.FromMinutes(90));
        await firstGreen.RecordInitAsync(cancellationToken);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal("First green in 1 h 35 min after init.", await firstGreen.ReportPassAsync(cancellationToken));
    }

    [Theory]
    [InlineData(0.5, "First green in under a minute after init.")]
    [InlineData(59, "First green in 59 min after init.")]
    [InlineData(60 * 26, "First green in 1 day after init.")]
    [InlineData(60 * 24 * 3, "First green in 3 days after init.")]
    public async Task TheTimeIsSaidInTheLargestUsefulUnit(double minutes, string expected)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FirstGreen firstGreen = new(root.FullName, time);
        await firstGreen.RecordInitAsync(cancellationToken);
        time.Advance(TimeSpan.FromMinutes(minutes));

        Assert.Equal(expected, await firstGreen.ReportPassAsync(cancellationToken));
    }

    [Fact]
    public async Task WithoutInitsRecordThereIsNoReport()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        FirstGreen firstGreen = new(root.FullName, time);

        Assert.Null(await firstGreen.ReportPassAsync(cancellationToken));
        Assert.False(File.Exists(Path.Combine(root.FullName, FirstGreen.RelativePath)));
    }

    [Fact]
    public async Task ARecordThatIsNoTimeIsNoReport()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = Path.Combine(root.FullName, FirstGreen.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a time\n");

        Assert.Null(await new FirstGreen(root.FullName, time).ReportPassAsync(cancellationToken));
    }

    [Fact]
    public async Task ARecordInPlaceOfAFolderIsNoFailure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        File.WriteAllText(Path.Combine(root.FullName, ".lockflight"), "a file where the folder goes");

        await new FirstGreen(root.FullName, time).RecordInitAsync(cancellationToken);

        Assert.Null(await new FirstGreen(root.FullName, time).ReportPassAsync(cancellationToken));
    }

    public void Dispose() => root.Delete(recursive: true);
}
