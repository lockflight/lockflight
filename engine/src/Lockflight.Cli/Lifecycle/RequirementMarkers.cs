namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Markers added, changed or removed on requirements that already existed: a marker takes a requirement out of the usual red/green
/// trace, so putting one on a requirement the base already had is a change a person should see, not one hidden in a spec diff.
/// </summary>
internal static class RequirementMarkers
{
    /// <summary>One line per requirement of <paramref name="before"/> whose marker differs in <paramref name="after"/>.</summary>
    public static IReadOnlyList<string> ChangesSince(IReadOnlyList<Requirement> before, IReadOnlyList<Requirement> after)
    {
        Dictionary<string, Requirement> earlier = before.DistinctBy(requirement => requirement.Id).ToDictionary(requirement => requirement.Id, StringComparer.Ordinal);
        List<string> changes = [];
        foreach (Requirement now in after.DistinctBy(requirement => requirement.Id))
        {
            if (!earlier.TryGetValue(now.Id, out Requirement? then) || then.Marker == now.Marker)
            {
                continue;
            }

            changes.Add((then.Marker, now.Marker) switch
            {
                (null, string added) => $"{now.Id}: [{added}] added",
                (string removed, null) => $"{now.Id}: [{removed}] removed",
                _ => $"{now.Id}: [{then.Marker}] changed to [{now.Marker}]",
            });
        }

        return changes;
    }
}
