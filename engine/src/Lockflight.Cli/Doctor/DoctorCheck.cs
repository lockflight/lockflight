namespace Lockflight.Cli.Doctor;

/// <summary>A readiness check's result, with the command or edit that fixes it when it is not a pass.</summary>
internal sealed record DoctorCheck(string Name, CheckStatus Status, string Detail, string? Fix);
