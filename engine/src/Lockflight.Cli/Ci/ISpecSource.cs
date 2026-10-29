namespace Lockflight.Cli.Ci;

/// <summary>Finds the spec a pull request is traced against; the tree's today, a workspace shell's when that lands.</summary>
internal interface ISpecSource
{
    Task<SpecResolution> ResolveAsync(JudgePlan plan, string? branch, string? shellPath, CancellationToken cancellationToken);
}
