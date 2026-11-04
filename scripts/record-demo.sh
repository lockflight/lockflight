#!/usr/bin/env bash
# Records docs/assets/demo-judge.gif: the CI judge failing a branch where the test and the code came in one "red" commit,
# then passing a branch with a real red commit. Real runs of the given engine, played back 2.5x faster.
#
# Usage: scripts/record-demo.sh [<lockflight executable>]   (default: lockflight on PATH)
# Needs: vhs (https://github.com/charmbracelet/vhs), git, the .NET 10 SDK, gitleaks.
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/.." && pwd)
engine=${1:-$(command -v lockflight || true)}
[ -x "$engine" ] || { echo "record-demo: no lockflight executable (pass its path)" >&2; exit 4; }
command -v vhs >/dev/null || { echo "record-demo: vhs is not installed" >&2; exit 4; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/discount-demo"
ln -s "$engine" "$work/bin/lockflight"
export PATH="$work/bin:$PATH" GIT_PAGER=cat DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
unset LOCKFLIGHT_PLUGIN_ROOT LOCKFLIGHT_PLUGIN_MIN_ENGINE

cd "$work/discount-demo"
git init -q -b main
git config user.name "Demo" && git config user.email "demo@example.com"
dotnet new sln -n Demo >/dev/null
dotnet new classlib -n Demo -o src/Demo >/dev/null
dotnet new xunit -n Demo.Tests -o tests/Demo.Tests >/dev/null
dotnet sln add src/Demo tests/Demo.Tests >/dev/null
dotnet add tests/Demo.Tests reference src/Demo >/dev/null
dotnet new gitignore >/dev/null
rm -f src/Demo/Class1.cs tests/Demo.Tests/UnitTest1.cs
git add -A && git commit -qm "A new solution"
lockflight init --ci-only >/dev/null
git add -A && git commit -qm "Set up Lockflight"

stub='namespace Demo;

public static class Pricing
{
    public static decimal Discount(decimal total) => throw new NotImplementedException();
}'
code='namespace Demo;

public static class Pricing
{
    public static decimal Discount(decimal total) => total > 100m ? total * 0.9m : total;
}'
git switch -qc feat/discount
printf '%s\n' "$stub" > src/Demo/Pricing.cs
cat > tests/Demo.Tests/PricingTests.cs <<'EOF'
namespace Demo.Tests;

public class PricingTests
{
    [Fact]
    public void TenPercentOffAboveOneHundred() => Assert.Equal(108m, Pricing.Discount(120m));
}
EOF
git add -A && git commit -qm "test: ten percent off above 100" --trailer "Lockflight-Red: 1"
printf '%s\n' "$code" > src/Demo/Pricing.cs
git commit -qam "feat: ten percent off above 100"

git switch -qc feat/shortcut main
git checkout feat/discount -- src tests
git add -A && git commit -qm "feat: discount, tests and code together" --trailer "Lockflight-Red: 1"

# A warm-up run, so the recording does not include the first restore.
lockflight ci >/dev/null 2>&1 || true

cat > "$work/judge.tape" <<EOF
Output "$work/demo-judge.gif"
Set Shell "bash"
Set FontSize 17
Set Width 1280
Set Height 470
Set Padding 28
Set WindowBar Colorful
Set Theme "Catppuccin Mocha"
Set TypingSpeed 120ms
Set PlaybackSpeed 2.5
Env PS1 "discount-demo \$ "
Hide
Type "cd '$work/discount-demo' && clear"
Enter
Sleep 1s
Show
Type \`# The agent committed the test and the code together, as "red"\`
Enter
Type "git log --oneline main.."
Enter
Sleep 3s
Type "lockflight ci | head -4"
Enter
Wait+Screen@90s /FAIL +red/
Sleep 10s
Type "clear"
Enter
Type "# Test-first: a failing test on a stub, then the code"
Enter
Type "git switch -q feat/discount && git log --oneline main.."
Enter
Sleep 3s
Type "lockflight ci | head -4"
Enter
Wait+Screen@90s /PASS +red/
Sleep 12s
EOF
vhs "$work/judge.tape"
cp "$work/demo-judge.gif" "$repo_root/docs/assets/demo-judge.gif"
echo "record-demo: wrote docs/assets/demo-judge.gif"
