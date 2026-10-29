#!/usr/bin/env bash
# Gate step order and run policies, pinned with a stand-in for the .NET SDK and gitleaks, so no real build runs: which steps each
# profile runs and in what order, which projects the stop profile chooses in a solution, where a single repository stops (the first
# step that does not pass), what a workspace runs once at the root and per unit, the N/A rows of skipped units, secrets only when
# every earlier step passed, and the verdict.
#   ENGINE=<path to lockflight> bash tests/gates/step-order.sh [--update]
# Compares the gate output with tests/gates/step-order.expected (durations blanked); --update rewrites that file instead.
# POSIX shells only: the stand-ins are shell scripts.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
expected="$here/step-order.expected"
engine="${ENGINE:?set ENGINE to the lockflight binary}"
update="${1:-}"

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
fakes="$scratch/bin"
mkdir -p "$fakes"

# dotnet: build, test (writes a TRX report), format and package list, each failing when FAKE_FAIL names it, in every directory
# or only in one whose path contains FAKE_FAIL_IN.
cat > "$fakes/dotnet" <<'FAKE'
#!/usr/bin/env bash
fails() { [ "${FAKE_FAIL:-}" = "$1" ] && { [ -z "${FAKE_FAIL_IN:-}" ] || case "$PWD" in *"$FAKE_FAIL_IN"*) true ;; *) false ;; esac; }; }
case "$1" in
  build)
    if fails build; then echo "Program.cs(1,1): error CS1002: ; expected"; exit 1; fi
    echo "Build succeeded."; exit 0 ;;
  test)
    results=""
    while [ "$#" -gt 0 ]; do [ "$1" = "--results-directory" ] && results="$2"; shift; done
    mkdir -p "$results"
    if fails test; then outcome=Failed; failed=1; passed=0; code=1; else outcome=Passed; failed=0; passed=1; code=0; fi
    cat > "$results/lockflight.trx" <<TRX
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results><UnitTestResult testName="Shop.Tests.OrderTotal" outcome="$outcome" /></Results>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="$passed" failed="$failed" /></ResultSummary>
</TestRun>
TRX
    exit "$code" ;;
  format)
    if fails format; then echo "format broke"; exit 3; fi
    exit 0 ;;
  package)
    if fails deps; then echo "error: no assets file"; exit 1; fi
    echo '{ "version": 1, "parameters": "--vulnerable", "projects": [] }'; exit 0 ;;
esac
echo "fake dotnet: unexpected arguments: $*" >&2
exit 99
FAKE

cat > "$fakes/gitleaks" <<'FAKE'
#!/usr/bin/env bash
while [ "$#" -gt 0 ]; do [ "$1" = "--report-path" ] && printf '[]' > "$2"; shift; done
exit 0
FAKE
chmod +x "$fakes/dotnet" "$fakes/gitleaks"

commit_all() {
  git -C "$1" add -A
  git -C "$1" -c user.email=t@example.com -c user.name=t commit -qm fixture
}

single() {
  local root="$scratch/single"
  rm -rf "$root"
  mkdir -p "$root/src"
  printf 'version: 1\n' > "$root/lockflight.yaml"
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$root/src/Shop.csproj"
  printf 'class Order {}\n' > "$root/src/Order.cs"
  printf '.lockflight/\n' > "$root/.gitignore"
  git init -q "$root"
  commit_all "$root"
  echo "$root"
}

workspace() {
  local root="$scratch/workspace"
  rm -rf "$root"
  mkdir -p "$root/services/orders" "$root/services/billing" "$root/web"
  cat > "$root/lockflight.yaml" <<'YAML'
version: 1
units:
  - path: services/orders
    kind: dotnet
  - path: missing
    kind: dotnet
  - path: services/billing
  - path: web
    kind: none
YAML
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$root/services/orders/Orders.csproj"
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$root/services/billing/Billing.csproj"
  printf 'console.log(1)\n' > "$root/web/index.js"
  printf '.lockflight/\n' > "$root/.gitignore"
  git init -q "$root"
  commit_all "$root"
  echo "$root"
}

# A solution of four projects: Api.Tests -> Api -> Core <- Core.Tests, for the stop profile's choice of projects.
layered() {
  local root="$scratch/layered"
  rm -rf "$root"
  mkdir -p "$root/src/Core" "$root/src/Api" "$root/tests/Core.Tests" "$root/tests/Api.Tests"
  printf 'version: 1\n' > "$root/lockflight.yaml"
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$root/src/Core/Core.csproj"
  printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup></Project>\n' > "$root/src/Api/Api.csproj"
  printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" /><ProjectReference Include="../../src/Core/Core.csproj" /></ItemGroup></Project>\n' > "$root/tests/Core.Tests/Core.Tests.csproj"
  printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" /><ProjectReference Include="../../src/Api/Api.csproj" /></ItemGroup></Project>\n' > "$root/tests/Api.Tests/Api.Tests.csproj"
  printf '<Solution>\n  <Project Path="src/Core/Core.csproj" />\n  <Project Path="src/Api/Api.csproj" />\n  <Project Path="tests/Core.Tests/Core.Tests.csproj" />\n  <Project Path="tests/Api.Tests/Api.Tests.csproj" />\n</Solution>\n' > "$root/Shop.slnx"
  printf 'class Api {}\n' > "$root/src/Api/Api.cs"
  printf '.lockflight/\n' > "$root/.gitignore"
  git init -q "$root"
  commit_all "$root"
  echo "$root"
}

# One case: a fresh fixture, optional extra setup, then the gate run with its exit code; durations become "N ms".
run_case() {
  local title="$1" fixture="$2" setup="$3"
  shift 3
  local root
  root="$("$fixture")"
  (cd "$root" && eval "$setup")
  echo "## $title"
  echo "\$ lockflight gate run $*"
  local code=0
  (cd "$root" && PATH="$fakes:$PATH" LOCKFLIGHT_GITLEAKS="${GITLEAKS:-$fakes/gitleaks}" "$engine" gate run "$@" 2>&1) > "$scratch/out" || code=$?
  sed -E -e 's/\([0-9]+ ms\)/(N ms)/' -e "s#$root#<root>#g" "$scratch/out"
  echo "exit: $code"
  echo
}

render() {
  run_case "single: edit runs format only" single : --profile edit
  run_case "single: stop runs format, build, test" single : --profile stop
  run_case "single: a changed file is formatted first" single 'printf "class Order { }\n" > src/Order.cs' --profile stop
  FAKE_FAIL=format run_case "single: a format error stops the run" single 'printf "class Order { }\n" > src/Order.cs' --profile stop
  FAKE_FAIL=build run_case "single: a build failure stops the run" single : --profile stop
  FAKE_FAIL="test" run_case "single: a test failure ends the run" single : --profile stop
  run_case "single: pr runs every step" single : --profile pr --base HEAD
  FAKE_FAIL="test" run_case "single: pr stops at a test failure" single : --profile pr --base HEAD
  FAKE_FAIL=deps run_case "single: pr stops at a dependency error, before secrets" single : --profile pr --base HEAD
  GITLEAKS="$scratch/no-gitleaks" run_case "single: pr without gitleaks" single : --profile pr --base HEAD
  run_case "single: pr without a base" single : --profile pr
  run_case "layered: stop builds and tests the projects a change affects" layered 'FAKE_FAIL= PATH="$fakes:$PATH" "$engine" gate run --profile stop > /dev/null; printf "class Api { }\n" > src/Api/Api.cs' --profile stop
  FAKE_FAIL="test" run_case "layered: a test failure among the affected projects ends the run" layered 'FAKE_FAIL= PATH="$fakes:$PATH" "$engine" gate run --profile stop > /dev/null; printf "class Api { }\n" > src/Api/Api.cs' --profile stop
  run_case "layered: without a recorded PASS every project runs" layered 'printf "class Api { }\n" > src/Api/Api.cs' --profile stop
  run_case "layered: --full builds and tests every project" layered 'FAKE_FAIL= PATH="$fakes:$PATH" "$engine" gate run --profile stop > /dev/null; printf "class Api { }\n" > src/Api/Api.cs' --profile stop --full
  run_case "layered: a shared build input runs every project" layered 'FAKE_FAIL= PATH="$fakes:$PATH" "$engine" gate run --profile stop > /dev/null; printf "<Project />\n" > Directory.Build.props' --profile stop
  run_case "workspace: edit" workspace : --profile edit
  run_case "workspace: stop judges every unit never judged" workspace : --profile stop
  FAKE_FAIL=build FAKE_FAIL_IN=orders run_case "workspace: one unit's build failure, the other unit still runs" workspace : --profile stop
  run_case "workspace: pr" workspace : --profile pr --base HEAD
  FAKE_FAIL="test" FAKE_FAIL_IN=billing run_case "workspace: pr with a test failure runs no secrets scan" workspace : --profile pr --base HEAD
  FAKE_FAIL=deps FAKE_FAIL_IN=orders run_case "workspace: pr with a dependency error" workspace : --profile pr --base HEAD
  FAKE_FAIL=format run_case "workspace: a format error at the root" workspace 'printf "class Orders { }\n" > services/orders/Orders.cs' --profile stop
  run_case "workspace: a unit unchanged since its PASS is not run again" workspace 'PATH="$fakes:$PATH" "$engine" gate run --profile stop > /dev/null' --profile stop
  run_case "workspace: --unit runs one unit" workspace : --profile stop --unit services/billing
}

render > "$scratch/actual"
if [ "$update" = "--update" ]; then
  cp "$scratch/actual" "$expected"
  echo "wrote $expected"
elif diff -u "$expected" "$scratch/actual"; then
  echo "step order: as expected ($(grep -c '^## ' "$expected") cases)"
else
  echo "step order: differs from $expected (rerun with --update after reviewing the difference)" >&2
  exit 1
fi
