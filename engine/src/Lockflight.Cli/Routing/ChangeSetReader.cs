using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads a pull request's <see cref="ChangeSet"/> from git, once per run and before any build: the zero-context diff of
/// <c>merge-base(base, head)..head</c> with deleted files kept, the blob ids and modes (<c>--raw</c>), which files are binary
/// (<c>--numstat</c>), the path listings of both sides, and the texts of the changed files, every project file and every
/// <c>*.props</c> / <c>*.targets</c> on both sides (one <c>cat-file --batch</c> per side).
/// <para>
/// With a head commit (CI) only the project's own repository is read. Without one (locally) the head is the working tree of every
/// repository of the workspace: one <c>git diff &lt;merge-base&gt;</c> per repository (committed and uncommitted changes in one,
/// so no line is counted twice) plus untracked files, each child repository's paths under its unit path.
/// </para>
/// </summary>
internal static class ChangeSetReader
{
    private static readonly string[] RawFormat = ["--raw", "-z", "--no-abbrev"];
    private static readonly string[] NumstatFormat = ["--numstat", "-z"];
    private static readonly string[] PatchFormat = ["-U0"];
    private static readonly string[] TextPatchFormat = ["-U0", "--text"];
    private static readonly string[] PolicyTextSuffixes = [".csproj", ".fsproj", ".vbproj", ".props", ".targets"];

    /// <summary>Reads the change from <paramref name="baseRef"/>'s merge base to <paramref name="headRef"/> (null: the working tree).</summary>
    public static async Task<ChangeSetReading> ReadAsync(string projectRoot, string baseRef, string? headRef, CancellationToken cancellationToken)
    {
        if (baseRef.Trim().Length == 0 || RoutingGit.IsZeroId(baseRef.Trim()))
        {
            return NoBase($"there is no base to compare with ('{baseRef}'); a pull request names the branch it targets");
        }

        if (await Git.CommitOfAsync(projectRoot, baseRef, cancellationToken) is not string baseCommit)
        {
            return NoBase($"the base '{baseRef}' names no commit in this clone; fetch it (e.g. git fetch origin {baseRef}) and run again");
        }

        string? headCommit = null;
        if (headRef is not null && (headCommit = await Git.CommitOfAsync(projectRoot, headRef, cancellationToken)) is null)
        {
            return Failed($"the head '{headRef}' names no commit in this clone");
        }

        IReadOnlyList<RepoRoot> repos = headCommit is null ? WorkspaceRepos.Of(projectRoot) : [new RepoRoot(projectRoot, string.Empty)];
        List<ChangedFile> files = [];
        HashSet<string> basePaths = new(StringComparer.Ordinal);
        HashSet<string> headPaths = new(StringComparer.Ordinal);
        Dictionary<string, string> baseTexts = new(StringComparer.Ordinal);
        Dictionary<string, string> headTexts = new(StringComparer.Ordinal);
        string? rootMergeBase = null;
        foreach (RepoRoot repo in repos)
        {
            string? repoBase = repo.Prefix.Length == 0 ? baseCommit : await Git.CommitOfAsync(repo.Directory, baseRef, cancellationToken);
            if (repoBase is null)
            {
                return NoBase($"the base '{baseRef}' names no commit in {repo.Prefix.TrimEnd('/')}; fetch it there and run again");
            }

            string? mergeBase = await RoutingGit.MergeBaseAsync(repo.Directory, repoBase, headCommit ?? "HEAD", cancellationToken);
            if (mergeBase is null)
            {
                return Failed(await NoMergeBaseMessageAsync(repo, baseRef, cancellationToken));
            }

            rootMergeBase ??= mergeBase;
            if (await ReadRepositoryAsync(repo, repos, mergeBase, headCommit, cancellationToken) is not RepositoryChange change)
            {
                return Failed($"git could not read the change in {(repo.Prefix.Length == 0 ? "the repository" : repo.Prefix.TrimEnd('/'))}; run git status there");
            }

            files.AddRange(change.Files);
            basePaths.UnionWith(change.BasePaths);
            headPaths.UnionWith(change.HeadPaths);
            Merge(baseTexts, change.BaseTexts);
            Merge(headTexts, change.HeadTexts);
        }

        ChangeSet changeSet = new(
            baseRef,
            baseCommit,
            rootMergeBase!,
            headCommit,
            [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)],
            new RevisionSnapshot(rootMergeBase, basePaths, baseTexts),
            new RevisionSnapshot(headCommit, headPaths, headTexts));
        return new ChangeSetReading(ChangeSetReadStatus.Read, changeSet, string.Empty);
    }

    private static async Task<RepositoryChange?> ReadRepositoryAsync(
        RepoRoot repo, IReadOnlyList<RepoRoot> repos, string mergeBase, string? headCommit, CancellationToken cancellationToken)
    {
        string[] range = headCommit is null ? [mergeBase] : [mergeBase, headCommit];
        string? raw = await RoutingGit.DiffAsync(repo.Directory, range, RawFormat, cancellationToken);
        string? numstat = await RoutingGit.DiffAsync(repo.Directory, range, NumstatFormat, cancellationToken);
        string? patch = await RoutingGit.DiffAsync(repo.Directory, range, PatchFormat, cancellationToken);
        IReadOnlyList<string>? basePaths = await RoutingGit.TreePathsAsync(repo.Directory, mergeBase, cancellationToken);
        IReadOnlyList<string>? headPaths = headCommit is null
            ? await RoutingGit.WorkingTreePathsAsync(repo.Directory, cancellationToken)
            : await RoutingGit.TreePathsAsync(repo.Directory, headCommit, cancellationToken);
        IReadOnlyList<string>? untracked = headCommit is null ? await RoutingGit.UntrackedPathsAsync(repo.Directory, cancellationToken) : [];
        string? prefix = await RoutingGit.PrefixAsync(repo.Directory, cancellationToken);
        if (raw is null || numstat is null || patch is null || basePaths is null || headPaths is null || untracked is null || prefix is null)
        {
            return null;
        }

        List<string> existingHeadPaths = headCommit is null ? [.. headPaths.Where(path => ExistsOnDisk(repo.Directory, path))] : [.. headPaths];
        IReadOnlySet<string> gitBinary = NumstatDiff.BinaryPaths(numstat);
        if (await TextMarkedBinaryAsync(repo.Directory, mergeBase, headCommit, prefix, gitBinary, cancellationToken) is not IReadOnlyList<string> markedText)
        {
            return null;
        }

        string? textPatch = markedText.Count == 0 ? string.Empty : await RoutingGit.DiffAsync(repo.Directory, range, TextPatchFormat, markedText, cancellationToken);
        if (textPatch is null)
        {
            return null;
        }

        IReadOnlySet<string> binaryPaths = new HashSet<string>(gitBinary.Except(markedText, StringComparer.Ordinal), StringComparer.Ordinal);
        List<ChangedFile> files = ChangedFiles(RawDiff.Parse(raw), binaryPaths, [.. UnifiedDiff.PatchFilesOf(patch), .. UnifiedDiff.PatchFilesOf(textPatch)]);
        HashSet<string> listed = new(files.Select(file => file.Path), StringComparer.Ordinal);
        files.AddRange(UntrackedFiles(repo.Directory, untracked.Where(path => !listed.Contains(path))));
        if (headCommit is null && !await FillWorkingTreeBlobsAsync(repo.Directory, files, cancellationToken))
        {
            return null;
        }

        IReadOnlyDictionary<string, string>? baseTexts = await CommitTextsAsync(
            repo.Directory, mergeBase, prefix, TextPaths(files.Where(file => file.ExistsAtBase), basePaths), cancellationToken);
        IReadOnlyDictionary<string, string>? headTexts = headCommit is null
            ? WorkingTreeTexts(repo.Directory, TextPaths(files.Where(file => file.ExistsAtHead), existingHeadPaths))
            : await CommitTextsAsync(repo.Directory, headCommit, prefix, TextPaths(files.Where(file => file.ExistsAtHead), existingHeadPaths), cancellationToken);
        if (baseTexts is null || headTexts is null)
        {
            return null;
        }

        return new RepositoryChange(
            [.. files.Where(file => Visible(repos, repo, file.Path)).Select(file => file with { Path = repo.Prefix + file.Path })],
            Prefixed(repos, repo, basePaths),
            Prefixed(repos, repo, existingHeadPaths),
            Prefixed(repos, repo, baseTexts),
            Prefixed(repos, repo, headTexts));
    }

    private static List<ChangedFile> ChangedFiles(IReadOnlyList<RawDiffEntry> entries, IReadOnlySet<string> binaryPaths, IReadOnlyList<PatchFile> patches)
    {
        Dictionary<string, PatchFile> patchByPath = new(StringComparer.Ordinal);
        foreach (PatchFile patch in patches)
        {
            patchByPath.TryAdd(patch.NewFile ?? patch.OldFile!, patch);
        }

        List<ChangedFile> files = [];
        foreach (RawDiffEntry entry in entries)
        {
            ChangeKind kind = entry.Status switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Deleted,
                'T' => ChangeKind.TypeChanged,
                _ => ChangeKind.Modified,
            };
            patchByPath.TryGetValue(entry.Path, out PatchFile? patch);
            files.Add(new ChangedFile(
                entry.Path,
                kind,
                kind == ChangeKind.Added ? null : entry.OldBlob,
                kind == ChangeKind.Deleted || RoutingGit.IsZeroId(entry.NewBlob) ? null : entry.NewBlob,
                kind == ChangeKind.Added ? null : entry.OldMode,
                kind == ChangeKind.Deleted ? null : entry.NewMode,
                binaryPaths.Contains(entry.Path),
                patch?.Added ?? [],
                patch?.Removed ?? []));
        }

        return files;
    }

    private static IEnumerable<ChangedFile> UntrackedFiles(string directory, IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            FileInfo file = new(Path.Combine(directory, path));
            if (!file.Exists || file.LinkTarget is not null)
            {
                continue;
            }

            byte[] content = File.ReadAllBytes(file.FullName);
            bool binary = RevisionText.LooksBinary(content);
            string? text = binary ? null : RevisionText.Decode(content);
            IReadOnlyList<DiffLine> lines = text is null ? [] : AllLines(text);
            yield return new ChangedFile(path, ChangeKind.Added, null, null, null, "100644", binary, lines, []);
        }
    }

    private static List<DiffLine> AllLines(string text)
    {
        string[] lines = text.EndsWith('\n') ? text[..^1].Split('\n') : text.Split('\n');
        return text.Length == 0 ? [] : [.. lines.Select((line, index) => new DiffLine(index + 1, line))];
    }

    /// <summary>A working-tree file's blob id is the one git would store; <c>--raw</c> prints zeros for it.</summary>
    private static async Task<bool> FillWorkingTreeBlobsAsync(string directory, List<ChangedFile> files, CancellationToken cancellationToken)
    {
        string[] unknown = [.. files.Where(file => file.ExistsAtHead && file.NewBlob is null && file.NewMode != ChangedFile.SubmoduleMode && ExistsAsFile(directory, file.Path)).Select(file => file.Path)];
        if (await RoutingGit.WorkingTreeBlobsAsync(directory, unknown, cancellationToken) is not IReadOnlyDictionary<string, string> blobs)
        {
            return false;
        }

        for (int index = 0; index < files.Count; index++)
        {
            if (files[index].NewBlob is null && blobs.TryGetValue(files[index].Path, out string? blob))
            {
                files[index] = files[index] with { NewBlob = blob };
            }
        }

        return true;
    }

    /// <summary>The changed text files of one side, plus every project, <c>*.props</c> and <c>*.targets</c> file it holds.</summary>
    private static string[] TextPaths(IEnumerable<ChangedFile> changed, IReadOnlyList<string> sidePaths) =>
        [.. changed.Where(file => !file.Binary && file.NewMode != ChangedFile.SubmoduleMode && file.OldMode != ChangedFile.SubmoduleMode).Select(file => file.Path)
            .Concat(sidePaths.Where(IsPolicyText))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static bool IsPolicyText(string path) => PolicyTextSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyDictionary<string, string>?> CommitTextsAsync(
        string directory, string commit, string prefix, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (await RoutingGit.BlobsAsync(directory, commit, [.. paths.Select(path => prefix + path)], cancellationToken) is not IReadOnlyDictionary<string, byte[]> blobs)
        {
            return null;
        }

        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (blobs.TryGetValue(prefix + path, out byte[]? content) && RevisionText.Decode(content) is string text)
            {
                texts[path] = text;
            }
        }

        return texts;
    }

    /// <summary>
    /// The files git calls binary whose content is text on every side that has them: a change's own <c>.gitattributes</c>
    /// (<c>*.cs -diff</c>, <c>binary</c>) marked them, and the attributes git reads are the head's, so routing decides by content
    /// (git's NUL test, and the size limit) and counts their lines. A file larger than the limit on either side stays binary and
    /// is never read. Null when git fails.
    /// </summary>
    private static async Task<IReadOnlyList<string>?> TextMarkedBinaryAsync(
        string directory, string mergeBase, string? headCommit, string prefix, IReadOnlySet<string> gitBinary, CancellationToken cancellationToken)
    {
        if (gitBinary.Count == 0)
        {
            return [];
        }

        if (await SideAsync(directory, mergeBase, prefix, gitBinary, cancellationToken) is not IReadOnlyDictionary<string, byte[]?> atBase)
        {
            return null;
        }

        IReadOnlyDictionary<string, byte[]?>? atHead = headCommit is null
            ? WorkingTreeSide(directory, gitBinary)
            : await SideAsync(directory, headCommit, prefix, gitBinary, cancellationToken);
        if (atHead is null)
        {
            return null;
        }

        return [.. gitBinary.Where(path => (atBase.ContainsKey(path) || atHead.ContainsKey(path)) && IsText(atBase, path) && IsText(atHead, path)).Order(StringComparer.Ordinal)];
    }

    /// <summary>A side's content is text when it decodes; a side that lacks the file says nothing; a file too large to read is not text.</summary>
    private static bool IsText(IReadOnlyDictionary<string, byte[]?> side, string path) =>
        !side.TryGetValue(path, out byte[]? content) || (content is not null && RevisionText.Decode(content) is not null);

    /// <summary>The paths a commit has, each with its content, or null for content larger than <see cref="RevisionText.MaxBytes"/>.</summary>
    private static async Task<IReadOnlyDictionary<string, byte[]?>?> SideAsync(
        string directory, string commit, string prefix, IReadOnlySet<string> paths, CancellationToken cancellationToken)
    {
        if (await RoutingGit.SizesAsync(directory, commit, [.. paths.Select(path => prefix + path)], cancellationToken) is not IReadOnlyDictionary<string, long> sizes)
        {
            return null;
        }

        string[] small = [.. sizes.Where(entry => entry.Value <= RevisionText.MaxBytes).Select(entry => entry.Key)];
        if (await RoutingGit.BlobsAsync(directory, commit, small, cancellationToken) is not IReadOnlyDictionary<string, byte[]> blobs)
        {
            return null;
        }

        return sizes.Keys.ToDictionary(path => path[prefix.Length..], path => blobs.TryGetValue(path, out byte[]? content) ? content : null, StringComparer.Ordinal);
    }

    private static Dictionary<string, byte[]?> WorkingTreeSide(string directory, IReadOnlySet<string> paths) =>
        paths.Where(path => ExistsAsFile(directory, path)).ToDictionary(
            path => path,
            path => new FileInfo(Path.Combine(directory, path)).Length <= RevisionText.MaxBytes ? File.ReadAllBytes(Path.Combine(directory, path)) : null,
            StringComparer.Ordinal);

    private static Dictionary<string, string> WorkingTreeTexts(string directory, IReadOnlyList<string> paths)
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            FileInfo file = new(Path.Combine(directory, path));
            if (file.Exists && file.LinkTarget is null && file.Length <= RevisionText.MaxBytes && RevisionText.Decode(File.ReadAllBytes(file.FullName)) is string text)
            {
                texts[path] = text;
            }
        }

        return texts;
    }

    private static async Task<string> NoMergeBaseMessageAsync(RepoRoot repo, string baseRef, CancellationToken cancellationToken) =>
        await RoutingGit.IsShallowAsync(repo.Directory, cancellationToken)
            ? $"the clone is shallow and does not hold the merge base with '{baseRef}'; fetch the full history (actions/checkout: fetch-depth: 0) and run again"
            : $"the head and '{baseRef}' have no common commit{(repo.Prefix.Length == 0 ? string.Empty : " in " + repo.Prefix.TrimEnd('/'))}; compare with the branch the change was made from";

    private static bool ExistsOnDisk(string directory, string path)
    {
        string full = Path.Combine(directory, path);
        return File.Exists(full) || Directory.Exists(full) || new FileInfo(full).LinkTarget is not null;
    }

    private static bool ExistsAsFile(string directory, string path)
    {
        FileInfo file = new(Path.Combine(directory, path));
        return file.Exists && file.LinkTarget is null;
    }

    private static bool Visible(IReadOnlyList<RepoRoot> repos, RepoRoot repo, string path) =>
        repo.Prefix.Length > 0 || !WorkspaceRepos.InChildRepository(repos, path);

    private static IEnumerable<string> Prefixed(IReadOnlyList<RepoRoot> repos, RepoRoot repo, IEnumerable<string> paths) =>
        paths.Where(path => Visible(repos, repo, path)).Select(path => repo.Prefix + path);

    private static Dictionary<string, string> Prefixed(IReadOnlyList<RepoRoot> repos, RepoRoot repo, IReadOnlyDictionary<string, string> texts) =>
        texts.Where(entry => Visible(repos, repo, entry.Key)).ToDictionary(entry => repo.Prefix + entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static void Merge(Dictionary<string, string> into, IReadOnlyDictionary<string, string> texts)
    {
        foreach ((string path, string text) in texts)
        {
            into[path] = text;
        }
    }

    private static ChangeSetReading NoBase(string message) => new(ChangeSetReadStatus.NoBase, null, message);

    private static ChangeSetReading Failed(string message) => new(ChangeSetReadStatus.GitFailed, null, message);

    private sealed record RepositoryChange(
        IReadOnlyList<ChangedFile> Files,
        IEnumerable<string> BasePaths,
        IEnumerable<string> HeadPaths,
        IReadOnlyDictionary<string, string> BaseTexts,
        IReadOnlyDictionary<string, string> HeadTexts);
}
