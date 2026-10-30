using System.Text.RegularExpressions;
using Lockflight.Cli.Mascot;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The verdict picture a person sees at the top of the PR comment and the job summary: Levi's pose for the verdict as a PNG in this
/// repository (<c>docs/assets/badges/</c>), served by GitHub from the release tag of the engine that judged, so a published URL
/// never changes under an old comment (a development build points at <c>main</c>). Only people see it: the alt text is the
/// verdict, and nothing an agent reads (the evidence, the engine's output) carries the picture.
/// </summary>
internal static partial class VerdictBadge
{
    public const string Directory = "docs/assets/badges";

    private const string Raw = "https://raw.githubusercontent.com/lockflight/lockflight";

    private const string DevelopmentRef = "main";

    /// <summary>The pose for a verdict: NOT-EARNED that waits for a person (a blocked report) is the barrier; ERROR is the red flag.</summary>
    public static MascotPose PoseOf(string verdict, bool waitsForAPerson) => verdict switch
    {
        "PASS" => MascotPose.Pass,
        "NOT-EARNED" => waitsForAPerson ? MascotPose.Blocked : MascotPose.NotEarned,
        _ => MascotPose.Fail,
    };

    /// <summary>Screen pixels per art pixel: the PNG is the pose's pixels scaled up, transparent around Levi.</summary>
    public static int Scale(BadgeSize size) => size == BadgeSize.Large ? 8 : 3;

    /// <summary>
    /// The badge's file name: <c>verdict-&lt;pose&gt;.png</c>, or <c>verdict-&lt;pose&gt;-large.png</c> for the job summary. Named for the
    /// verdict, not the mascot, since agents read the comment's text.
    /// </summary>
    public static string FileName(MascotPose pose, BadgeSize size) =>
        $"verdict-{Name(pose)}{(size == BadgeSize.Large ? "-large" : string.Empty)}.png";

    /// <summary>The badge's URL at the engine's release tag (<c>v0.3.0</c>), or at <c>main</c> for any other version.</summary>
    public static string Url(string? engineVersion, MascotPose pose, BadgeSize size) =>
        $"{Raw}/{(engineVersion is not null && Release().IsMatch(engineVersion) ? $"v{engineVersion}" : DevelopmentRef)}/{Directory}/{FileName(pose, size)}";

    /// <summary>The pose as it is named in the asset files.</summary>
    public static string Name(MascotPose pose) => pose switch
    {
        MascotPose.Pass => "pass",
        MascotPose.NotEarned => "not-earned",
        MascotPose.Fail => "fail",
        MascotPose.Blocked => "blocked",
        MascotPose.OnDuty => "on-duty",
        _ => throw new ArgumentOutOfRangeException(nameof(pose), pose, "Unknown pose."),
    };

    /// <summary>A released version: a tag of that name exists (local, development and CI dry-run builds have none).</summary>
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(alpha|beta|rc)\.[0-9]+)?\z")]
    private static partial Regex Release();
}
