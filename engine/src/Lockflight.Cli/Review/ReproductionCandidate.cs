namespace Lockflight.Cli.Review;

/// <summary>One reproduction to build: the finding's position (its class is <c>Finding{Index + 1}</c>), its test method and its source.</summary>
internal sealed record ReproductionCandidate(int Index, string Method, string Source)
{
    public string ClassName => $"Finding{Index + 1}";

    /// <summary>The file name compiler errors in this reproduction report (the generated file maps the reproduction's lines to it).</summary>
    public string FileName => $"{ClassName}.cs";
}
