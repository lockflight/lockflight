using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>One reason a change is in its class: what was found (<see cref="Id"/>), the class it raises to, where, and how.</summary>
internal sealed record ChangeSignal(string Id, ChangeClass RaisesTo, SignalStrength Strength, string Where, string Detail);
