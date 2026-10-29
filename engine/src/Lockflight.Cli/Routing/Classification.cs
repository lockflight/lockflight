using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The class of a change: the final <see cref="Class"/> (after raises), the one the signals computed, the signals sorted by the
/// class they raise to (highest first), then id and place, the raises, failures of routing rules, and the policy read.
/// </summary>
internal sealed record Classification(
    ChangeClass Class,
    ChangeClass Computed,
    IReadOnlyList<ChangeSignal> Signals,
    IReadOnlyList<ClassRaise> Raises,
    IReadOnlyList<string> Failures,
    PolicySource Policy);
