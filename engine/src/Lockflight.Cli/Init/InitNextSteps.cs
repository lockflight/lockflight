using Lockflight.Cli.Adoption;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>What <c>init</c> tells the person to do once the project is linked, for the profile it set up.</summary>
internal static class InitNextSteps
{
    /// <param name="specKit">How far Spec Kit is set up: an existing one without the preset gets it from <c>init --speckit-preset</c>, not a new Spec Kit.</param>
    /// <param name="presetDirectory">The Lockflight Spec Kit preset on this machine; null leaves a placeholder.</param>
    public static IReadOnlyList<string> For(InitProfile profile, SpecKitSetup specKit, string? presetDirectory)
    {
        string doctor = $"commit these files, then run `{EngineCommand.Linked} doctor` from the project root.";
        if (profile != InitProfile.Standard)
        {
            return [$"Next: {doctor}"];
        }

        string specify = $"uvx --from specify-cli=={SetupInventory.PinnedSpecKitVersion} specify";
        string preset = presetDirectory is null ? InitAdoption.PresetPlaceholder : ShellQuoting.Quote(presetDirectory);
        List<string> lines = ["Next (standard profile):"];
        int step = 1;
        if (specKit == SpecKitSetup.None)
        {
            lines.Add($"  {step++}. Set up Spec Kit with the Lockflight preset (the init skill does this with you):");
            lines.Add($"       {specify} init --here --force --integration claude --script sh");
            lines.Add($"       {specify} preset add --dev {preset}");
        }
        else if (specKit == SpecKitSetup.WithoutPreset)
        {
            lines.Add($"  {step++}. The project already has Spec Kit without the Lockflight preset: run `{EngineCommand.Linked} init --speckit-preset {preset}` (it upgrades Spec Kit and keeps your customised files).");
        }

        lines.Add($"  {step++}. {char.ToUpperInvariant(doctor[0])}{doctor[1..]}");
        lines.Add($"  {step}. Before the first feature, accept the tests that already fail: you type /lockflight:baseline tests in Claude Code, or {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.Linked} baseline tests")}; then merge that baseline in its own pull request.");
        return lines;
    }
}
