using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>A raise of the class from outside the diff (a spec's class line, a label), with where it came from.</summary>
internal sealed record ClassRaise(string Source, ChangeClass To);
