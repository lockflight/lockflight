#!/usr/bin/env bash
# A fixture for the CI judge's action (action/) on a runner: a copy of samples/dotnet-single with a pull request history, as
# tests/e2e/ci-judge.sh builds it, whose main pins the engine the way `lockflight init` does: .lockflight/engine.lock and the
# vendored .lockflight/ci/install.sh (this checkout's scripts/install.sh, by hash). The engine under test is served from a
# file:// feed as its platform's package, so the action installs it through install.sh and checks its hash, as it would a
# release. Branch 001-shipping (a red commit, then the implementation) must PASS; branch 002-cheat (the test and the code in
# one red commit) must FAIL.
# Usage: ENGINE=<lockflight binary> action-fixture.sh <repository dir> <feed dir>
# The repository directory may hold other files (the Lockflight checkout); they are excluded, never committed. Prints
# base=<sha>, pass=<sha>, fail=<sha> and feed=<url> lines (for GITHUB_OUTPUT).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
repo="${1:?repository dir}" feed="${2:?feed dir}"
PY="$(command -v python3 || command -v python)"
# From stdin: GNU sha256sum starts its line with a backslash when the file name holds one (a Windows path).
sha256() { if command -v sha256sum >/dev/null; then sha256sum < "$1" | cut -d ' ' -f 1; else shasum -a 256 < "$1" | cut -d ' ' -f 1; fi; }

case "$(uname -s)/$(uname -m)" in
  Darwin/arm64) rid=osx-arm64 exe="" ;;
  Darwin/x86_64) rid=osx-x64 exe="" ;;
  Linux/x86_64) rid=linux-x64 exe="" ;;
  Linux/aarch64) rid=linux-arm64 exe="" ;;
  MINGW*/x86_64|MSYS*/x86_64|CYGWIN*/x86_64) rid=win-x64 exe=.exe ;;
  *) echo "no engine platform for $(uname -s) $(uname -m)" >&2; exit 1 ;;
esac
version=$("$E" version | tr -d '\r')
lower=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')

# The feed: the platform's package with the engine at tools/any/<rid>/lockflight[.exe].
mkdir -p "$feed/lockflight.cli.$rid/$lower"
"$PY" -I -c 'import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED) as archive: archive.write(sys.argv[2], sys.argv[3])' \
  "$feed/lockflight.cli.$rid/$lower/lockflight.cli.$rid.$lower.nupkg" "$E" "tools/any/$rid/lockflight$exe"
if [ "$rid" = win-x64 ]; then url="file:///$(cygpath -m "$(cd "$feed" && pwd)")/"; else url="file://$(cd "$feed" && pwd)/"; fi

mkdir -p "$repo"
cd "$repo"
[ ! -e .git ] || { echo "$repo is already a repository" >&2; exit 1; }
cp -R "$ROOT/samples/dotnet-single/." .
rm -rf ./*/*/bin ./*/*/obj tests/Shop.Tests/ContainerSmokeTests.cs
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main
git config commit.gpgsign false
git config core.autocrlf false
# Whatever else the directory holds stays out of the fixture's history.
for entry in * .[!.]*; do
  [ -e "$entry" ] || continue
  case "$entry" in .git|src|tests|Shop.slnx|global.json|Directory.Build.props) ;; *) printf '/%s\n' "$entry" >> .git/info/exclude ;; esac
done
printf '/artifacts/\n' >> .git/info/exclude
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
printf '.lockflight/ci/install.sh text eol=lf\n' > .gitattributes
mkdir -p .lockflight/ci
cp "$ROOT/scripts/install.sh" .lockflight/ci/install.sh
zeros=$(printf '0%.0s' $(seq 64))
{
  printf '{\n  "schema": "lockflight.engine-lock/v1",\n  "package": "Lockflight.Cli",\n  "version": "%s",\n  "channel": "release",\n' "$version"
  printf '  "installScript": { "sha256": "%s" },\n  "binaries": {\n' "$(sha256 .lockflight/ci/install.sh)"
  separator=""
  for platform in linux-arm64 linux-x64 osx-arm64 osx-x64 win-x64; do
    if [ "$platform" = "$rid" ]; then hash=$(sha256 "$E"); else hash=$zeros; fi
    printf '%s    "%s": { "sha256": "%s" }' "$separator" "$platform" "$hash"
    separator=$',\n'
  done
  printf '\n  }\n}\n'
} > .lockflight/engine.lock
git add -A
git commit -qm base
main=$(git rev-parse HEAD)

calc=src/Shop.Api/PriceCalculator.cs tests=tests/Shop.Tests/PriceCalculatorTests.cs
git checkout -q -b 001-shipping
mkdir -p specs/001-shipping
printf '# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n- **FR-001**: System MUST charge 5.00 for the first kilogram\n' > specs/001-shipping/spec.md
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A
git commit -qm "test(001-shipping): red for US1" -m "Lockflight-Red: 1"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> 5m;/' "$calc"
git commit -qam "feat(001-shipping): US1"
pass=$(git rev-parse HEAD)

git checkout -q -b 002-cheat "$main"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A
git commit -qm "test: red" -m "Lockflight-Red: 1"
fail=$(git rev-parse HEAD)
git checkout -q main

printf 'base=%s\npass=%s\nfail=%s\nfeed=%s\n' "$main" "$pass" "$fail" "$url"
