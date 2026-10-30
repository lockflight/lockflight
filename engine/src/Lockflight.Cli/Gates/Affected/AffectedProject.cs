namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// One project the stop profile builds, and why: <c>changed: src/Core/Order.cs</c>, or
/// <c>affected by src/Core/Order.cs via Core.csproj</c> for a project that depends on a changed one.
/// </summary>
/// <param name="Name">The project file's name (<c>Core.Tests.csproj</c>).</param>
/// <param name="SolutionEntry">The project's path as the solution lists it; null for a project only another project references.</param>
internal sealed record AffectedProject(string Name, string? SolutionEntry, string Reason, bool DeclaresTests);
