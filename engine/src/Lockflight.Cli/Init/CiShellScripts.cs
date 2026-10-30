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
    public const string PolicyFiles = "dotnet-tools.json .config/dotnet-tools.json lockflight.yaml .lockflight/engine.lock .gitleaks.toml .gitleaksignore nuget.config NuGet.config";

    /// <summary>The policy directories restored from the base commit as a whole: accepted baselines and the vendored install script.</summary>
    public const string PolicyDirectories = ".lockflight/baselines .lockflight/ci";

    /// <summary>The first and last lines of the engine install step, which <c>update</c> replaces as a whole.</summary>
    public const string InstallBeginMarker = "# lockflight:install-engine:begin";
    public const string InstallEndMarker = "# lockflight:install-engine:end";

    /// <summary>Code-style and analyzer config, at any depth, restored from the base commit.</summary>
    public const string StylePattern = @"'(^|/)(\.editorconfig|\.globalconfig|[^/]*\.globalconfig|[^/]*\.ruleset|stylecop\.json|BannedSymbols\.txt|CodeMetricsConfig\.txt)$'";

    /// <summary>The gate command every pipeline runs, before its base and result options.</summary>
    public const string GateCommand = "lockflight gate run --profile pr";

    public const string ResultDirectory = "artifacts/lockflight";

    /// <summary>Where a container judge step leaves its evidence for the report steps (the service's artifacts).</summary>
    public const string EvidenceDirectory = "artifacts/lockflight-evidence";

    /// <summary>Where the report steps write the verdict, the comment and the summary.</summary>
    public const string VerdictDirectory = "artifacts/lockflight-verdict";

    /// <summary>The .NET SDK container image the Bitbucket and GitLab jobs run in.</summary>
    public const string SdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    /// <summary>Where <see cref="SdkImage"/> keeps .NET; extra channels are installed next to its SDK.</summary>
    private const string SdkImageDotnetRoot = "/usr/share/dotnet";

    /// <summary>Restores the policy from <c>$BASE</c>: a PR cannot loosen the checks that judge it.</summary>
    public const string RestorePolicy = $$"""
        for file in {{PolicyFiles}}; do
          if git cat-file -e "$BASE:$file" 2>/dev/null; then git checkout "$BASE" -- "$file"; else rm -f "$file"; fi
        done
        # Accepted baselines (known-failing tests, warnings, advisories) are policy too: a PR cannot accept its own failures. So is
        # the vendored engine install script: CI runs the base commit's copy, checked against the base commit's lock.
        for directory in {{PolicyDirectories}}; do
          rm -rf "$directory"
          if git cat-file -e "$BASE:$directory" 2>/dev/null; then git checkout "$BASE" -- "$directory"; fi
        done
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
    /// Installs the engine the base commit pins into <c>$LOCKFLIGHT_TEMP/lockflight</c>, outside the repository's
    /// <c>global.json</c>. With <c>.lockflight/engine.lock</c>: the vendored install script, after checking it against the hash the
    /// lock names (both restored from the base commit), which downloads the platform's package and checks the executable
    /// against the lock before anything runs it; through <c>dotnet</c> when the repository's nuget.config names the sources and
    /// <c>LOCKFLIGHT_NUGET_FEED</c> is unset; and the <c>dotnet-tools.json</c> mirror must name the lock's version. Without a lock
    /// (a project still pinned the way Lockflight 0.2 pins, as the base of the pull request that moves it to the lock is): the
    /// pinned version with <c>dotnet tool install</c>. Between the markers, so <c>update</c> replaces only this step.
    /// </summary>
    public const string InstallEngine = """
        # lockflight:install-engine:begin
        if [ -f .config/dotnet-tools.json ]; then manifest=.config/dotnet-tools.json; else manifest=dotnet-tools.json; fi
        pinned=$( { tr -d '\r\n' < "$manifest" 2>/dev/null || true; } | grep -oiE '"lockflight\.cli"[[:space:]]*:[[:space:]]*\{[^}]*\}' | grep -oE '"version"[[:space:]]*:[[:space:]]*"[^"]*"' | sed -E 's/.*"([^"]*)"$/\1/' || true)
        if [ -f .lockflight/engine.lock ]; then
          expected=$(sed -n 's/.*"installScript"[^}]*"sha256"[[:space:]]*:[[:space:]]*"\([0-9a-f]\{64\}\)".*/\1/p' .lockflight/engine.lock)
          if command -v sha256sum >/dev/null 2>&1; then actual=$(sha256sum < .lockflight/ci/install.sh); else actual=$(shasum -a 256 < .lockflight/ci/install.sh); fi
          if [ -z "$expected" ] || [ "${actual%% *}" != "$expected" ]; then echo "lockflight: .lockflight/ci/install.sh is not the script .lockflight/engine.lock pins" >&2; exit 1; fi
          locked=$(sed -n 's/^  "version":[[:space:]]*"\([^"]*\)".*/\1/p' .lockflight/engine.lock)
          if [ -n "$pinned" ] && [ "$pinned" != "$locked" ]; then echo "lockflight: $manifest pins $pinned but .lockflight/engine.lock pins $locked; they must agree" >&2; exit 1; fi
          set --
          if [ -z "${LOCKFLIGHT_NUGET_FEED:-}" ]; then for file in nuget.config NuGet.config; do if [ -f "$file" ]; then set -- --via-dotnet --nuget-config "$PWD/$file"; fi; done; fi
          sh .lockflight/ci/install.sh --lock .lockflight/engine.lock --dir "$LOCKFLIGHT_TEMP/lockflight" "$@"
          # A pull request that changes the lock: the proposed lock must match the hashes its release published, and where the
          # gh CLI has a token (GitHub) the release's attestation must verify. The reviewer reads "matches release vX".
          base="${BASE:-${LOCKFLIGHT_BASE:-}}"
          if [ -z "$base" ] && [ "${GITHUB_EVENT_NAME:-}" = pull_request ]; then base=$(git rev-parse --verify --quiet 'HEAD^1' || true); fi
          if [ -n "$base" ] && git cat-file -e "$base:.lockflight/engine.lock" 2>/dev/null && git cat-file -e "HEAD:.lockflight/engine.lock" 2>/dev/null \
            && ! git diff --quiet "$base" HEAD -- .lockflight/engine.lock; then
            git show HEAD:.lockflight/engine.lock > "$LOCKFLIGHT_TEMP/proposed-engine.lock"
            "$LOCKFLIGHT_TEMP/lockflight/lockflight" engine verify-lock "$LOCKFLIGHT_TEMP/proposed-engine.lock" ${GH_TOKEN:+--attestation}
          fi
        else
          if [ -z "$pinned" ]; then echo "lockflight: neither .lockflight/engine.lock nor $manifest pins the engine; run lockflight init and commit the pin" >&2; exit 1; fi
          config=""
          for file in nuget.config NuGet.config; do if [ -f "$file" ]; then config="$PWD/$file"; fi; done
          (cd "$LOCKFLIGHT_TEMP" && dotnet tool install Lockflight.Cli --tool-path "$LOCKFLIGHT_TEMP/lockflight" --version "$pinned" ${config:+--configfile "$config"})
        fi
        # lockflight:install-engine:end
        """;

    /// <summary>
    /// The engine install step as Lockflight 0.2's <c>init</c> wrote it (the container and Azure pipelines): what <c>update</c>
    /// replaces when the step has no markers yet.
    /// </summary>
    public const string LegacyInstallEngine = """
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

    /// <summary>
    /// The CI judge's step for a service that runs one shell in <see cref="SdkImage"/>: <paramref name="findInputs"/> sets
    /// <c>$HEAD_COMMIT</c>, <c>$BASE</c> (the target branch's tip; empty on a push without a previous commit), <c>$EVENT</c>
    /// and <c>$PULL_REQUEST</c>; the judge's exit code is kept so the report steps that follow decide each status context, and
    /// its evidence is copied to <c>artifacts/lockflight-evidence/</c> for them.
    /// </summary>
    public static string JudgeJob(string findInputs, IReadOnlyList<string> dotnetChannels, string platform, string branchVariable) =>
        Job(findInputs, dotnetChannels, gitleaks: true, $$"""
            set +e
            lockflight ci --platform {{platform}} --event "$EVENT" --head "$HEAD_COMMIT" ${BASE:+--base "$BASE"} ${{{branchVariable}}:+--branch "${{branchVariable}}"} --no-review-expected --evidence "$LOCKFLIGHT_TEMP/evidence"
            code=$?
            set -e
            mkdir -p {{EvidenceDirectory}}
            cp -R "$LOCKFLIGHT_TEMP/evidence/." {{EvidenceDirectory}}/ || true
            echo "lockflight ci exited $code; the steps that follow report the gates and lockflight/judge verdicts"
            """);

    /// <summary>
    /// A report step: installs the engine the base pins and runs <c>lockflight ci report</c> for <paramref name="statusContext"/>
    /// over the judge step's evidence, as data; the step fails with that context's verdict.
    /// </summary>
    public static string ReportJob(string findInputs, string statusContext) =>
        Job(findInputs, [DotnetChannels.EngineChannel], gitleaks: false, $"""
            lockflight ci report --evidence {EvidenceDirectory} --head "$HEAD_COMMIT" --context {statusContext} --out {VerdictDirectory}/{statusContext.Replace('/', '-')}
            """);

    private static string Job(string findInputs, IReadOnlyList<string> dotnetChannels, bool gitleaks, string run)
    {
        string extraChannels = InstallExtraChannels(dotnetChannels);
        string[] sections =
        [
            "set -e",
            "LOCKFLIGHT_TEMP=$(mktemp -d)",
            .. gitleaks ? ["GITLEAKS_VERSION=" + GitleaksVersion, "GITLEAKS_SHA256=" + GitleaksSha256] : Array.Empty<string>(),
            "export LOCKFLIGHT_TEMP",
            findInputs,
            "# Use the base branch's engine pin and policy.",
            "if [ -n \"$PULL_REQUEST\" ]; then",
            Indent(RestorePolicy, 2),
            "fi",
            .. gitleaks ? ["# Install gitleaks (pinned, checksum-verified).", InstallGitleaks, "export LOCKFLIGHT_GITLEAKS=\"$LOCKFLIGHT_TEMP/gitleaks\""] : Array.Empty<string>(),
            .. extraChannels.Length == 0 ? Array.Empty<string>() : ["# Install the .NET channels the test projects target.", extraChannels],
            "# Install the pinned engine (outside the repository's global.json).",
            InstallEngine,
            "export PATH=\"$LOCKFLIGHT_TEMP/lockflight:$PATH\"",
            run,
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
