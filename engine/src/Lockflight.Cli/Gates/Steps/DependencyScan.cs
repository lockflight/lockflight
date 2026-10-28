using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>The dependency step's verdict and every vulnerable package it found, accepted or not (a baseline records them all).</summary>
internal sealed record DependencyScan(GateStep Step, IReadOnlyList<VulnerablePackage> Found);
