namespace Lockflight.Cli.Gates.Parsers;

/// <summary>A recognized environment failure in tool output, with what to do about it.</summary>
internal sealed record EnvironmentProblem(string Code, string Explanation);
