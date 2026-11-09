# shellcheck shell=bash
# Shared helpers of the red-team suites (sourced, not run). Each case plants one attack, runs the engine on it, checks the
# outcome, and checks that the guides say the same thing, so a guide that drifts from the behaviour fails here too.
# The scripts that source it set ROOT (the Lockflight checkout) first.
PY="$(command -v python3 || command -v python)"
failures=0

case_start() { # case_start <what the attack does>
  printf '\n== %s\n' "$1"
}
ok() { echo "ok   $*"; }
bad() { echo "FAIL $*"; failures=$((failures + 1)); }

# check <evidence file> <check id> <verdict>
check() {
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c["verdict"] for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$1" "$2")
  if [ "$got" = "$3" ]; then ok "$2 is $3"
  else
    bad "$2 is $got, want $3"
    "$PY" -c 'import json,sys; [print("     ", c["id"], c["verdict"], c.get("why", "")) for c in json.load(open(sys.argv[1]))["checks"]]' "$1"
  fi
}

# check_reason <evidence file> <check id> <reason code>
check_reason() {
  local got
  got=$("$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(next((c.get("reason") or "none" for c in d["checks"] if c["id"]==sys.argv[2]), "none"))' "$1" "$2")
  if [ "$got" = "$3" ]; then ok "$2 names $3"; else bad "$2 names $got, want $3"; fi
}

# expect_exit <code> <what> -- command…
expect_exit() {
  local want="$1" what="$2"; shift 3
  local out got
  out="$("$@" 2>&1)"; got=$?
  if [ "$got" = "$want" ]; then ok "$what"; else bad "$what (exit $got, want $want)"; echo "$out" | sed 's/^/     /'; fi
}

# contains <file> <text> <what>; lacks <file> <text> <what>
contains() {
  if grep -qF -- "$2" "$1"; then ok "$3"; else bad "$3: '$2' not in $1"; fi
}
lacks() {
  if grep -qF -- "$2" "$1"; then bad "$3: '$2' is in $1"; else ok "$3"; fi
}

# guide_says <file under docs/guides> <sentence>: the guide states the behaviour the case checks, compared with line breaks and
# runs of spaces read as one space (the guides wrap their lines).
guide_says() {
  if "$PY" -I -c 'import re,sys
text = re.sub(r"\s+", " ", open(sys.argv[1], encoding="utf-8").read())
sys.exit(0 if re.sub(r"\s+", " ", sys.argv[2]) in text else 1)' "$ROOT/docs/guides/$1" "$2"
  then ok "$1 says so"
  else bad "$1 no longer says: $2"; fi
}

# finish <suite name>: the exit code of the suite.
finish() {
  echo
  if [ "$failures" -gt 0 ]; then echo "$failures $1 checks failed"; exit 1; fi
  echo "$1: all checks passed"
}
