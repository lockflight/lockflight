using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The common PowerShell cmdlets that write, by full name (in either dialect) or alias (PowerShell only): <c>Set-Content</c>,
/// <c>Add-Content</c>, <c>Clear-Content</c>, <c>Out-File</c>, <c>Tee-Object</c> and <c>New-Item</c> write their paths;
/// <c>Remove-Item</c> deletes them as trees; <c>Copy-Item</c> writes the destination and <c>DEST\&lt;name&gt;</c> (a tree with
/// <c>-Recurse</c>); <c>Move-Item</c> moves whole directories, so source and target are trees. A path may be an array (<c>a,b</c>),
/// each element checked too, and carry a provider prefix (<c>FileSystem::C:\x</c>). Paths that come from the pipeline are not seen.
/// </summary>
internal static partial class PowerShellWriteTargets
{
    private static readonly string[] PathParameters = ["path", "literalpath", "filepath"];

    private static readonly HashSet<string> PathWriters =
        ["set-content", "add-content", "clear-content", "out-file", "tee-object", "new-item"];

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["rm"] = "remove-item", ["del"] = "remove-item", ["erase"] = "remove-item", ["ri"] = "remove-item", ["rd"] = "remove-item", ["rmdir"] = "remove-item",
        ["cp"] = "copy-item", ["copy"] = "copy-item", ["cpi"] = "copy-item",
        ["mv"] = "move-item", ["move"] = "move-item", ["mi"] = "move-item",
        ["sc"] = "set-content", ["ac"] = "add-content", ["clc"] = "clear-content", ["ni"] = "new-item", ["tee"] = "tee-object",
    };

    private static readonly HashSet<string> OtherWriters = ["remove-item", "copy-item", "move-item"];

    /// <summary>The cmdlet a program name stands for in the dialect; null when it is not a writing cmdlet.</summary>
    public static string? Cmdlet(string program, ShellDialect dialect)
    {
        string name = dialect == ShellDialect.PowerShell && Aliases.TryGetValue(program, out string? cmdlet) ? cmdlet : program;
        return PathWriters.Contains(name) || OtherWriters.Contains(name) ? name : null;
    }

    public static CommandWrites Of(string cmdlet, IReadOnlyList<string> arguments)
    {
        CommandWrites writes = Written(cmdlet, arguments);
        return writes with { Files = [.. writes.Files.SelectMany(Elements)], Trees = [.. writes.Trees.SelectMany(Elements)] };
    }

    private static CommandWrites Written(string cmdlet, IReadOnlyList<string> arguments)
    {
        PowerShellArguments parsed = PowerShellArguments.Parse(arguments);
        List<string> named = [.. parsed.Values(PathParameters)];
        return cmdlet switch
        {
            "remove-item" => CommandWrites.OfFilesAndTrees([.. named, .. parsed.Positional]),
            "copy-item" or "move-item" => CopyOrMove(cmdlet == "move-item", parsed, named),
            // A new directory replaces no file (New-Item fails on an existing one; -Force on a directory keeps its contents).
            "new-item" when FirstOf(parsed.Values("itemtype")) is { Length: > 0 } itemType && "directory".StartsWith(itemType.ToLowerInvariant(), StringComparison.Ordinal) => CommandWrites.None,
            "new-item" => CommandWrites.OfFiles([.. Targets(parsed, named), .. NewItemPaths(parsed, named)]),
            _ => CommandWrites.OfFiles(Targets(parsed, named)),
        };
    }

    private static CommandWrites CopyOrMove(bool move, PowerShellArguments parsed, List<string> named)
    {
        List<string> sources = named.Count > 0 ? named : [.. parsed.Positional.Take(1)];
        string? destination = FirstOf(parsed.Values("destination"))
            ?? parsed.Positional.Skip(named.Count > 0 ? 0 : 1).FirstOrDefault();
        if (destination is null)
        {
            return move ? CommandWrites.OfFilesAndTrees(sources) : CommandWrites.None;
        }

        List<string> files = [destination, .. sources.Select(ShellArguments.BaseName).OfType<string>().Select(name => ShellArguments.Join(destination, name))];
        List<string> trees = move || parsed.HasSwitch("recurse") ? [destination] : [];
        if (move)
        {
            files.AddRange(sources);
            trees.AddRange(sources);
        }

        return new CommandWrites(files, trees, []);
    }

    private static IEnumerable<string> NewItemPaths(PowerShellArguments parsed, List<string> named) =>
        parsed.Values("name").SelectMany(name => named.Count > 0 ? named.Select(path => ShellArguments.Join(path, name)) : [name]);

    /// <summary>The cmdlet's path: <c>-Path</c>/<c>-LiteralPath</c>/<c>-FilePath</c>, else its first positional word (the second is its <c>-Value</c>).</summary>
    private static List<string> Targets(PowerShellArguments parsed, List<string> named) => named.Count > 0 ? named : [.. parsed.Positional.Take(1)];

    private static string? FirstOf(IReadOnlyList<string> values) => values.Count > 0 ? values[0] : null;

    /// <summary>
    /// The path without a provider prefix, and each element of an array (<c>a,b</c>; a name with a comma in it is kept whole as
    /// well).
    /// </summary>
    private static IEnumerable<string> Elements(string path)
    {
        string plain = ProviderPrefix().Replace(path, string.Empty);
        yield return plain;
        if (plain.Contains(',', StringComparison.Ordinal))
        {
            foreach (string element in plain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return ProviderPrefix().Replace(element, string.Empty);
            }
        }
    }

    [GeneratedRegex(@"^(Microsoft\.PowerShell\.Core\\)?FileSystem::", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderPrefix();
}
