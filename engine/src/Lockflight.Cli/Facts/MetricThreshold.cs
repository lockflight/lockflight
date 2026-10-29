namespace Lockflight.Cli.Facts;

/// <summary>One line of a tracked <c>CodeMetricsConfig.txt</c>: <c>CA1502(Method): 15</c>.</summary>
/// <param name="Rule">The code metrics rule id (<c>CA1502</c>).</param>
/// <param name="Target">The symbol kind in parentheses (<c>Method</c>, <c>Type</c>); null when the line names none.</param>
/// <param name="Threshold">The number after the colon.</param>
internal sealed record MetricThreshold(string File, string Rule, string? Target, int Threshold);
