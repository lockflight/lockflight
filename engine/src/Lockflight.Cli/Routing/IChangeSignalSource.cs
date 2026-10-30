namespace Lockflight.Cli.Routing;

/// <summary>
/// One family of signals found in a change. The sources are a fixed list of the engine (<see cref="ChangeSignalSources"/>): a team
/// cannot add or remove one, because a source that lowers nothing could still be replaced by one that misses everything.
/// </summary>
internal interface IChangeSignalSource
{
    IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy);
}
