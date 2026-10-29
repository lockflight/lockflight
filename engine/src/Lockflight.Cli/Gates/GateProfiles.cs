using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Gates;

/// <summary>
/// The one place gate steps are registered, per profile — edit: format · stop: format, build, test · pr: a protected-changes
/// report, format, new suppressions and broken instruction references, then build, test, patch coverage and dependencies, then
/// secrets. Both runners iterate this table; what differs between them (where a run stops, which units run, how the verdict is
/// formed) is their own, named policy.
/// </summary>
internal static class GateProfiles
{
    private static readonly IGateStep ProtectedChanges = new ProtectedChangesStep();
    private static readonly IGateStep Format = new FormatStep();
    private static readonly IGateStep Suppressions = new SuppressionsStep();
    private static readonly IGateStep DocsReferences = new DocsReferencesStep();
    private static readonly IGateStep Build = new BuildStep();
    private static readonly IGateStep Test = new TestStep();
    private static readonly IGateStep Coverage = new CoverageStep();
    private static readonly IGateStep Dependencies = new DependencyStep();
    private static readonly IGateStep Secrets = new SecretsStep();

    public static IReadOnlyList<GateProfileDefinition> All { get; } =
    [
        new(GateProfile.Edit, [Format], [], []),
        new(GateProfile.Stop, [Format], [Build, Test], []),
        new(GateProfile.PullRequest, [ProtectedChanges, Format, Suppressions, DocsReferences], [Build, Test, Coverage, Dependencies], [Secrets]),
    ];

    public static GateProfileDefinition For(GateProfile profile) => All.Single(definition => definition.Profile == profile);
}
