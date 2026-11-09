#!/usr/bin/env bash
# Red-team cases against what the CI judge's other parts read, with nothing built: the report's evidence file, the CI action's
# own steps (run from action/action.yml, with the engine under test or a stand-in), the engine lock and the package feed it is
# checked against. Each case plants one attack, checks the outcome, and checks that the guide states the same behaviour.
#   - evidence too large, nested too deep, or naming a property twice: the report refuses it (ERROR), naming the limit;
#   - the judge's evidence replaced after the action took its digest, and a review from a job that named none: the report's
#     artifact check refuses the first (ARTIFACT_MISMATCH, FAIL) and never reads the second;
#   - a pull request that commits artifacts/lockflight/ (a result of its own, a symlink to it): the action uploads the
#     engine's result as the gates' result, never the pull request's;
#   - a first engine lock with no token to check it, and a pull request that removes the lock: the action refuses both;
#   - a pull request that changes the vendored install script and not the lock: CI installs with the target branch's script;
#   - a lock that moves the engine down to an older, genuine release: verify-lock passes it and names the move down;
#   - an engine package on the feed replaced with another executable: install.sh refuses it, and verify-lock refuses a lock
#     rewritten to the replacement's hash.
# Usage: ENGINE=<lockflight binary> inputs.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
# shellcheck source=tests/redteam/lib.sh
. "$ROOT/tests/redteam/lib.sh"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W"
W="$(cd "$W" && pwd)"
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
windows=false
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) windows=true ;; esac
file_url() { if $windows; then printf 'file:///%s/' "$(cygpath -m "$1")"; else printf 'file://%s/' "$1"; fi; }

# run_step <step name> <log> [VAR=value…]: one step of the action, as GitHub runs a bash step, in the current directory.
run_step() {
  local name="$1" log="$2"; shift 2
  "$PY" -I "$ROOT/tests/redteam/action-step.py" "$ROOT/action/action.yml" "$name" > "$W/step.sh" || return 99
  : > "$W/github-env"; : > "$W/github-output"
  env RUNNER_TEMP="$W/runner-temp" GITHUB_ENV="$W/github-env" GITHUB_OUTPUT="$W/github-output" GITHUB_ACTION_PATH="$ROOT/action" \
    "$@" bash --noprofile --norc -eo pipefail "$W/step.sh" > "$log" 2>&1
}

# ---------------------------------------------------------------------------------------------------------------------------
case_start "evidence too large, nested too deep, or naming a property twice"
head=$(printf 'a%.0s' $(seq 40))
mkdir -p "$W/huge" "$W/deep" "$W/twice"
"$PY" -I -c 'import sys
open(sys.argv[1], "w").write("{\"schema\": \"lockflight.ci-evidence/v1\", \"pad\": \"" + "x" * (9 * 1024 * 1024) + "\"}")
open(sys.argv[2], "w").write("{\"schema\": \"lockflight.ci-evidence/v1\", \"deep\": " + "[" * 40 + "]" * 40 + "}")
open(sys.argv[3], "w").write("{\"schema\": \"lockflight.ci-evidence/v1\", \"verdict\": \"FAIL\", \"verdict\": \"PASS\"}")' \
  "$W/huge/evidence.json" "$W/deep/evidence.json" "$W/twice/evidence.json"
for kind in huge deep twice; do
  expect_exit 3 "the report refuses the $kind evidence (ERROR)" -- "$E" ci report --evidence "$W/$kind" --head "$head" --out "$W/verdict-$kind"
done
contains "$W/verdict-huge/verdict.json" "more than the 8388608" "the refusal names the size limit"
contains "$W/verdict-deep/verdict.json" "deeper than 32 levels" "the refusal names the depth limit"
contains "$W/verdict-twice/verdict.json" "appears twice in one object" "the refusal names the repeated property"
guide_says commands.md "one over 8 MiB is refused before it is parsed, as is one nested deeper than 32 levels, one that names a property twice in an object, or one that is not a JSON object. Refused evidence is ERROR"

# ---------------------------------------------------------------------------------------------------------------------------
case_start "a pull request commits artifacts/lockflight/: a result of its own, and a symlink to it"
mkdir -p "$W/planted/artifacts/lockflight" "$W/standin"
printf '{ "schema": "lockflight.result/v1", "verdict": "PASS", "planted": true }\n' > "$W/planted/artifacts/lockflight/result.json"
cp "$W/planted/artifacts/lockflight/result.json" "$W/planted/artifacts/lockflight/gates-result.json"
# The engine's stand-in writes a failing gates result where the judge writes its evidence, and exits 1 as the judge would.
cat > "$W/standin/lockflight" <<'STANDIN'
#!/usr/bin/env bash
while [ $# -gt 0 ]; do case "$1" in --evidence) evidence="$2"; shift ;; esac; shift; done
mkdir -p "$evidence"
printf '{ "schema": "lockflight.result/v1", "verdict": "FAIL", "engine": true }\n' > "$evidence/gates-result.json"
[ -z "${PLANT_LINK:-}" ] || { rm -f "$evidence/gates-result.json"; ln -s "$PLANT_LINK" "$evidence/gates-result.json"; }
exit 1
STANDIN
chmod +x "$W/standin/lockflight"
cd "$W/planted" || exit 1
run_step "Judge the pull request" "$W/judge-step.log" LF_LOCKFLIGHT="$W/standin/lockflight" LF_HEAD="$head" LF_BASE="" LF_BRANCH="" \
  LF_EVENT=pr LF_REVIEW=false LF_SHELL_CLONE=false
contains "$W/github-output" "verdict=FAIL" "the step reports the engine's verdict"
contains "$W/runner-temp/lockflight-result/result.json" '"engine": true' "the uploaded gates' result is the engine's"
lacks "$W/runner-temp/lockflight-result/result.json" planted "the pull request's committed result is not uploaded"
# The evidence's gates result replaced by a symlink to the planted file (as code the build left running could do): not copied.
ln -s "$W/planted/artifacts/lockflight/result.json" "$W/link-test" 2>/dev/null
if [ -L "$W/link-test" ]; then
  run_step "Judge the pull request" "$W/judge-step-link.log" LF_LOCKFLIGHT="$W/standin/lockflight" LF_HEAD="$head" LF_BASE="" \
    LF_BRANCH="" LF_EVENT=pr LF_REVIEW=false LF_SHELL_CLONE=false PLANT_LINK="$W/planted/artifacts/lockflight/result.json"
  if [ -e "$W/runner-temp/lockflight-result/result.json" ]; then bad "a symlinked gates result was uploaded"
  else ok "a gates result that is a symlink is not uploaded"; fi
else
  echo "skip a symlinked gates result: this platform made no symlink"
fi
guide_says ci-hardening.md "copied from the engine's evidence outside the checkout, so a pull request that commits \`artifacts/lockflight/\` cannot replace it"

# ---------------------------------------------------------------------------------------------------------------------------
case_start "the judge's evidence replaced after its digest was taken, and a review uploaded by a job that ran no review"
mkdir -p "$W/digest/evidence" "$W/digest/review"
printf '{ "schema": "lockflight.ci-evidence/v1", "head": "%s", "verdict": "FAIL" }\n' "$head" > "$W/digest/evidence/evidence.json"
sum=$(bash "$ROOT/action/artifacts.sh" digest "$W/digest/evidence")
printf '{ "schema": "lockflight.ci-evidence/v1", "head": "%s", "verdict": "PASS" }\n' "$head" > "$W/digest/evidence/evidence.json"
printf '{ "schema": "lockflight.ci-review/v1", "verdict": "PASS" }\n' > "$W/digest/review/review-result.json"
bash "$ROOT/action/artifacts.sh" verify "$W/digest/evidence" "$sum" "$W/digest/review" "" "$head" "" > "$W/digest.log" 2>&1
contains "$W/digest.log" "the report refused an artifact" "the replaced evidence is refused"
contains "$W/digest/evidence/refused.json" '"reason": "ARTIFACT_MISMATCH"' "the refusal names ARTIFACT_MISMATCH"
if [ -e "$W/digest/review" ]; then bad "a review whose job named no digest is still there to read"; else ok "a review whose job named no digest is not read"; fi
expect_exit 1 "the report of the refused evidence is FAIL" -- "$E" ci report --evidence "$W/digest/evidence" --head "$head" --out "$W/verdict-digest"
guide_says ci-hardening.md "refuses one whose files hash to anything else (FAIL)"
guide_says ci-hardening.md "so a review the judge job's code uploaded under the review's name is never joined"

# ---------------------------------------------------------------------------------------------------------------------------
# The fixture of the action's own CI job: main pins the engine under test by lock and vendored install script, served from a
# file:// feed; main is the pull request that adds the first lock to the commit before it.
ENGINE="$E" bash "$ROOT/tests/e2e/action-fixture.sh" "$W/fixture" "$W/feed" > "$W/fixture.txt" || { bad "the action fixture"; finish "Red-team inputs"; }
value() { sed -n "s/^$1=//p" "$W/fixture.txt"; }
base=$(value base) prelock=$(value prelock) feed=$(value feed)
cd "$W/fixture" || exit 1

case_start "a first engine lock, with no token to check it against its release"
lock_step="A pull request that changes the engine lock must name a published release"
rm -rf "$W/runner-temp"
run_step "$lock_step" "$W/first-lock.log" LF_BASE="$prelock" LF_HEAD="$base" GH_TOKEN=
code=$?
if [ "$code" = 1 ]; then ok "the step fails"; else bad "the step exits $code, want 1"; sed 's/^/     /' "$W/first-lock.log"; fi
contains "$W/first-lock.log" "engine lock: FAIL unattested" "it names the unchecked lock"
contains "$W/runner-temp/lockflight-evidence/refused.json" '"judge": "refused"' "it leaves a refusal for the report"
guide_says ci-hardening.md "Without a token nothing can be checked, so the step fails with \`engine lock: FAIL unattested\`"

case_start "a pull request that removes the engine lock"
git checkout -q -b remove-lock "$base"
git rm -q .lockflight/engine.lock; git commit -qm "chore: no lock"
rm -rf "$W/runner-temp"
run_step "$lock_step" "$W/lock-removed.log" LF_BASE="$base" LF_HEAD="$(git rev-parse HEAD)" GH_TOKEN=
code=$?
if [ "$code" = 1 ]; then ok "the step fails"; else bad "the step exits $code, want 1"; sed 's/^/     /' "$W/lock-removed.log"; fi
contains "$W/lock-removed.log" "engine lock: FAIL lock-removed" "it names the removed lock"
guide_says ci-hardening.md "a pull request that removes the lock: \`lock-removed\`), the judge does not run"

case_start "a pull request changes the vendored install script, not the lock"
git checkout -q -b own-script "$base"
marker="$W/pull-request-script-ran"
printf 'touch "%s"\n' "$marker" | cat - .lockflight/ci/install.sh > "$W/install.sh" && cp "$W/install.sh" .lockflight/ci/install.sh
git commit -qam "chore: a faster install"
pr=$(git rev-parse HEAD)
rm -rf "$W/runner-temp"
run_step "$lock_step" "$W/own-script-lock.log" LF_BASE="$base" LF_HEAD="$pr" GH_TOKEN=
code=$?
if [ "$code" = 0 ]; then ok "an unchanged lock needs no release check"; else bad "the lock step exits $code"; sed 's/^/     /' "$W/own-script-lock.log"; fi
run_step "Install the engine the target branch pins (outside the repository's global.json)" "$W/own-script.log" \
  LF_BASE="$base" LF_HEAD="$pr" LF_ENGINE= LF_EVENT=pr LOCKFLIGHT_NUGET_FEED="$feed"
code=$?
if [ "$code" = 0 ]; then ok "the engine installs"; else bad "the install exits $code"; sed 's/^/     /' "$W/own-script.log"; fi
if [ -e "$marker" ]; then bad "the pull request's install script ran"; else ok "the pull request's install script did not run"; fi
contains "$W/github-env" "LF_LOCKFLIGHT=$W/runner-temp/lockflight/lockflight" "the engine is the one the target branch's lock pins"
guide_says ci-hardening.md "The engine is the one the target branch pins, read from that commit with \`git show\` (never from the pull request's tree)"

# ---------------------------------------------------------------------------------------------------------------------------
# A feed of real releases' engine-hashes.json (the pointer packages), and locks made from them.
mkdir -p "$W/releases"
"$PY" -I -c 'import json, sys, zipfile
out, released = sys.argv[1], sys.argv[2]
for version in ("0.3.0", "0.3.1"):
    path = f"{released}/{version}/engine-hashes.json"
    with zipfile.ZipFile(f"{out}/lockflight.cli.{version}.nupkg", "w") as package:
        package.write(path, "engine-hashes.json")
    hashes = json.load(open(path))
    lock = {"schema": "lockflight.engine-lock/v1", "package": "Lockflight.Cli", "version": version, "channel": "release",
            "installScript": {"sha256": hashes["installScript"]["sha256"]},
            "binaries": {rid: {"sha256": entry["sha256"]} for rid, entry in hashes["binaries"].items()}}
    json.dump(lock, open(f"{out}/{version}.lock", "w"), indent=2)' "$W/releases" "$ROOT/schemas/fixtures/released"

case_start "a lock that moves the engine down to an older, genuine release"
"$E" engine verify-lock "$W/releases/0.3.0.lock" --base "$W/releases/0.3.1.lock" --source "$W/releases" > "$W/downgrade.log" 2>&1
code=$?
if [ "$code" = 0 ]; then ok "the older release's lock matches its published hashes"; else bad "verify-lock exits $code"; sed 's/^/     /' "$W/downgrade.log"; fi
contains "$W/downgrade.log" "moves the engine down from 0.3.1 to 0.3.0" "the move down is named"
guide_says ci-hardening.md "A lock that moves the engine to an older version is named in the verdict (\"moves the engine down\"), so a downgrade is accepted only on purpose."

case_start "an engine package on the feed replaced with another executable"
rid=linux-x64
mkdir -p "$W/tampered/lockflight.cli.$rid/0.3.1" "$W/installed"
printf '#!/bin/sh\necho forged\n' > "$W/forged"
"$PY" -I -c 'import sys, zipfile
with zipfile.ZipFile(sys.argv[1], "w") as package: package.write(sys.argv[2], sys.argv[3])' \
  "$W/tampered/lockflight.cli.$rid/0.3.1/lockflight.cli.$rid.0.3.1.nupkg" "$W/forged" "tools/any/$rid/lockflight"
LOCKFLIGHT_NUGET_FEED="$(file_url "$W/tampered")" sh "$ROOT/scripts/install.sh" --lock "$W/releases/0.3.1.lock" --rid "$rid" \
  --dir "$W/installed" > "$W/tampered-install.log" 2>&1
code=$?
if [ "$code" = 1 ]; then ok "install.sh refuses the package"; else bad "install.sh exits $code, want 1"; sed 's/^/     /' "$W/tampered-install.log"; fi
if [ -e "$W/installed/lockflight" ]; then bad "the replaced executable was left installed"; else ok "nothing is left installed"; fi
guide_says getting-started.md "compares its SHA-256 with the hash the release published, before anything runs it; a mismatch deletes the file and fails."
# The lock rewritten to the replacement's hash, as a pull request would have to: the release's published hashes refuse it.
forged=$( (sha256sum < "$W/forged" 2>/dev/null || shasum -a 256 < "$W/forged") | cut -d' ' -f1)
"$PY" -I -c 'import json, sys
lock = json.load(open(sys.argv[1])); lock["binaries"][sys.argv[3]]["sha256"] = sys.argv[4]
json.dump(lock, open(sys.argv[2], "w"), indent=2)' "$W/releases/0.3.1.lock" "$W/forged.lock" "$rid" "$forged"
"$E" engine verify-lock "$W/forged.lock" --source "$W/releases" > "$W/forged-lock.log" 2>&1
code=$?
if [ "$code" = 1 ]; then ok "verify-lock refuses the rewritten lock"; else bad "verify-lock exits $code, want 1"; sed 's/^/     /' "$W/forged-lock.log"; fi
contains "$W/forged-lock.log" "engine lock: FAIL hash-mismatch" "it names the hash that differs"
guide_says ci-hardening.md "it fails unless every platform's hash and the install script's hash are the ones the release of that version published"

finish "Red-team inputs"
