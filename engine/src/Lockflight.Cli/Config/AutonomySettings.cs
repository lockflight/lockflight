namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>autonomy:</c> section: reserved. It is validated so a manifest written for a later autonomous mode is
/// accepted and its typos are caught, but nothing in this engine reads it except <c>doctor</c>, which says it has no effect.
/// Each value is null when the manifest leaves it out.
/// </summary>
/// <param name="MaxClass">The highest class a headless run may change without a person.</param>
/// <param name="BudgetUsd">The most one run may spend, in US dollars.</param>
/// <param name="TimeMinutes">The longest one run may take.</param>
internal sealed record AutonomySettings(ChangeClass? MaxClass, decimal? BudgetUsd, int? TimeMinutes)
{
    public const decimal MaxBudgetUsd = 100m;
    public const int MaxTimeMinutes = 1440;
}
