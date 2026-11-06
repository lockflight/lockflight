using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Tests.Processes.Survivors;

public sealed class ProcessSweepTests
{
    [Fact]
    public void TheSweepRunsOnlyWhenTheLastChildInFlightHasFinished()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);

        sweep.Starting();
        sweep.Starting();
        sweep.Finished("dotnet build");
        Assert.Equal(0, scope.Sweeps);

        sweep.Finished("dotnet test");
        Assert.Equal(1, scope.Sweeps);
    }

    [Fact]
    public async Task ASurvivorIsLabelledWithItsRegionAndTheChildItOutlived()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        sweep.Label(SweepRegion.Head);
        scope.Next.Add(new SweptProcess(4711, "/usr/bin/sleep", "sleep 600", "runner", SurvivorOutcome.Stopped));

        sweep.Starting();
        sweep.Finished("dotnet build");
        ProcessesReport report = await sweep.FinishAsync(CancellationToken.None);

        Survivor survivor = Assert.Single(report.Survivors);
        Assert.Equal(SweepRegion.Head, survivor.Region);
        Assert.Equal("dotnet build", survivor.After);
        Assert.Equal(4711, survivor.Process.Pid);
        Assert.Equal(SweepMechanism.Subreaper, report.Mechanism);
        Assert.Equal(2, report.Sweeps);
    }

    [Fact]
    public async Task TheLastSweepIsLabelledFinal()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        scope.Next.Add(new SweptProcess(1, "x", "x", null, SurvivorOutcome.NotStopped));

        Survivor survivor = Assert.Single((await sweep.FinishAsync(CancellationToken.None)).Survivors);

        Assert.Equal(SweepRegion.Final, survivor.Region);
        Assert.Equal(SurvivorOutcome.NotStopped, survivor.Process.Outcome);
    }

    [Fact]
    public async Task SurvivorsPastTheCapAreCounted()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        scope.Next.AddRange(Enumerable.Range(1, ProcessSweep.MaxSurvivors + 3).Select(pid => new SweptProcess(pid, "x", "x", null, SurvivorOutcome.Stopped)));

        ProcessesReport report = await sweep.FinishAsync(CancellationToken.None);

        Assert.Equal(ProcessSweep.MaxSurvivors, report.Survivors.Count);
        Assert.Equal(3, report.More);
    }

    [Fact]
    public async Task WithoutAMechanismNothingIsSweptAndTheReportSaysWhy()
    {
        ProcessSweep sweep = new(new NoProcessScope("not on this system"));

        sweep.Starting();
        sweep.Finished("git status");
        ProcessesReport report = await sweep.FinishAsync(CancellationToken.None);

        Assert.Equal(SweepMechanism.None, report.Mechanism);
        Assert.Equal("not on this system", report.Note);
        Assert.Equal(0, report.Sweeps);
        Assert.Empty(report.Survivors);
    }

    [Fact]
    public async Task ProcessesThatCouldNotBeStoppedAreCountedPastTheListedOnes()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        scope.Next.AddRange(Enumerable.Range(1, ProcessSweep.MaxSurvivors + 2).Select(pid => new SweptProcess(pid, "x", "x", null, SurvivorOutcome.NotStopped)));

        ProcessesReport report = await sweep.FinishAsync(CancellationToken.None);

        Assert.Equal(ProcessSweep.MaxSurvivors + 2, report.NotStopped);
        Assert.Equal(ProcessSweep.MaxSurvivors + 2, sweep.NotStopped);
    }

    [Fact]
    public void ADisposedSweepIsNoLongerCurrent()
    {
        ProcessSweep sweep = ProcessSweep.Start(new FakeScope());

        sweep.Dispose();

        Assert.NotSame(sweep, ProcessSweep.Current);
    }

    internal sealed class FakeScope : IProcessScope
    {
        public List<SweptProcess> Next { get; } = [];

        public int Sweeps { get; private set; }

        public SweepMechanism Mechanism => SweepMechanism.Subreaper;

        public string? Note => null;

        public IReadOnlyList<SweptProcess> Sweep()
        {
            Sweeps++;
            SweptProcess[] found = [.. Next];
            Next.Clear();
            return found;
        }
    }
}
