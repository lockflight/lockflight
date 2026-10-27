using Lockflight.Cli.Adoption;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>What <c>init</c> tells the person to do once the project is linked, for the profile it set up.</summary>
internal static class InitNextSteps
{
    /// <param name="hasSpecKit">The project already has Spec Kit (<c>.specify/</c>): it is adopted, not initialised again.</param>
    /// <param name="presetDirectory">The Lockflight Spec Kit preset on this machine; null leaves a placeholder.</param>
    public static IReadOnlyList<string> For(InitProfile profile, bool hasSpecKit, string? presetDirectory)
    {
        string doctor = $"commit these files, then run `{EngineCommand.Linked} doctor` from the project root.";
        if (profile != InitProfile.Standard)
        {
            return [$"Next: {doctor}"];
        }

        string specify = $"uvx --from specify-cli=={SetupInventory.PinnedSpecKitVersion} specify";
        string preset = presetDirectory is null ? AdoptApplyLine.PresetPlaceholder : ShellQuoting.Quote(presetDirectory);
        string[] specKit = hasSpecKit
            ? [$"  1. The project already has Spec Kit: run `{EngineCommand.Linked} adopt plan`, review .lockflight/adopt/plan.md and run the adopt apply line it gives (it adds the Lockflight preset)."]
            :
            [
                "  1. Set up Spec Kit with the Lockflight preset (the init skill does this with you):",
                $"       {specify} init --here --force --integration claude --script sh",
                $"       {specify} preset add --dev {preset}",
            ];
        return
        [
            "Next (standard profile):",
            .. specKit,
            $"  2. {char.ToUpperInvariant(doctor[0])}{doctor[1..]}",
            $"  3. Before the first feature, accept the tests that already fail: you type /lockflight:baseline tests in Claude Code, or {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.Linked} baseline tests")}; then merge that baseline in its own pull request.",
        ];
    }
}
