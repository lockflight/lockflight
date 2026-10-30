using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ClassRequirementsTests
{
    private static readonly ChangeClass[] Classes = [ChangeClass.Fix, ChangeClass.Behaviour, ChangeClass.Feature, ChangeClass.Critical];
    private static readonly RoutingPreset[] Presets = [RoutingPreset.Relaxed, RoutingPreset.Default, RoutingPreset.Strict];

    [Fact]
    public void EveryPresetsRequirementsPerClassAreAsTheGoldenSays()
    {
        StringBuilder table = new();
        foreach (RoutingPreset preset in Presets)
        {
            RoutingPolicy policy = PolicyFor(preset);
            table.Append($"## {RoutingPresetNames.ToName(preset)}\n");
            foreach (ChangeClass changeClass in Classes)
            {
                RequirementSet requirements = ClassRequirements.For(changeClass, policy);
                table.Append($"{ChangeClassNames.ToName(changeClass)}: ")
                    .Append(string.Join(", ", requirements.Items.Select(item => $"{RequirementNames.ToName(item.Kind)}={RequirementNames.ToName(item.Level)}{(item.Threshold is int threshold ? "@" + threshold : string.Empty)}")))
                    .Append('\n')
                    .Append($"  {RoutingComment.NeedsLine(requirements)}\n");
            }
        }

        GoldenFiles.Verify("routing-requirements", table.ToString());
    }

    [Fact]
    public void WhatAClassRequiresEveryHigherClassRequiresToo()
    {
        foreach (RoutingPreset preset in Presets)
        {
            RoutingPolicy policy = PolicyFor(preset) with { Approvals = ApprovalSettings.Default with { SpecFirstFrom = ChangeClass.Feature } };
            for (int index = 1; index < Classes.Length; index++)
            {
                RequirementSet lower = ClassRequirements.For(Classes[index - 1], policy);
                RequirementSet higher = ClassRequirements.For(Classes[index], policy);
                foreach (Requirement requirement in lower.Items)
                {
                    Requirement above = higher.Items.Single(item => item.Kind == requirement.Kind);
                    Assert.True(Strength(above.Level) >= Strength(requirement.Level), $"{preset}: {requirement.Kind} is {requirement.Level} at {Classes[index - 1]} but {above.Level} at {Classes[index]}");
                    Assert.True((above.Threshold ?? 0) >= (requirement.Threshold ?? 0), $"{preset}: {requirement.Kind} threshold drops at {Classes[index]}");
                }
            }
        }
    }

    [Fact]
    public void ACriticalChangeWithoutAReviewKeyRuleIsOnlyAdvisory()
    {
        RoutingPolicy policy = PolicyFor(RoutingPreset.Default);
        RoutingPolicy noKey = policy with { Settings = policy.Settings with { Require = policy.Settings.Require with { ReviewKeyFrom = null } } };

        Assert.Equal(RequirementLevel.RequiredWithKey, ClassRequirements.For(ChangeClass.Critical, policy).LevelOf(RequirementKind.Review));
        Assert.Equal(RequirementLevel.Advisory, ClassRequirements.For(ChangeClass.Critical, noKey).LevelOf(RequirementKind.Review));
    }

    private static int Strength(RequirementLevel level) => level switch
    {
        RequirementLevel.Off => 0,
        RequirementLevel.Advisory => 1,
        RequirementLevel.WhenApplicable => 2,
        RequirementLevel.Required => 3,
        RequirementLevel.RequiredWithKey => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private static RoutingPolicy PolicyFor(RoutingPreset preset) =>
        RoutingPolicy.From(null, new PolicySource("main", "base", ManifestBlob: null, RoutingPresetNames.ToName(preset))) with
        {
            Settings = RoutingPresets.For(preset),
            Approvals = ApprovalSettings.Default with { SpecFirstFrom = RoutingPresets.SpecFirstFrom(preset) },
        };
}
