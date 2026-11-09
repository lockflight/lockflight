#!/usr/bin/env bash
# Writes the released fixtures of one Lockflight version: the public contracts as that version's published engine produced them,
# for ReleasedContractTests to read with the current engine. Run it once per release, with that release's engine, and commit the
# folder it writes; never edit a fixture by hand.
#
#   generate.sh <version> <engine> [<out dir>]
#
#   <version>  a released version with a tag v<version> in this clone (0.3.0)
#   <engine>   that version's published engine, for example from `scripts/install.sh --version <version> --dir <dir>`
#   <out dir>  default: schemas/fixtures/released/<version>
#
# What it does, in a scratch directory: takes samples/dotnet-single as the tag has it, runs that engine's `init --profile
# standard` on it (manifest, engine lock, CLAUDE.md block, GitHub workflow), commits a feature on a branch the way the lifecycle
# does (a spec a person approves, a red commit with its Lockflight-Red trailer, the implementation), and judges the branch with
# `ci --platform github` and `ci report` for both status contexts. It copies the pointer package's engine-hashes.json from
# nuget.org, and the version's own valid schema fixtures for the review result (only a review job with an API key writes one) and the
# blocked report (written only when a person must act), and the version's JSON Schemas, action.yml and exit code table.
# Machine-specific paths in the copies are replaced with /work. Needs git, curl, unzip, python3 and a .NET SDK for the sample.
set -euo pipefail
VERSION="${1:?version, for example 0.3.0}"
ENGINE="$(cd "$(dirname "${2:?the published engine of that version}")" && pwd)/$(basename "$2")"
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
OUT="${3:-$ROOT/schemas/fixtures/released/$VERSION}"
FEED="${LOCKFLIGHT_NUGET_FEED:-https://api.nuget.org/v3-flatcontainer}"

[ "$("$ENGINE" version)" = "$VERSION" ] || { echo "the engine is not $VERSION" >&2; exit 4; }
git -C "$ROOT" rev-parse -q --verify "refs/tags/v$VERSION" > /dev/null || { echo "no tag v$VERSION in this clone" >&2; exit 4; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
REPO="$WORK/samples/dotnet-single"
# A person runs approve; the engine refuses it inside Claude Code. The NuGet cache stays shared, everything else is private.
unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}" HOME="$WORK/home" LOCKFLIGHT_TEMP="$WORK/temp"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
export GIT_AUTHOR_DATE=2026-11-01T10:00:00Z GIT_COMMITTER_DATE=2026-11-01T10:00:00Z
mkdir -p "$HOME" "$LOCKFLIGHT_TEMP"

git -C "$ROOT" archive "v$VERSION" samples/dotnet-single | tar -x -C "$WORK"
cd "$REPO"
rm -f tests/Shop.Tests/ContainerSmokeTests.cs # needs Docker
git init -q -b main
git config commit.gpgsign false
git add -A
git commit -qm "chore: sample"
"$ENGINE" init --profile standard > "$WORK/init.log" 2>&1 || { cat "$WORK/init.log" >&2; exit 1; }
git add -A
git commit -qm "chore: lockflight init"
main="$(git rev-parse HEAD)"

feature=001-shipping
calc=src/Shop.Api/PriceCalculator.cs
tests=tests/Shop.Tests/PriceCalculatorTests.cs
git checkout -q -b "$feature"
mkdir -p "specs/$feature"
cat > "specs/$feature/spec.md" <<'EOF'
# Feature Specification: Shipping

**Feature**: `001-shipping`
**Status**: Draft

## User Scenarios & Testing

### User Story 1 - Pay for shipping (Priority: P1)

A shopper sees the shipping cost of a parcel.

**Acceptance Scenarios**:

1. **Given** a parcel of 1 kg, **When** shipping is priced, **Then** it costs 5.00

## Requirements

### Functional Requirements

- **FR-001**: System MUST charge 5.00 for the first kilogram

## Success Criteria

- **SC-001**: Every scenario above is covered by a test that failed before the code existed.
EOF
"$ENGINE" approve spec --feature "$feature" > "$WORK/approve.log" 2>&1 || { cat "$WORK/approve.log" >&2; exit 1; }
git add -A
git commit -qm "docs($feature): spec"
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => throw new NotImplementedException();\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    [Trait("Requirement", "FR-001")]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A
git commit -qm "test($feature): red for US1" -m "Lockflight-Red: 1"
red="$(git rev-parse HEAD)"
perl -pi -e 's/=> throw new NotImplementedException\(\);/=> 5m;/' "$calc"
git commit -qam "feat($feature): US1"
head="$(git rev-parse HEAD)"

set +e
"$ENGINE" ci --platform github --head "$head" --base "$main" --branch "$feature" --evidence "$WORK/evidence" > "$WORK/ci.log" 2>&1
judged=$?
set -e
[ -f "$WORK/evidence/evidence.json" ] || { cat "$WORK/ci.log" >&2; echo "the judge wrote no evidence (exit $judged)" >&2; exit 1; }
for context in gates lockflight/judge; do
  name="${context//\//-}"
  set +e
  "$ENGINE" ci report --evidence "$WORK/evidence" --head "$head" --out "$WORK/report-$name" --context "$context" > "$WORK/report-$name.log" 2>&1
  set -e
  [ -f "$WORK/report-$name/verdict.json" ] || { cat "$WORK/report-$name.log" >&2; exit 1; }
done

rm -rf "$OUT"
mkdir -p "$OUT/ci" "$OUT/repository/.lockflight" "$OUT/repository/.github/workflows" "$OUT/approval"
cp lockflight.yaml CLAUDE.md "$OUT/repository/"
cp .lockflight/engine.lock "$OUT/repository/.lockflight/"
cp .github/workflows/lockflight-gates.yml "$OUT/repository/.github/workflows/"
cp ".lockflight/features/$feature/approvals/spec.json" "$OUT/approval/spec.json"
cp "specs/$feature/spec.md" "$OUT/approval/spec.md"
git log -1 --format=%B "$red" > "$OUT/red-commit.txt"
cp "$WORK/evidence/evidence.json" "$OUT/ci/evidence.json"
cp "$WORK/report-gates/verdict.json" "$OUT/ci/verdict-gates.json"
cp "$WORK/report-gates/comment.md" "$OUT/ci/comment-gates.md"
cp "$WORK/report-lockflight-judge/verdict.json" "$OUT/ci/verdict-judge.json"
cp "$WORK/report-lockflight-judge/comment.md" "$OUT/ci/comment-judge.md"
printf '%s\n' "$judged" > "$OUT/ci/exit-code.txt"
curl -fsSL -o "$WORK/pointer.nupkg" "$FEED/lockflight.cli/$VERSION/lockflight.cli.$VERSION.nupkg"
unzip -p "$WORK/pointer.nupkg" engine-hashes.json > "$OUT/engine-hashes.json"
for id in ci-review blocked; do
  mkdir -p "$OUT/$id"
  git -C "$ROOT" archive "v$VERSION" "schemas/fixtures/lockflight.$id.v1/valid" | tar -x -C "$WORK"
  cp "$WORK/schemas/fixtures/lockflight.$id.v1/valid/"*.json "$OUT/$id/"
done
# The version's own contract files: its JSON Schemas, its action's inputs and outputs, and its exit codes as the commands guide
# lists them, for the checks that a later version only adds.
mkdir -p "$OUT/schemas"
git -C "$ROOT" archive "v$VERSION" action/action.yml docs/guides/commands.md schemas | tar -x -C "$WORK"
cp "$WORK"/schemas/*.json "$OUT/schemas/"
cp "$WORK/action/action.yml" "$OUT/action.yml"
awk '/^## Exit codes/ { on = 1; next } on && /^## / { exit } on && /^\| [0-9]/' "$WORK/docs/guides/commands.md" > "$OUT/exit-codes.md"

# The scratch directory's path is the same /work in every fixture, on every machine.
python3 -I - "$OUT" "$WORK" "$(cd "$WORK" && pwd -P)" <<'EOF'
import pathlib, sys
out, *paths = sys.argv[1:]
for file in pathlib.Path(out).rglob("*"):
    if file.is_file():
        text = file.read_text(encoding="utf-8")
        for path in sorted(set(paths), key=len, reverse=True):
            text = text.replace(path, "/work")
        file.write_text(text, encoding="utf-8")
EOF
echo "wrote the $VERSION fixtures to $OUT (judge exit $judged)"
