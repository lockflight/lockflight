namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// Finds the processes the engine's children leave running, in the CI judge. <see cref="ProcessRunner"/> counts the children it has
/// in flight; whenever the count drops to zero (an idle point) the sweep runs, before the runner returns to its caller, so the
/// caller's next read of a build output, a test report or a result happens with nothing the pull request started still running.
/// A process that starts a child waits until the sweep has finished. The sweep stops what it finds and records it; it does not
/// change a verdict. A guardrail: code that runs as root, or asks a service outside the engine's tree to start a process, is not
/// seen.
/// </summary>
internal sealed class ProcessSweep(IProcessScope scope) : IDisposable
{
    /// <summary>The most processes left running the report lists; past it, <see cref="ProcessesReport.More"/> counts the rest.</summary>
    public const int MaxSurvivors = 50;

    private static ProcessSweep? current;

    private readonly Lock gate = new();
    private readonly List<Survivor> survivors = [];
    private int inFlight;
    private int sweeps;
    private int more;
    private SweepRegion region = SweepRegion.Setup;

    /// <summary>The sweep of this run of the engine; null outside the CI judge.</summary>
    public static ProcessSweep? Current => Volatile.Read(ref current);

    /// <summary>Starts sweeping with <paramref name="processScope"/> for the rest of the command; disposing the sweep stops it.</summary>
    public static ProcessSweep Start(IProcessScope processScope)
    {
        ProcessSweep sweep = new(processScope);
        Volatile.Write(ref current, sweep);
        return sweep;
    }

    /// <summary>Labels the processes found from now on with <paramref name="region"/>; nothing when no sweep runs.</summary>
    public static void Enter(SweepRegion region) => Current?.Label(region);

    public void Label(SweepRegion region)
    {
        lock (gate)
        {
            this.region = region;
        }
    }

    /// <summary>Counts a child about to start; waits while a sweep runs, so the sweep never sees a child the engine is starting.</summary>
    public void Starting()
    {
        lock (gate)
        {
            inFlight++;
        }
    }

    /// <summary>Counts a child that has exited and whose output has ended or been given up; sweeps when it was the last one in flight.</summary>
    /// <param name="command">The child's program and first argument, the process the survivors outlived.</param>
    public void Finished(string command)
    {
        lock (gate)
        {
            inFlight--;
            if (inFlight == 0)
            {
                SweepNow(command);
            }
        }
    }

    /// <summary>Sweeps once more, right before the evidence is written, and reports every sweep of the run.</summary>
    public ProcessesReport Finish()
    {
        lock (gate)
        {
            region = SweepRegion.Final;
            if (inFlight == 0)
            {
                SweepNow("the judge's last step");
            }

            return new ProcessesReport(scope.Mechanism, scope.Note, sweeps, [.. survivors], more, HeldOutputs.Snapshot());
        }
    }

    public void Dispose() => Interlocked.CompareExchange(ref current, null, this);

    private void SweepNow(string after)
    {
        if (scope.Mechanism == SweepMechanism.None)
        {
            return;
        }

        sweeps++;
        foreach (SweptProcess process in scope.Sweep())
        {
            if (survivors.Count < MaxSurvivors)
            {
                survivors.Add(new Survivor(region, after, process));
            }
            else
            {
                more++;
            }
        }
    }
}
