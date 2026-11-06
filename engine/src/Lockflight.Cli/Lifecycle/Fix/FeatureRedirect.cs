using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// Keeps <c>lockflight red</c> outside a feature's lifecycle, where <c>redgreen red</c> and its spec and tasks approval apply. It
/// refuses while any feature has an open red cycle (or a record that cannot be read), when the feature a Spec Kit source names
/// (<c>SPECIFY_FEATURE_DIRECTORY</c>, <c>.specify/feature.json</c>, <c>SPECIFY_FEATURE</c>, the branch; not the "only feature"
/// fallback) has a red/green record, and, in a Claude Code session, when such a source names a feature at all. A guardrail: the
/// session variable can be unset and a branch can name no feature; the CI verdict on a feature-class change without a spec is the
/// boundary.
/// </summary>
internal static class FeatureRedirect
{
    public static string? Refusal(Manifest manifest, EnvironmentReader environment)
    {
        IReadOnlyList<TestLockHolder> holders = TestLock.Holders(manifest);
        if (holders.FirstOrDefault(holder => holder.Unreadable) is TestLockHolder unreadable)
        {
            return $"the red/green record {Path.GetRelativePath(manifest.Root, unreadable.RecordPath).Replace('\\', '/')} cannot be read, so the test projects are locked; "
                + $"a person discards it with '{EngineCommand.Name} redgreen reset --feature {unreadable.FeatureId}' {HumanOnlyCommands.WhereToRun}";
        }

        if (holders.Count > 0)
        {
            return $"a feature's red/green session is open ({string.Join(", ", holders.Select(holder => holder.Reason))}); its tests go through "
                + $"'{EngineCommand.Name} redgreen red' and 'redgreen green' (with the plugin, /lockflight:feature), not lockflight red";
        }

        if (FeatureLocator.Active(manifest.Root, environment) is not { Feature: Feature feature })
        {
            return null;
        }

        if (File.Exists(RedGreenRecord.PathFor(feature)))
        {
            return $"this branch's feature {feature.Id} has a red/green record; its tests go through '{EngineCommand.Name} redgreen red' (with the plugin, /lockflight:feature)";
        }

        return HumanOnlyCommands.RunsUnderAgent(environment)
            ? $"the current feature {feature.Id} has a spec; in a Claude Code session a feature's tests go through /lockflight:feature and 'redgreen red', "
              + "after a person approves its spec and tasks. lockflight red is for a fix that no feature covers"
            : null;
    }
}
