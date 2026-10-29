using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Whether a command that may rewrite everything under a directory could touch a path a rule protects. One scan serves one guard
/// decision. A directory is walked only for the rules that could match below it by pattern (<c>secrets/**</c> never sends a walk
/// into <c>node_modules</c>; <c>**/Directory.Build.props</c> does), the walk keeps only files whose name fits one of those rules,
/// and the result is reused for every directory below it.
/// </summary>
internal sealed class ProtectedTreeScan(string root, IReadOnlyList<string> rules, DirectoryReads reads)
{
    private const string AnyDepth = "**";
    private const string GitDirectory = ".git";

    private static readonly string[] SourceExtensions = [".cs", ".csx", ".vb", ".fs", ".fsx"];

    private readonly Dictionary<string, List<string>> ruleFilesByDirectory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FileAttributes?> attributesByPath = new(StringComparer.Ordinal);

    /// <summary>The directory relative to the root ('/'-separated, "" for the root itself); null when it lies outside the root.</summary>
    public static string? RelativeTree(string root, string tree, string workingDirectory)
    {
        string rootPath = PathRules.ToAbsolute(PathRules.Canonicalize(root, baseDirectory: null), baseDirectory: null).TrimEnd('/');
        string treePath = PathRules.ToAbsolute(PathRules.Canonicalize(tree, workingDirectory), baseDirectory: null).TrimEnd('/');
        return string.Equals(rootPath, treePath, StringComparison.OrdinalIgnoreCase) ? string.Empty : PathRules.ToRelative(rootPath, treePath);
    }

    /// <summary>
    /// Whether a rewrite of everything under the directory could touch a path the glob matches. A directory inside the glob's fixed
    /// directory part (<c>secrets/</c> of <c>secrets/**</c>) always could. Otherwise the files that exist where both overlap are
    /// checked (only source files for a source-only rewrite). Files that do not exist yet are not considered (a
    /// <c>git checkout &lt;rev&gt; -- &lt;dir&gt;</c> that recreates a deleted protected file is not seen).
    /// </summary>
    public bool CouldMatchUnder(string relativeTree, string glob, bool sourceOnly)
    {
        string pattern = glob.Replace('\\', '/');
        int wildcard = pattern.IndexOfAny(['*', '?']);
        string fixedPart = wildcard < 0 ? pattern : pattern[..wildcard];
        string fixedDirectory = fixedPart[..(fixedPart.LastIndexOf('/') + 1)];
        string treePrefix = relativeTree.Length == 0 ? string.Empty : relativeTree + "/";
        if (fixedDirectory.Length > 0 && treePrefix.Length > 0 && treePrefix.StartsWith(fixedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (wildcard < 0)
        {
            return pattern.StartsWith(treePrefix, StringComparison.OrdinalIgnoreCase) && ExistingMatchUnder(pattern, pattern, sourceOnly);
        }

        if (fixedDirectory.StartsWith(treePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ExistingMatchUnder(fixedDirectory.TrimEnd('/'), pattern, sourceOnly);
        }

        return fixedDirectory.Length == 0 && ExistingMatchUnder(relativeTree, pattern, sourceOnly);
    }

    /// <summary>
    /// The first glob that matches an existing file below the directory which the shell wildcard pattern names (or, for a tree,
    /// that lies below a path the pattern names), skipping the files <paramref name="skip"/> accepts for a glob; null when none
    /// does. Both paths are relative to the root. Only the globs that could match the pattern are looked for, and without a
    /// <c>**</c> or a tree the walk goes no deeper than the pattern does.
    /// </summary>
    public string? FirstMatchOfPattern(string relativeDirectory, string relativePattern, IReadOnlyList<string> globs, bool asTree, Func<string, string, bool> skip)
    {
        List<string> relevant = [.. globs.Where(glob => GlobOverlap.CouldMatch(glob, relativePattern, asTree))];
        FileAttributes? attributes = relevant.Count == 0 ? null : AttributesOf(relativeDirectory);
        if (attributes is null || !attributes.Value.HasFlag(FileAttributes.Directory))
        {
            return null;
        }

        string[] patternSegments = relativePattern.Split('/');
        int directoryDepth = relativeDirectory.Length == 0 ? 0 : relativeDirectory.Split('/').Length;
        int? maxDepth = asTree || patternSegments.Contains(AnyDepth) ? null : patternSegments.Length - directoryDepth - 1;
        string directory = relativeDirectory.Length == 0 ? root : Path.Combine(root, relativeDirectory);
        foreach (string file in Walk(directory, NameFilters(relevant), maxDepth).Select(Relative))
        {
            if (GlobOverlap.CouldMatch(file, relativePattern, asTree)
                && relevant.FirstOrDefault(glob => PathRules.Matches(glob, file) && !skip(file, glob)) is string glob)
            {
                return glob;
            }
        }

        return null;
    }

    private bool ExistingMatchUnder(string relativeTree, string pattern, bool sourceOnly)
    {
        FileAttributes? attributes = AttributesOf(relativeTree);
        if (attributes is null)
        {
            return false;
        }

        if (!attributes.Value.HasFlag(FileAttributes.Directory))
        {
            return (!sourceOnly || IsSource(relativeTree)) && PathRules.Matches(pattern, relativeTree);
        }

        return RuleFilesUnder(relativeTree).Any(relative => (!sourceOnly || IsSource(relative)) && PathRules.Matches(pattern, relative));
    }

    /// <summary>The entry's attributes, read once per decision; null when it does not exist.</summary>
    private FileAttributes? AttributesOf(string relativePath)
    {
        if (!attributesByPath.TryGetValue(relativePath, out FileAttributes? attributes))
        {
            string path = relativePath.Length == 0 ? root : Path.Combine(root, relativePath);
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                attributes = null;
            }

            attributesByPath[relativePath] = attributes;
        }

        return attributes;
    }

    /// <summary>
    /// The files below the directory that match one of the rules, relative to the root; taken from a walked ancestor when there is
    /// one (an ancestor's walk keeps every file a rule could want below it).
    /// </summary>
    private List<string> RuleFilesUnder(string relativeTree)
    {
        if (ruleFilesByDirectory.TryGetValue(relativeTree, out List<string>? cached))
        {
            return cached;
        }

        for (string? ancestor = ParentOf(relativeTree); ancestor is not null; ancestor = ParentOf(ancestor))
        {
            if (ruleFilesByDirectory.TryGetValue(ancestor, out List<string>? ancestorFiles))
            {
                string prefix = relativeTree + "/";
                List<string> inherited = [.. ancestorFiles.Where(file => file.StartsWith(prefix, StringComparison.Ordinal))];
                ruleFilesByDirectory[relativeTree] = inherited;
                return inherited;
            }
        }

        List<string> relevant = [.. rules.Where(rule => GlobOverlap.CouldMatch(rule, relativeTree, asTree: true))];
        string directory = relativeTree.Length == 0 ? root : Path.Combine(root, relativeTree);
        List<string> files = relevant.Count == 0 || !Directory.Exists(directory)
            ? []
            : [.. Walk(directory, NameFilters(relevant)).Select(Relative).Where(file => relevant.Any(rule => PathRules.Matches(rule, file)))];
        ruleFilesByDirectory[relativeTree] = files;
        return files;
    }

    /// <summary>The file names the rules can match (their last segment); null in the array when a rule accepts any name.</summary>
    private static Regex?[] NameFilters(IReadOnlyList<string> rules) =>
        [.. rules.Select(rule => rule.Replace('\\', '/').Split('/')[^1]).Distinct(StringComparer.Ordinal).Select(name => name == AnyDepth ? null : GlobOverlap.SegmentRegex(name))];

    /// <summary>Files below the directory whose name passes a filter, without links and <c>.git</c>, as absolute paths.</summary>
    private FileSystemEnumerable<string> Walk(string directory, Regex?[] names, int? maxDepth = null)
    {
        bool anyName = names.Any(name => name is null);
        reads.StartListing();
        EnumerationOptions options = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        if (maxDepth is int depth)
        {
            options.MaxRecursionDepth = Math.Max(depth, 0);
        }

        return new FileSystemEnumerable<string>(directory, (ref FileSystemEntry entry) => entry.ToFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                reads.SeeEntry();
                return !entry.IsDirectory && (anyName || MatchesAny(names, entry.FileName));
            },
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !entry.FileName.SequenceEqual(GitDirectory),
        };
    }

    private static bool MatchesAny(Regex?[] names, ReadOnlySpan<char> fileName)
    {
        // A name stored decomposed (NFD) is compared in the composed form the patterns have.
        ReadOnlySpan<char> composed = UnicodeNormalization.MayChange(fileName) ? UnicodeNormalization.Compose(fileName.ToString()) : fileName;
        foreach (Regex? name in names)
        {
            if (name is not null && name.IsMatch(composed))
            {
                return true;
            }
        }

        return false;
    }

    private string Relative(string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    private static string? ParentOf(string relativePath) =>
        relativePath.Length == 0 ? null : relativePath.LastIndexOf('/') is int separator and > 0 ? relativePath[..separator] : string.Empty;

    private static bool IsSource(string path) => SourceExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
