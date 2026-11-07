namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// Finds the processes the engine's children leave running, in the CI judge. <see cref="ProcessRunner"/> counts the children it has
/// in flight; whenever the count drops to zero (an idle point) the sweep runs, before the runner returns to its caller, so the
/// caller's next read of a build output, a test report or a result happens with nothing the pull request started still running.
/// A process that starts a child waits until the sweep has finished. The sweep stops what it finds and records it; one it could not
/// stop (<see cref="NotStopped"/>) makes the judge's verdict ERROR. At the end of each part of the run (<see cref="EnterAsync"/>) it
/// also checks the Docker containers created since the judge started, when it watches them. A guardrail: code that runs as root, or
/// asks a service outside the engine's tree to start a process, is not seen.
/// </summary>
internal sealed class ProcessSweep(IProcessScope scope) : IDisposable
{
    /// <summary>The most processes left running the report lists; past it, <see cref="ProcessesReport.More"/> counts the rest.</summary>
    public const int MaxSurvivors = 50;

    /// <summary>The variable that carries <see cref="IProcessScope.Marker"/> into every process the engine starts.</summary>
    public const string MarkerVariable = "LOCKFLIGHT_RUN";

    private static ProcessSweep? current;

    private readonly Lock gate = new();
    private readonly List<Survivor> survivors = [];
    private int inFlight;
    private int sweeps;
    private int more;
    private int notStopped;
    private string lastFinished = "the judge's first step";
    private SweepRegion region = SweepRegion.Setup;
    private DockerContainers? containers;

    /// <summary>The sweep of this run of the engine; null outside the CI judge.</summary>
    public static ProcessSweep? Current => Volatile.Read(ref current);

    /// <summary>Starts sweeping with <paramref name="processScope"/> for the rest of the command; disposing the sweep stops it.</summary>
    public static ProcessSweep Start(IProcessScope processScope)
    {
        ProcessSweep sweep = new(processScope);
        Volatile.Write(ref current, sweep);
        return sweep;
    }

    public SweepMechanism Mechanism => scope.Mechanism;

    /// <summary>The marker every child gets in <see cref="MarkerVariable"/>; null when the mechanism needs none.</summary>
    public string? Marker => scope.Marker;

    /// <summary>How many processes or containers the sweeps found that could not be stopped, listed or not.</summary>
    public int NotStopped
    {
        get
        {
            lock (gate)
            {
                return notStopped;
            }
        }
    }

    /// <summary>
    /// Ends the part of the run before <paramref name="region"/>: checks the Docker containers created so far, when the sweep watches
    /// them, and labels what is found from now on with <paramref name="region"/>; nothing when no sweep runs.
    /// </summary>
    public static Task EnterAsync(SweepRegion region, CancellationToken cancellationToken) =>
        Current is ProcessSweep sweep ? sweep.MoveToAsync(region, cancellationToken) : Task.CompletedTask;

    /// <summary>Checks the containers created since <paramref name="watched"/> listed them at the end of each part of the run.</summary>
    public void Watch(DockerContainers watched)
    {
        lock (gate)
        {
            containers = watched;
        }
    }

    public async Task MoveToAsync(SweepRegion next, CancellationToken cancellationToken)
    {
        await SweepContainersAsync(cancellationToken);
        Label(next);
    }

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
            lastFinished = command;
            if (inFlight == 0)
            {
                SweepNow(command);
            }
        }
    }

    /// <summary>Sweeps once more, and checks the containers, right before the evidence is written; reports every sweep of the run.</summary>
    public async Task<ProcessesReport> FinishAsync(CancellationToken cancellationToken)
    {
        await SweepContainersAsync(cancellationToken);
        lock (gate)
        {
            region = SweepRegion.Final;
            if (inFlight == 0)
            {
                SweepNow("the judge's last step");
            }

            return new ProcessesReport(scope.Mechanism, scope.Note, sweeps, [.. survivors], more, HeldOutputs.Snapshot()) { NotStopped = notStopped };
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
        Record(scope.Sweep(), after);
    }

    private async Task SweepContainersAsync(CancellationToken cancellationToken)
    {
        DockerContainers? watched;
        lock (gate)
        {
            watched = containers;
        }

        if (watched is null)
        {
            return;
        }

        // The listing is itself a child the engine starts, so it gets a process sweep of its own when it exits.
        IReadOnlyList<SweptProcess> found = await watched.SweepAsync(cancellationToken);
        lock (gate)
        {
            Record(found, lastFinished);
        }
    }

    private void Record(IReadOnlyList<SweptProcess> found, string after)
    {
        foreach (SweptProcess process in found)
        {
            if (process.Outcome == SurvivorOutcome.NotStopped)
            {
                notStopped++;
            }

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
