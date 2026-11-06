using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.WindowsTargets;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Explains what the gates do off Windows with a solution whose projects target Windows (WPF, Windows Forms, WinUI): they build
/// those projects with <c>EnableWindowsTargeting=true</c> and leave their test projects out of the test run, so those tests run
/// only on Windows. Nothing to report on Windows or for a solution without such projects.
/// </summary>
internal static class WindowsTargetCheck
{
    private const string Name = "windows targets";

    public static DoctorCheck? Check(string root, bool onWindows)
    {
        if (onWindows)
        {
            return null;
        }

        Manifest? manifest = ManifestLocator.Discover(root).Manifest;
        string target = Path.GetFullPath(manifest?.StopTarget ?? ".", root);
        if (WindowsTargeting.Find(target, manifest?.Gates.BuildFile) is not WindowsTargetedSolution windows)
        {
            return null;
        }

        IReadOnlyList<string> tests = windows.WindowsTestsAmong(windows.Entries);
        string testsLeftOut = tests.Count == 0
            ? "none of them is a test project"
            : $"the gates leave their test projects out here ({string.Join(", ", tests)}), so those tests run only on Windows: red/green for them, and the CI judge, need a Windows machine or runner";
        return new(Name, CheckStatus.Info,
            $"{windows.WindowsEntries.Count} projects target Windows and this is not Windows: the gates build them with EnableWindowsTargeting=true; {testsLeftOut}",
            null);
    }
}
