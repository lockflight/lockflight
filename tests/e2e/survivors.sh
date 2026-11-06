#!/usr/bin/env bash
# Processes a pull request's code leaves running cannot forge the CI judge's verdict. On a copy of samples/dotnet-single, a pull
# request whose red commit already holds the implementation (the judge FAILs it) also carries a fixture (tests/e2e/survivors/):
# when armed, its test project's build leaves daemons running that wait for the judge to write a test report, the gates' result
# or the evidence, and rewrite each to a pass. The daemons are started the ways a build can start them: from an MSBuild <Exec>
# (setsid and nohup on Linux, Start-Process on Windows), from a source generator inside the compiler, on Linux one that
# double-forks into a session of its own and ignores SIGTERM, and a Docker container (Linux, when Docker answers). The same
# commit is judged unarmed, then armed: the armed run must give the same verdict and check verdicts, every daemon must have
# started and be listed as stopped in the evidence, none may have forged anything, none may be left running, and no container
# may be left. On Linux with passwordless sudo, a daemon started as root cannot be stopped: green and gates are ERROR with
# SURVIVOR_NOT_STOPPED. A first check runs a daemon outside the judge to show it does forge what it finds.
# Usage: ENGINE=<lockflight binary> survivors.sh <workdir>
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
E="${ENGINE:?path to the lockflight binary}"
W="$1"; rm -rf "${W:?}"; mkdir -p "$W/repo" "$W/temp"
cp -R "$ROOT/samples/dotnet-single/." "$W/repo/"; rm -rf "$W"/repo/*/*/bin "$W"/repo/*/*/obj
rm -f "$W/repo/tests/Shop.Tests/ContainerSmokeTests.cs"
export LOCKFLIGHT_TEMP="$W/temp"
failures=0
PY="$(command -v python3 || command -v python)"
windows=false; case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) windows=true ;; esac
fail() { echo "FAIL $*"; failures=$((failures + 1)); }
ok() { echo "ok   $*"; }
native() { if $windows; then cygpath -w "$1"; else printf '%s' "$1"; fi; }
alive() { # alive <pid>: whether the process runs
  if $windows; then tasklist //FI "PID eq $1" //NH 2>/dev/null | grep -q " $1 "; else kill -0 "$1" 2>/dev/null; fi
}

# The fixture forges what it finds when nothing stops it.
mkdir -p "$W/selftest/markers" "$W/selftest/evidence"
(LOCKFLIGHT_FIXTURE_MARKERS="$(native "$W/selftest/markers")" LOCKFLIGHT_FIXTURE_EVIDENCE="$(native "$W/selftest/evidence")" \
  "$PY" "$ROOT/tests/e2e/survivors/daemon.py" exec self > /dev/null 2>&1 &)
for _ in $(seq 50); do [ -s "$W/selftest/markers/started-exec-self" ] && break; sleep 0.1; done
printf '{"verdict": "FAIL", "checks": [{"id": "red", "verdict": "FAIL", "reason": "GREEN_BEFORE"}]}' > "$W/selftest/evidence/evidence.json"
for _ in $(seq 50); do [ -s "$W/selftest/markers/forged-exec-self" ] && break; sleep 0.1; done
if [ -s "$W/selftest/markers/forged-exec-self" ] && grep -q '"verdict": "PASS"' "$W/selftest/evidence/evidence.json"; then ok "the daemon forges the evidence when nothing stops it"
else fail "the daemon did not forge the evidence, so the fixture proves nothing"; fi
self=$(cat "$W/selftest/markers/started-exec-self" 2>/dev/null); [ -n "$self" ] && kill "$self" 2>/dev/null; [ -n "$self" ] && { $windows && taskkill //F //PID "$self" > /dev/null 2>&1; kill -9 "$self" 2>/dev/null; }

cd "$W/repo" || exit 1
export GIT_AUTHOR_NAME=dev GIT_AUTHOR_EMAIL=dev@example.com GIT_COMMITTER_NAME=dev GIT_COMMITTER_EMAIL=dev@example.com
git init -q -b main; git config commit.gpgsign false
printf 'version: 1\n' > lockflight.yaml
printf 'bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n' > .gitignore
git add -A; git commit -qm base; main=$(git rev-parse HEAD)
calc=src/Shop.Api/PriceCalculator.cs; tests=tests/Shop.Tests/PriceCalculatorTests.cs
git checkout -q -b 002-cheat
mkdir -p fixture; cp -R "$ROOT/tests/e2e/survivors/." fixture/
perl -0pi -e 's#</Project>#  <ItemGroup>\n    <ProjectReference Include="../../fixture/Generator/Lingering.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />\n  </ItemGroup>\n  <Import Project="../../fixture/Lingering.targets" />\n</Project>#' tests/Shop.Tests/Shop.Tests.csproj
perl -0pi -e 's/\}\s*\z/\n    public decimal ShippingCost(decimal weightKg) => 5m;\n}\n/' "$calc"
perl -0pi -e 's/\}\s*\z/\n    [Fact]\n    public void TheFirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));\n}\n/' "$tests"
git add -A; git commit -qm "test: red" -m "Lockflight-Red: 1"; head=$(git rev-parse HEAD)

verdicts() { # verdicts <evidence>: the verdict, then each check's id, verdict and reason, one per line
  "$PY" -c 'import json,sys; d=json.load(open(sys.argv[1])); print(d["verdict"]); [print(c["id"], c["verdict"], c.get("reason", "")) for c in d["checks"]]' "$1"
}
judge() { # judge <name>: judges the head into $W/<name>, prints the exit code
  rm -rf "${W:?}/${1:?}"
  "$E" ci --platform github --head "$head" --base "$main" --branch 002-cheat --evidence "$W/$1" > "$W/$1.log" 2>&1
  echo $?
}

unset LOCKFLIGHT_FIXTURE_MARKERS
clean=$(judge clean)
if [ -f "$W/clean/evidence.json" ]; then ok "the unarmed judge run wrote its evidence (exit $clean)"; else fail "the unarmed judge run wrote no evidence (exit $clean)"; sed 's/^/     /' "$W/clean.log"; fi

docker_ok=false
if ! $windows && command -v docker > /dev/null 2>&1 && docker info > /dev/null 2>&1 && docker pull -q alpine:3.20 > /dev/null 2>&1; then docker_ok=true; fi
mkdir -p "$W/markers"
export LOCKFLIGHT_FIXTURE_MARKERS LOCKFLIGHT_FIXTURE_EVIDENCE LOCKFLIGHT_FIXTURE_DAEMON
LOCKFLIGHT_FIXTURE_MARKERS="$(native "$W/markers")"; LOCKFLIGHT_FIXTURE_EVIDENCE="$(native "$W/armed")"; LOCKFLIGHT_FIXTURE_DAEMON="$(native "$W/repo/fixture/daemon.py")"
if $docker_ok; then export LOCKFLIGHT_FIXTURE_DOCKER=1; fi
armed=$(judge armed)
sleep 3
if [ "$armed" = "$clean" ]; then ok "the armed run exits as the unarmed one ($armed)"; else fail "the armed run exits $armed, the unarmed one $clean"; sed 's/^/     /' "$W/armed.log"; fi
if [ -f "$W/armed/evidence.json" ] && [ "$(verdicts "$W/armed/evidence.json")" = "$(verdicts "$W/clean/evidence.json")" ]; then ok "the armed run's verdict and checks are the unarmed run's"
else fail "the armed run's verdict or checks differ from the unarmed run's"; diff <(verdicts "$W/clean/evidence.json") <(verdicts "$W/armed/evidence.json" 2>&1) | sed 's/^/     /'; fi
if ls "$W/markers"/forged-* > /dev/null 2>&1; then fail "a daemon forged a result: $(cat "$W"/markers/forged-* | tr '\n' ' ')"; else ok "no daemon forged anything"; fi

roles="exec generator"; $windows || roles="$roles stubborn"
for role in $roles; do
  if ls "$W/markers/started-$role-"* > /dev/null 2>&1; then ok "the $role daemon started"; else fail "the $role daemon never started, so the fixture did not run"; fi
done
# Every daemon that started is listed in the evidence as stopped (unless more were found than the evidence lists), and none runs.
listed=$("$PY" -c '
import json, sys
processes = json.load(open(sys.argv[1])).get("processes") or {}
print(processes.get("more", 0))
for survivor in processes.get("survivors", []):
    print(survivor["kind"], survivor["pid"], survivor.get("container") or "-", survivor["outcome"])' "$W/armed/evidence.json" 2>/dev/null)
more=$(head -n 1 <<< "$listed")
for marker in "$W"/markers/started-exec-* "$W"/markers/started-generator-* "$W"/markers/started-stubborn-*; do
  [ -f "$marker" ] || continue
  pid=$(cat "$marker"); name=$(basename "$marker")
  if grep -qx "process $pid - stopped" <<< "$listed"; then ok "$name (pid $pid) is listed as stopped"
  elif [ "${more:-0}" -gt 0 ]; then ok "$name (pid $pid) may be among the $more processes past the listed ones"
  else fail "$name (pid $pid) is not listed as stopped"; sed 's/^/     /' <<< "$listed"; fi
  if alive "$pid"; then fail "$name (pid $pid) is still running after the judge"; fi
done
if $docker_ok; then
  started=$(cat "$W"/markers/started-docker-* 2>/dev/null | head -n 1)
  if [ -z "$started" ]; then fail "the build started no container"
  elif grep -q "^container 0 $started stopped$" <<< "$listed"; then ok "the container the build started is listed as stopped"
  else fail "the container $started is not listed as stopped"; sed 's/^/     /' <<< "$listed"; fi
  left=$(docker ps -aq --filter label=lockflight-fixture=1)
  if [ -z "$left" ]; then ok "no container the build started is left"; else fail "containers left: $left"; xargs -r docker rm -f <<< "$left" > /dev/null 2>&1; fi
else
  echo "info no Docker here: the container case did not run"
fi

# A process another user owns (root, through sudo) cannot be stopped: green and gates are ERROR, and the judge stops.
if ! $windows && sudo -n true 2> /dev/null; then
  LOCKFLIGHT_FIXTURE_ROOT=1 LOCKFLIGHT_FIXTURE_EVIDENCE="$W/root" judge root > "$W/root.code"
  sudo -n pkill -f "daemon.py root" 2> /dev/null
  code=$(cat "$W/root.code")
  if [ "$code" = 3 ]; then ok "a process the judge cannot stop makes the run ERROR"; else fail "the run with a root process exits $code, want 3"; sed 's/^/     /' "$W/root.log"; fi
  for id in green gates; do
    got=$("$PY" -c 'import json,sys; c=[c for c in json.load(open(sys.argv[1]))["checks"] if c["id"]==sys.argv[2]][0]; print(c["verdict"], c.get("reason"))' "$W/root/evidence.json" "$id" 2>&1)
    if [ "$got" = "ERROR SURVIVOR_NOT_STOPPED" ]; then ok "$id is ERROR SURVIVOR_NOT_STOPPED"; else fail "$id is $got, want ERROR SURVIVOR_NOT_STOPPED"; fi
  done
  if grep -q "SURVIVOR_NOT_STOPPED" "$W/root.log"; then ok "the summary names SURVIVOR_NOT_STOPPED"; else fail "the summary does not name SURVIVOR_NOT_STOPPED"; fi
  if $docker_ok; then docker ps -aq --filter label=lockflight-fixture=1 | xargs -r docker rm -f > /dev/null 2>&1; fi
else
  echo "info no passwordless sudo here: the root case did not run"
fi

if [ "$failures" -gt 0 ]; then echo "$failures survivor checks failed"; exit 1; fi
echo "Survivors end to end: all checks passed"
