using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class GuardPatternsTests
{
    [Fact]
    public void ExactFileNamesInCaseArmsAreGuardedButToolNamesAreNot()
    {
        const string script = """
            case "$TOOL" in
              Edit|Write|MultiEdit) ;;
              *) exit 0 ;;
            esac
            case "$(basename "$FILE")" in
              ".env")
                block "env-file" "local only (secrets)." ;;
              "appsettings.Local.json")
                block "local-config" "local only." ;;
            esac
            """;

        Assert.Equal(["**/.env", "**/appsettings.Local.json"], GuardPatterns.Extract(script));
    }

    [Fact]
    public void AnEqualityTestOnThePathIsGuarded()
    {
        const string script = """
            [[ "$TOOL" != "Write" ]] && exit 0
            if [[ "$FILE" == *"docker-compose.override.yml" ]]; then
              exit 2
            fi
            """;

        Assert.Equal(["**/docker-compose.override.yml"], GuardPatterns.Extract(script));
    }

    [Fact]
    public void AnAllowListOfASecretsScannerIsNotAGuardedPath()
    {
        const string script = """
            case "$FILE" in
              *"docker-compose.override.yml") exit 0 ;;
              *"/.env"|*"/.env."*)             exit 0 ;;
              *"appsettings.Development.json") exit 0 ;;
            esac
            [[ "$FILE" == *".example" ]] && exit 0
            echo "$CONTENT" | grep -qE 'sk-ant-' && exit 2
            """;

        Assert.Empty(GuardPatterns.Extract(script));
    }

    [Fact]
    public void WildcardArmsKeepWorking()
    {
        Assert.Equal(["**/docker-compose.override.yml", "**/Contracts/**"], GuardPatterns.Extract("""
            case "$input" in
              *docker-compose.override.yml*|*"/Contracts/"*)
                exit 2 ;;
            esac
            """));
    }
}
