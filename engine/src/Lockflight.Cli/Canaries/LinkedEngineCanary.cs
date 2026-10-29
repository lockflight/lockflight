using Lockflight.Cli.Doctor;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The self-test's linked engine canary: a linked engine without the hash record <c>lockflight link</c> writes must not be
/// trusted: <c>doctor</c> fails it without running it, as the hook shim refuses it. The harmless twin, the same engine with a
/// matching record, must be trusted. Planted in the scratch project; the planted engine is never run.
/// </summary>
internal static class LinkedEngineCanary
{
    public const string Name = "linked engine: one without a hash record fails doctor";

    public static CanaryReport Run(CanaryScratch scratch)
    {
        string binary = LinkCommand.BinaryPath(scratch.Project);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "lockflight canary engine\n");
        EngineRecord.Write(scratch.Project, EngineRecord.HashOf(binary));
        if (ReadinessChecks.UntrustedLink(scratch.Project, binary) is DoctorCheck twin)
        {
            return new CanaryReport(Name, CanaryResult.Broken, $"an engine that matches its record fails too ({twin.Detail}), so a failure proves nothing");
        }

        File.Delete(EngineRecord.PathOf(scratch.Project));
        DoctorCheck? planted = ReadinessChecks.UntrustedLink(scratch.Project, binary);
        File.Delete(binary);
        return planted is { Status: CheckStatus.Fail }
            ? new CanaryReport(Name, CanaryResult.Caught, planted.Detail)
            : new CanaryReport(Name, CanaryResult.Missed, planted is null ? "trusted without a record" : $"{planted.Status}: {planted.Detail}");
    }
}
