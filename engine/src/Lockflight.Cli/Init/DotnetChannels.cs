using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Init;

/// <summary>
/// The <c>actions/setup-dotnet</c> channels CI needs: the engine's own SDK (10.0) plus the runtime of every .NET (Core) version the
/// test projects target, so a multi-targeted test project (<c>net8.0;net10.0</c>) does not fail CI for a missing runtime.
/// .NET Framework targets (<c>net48</c>) need no channel.
/// </summary>
internal static partial class DotnetChannels
{
    public const string EngineChannel = "10.0.x";

    /// <summary>Channels such as <c>8.0.x</c>, oldest first, always including <see cref="EngineChannel"/>.</summary>
    public static IReadOnlyList<string> For(IEnumerable<string> targetFrameworks) =>
        [.. targetFrameworks
            .Select(framework => ModernFramework().Match(framework))
            .Where(match => match.Success)
            .Select(match => new Version(int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture)))
            .Append(new Version(10, 0))
            .Distinct()
            .Order()
            .Select(version => $"{version.Major}.{version.Minor}.x")];

    [GeneratedRegex(@"^net(?:coreapp)?(?<major>[3-9]|[1-9]\d)\.(?<minor>\d+)(?:-.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ModernFramework();
}
