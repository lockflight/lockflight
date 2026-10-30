using System.Text;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// Clones a unit's workspace shell for its CI: one commit of one branch, no checkout (the policy and specs are read with
/// <c>git show</c>, so no file of the shell is ever written or run), never prompting, over https or SSH only. A token in
/// <see cref="TokenVariable"/> authenticates an <c>https://</c> URL through git's environment configuration, so it is neither
/// on the command line nor written into the clone, and git's tracing of it is turned off; an SSH URL uses the job's own SSH
/// setup (a deploy key).
/// </summary>
internal static class ShellClone
{
    /// <summary>A read-only token for the shell repository (a deploy token, repository access token or fine-grained token).</summary>
    public const string TokenVariable = "LOCKFLIGHT_SHELL_TOKEN";

    /// <summary>The user name sent with the token; <see cref="DefaultTokenUser"/> when unset.</summary>
    public const string TokenUserVariable = "LOCKFLIGHT_SHELL_TOKEN_USER";

    /// <summary>The user name GitHub expects with a token (Bitbucket's repository access tokens want <c>x-token-auth</c>).</summary>
    public const string DefaultTokenUser = "x-access-token";

    private const string Https = "https://";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(5);

    /// <summary>Clones <paramref name="url"/> (its <paramref name="branch"/>, else the default branch) into <paramref name="directory"/>.</summary>
    public static async Task<ShellCloneResult> CloneAsync(string url, string? branch, string directory, EnvironmentReader environment, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string?> variables = GitEnvironment(url, environment);
        string[] arguments = ["clone", "--quiet", "--no-checkout", "--depth", "1", "--single-branch", "--no-tags", .. branch is null ? Array.Empty<string>() : ["--branch", branch], "--", url, directory];
        ProcessResult clone;
        try
        {
            clone = await ProcessRunner.RunAsync("git", arguments, Path.GetTempPath(), TimeLimit, variables, cancellationToken);
        }
        catch (ExecutableNotFoundException)
        {
            return ShellCloneResult.Failed("git is not installed or not on PATH");
        }

        if (clone.ExitCode == 0)
        {
            return ShellCloneResult.Cloned;
        }

        string? token = Token(environment);
        bool withToken = token is not null && IsHttps(url);
        string access = withToken
            ? $"check that {TokenVariable} can read it ({TokenUserVariable} sets the user name, default {DefaultTokenUser})"
            : IsHttps(url)
                ? $"a private shell needs a read-only token in {TokenVariable}"
                : "a private shell needs a read-only deploy key in the job's SSH setup";
        string message = $"git clone of the workspace shell {url}{(branch is null ? string.Empty : $" (branch {branch})")} failed: {clone.Combined.Trim()}; {access}";
        return ShellCloneResult.Failed(Redact(message, token, variables));
    }

    /// <summary>
    /// The variables git runs with: no prompt, no protocol but https and SSH, no tracing that could print the token, and the token as
    /// an <c>Authorization</c> header for an <c>https://</c> URL only.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> GitEnvironment(string url, EnvironmentReader environment)
    {
        Dictionary<string, string?> variables = new(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ASKPASS"] = null,
            ["SSH_ASKPASS"] = null,
            ["GIT_ALLOW_PROTOCOL"] = "https:ssh",
            ["GIT_TRACE"] = null,
            ["GIT_TRACE_CURL"] = null,
            ["GIT_TRACE_PACKET"] = null,
            ["GIT_CURL_VERBOSE"] = null,
            ["GIT_TRACE_REDACT"] = "1",
        };
        if (Token(environment) is string token && IsHttps(url))
        {
            string user = environment.Get(TokenUserVariable) is { Length: > 0 } named ? named : DefaultTokenUser;
            variables["GIT_CONFIG_COUNT"] = "1";
            variables["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            variables["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{token}"))}";
        }

        return variables;
    }

    private static bool IsHttps(string url) => url.StartsWith(Https, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The token, or null when unset. Azure Pipelines passes an undefined variable on as its own macro text, <c>$(NAME)</c>, which is
    /// no token: the clone then goes without one.
    /// </summary>
    private static string? Token(EnvironmentReader environment) =>
        environment.Get(TokenVariable) is { Length: > 0 } token && !(token.StartsWith("$(", StringComparison.Ordinal) && token.EndsWith(')')) ? token : null;

    /// <summary>The text without the token, raw or in its header form.</summary>
    private static string Redact(string text, string? token, IReadOnlyDictionary<string, string?> variables)
    {
        string redacted = string.IsNullOrEmpty(token) ? text : text.Replace(token, "***", StringComparison.Ordinal);
        return variables.GetValueOrDefault("GIT_CONFIG_VALUE_0") is string header
            ? redacted.Replace(header["Authorization: Basic ".Length..], "***", StringComparison.Ordinal)
            : redacted;
    }
}
