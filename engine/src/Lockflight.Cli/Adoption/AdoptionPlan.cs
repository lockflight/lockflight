namespace Lockflight.Cli.Adoption;

/// <summary>What <c>init</c> found in an existing setup and what it does with each part of it.</summary>
internal sealed record AdoptionPlan(string Root, DateTimeOffset At, IReadOnlyList<AdoptionItem> Items);
