namespace Lockflight.Cli.Init;

/// <summary>
/// The shell steps the Azure, Bitbucket and GitLab pipelines share, written for a POSIX <c>sh</c> under <c>set -e</c> so they
/// run the same in each service's shell. They mirror the GitHub workflow step by step: the same policy files from the base
/// commit, the same checksum-verified gitleaks, the engine pinned on the base branch installed outside the repository, and the
/// same gate command. A step reads <c>$BASE</c> (the base commit, empty when there is none) and <c>$LOCKFLIGHT_TEMP</c> (a
/// directory outside the repository).
/// </summary>
internal static class CiShellScripts
{
    public const string GitleaksVersion = "8.30.1";
    public const string GitleaksSha256 = "551f6fc83ea457d62a0d98237cbad105af8d557003051f41f3e7ca7b3f2470eb";

    /// <summary>The single policy files restored from the base commit, as the GitHub workflow's loop lists them.</summary>
    public const string PolicyFiles = "dotnet-tools.json .config/dotnet-tools.json lockflight.yaml .gitleaks.toml .gitleaksignore nuget.config NuGet.config";

    /// <summary>Code-style and analyzer config, at any depth, restored from the base commit.</summary>
    public const string StylePattern = @"'(^|/)(\.editorconfig|\.globalconfig|[^/]*\.globalconfig|[^/]*\.ruleset|stylecop\.json|BannedSymbols\.txt)$'";

    /// <summary>The gate command every pipeline runs, before its base and result options.</summary>
    public const string GateCommand = "lockflight gate run --profile pr";

    public const string ResultDirectory = "artifacts/lockflight";

    /// <summary>The .NET SDK container image the Bitbucket and GitLab jobs run in.</summary>
    public const string SdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    /// <summary>Where <see cref="SdkImage"/> keeps .NET; extra channels are installed next to its SDK.</summary>
    private const string SdkImageDotnetRoot = "/usr/share/dotnet";

    /// <summary>Restores the policy from <c>$BASE</c>: a PR cannot loosen the checks that judge it.</summary>
    public const string RestorePolicy = $$"""
        for file in {{PolicyFiles}}; do
          if git cat-file -e "$BASE:$file" 2>/dev/null; then git checkout "$BASE" -- "$file"; else rm -f "$file"; fi
        done
        # Accepted baselines (known-failing tests, warnings, advisories) are policy too: a PR cannot accept its own failures.
        rm -rf .lockflight/baselines
        if git cat-file -e "$BASE:.lockflight/baselines" 2>/dev/null; then git checkout "$BASE" -- .lockflight/baselines; fi
        # Code-style and analyzer config (nested files too) decides what the build and format gates enforce: a PR is
        # judged by the base's rules, and its own rule changes apply after the merge.
        style={{StylePattern}}
        { git ls-tree -r --name-only "$BASE"; git ls-files; } | { grep -E "$style" || true; } | sort -u | while IFS= read -r file; do
          if git cat-file -e "$BASE:$file" 2>/dev/null; then git checkout "$BASE" -- "$file"; else rm -f "$file"; fi
        done
        """;

    /// <summary>Downloads gitleaks at <see cref="GitleaksVersion"/> into <c>$LOCKFLIGHT_TEMP</c> and verifies its SHA-256.</summary>
    public const string InstallGitleaks = """
        archive="gitleaks_${GITLEAKS_VERSION}_linux_x64.tar.gz"
        curl -fsSLo "$LOCKFLIGHT_TEMP/$archive" "https://github.com/gitleaks/gitleaks/releases/download/v${GITLEAKS_VERSION}/$archive"
        echo "$GITLEAKS_SHA256  $LOCKFLIGHT_TEMP/$archive" | sha256sum -c -
        tar -xzf "$LOCKFLIGHT_TEMP/$archive" -C "$LOCKFLIGHT_TEMP" gitleaks
        """;

    /// <summary>
    /// Installs the engine version the tool manifest pins into <c>$LOCKFLIGHT_TEMP/lockflight</c>, outside the repository's
    /// <c>global.json</c> (which may pin an SDK older than 10), from nuget.org or the sources in the repository's nuget.config.
    /// The pin is read with grep and sed, which every image has, where the GitHub workflow uses python3.
    /// </summary>
    public const string InstallEngine = """
        if [ -f .config/dotnet-tools.json ]; then manifest=.config/dotnet-tools.json; else manifest=dotnet-tools.json; fi
        version=$(tr -d '\r\n' < "$manifest" | grep -oiE '"lockflight\.cli"[[:space:]]*:[[:space:]]*\{[^}]*\}' | grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]*"' | sed -E 's/.*"([^"]*)"$/\1/' || true)
        if [ -z "$version" ]; then echo "lockflight: $manifest pins no lockflight.cli version; run lockflight init and commit the pin" >&2; exit 1; fi
        config=""
        for file in nuget.config NuGet.config; do if [ -f "$file" ]; then config="$PWD/$file"; fi; done
        (cd "$LOCKFLIGHT_TEMP" && dotnet tool install Lockflight.Cli --tool-path "$LOCKFLIGHT_TEMP/lockflight" --version "$version" ${config:+--configfile "$config"})
        """;

    /// <summary>Runs the <c>pr</c> gates against <c>$BASE</c> when it is a commit in the clone, else without a base.</summary>
    public const string Gates = $$"""
        if [ -z "$BASE" ] || ! git cat-file -e "$BASE^{commit}" 2>/dev/null; then BASE=""; fi
        {{GateCommand}} ${BASE:+--base "$BASE"} --result {{ResultDirectory}}/result.json
        """;

    /// <summary>
    /// Installs every .NET channel but the engine's into the SDK image's own .NET root, so the test projects' older runtimes
    /// are there; nothing when the engine's channel is all the tests need.
    /// </summary>
    public static string InstallExtraChannels(IReadOnlyList<string> dotnetChannels)
    {
        string[] extra = [.. dotnetChannels.Where(channel => channel != DotnetChannels.EngineChannel)];
        if (extra.Length == 0)
        {
            return string.Empty;
        }

        IEnumerable<string> installs = extra.Select(channel => $"bash \"$LOCKFLIGHT_TEMP/dotnet-install.sh\" --channel {channel[..^2]} --install-dir {SdkImageDotnetRoot}");
        return string.Join('\n', ["curl -fsSLo \"$LOCKFLIGHT_TEMP/dotnet-install.sh\" https://dot.net/v1/dotnet-install.sh", .. installs]);
    }

    /// <summary>
    /// The whole job for a service that runs one shell in <see cref="SdkImage"/> (Bitbucket, GitLab): <paramref name="findBase"/>
    /// sets <c>$BASE</c> and <c>$PULL_REQUEST</c> (non-empty on a pull request); the steps follow in the GitHub workflow's order.
    /// </summary>
    public static string ContainerJob(string findBase, IReadOnlyList<string> dotnetChannels)
    {
        string extraChannels = InstallExtraChannels(dotnetChannels);
        string[] sections =
        [
            "set -e",
            "LOCKFLIGHT_TEMP=$(mktemp -d)",
            "GITLEAKS_VERSION=" + GitleaksVersion,
            "GITLEAKS_SHA256=" + GitleaksSha256,
            findBase,
            "# Use the base branch's engine pin and policy.",
            "if [ -n \"$PULL_REQUEST\" ]; then",
            Indent(RestorePolicy, 2),
            "fi",
            "# Install gitleaks (pinned, checksum-verified).",
            InstallGitleaks,
            "export LOCKFLIGHT_GITLEAKS=\"$LOCKFLIGHT_TEMP/gitleaks\"",
            .. extraChannels.Length == 0 ? Array.Empty<string>() : ["# Install the .NET channels the test projects target.", extraChannels],
            "# Install the pinned engine (outside the repository's global.json).",
            InstallEngine,
            "export PATH=\"$LOCKFLIGHT_TEMP/lockflight:$PATH\"",
            "# Gates.",
            Gates,
        ];
        return string.Join('\n', sections);
    }

    /// <summary>Every line of <paramref name="script"/> indented by <paramref name="spaces"/>; empty lines stay empty.</summary>
    public static string Indent(string script, int spaces)
    {
        string indent = new(' ', spaces);
        return string.Join('\n', script.Split('\n').Select(line => line.Length == 0 ? line : indent + line));
    }
}
