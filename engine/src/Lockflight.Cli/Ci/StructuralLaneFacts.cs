using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Routing;
using Requirement = Lockflight.Cli.Lifecycle.Requirement;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What git shows about a pull request that <see cref="StructuralLane.Decide"/> needs: whether it is a pull request judged
/// against a base, whether its class came from the base's routing policy, whether the repository is a unit of a workspace shell,
/// how many commits carry a <c>Lockflight-Red</c> trailer, the spec found in the head's tree and its requirements, the approval
/// facts (null when there is no base to read them from), the base's spec globs, the files the pull request changes and the
/// routing signals.
/// </summary>
internal sealed record StructuralLaneFacts(
    bool PullRequest,
    bool Routed,
    bool Unit,
    int RedCommits,
    SpecResolution Spec,
    IReadOnlyList<Requirement> Requirements,
    ApprovalsFacts? Approvals,
    IReadOnlyList<string> SpecPaths,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<ChangeSignal> Signals);
