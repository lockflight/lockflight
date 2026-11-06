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
    public void ASurvivorIsLabelledWithItsRegionAndTheChildItOutlived()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        sweep.Label(SweepRegion.Head);
        scope.Next.Add(new SweptProcess(4711, "/usr/bin/sleep", "sleep 600", "runner", SurvivorOutcome.Stopped));

        sweep.Starting();
        sweep.Finished("dotnet build");
        ProcessesReport report = sweep.Finish();

        Survivor survivor = Assert.Single(report.Survivors);
        Assert.Equal(SweepRegion.Head, survivor.Region);
        Assert.Equal("dotnet build", survivor.After);
        Assert.Equal(4711, survivor.Process.Pid);
        Assert.Equal(SweepMechanism.Subreaper, report.Mechanism);
        Assert.Equal(2, report.Sweeps);
    }

    [Fact]
    public void TheLastSweepIsLabelledFinal()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        scope.Next.Add(new SweptProcess(1, "x", "x", null, SurvivorOutcome.NotStopped));

        Survivor survivor = Assert.Single(sweep.Finish().Survivors);

        Assert.Equal(SweepRegion.Final, survivor.Region);
        Assert.Equal(SurvivorOutcome.NotStopped, survivor.Process.Outcome);
    }

    [Fact]
    public void SurvivorsPastTheCapAreCounted()
    {
        FakeScope scope = new();
        ProcessSweep sweep = new(scope);
        scope.Next.AddRange(Enumerable.Range(1, ProcessSweep.MaxSurvivors + 3).Select(pid => new SweptProcess(pid, "x", "x", null, SurvivorOutcome.Stopped)));

        ProcessesReport report = sweep.Finish();

        Assert.Equal(ProcessSweep.MaxSurvivors, report.Survivors.Count);
        Assert.Equal(3, report.More);
    }

    [Fact]
    public void WithoutAMechanismNothingIsSweptAndTheReportSaysWhy()
    {
        ProcessSweep sweep = new(new NoProcessScope("not on this system"));

        sweep.Starting();
        sweep.Finished("git status");
        ProcessesReport report = sweep.Finish();

        Assert.Equal(SweepMechanism.None, report.Mechanism);
        Assert.Equal("not on this system", report.Note);
        Assert.Equal(0, report.Sweeps);
        Assert.Empty(report.Survivors);
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
