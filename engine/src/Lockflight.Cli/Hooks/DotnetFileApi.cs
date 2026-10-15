using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// .NET file APIs called from PowerShell (<c>[IO.File]::WriteAllText(…)</c>, <c>[System.IO.Directory]::Delete(…)</c>). Their paths
/// are .NET expressions, not shell words, so a call that writes or deletes makes the command hidden.
/// </summary>
internal static partial class DotnetFileApi
{
    public static bool Writes(string word) => WritingCall().IsMatch(word);

    [GeneratedRegex(@"^\[(System\.)?IO\.(File|Directory|FileInfo|DirectoryInfo)\]::(Write|Append|Delete|Move|Copy|Create|Replace|Encrypt|Decrypt|Set|Open(?!Read|Text))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WritingCall();
}
