namespace Lockflight.Cli.Ci;

/// <summary>
/// The part of a constructor's body between two field stores (<see cref="TamperingBody"/>): a field initializer or a captured
/// parameter, judged on its own so one initializer that reads a variable does not mark every field the constructor fills.
/// </summary>
/// <param name="Field">The key of the field the segment stores to.</param>
/// <param name="Calls">The members the segment calls.</param>
/// <param name="Strings">The strings the segment loads.</param>
/// <param name="Members">The keys of the members of the assembly the segment calls or reads.</param>
internal sealed record TamperingSegment(string Field, List<(string Type, string Member)> Calls, List<string> Strings, HashSet<string> Members);
