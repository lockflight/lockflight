#!/usr/bin/env bash
# The CI judge's artifact digests, for the action's steps (bash, Git Bash on Windows).
#
#   artifacts.sh digest <dir>
#     Prints the SHA-256 digest of the files an artifact upload of <dir> holds: one line "<sha256>  <path>" per regular file
#     (paths relative to <dir>, sorted bytewise, hidden files left out as actions/upload-artifact leaves them out), hashed as a
#     whole. A missing or empty directory has the digest of nothing.
#
#   artifacts.sh verify <evidence dir> <evidence sha256> <review dir> <review sha256> <head> <artifact suffix>
#     The report's check of the artifacts it downloaded against the digests the judge and review jobs output after their upload
#     steps. An artifact whose job named no digest is not read (its folder is removed). One whose files hash to anything else, or
#     that is gone although its job named files, is refused: both folders are replaced by a short evidence (refused.json,
#     lockflight.ci-evidence/v1 with judge "refused" and the reason ARTIFACT_MISMATCH), FAIL when the files differ and ERROR
#     when they are missing, which the report shows instead of the artifacts.
set -euo pipefail

sum() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi
}

files() {
  [ -d "$1" ] || return 0
  (cd "$1" && find . -type f ! -path '*/.*' -print | LC_ALL=C sort)
}

digest() {
  local file hash
  files "$1" | while IFS= read -r file; do
    hash=$(sum < "$1/$file")
    printf '%s  %s\n' "${hash%% *}" "${file#./}"
  done | sum | cut -d ' ' -f 1
}

empty_digest() {
  printf '' | sum | cut -d ' ' -f 1
}

# The problem with one artifact, as "<kind> <what is wrong>": kind "differs" for files other than the job's, "missing" for none
# where the job named some. Nothing when the artifact is the job's, or when its job named no digest (the folder is then removed).
check() {
  local name="$1" dir="$2" expected="$3" actual
  if [ -z "$expected" ]; then
    rm -rf "$dir"
    return 0
  fi
  if [ -z "$(files "$dir")" ]; then
    if [ "$expected" != "$(empty_digest)" ]; then
      printf 'missing %s: the job that uploads it named its files (sha256 %s) and the report found none; it was deleted or replaced after the upload, or the upload failed' \
        "$name" "${expected:0:12}"
    fi
    return 0
  fi
  actual=$(digest "$dir")
  if [ "$actual" != "$expected" ]; then
    printf 'differs %s: its files hash to sha256 %s, not to the %s its job named after the upload; another step wrote it' \
      "$name" "${actual:0:12}" "${expected:0:12}"
  fi
}

verify() {
  local evidence="$1" evidence_expected="$2" review="$3" review_expected="$4" head="$5" suffix="$6"
  local problem why="" verdict=ERROR
  for problem in "$(check "lockflight-evidence$suffix" "$evidence" "$evidence_expected")" \
    "$(check "lockflight-review$suffix" "$review" "$review_expected")"; do
    [ -n "$problem" ] || continue
    if [ "${problem%% *}" = differs ]; then verdict=FAIL; fi
    why="${why:+$why; }${problem#* }"
  done
  [ -n "$why" ] || return 0
  echo "::error::the report refused an artifact: $why"
  rm -rf "$evidence" "$review"
  mkdir -p "$evidence"
  check_json() {
    printf '{ "id": "%s", "context": "%s", "verdict": "%s", "requirement": "required", "reason": "ARTIFACT_MISMATCH", "why": "%s" }' \
      "$1" "$2" "$verdict" "$why"
  }
  printf '{ "schema": "lockflight.ci-evidence/v1", "head": "%s", "judge": "refused", "checks": [ %s, %s ], "contexts": { "gates": "%s", "lockflight/judge": "%s" }, "verdict": "%s" }\n' \
    "$head" "$(check_json green gates)" "$(check_json red lockflight/judge)" "$verdict" "$verdict" "$verdict" > "$evidence/refused.json"
}

case "${1:-}" in
  digest) [ "$#" -eq 2 ] || { echo "usage: artifacts.sh digest <dir>" >&2; exit 4; }; digest "$2" ;;
  verify) [ "$#" -eq 7 ] || { echo "usage: artifacts.sh verify <evidence dir> <evidence sha256> <review dir> <review sha256> <head> <suffix>" >&2; exit 4; }
    shift; verify "$@" ;;
  *) echo "usage: artifacts.sh digest <dir> | verify <evidence dir> <evidence sha256> <review dir> <review sha256> <head> <suffix>" >&2; exit 4 ;;
esac
