namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>One answer a question offers; the recommended one comes first.</summary>
internal sealed record BlockedOption(string Label, bool Recommended);
