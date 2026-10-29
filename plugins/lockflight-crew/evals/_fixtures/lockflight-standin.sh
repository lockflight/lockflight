#!/usr/bin/env bash
# Stand-in for the Lockflight engine: `phase check specify|tasks` applies the main rules of the real checks (see
# docs/guides/spec-contract.md) with awk; commands that record a human decision are refused as the real engine refuses them
# inside a Claude Code session; anything else says it is a stand-in.
set -u
root=$(cd "$(dirname "$0")/../.." && pwd)
feature=""
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  [ "${args[$i]}" = "--feature" ] && feature="${args[$((i + 1))]:-}"
done
if [ -z "$feature" ]; then
  feature=$(ls "$root/specs" 2>/dev/null | head -1)
fi
dir="$root/specs/$feature"
case "${1:-} ${2:-}" in
  "approve verify")
    if [ -f "$root/.lockflight/standin-spec-approved" ]; then echo "PASS $feature spec: approved"; else echo "FAIL $feature spec: not approved"; exit 1; fi
    exit 0 ;;
  "approve "*|"baseline "*|"redgreen reset"|"redgreen amend"|"review dismiss"|"adopt apply"|"adopt undo"|"update "*)
    echo "lockflight: '$1 ${2:-}' records a human decision and is refused inside a Claude Code session; ask the user to type /lockflight:$1 in Claude Code, or to run it in a separate terminal window" >&2
    exit 2 ;;
  "phase specify")
    ;;
  "phase tasks")
    ;;
esac
if [ "${1:-}" != "phase" ] || [ "${2:-}" != "check" ]; then
  echo "lockflight (eval stand-in): '$*' is not available in this fixture" >&2
  exit 3
fi
phase="${3:-}"
echo "Feature: $feature · phase: $phase"
if [ "$phase" = "specify" ]; then
  [ -f "$dir/spec.md" ] || { echo "FAIL spec.exists: $feature/spec.md is missing"; exit 1; }
  awk '
    /^[[:space:]]*<!--/ { c = ($0 !~ /-->/); next }
    c { if ($0 ~ /-->/) c = 0; next }
    /\[NEEDS CLARIFICATION/ { clar++ }
    /\[[A-Z][A-Z0-9 _\/-][A-Z0-9 _\/-]+\]([^(\[]|$)|\[###-|\[[^]]*e\.g\.|\$ARGUMENTS|\[(Brief Title|initial state|action|expected outcome)/ { if ($0 !~ /NEEDS CLARIFICATION/) ph++ }
    /^[[:space:]]*[-*][[:space:]]+\*\*FR-[0-9][0-9][0-9]+\*\*/ { match($0, /FR-[0-9]+/); id = substr($0, RSTART, RLENGTH); if (seen[id]++) dup = dup " " id; req++ }
    /^### +User Story +[0-9]+ *[-:–—]/ { if (story) { if (!gwt) bad = bad " " story }; story = $4; gwt = 0; stories++; next }
    /^##+ / { if (story && !gwt) bad = bad " " story; story = ""; next }
    story && tolower($0) ~ /\*\*given\*\*.*\*\*when\*\*.*\*\*then\*\*/ { gwt = 1 }
    END {
      if (story && !gwt) bad = bad " " story
      fail = 0
      if (req) print "PASS spec.requirements: " req " requirements"; else { print "FAIL spec.requirements: no requirements; list them as '\''- **FR-001**: System MUST …'\'' under ## Requirements"; fail = 1 }
      if (dup == "") print "PASS spec.unique-ids: requirement IDs are unique"; else { print "FAIL spec.unique-ids: duplicate IDs:" dup; fail = 1 }
      if (!clar) print "PASS spec.clarified: no open [NEEDS CLARIFICATION]"; else { print "FAIL spec.clarified: " clar " open [NEEDS CLARIFICATION]; resolve them with the user"; fail = 1 }
      if (!ph) print "PASS spec.no-placeholders: no Spec Kit template placeholders left"; else { print "FAIL spec.no-placeholders: " ph " template placeholders left; replace them with this feature'\''s content"; fail = 1 }
      if (!stories) { print "FAIL spec.stories: no '\''### User Story N - Title'\'' sections"; fail = 1 }
      else if (bad == "") print "PASS spec.stories: " stories " user stories with Given/When/Then scenarios"
      else { print "FAIL spec.stories: no Given/When/Then scenario in: User Story" bad; fail = 1 }
      exit fail
    }' "$dir/spec.md"
  exit $?
fi
if [ "$phase" = "tasks" ]; then
  fail=0
  if [ -f "$root/.lockflight/standin-spec-approved" ]; then echo "PASS spec.approved: spec approved"; else echo "FAIL spec.approved: spec is not approved; ask the user to review it, then to type /lockflight:approve spec"; fail=1; fi
  if [ ! -f "$dir/tasks.md" ] || [ ! -f "$dir/spec.md" ]; then echo "FAIL tasks.exists: $feature/tasks.md (and spec.md) must exist"; exit 1; fi
  known=$(grep -Eo '^[[:space:]]*[-*][[:space:]]+\*\*FR-[0-9]{3,}\*\*' "$dir/spec.md" | grep -Eo 'FR-[0-9]+' | sort -u | tr '\n' ' ')
  awk -v known="$known" '
    BEGIN { n = split(known, k, " "); for (i = 1; i <= n; i++) isknown[k[i]] = 1 }
    /^#/ { tests = (tolower($0) ~ /^### tests/); next }
    /^[[:space:]]*-[[:space:]]+\[[ xX]\][[:space:]]+T[0-9][0-9][0-9]+/ {
      line = $0; s = ""
      if (match(line, /\[US[0-9]+\]/)) s = substr(line, RSTART + 3, RLENGTH - 4)
      rest = line; cites = 0
      while (match(rest, /FR-[0-9][0-9][0-9]+/)) { id = substr(rest, RSTART, RLENGTH); cited[id] = 1; cites++; if (!(id in isknown)) unknown = unknown " " id; rest = substr(rest, RSTART + RLENGTH) }
      d = line; sub(/^[[:space:]]*-[[:space:]]+\[[ xX]\][[:space:]]+T[0-9]+[[:space:]]+(\[P\][[:space:]]+)?(\[US[0-9]+\][[:space:]]+)?(\([^)]*\)[[:space:]]+)?/, "", d)
      ld = tolower(d)
      if (tests || ld ~ /^([a-z]+ +)?(tests?|write tests?|add tests?)( |$)/ && ld !~ /^(make|run|implement|fix) /) role = "test"
      else if (ld ~ /stub|skeleton|notimplementedexception|(declare|scaffold).*signature/) role = "stub"
      else if (d ~ /Tests?\.cs|\.Tests\// && ld !~ /^(implement|make)/) role = "test"
      else role = "impl"
      if (s == "") next
      if (!cites) uncited = uncited " line " NR
      story[s] = 1
      if (role == "test") lasttest[s] = NR
      if (role == "impl") { nimpl[s]++; impl[s, nimpl[s]] = NR }
    }
    END {
      fail = 0
      for (id in isknown) if (!(id in cited)) missing = missing " " id
      if (missing == "") print "PASS tasks.cover-requirements: all requirements have tasks"; else { print "FAIL tasks.cover-requirements: requirements without a task:" missing; fail = 1 }
      if (uncited == "" && unknown == "") print "PASS tasks.cite-requirements: every story task cites known requirements"
      else if (unknown == "") { print "FAIL tasks.cite-requirements: story tasks that cite no FR-xxx:" uncited; fail = 1 }
      else { print "FAIL tasks.cite-requirements: tasks cite requirements the spec does not define:" unknown; fail = 1 }
      for (s in story) {
        if (!(s in lasttest)) order = order " US" s " has no test task;"
        else for (i = 1; i <= nimpl[s]; i++) if (impl[s, i] < lasttest[s]) order = order " line " impl[s, i] " (implementation) comes before the last test task of US" s ";"
      }
      if (order == "") print "PASS tasks.tests-first: in every story the tests come before the implementation"; else { print "FAIL tasks.tests-first:" order " put test tasks under a ### Tests for User Story N heading and implementation after them"; fail = 1 }
      exit fail
    }' "$dir/tasks.md" || fail=1
  exit $fail
fi
echo "lockflight: unknown phase '$phase'; use specify or tasks" >&2
exit 4
