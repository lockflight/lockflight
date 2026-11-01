#!/usr/bin/env bash
# The QUICKSTART path on a copy of samples/dotnet-single, with the published engine: `init --ci-only` committed on main, a red
# commit (a new test and a stub), a green commit, and `lockflight ci` with no arguments gives PASS. Then "see it say no": the
# test and the implementation in one red commit gives FAIL. The time from init to the first PASS goes to the job summary.
# Usage: ENGINE=<lockflight binary> quickstart.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
# The container smoke test needs Docker, which this path does not assume.
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
failures=0
expect_exit() { # expect_exit <code> <what> -- command…
  local want="$1" what="$2"; shift 3
  local out; out="$("$@" 2>&1)"; local got=$?
  if [ "$got" = "$want" ]; then echo "ok   $what"; else echo "FAIL $what (exit $got, want $want)"; failures=$((failures + 1)); fi
  echo "$out" | sed 's/^/     /'
}
cd "$W/repo" || exit 1
git init -q -b main; git config commit.gpgsign false
printf 'bin/\nobj/\n' > .gitignore
git add -A; git commit -qm "A new solution"
started=$(date +%s)

expect_exit 0 "init --ci-only" -- "$E" init --ci-only
git add -A; git commit -qm "Set up Lockflight"

git switch -q -c feat/discount
cat > src/Shop.Api/Discount.cs <<'EOF'
namespace Shop.Api;

public static class Discount
{
    public static decimal Apply(decimal total) => throw new NotImplementedException();
}
EOF
cat > tests/Shop.Tests/DiscountTests.cs <<'EOF'
using Shop.Api;

namespace Shop.Tests;

public sealed class DiscountTests
{
    [Fact]
    public void TenPercentOffAboveOneHundred() => Assert.Equal(108m, Discount.Apply(120m));
}
EOF
git add -A; git commit -qm "test: ten percent off above 100" --trailer "Lockflight-Red: 1"
cat > src/Shop.Api/Discount.cs <<'EOF'
namespace Shop.Api;

public static class Discount
{
    public static decimal Apply(decimal total) => total > 100m ? total * 0.9m : total;
}
EOF
git commit -qam "feat: ten percent off above 100"
before=$failures
expect_exit 0 "lockflight ci gives PASS on the red and green commits" -- "$E" ci
if [ "$failures" = "$before" ]; then
  first_green="**$(( $(date +%s) - started ))s**"
else
  first_green="no green: see the log"
fi
echo "time to first green: ${first_green} (init, a red commit, a green commit, lockflight ci)"

git switch -q -c feat/fake main
git checkout -q feat/discount -- src tests
git add src tests; git commit -qm "test and code together" --trailer "Lockflight-Red: 1"
before=$failures
expect_exit 1 "lockflight ci gives FAIL when the red commit holds the implementation" -- "$E" ci
said_no=$([ "$failures" = "$before" ] && echo "FAIL, as expected" || echo "not FAIL: see the log")

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "### QUICKSTART on samples/dotnet-single"
    echo "Time to first green: ${first_green} (init --ci-only, a red commit, a green commit, \`lockflight ci\` PASS)."
    echo "See it say no (test and code in one red commit): ${said_no}."
  } >> "$GITHUB_STEP_SUMMARY"
fi
if [ "$failures" -gt 0 ]; then echo "$failures checks failed"; exit 1; fi
echo "quickstart: all checks passed"
