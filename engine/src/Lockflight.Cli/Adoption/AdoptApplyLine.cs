using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Adoption;

/// <summary>The <c>adopt apply</c> command line a person pastes into a separate terminal window after reviewing the plan.</summary>
internal static class AdoptApplyLine
{
    /// <summary>Stands in for the preset's directory when it was not found on this machine.</summary>
    public const string PresetPlaceholder = "<plugin>/speckit-preset";

    /// <summary>
    /// <c>cd</c> to <paramref name="root"/>, then <paramref name="engine"/> <c>adopt apply</c>; when the plan upgrades Spec Kit, with
    /// <c>--speckit-preset</c> and the preset's absolute path, or <see cref="PresetPlaceholder"/> when <paramref name="presetDirectory"/> is null.
    /// </summary>
    public static string For(string root, string engine, bool upgradesSpecKit, string? presetDirectory)
    {
        string line = $"cd {ShellQuoting.Quote(root)} && {engine} adopt apply";
        return upgradesSpecKit
            ? $"{line} --speckit-preset {(presetDirectory is null ? PresetPlaceholder : ShellQuoting.Quote(presetDirectory))}"
            : line;
    }

    /// <summary>What to tell the person under the line: how to skip Spec Kit, or what to put in place of the placeholder; null without a Spec Kit upgrade.</summary>
    public static string? Note(bool upgradesSpecKit, string? presetDirectory) => upgradesSpecKit
        ? presetDirectory is null
            ? $"Replace {PresetPlaceholder} with the speckit-preset directory of the installed Lockflight plugin (under ~/.claude/plugins/cache/lockflight/lockflight/<version>/) or of a Lockflight checkout (plugins/lockflight/speckit-preset), or pass --skip-speckit to leave Spec Kit as it is."
            : "To leave Spec Kit as it is, pass --skip-speckit instead of --speckit-preset."
        : null;
}
