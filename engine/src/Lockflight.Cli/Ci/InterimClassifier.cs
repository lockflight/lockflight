using Lockflight.Cli.Config;
using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Classifies a pull request from git alone, before any build, until risk routing computes classes: <see cref="ChangeClass.Fix"/>
/// only when every changed path is on the base's <c>ci.lowRiskPaths</c> allowlist; <see cref="ChangeClass.Behaviour"/> for
/// everything else, a dependency-only change included (it needs no red cycle). It never asks which project a file belongs to, so
/// nothing in the pull request's own files can lower its class; when unsure it classifies higher.
/// </summary>
internal static partial class InterimClassifier
{
    public const string Source = "interim";

    private const int MaxNamedPaths = 5;
    private const string LockFileName = "packages.lock.json";
    private const string CentralVersionsFile = "Directory.Packages.props";
    private const string ProjectExtension = ".csproj";

    public static InterimClassification Classify(
        IReadOnlyList<string> changed, IReadOnlyList<string> lowRiskPaths, IReadOnlySet<string> filesAtBase, IReadOnlySet<string> filesAtHead, string? packageDiff, PolicySource policy)
    {
        string[] risky = [.. changed.Where(path => !lowRiskPaths.Any(glob => PathRules.Matches(glob, path)))];
        if (risky.Length == 0)
        {
            string where = changed.Count == 0 ? "no files changed" : $"{changed.Count} files";
            return Result(ChangeClass.Fix, InterimShape.LowRisk, new ChangeSignal("interim.lowRisk", ChangeClass.Fix, SignalStrength.Boundary, where, "every changed path is on the base's ci.lowRiskPaths"), policy);
        }

        if (IsDependencyOnly(changed, filesAtBase, filesAtHead, packageDiff))
        {
            return Result(ChangeClass.Behaviour, InterimShape.DependencyOnly, new ChangeSignal("interim.dependencyOnly", ChangeClass.Behaviour, SignalStrength.Heuristic, $"{changed.Count} files", "only package versions and lock files changed"), policy);
        }

        string named = string.Join(", ", risky.Take(MaxNamedPaths)) + (risky.Length > MaxNamedPaths ? $" and {risky.Length - MaxNamedPaths} more" : string.Empty);
        return Result(ChangeClass.Behaviour, InterimShape.Other, new ChangeSignal("interim.changed", ChangeClass.Behaviour, SignalStrength.Heuristic, named, "paths outside ci.lowRiskPaths changed"), policy);
    }

    /// <summary>The files whose diff <see cref="Classify"/> needs to tell a dependency-only change: project and central version files.</summary>
    public static IReadOnlyList<string> PackageFiles(IReadOnlyList<string> changed) => [.. changed.Where(IsPackageFile)];

    private static bool IsDependencyOnly(IReadOnlyList<string> changed, IReadOnlySet<string> filesAtBase, IReadOnlySet<string> filesAtHead, string? packageDiff)
    {
        if (packageDiff is null || !changed.All(path => (IsLockFile(path) || IsPackageFile(path)) && filesAtBase.Contains(path) && filesAtHead.Contains(path)))
        {
            return false;
        }

        IReadOnlyList<FileDiff> files = UnifiedDiff.FilesOf(packageDiff);
        foreach (FileDiff file in files)
        {
            Match[] removed = [.. file.Removed.Select(line => PackageVersionLine().Match(line.Text))];
            Match[] added = [.. file.Added.Select(line => PackageVersionLine().Match(line.Text))];
            if (removed.Any(match => !match.Success) || added.Any(match => !match.Success))
            {
                return false;
            }

            string[] removedPackages = [.. removed.Select(match => match.Groups["package"].Value).Order(StringComparer.OrdinalIgnoreCase)];
            string[] addedPackages = [.. added.Select(match => match.Groups["package"].Value).Order(StringComparer.OrdinalIgnoreCase)];
            if (!removedPackages.SequenceEqual(addedPackages, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Every package file that changed must show up in the diff with version lines only.
        return PackageFiles(changed).All(path => files.Any(file => file.File == path));
    }

    private static bool IsLockFile(string path) => Path.GetFileName(path).Equals(LockFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsPackageFile(string path) =>
        Path.GetFileName(path).Equals(CentralVersionsFile, StringComparison.OrdinalIgnoreCase) || path.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase);

    private static InterimClassification Result(ChangeClass changeClass, InterimShape shape, ChangeSignal signal, PolicySource policy) =>
        new(new Classification(changeClass, changeClass, [signal], [], [], policy), shape);

    [GeneratedRegex(@"^\s*<(?:PackageVersion|PackageReference)\s+(?:Include|Update)=""(?<package>[^""]+)""\s+(?:Version|VersionOverride)=""[^""]+""\s*/>\s*$")]
    private static partial Regex PackageVersionLine();
}
