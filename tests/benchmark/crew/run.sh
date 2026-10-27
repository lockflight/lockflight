#!/usr/bin/env bash
# Crew benchmark: the test author (lockflight-crew) against a plain agent on the red step of seeded user stories.
# Each run gets a fresh copy of samples/dotnet-single with one feature whose spec and tasks are approved, red/green started
# and the story's stubs declared, then a headless Claude Code session is asked for the story's red. Both arms use the same
# model and the same prompt; the crew arm also has the crew plugin loaded, so the feature skill delegates the red step.
# The engine's ledger is the evidence: what the agent says is not counted.
#
# Usage: ENGINE=<lockflight binary> tests/benchmark/crew/run.sh [options]
#   --stories a,b,c        stories under stories/ (default: all, sorted)
#   --runs N               runs per story and arm (default 2)
#   --arms LIST            arms to run (default plain,crew): plain = core plugin only; crew = core and crew plugins, the
#                          test author inheriting the session model; crew-<model> (crew-haiku, crew-opus, …) = the same with
#                          the test author on <model>, through a generated copy of the crew plugin whose agent file sets it
#   --model M              session model for every run (default sonnet)
#   --max-budget-usd X     cap per run, passed to claude (default 2.50)
#   --total-budget-usd X   stop starting runs once the spend plus one run's cap would pass this (default 20)
#   --out DIR              where runs and the report go (default: a new temporary directory)
#   --report-only DIR      rebuild the report from an earlier --out directory without running anything
#   --setup-only STORY DIR seed STORY into DIR (approved, red/green started) without a session, to check a new story
# Needs: claude, dotnet (the sample's SDK), jq, git, perl.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../../.." && pwd)"

stories="" runs=2 arms="plain,crew" model="sonnet" run_budget="2.50" total_budget="20" out="" report_only="" setup_only=""
while [ $# -gt 0 ]; do
  case "$1" in
    --stories) stories="$2"; shift 2 ;;
    --runs) runs="$2"; shift 2 ;;
    --arms) arms="$2"; shift 2 ;;
    --model) model="$2"; shift 2 ;;
    --max-budget-usd) run_budget="$2"; shift 2 ;;
    --total-budget-usd) total_budget="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --report-only) report_only="$2"; shift 2 ;;
    --setup-only) setup_only="$2"; setup_dir="$3"; shift 3 ;;
    *) echo "unknown option: $1" >&2; exit 4 ;;
  esac
done
for tool in claude jq git perl; do command -v "$tool" > /dev/null || { echo "needs $tool on PATH" >&2; exit 1; }; done

# The session's transcripts: main session file plus subagent files, found by session id.
transcripts() { # transcripts <session id> <dest dir>
  local main; main="$(find "$HOME/.claude/projects" -maxdepth 2 -name "$1.jsonl" 2>/dev/null | head -n 1)"
  [ -n "$main" ] || return 0
  cp "$main" "$2/session.jsonl"
  local sub="${main%.jsonl}/subagents"
  if [ -d "$sub" ]; then mkdir -p "$2/subagents" && cp "$sub"/* "$2/subagents/" 2>/dev/null; fi
  return 0
}

# Token totals of a transcript: the usage of each API message counted once (a message is logged once per content block).
tokens() { # tokens <jsonl files…> -> "<all tokens> <output tokens>"
  [ $# -gt 0 ] || { echo "0 0"; return; }
  cat "$@" | jq -s -r '
    [ .[] | select(.type == "assistant" and .message.usage != null) | {id: (.message.id // .uuid), u: .message.usage} ]
    | group_by(.id) | map(.[-1].u)
    | [ (map((.input_tokens // 0) + (.output_tokens // 0) + (.cache_creation_input_tokens // 0) + (.cache_read_input_tokens // 0)) | add // 0),
        (map(.output_tokens // 0) | add // 0) ] | "\(.[0]) \(.[1])"'
}

setup_project() { # setup_project <story dir> <project dir>
  local story="$1" w="$2" slug; slug="$(basename "$story")"
  mkdir -p "$w"
  cp -R "$ROOT/samples/dotnet-single/." "$w/"; rm -rf "$w"/*/*/bin "$w"/*/*/obj
  # The container smoke test needs Docker; it is not what this benchmark measures.
  rm -f "$w/tests/Shop.Tests/ContainerSmokeTests.cs"
  mkdir -p "$w/.lockflight/bin" "$w/specs/001-$slug"
  cp "$ENGINE" "$w/.lockflight/bin/lockflight"
  printf 'version: 1\n' > "$w/lockflight.yaml"
  printf 'bin/\nobj/\n.lockflight/bin/\n.lockflight/ledger/\n.claude/worktrees/\n' > "$w/.gitignore"
  cp "$story/spec.md" "$story/plan.md" "$story/tasks.md" "$w/specs/001-$slug/"
  if [ -d "$story/stub" ]; then cp -R "$story/stub/." "$w/"; fi
  (
    cd "$w" || exit 1
    export GIT_AUTHOR_NAME=bench GIT_AUTHOR_EMAIL=bench@example.com GIT_COMMITTER_NAME=bench GIT_COMMITTER_EMAIL=bench@example.com
    git init -q && git add -A && git commit -qm "seeded story $slug"
    # The person's steps, in the temporary copy only: approvals are human decisions, so CLAUDECODE is unset.
    env -u CLAUDECODE .lockflight/bin/lockflight phase check specify > /dev/null &&
      env -u CLAUDECODE .lockflight/bin/lockflight approve spec > /dev/null &&
      env -u CLAUDECODE .lockflight/bin/lockflight phase check tasks > /dev/null &&
      env -u CLAUDECODE .lockflight/bin/lockflight approve tasks > /dev/null &&
      env -u CLAUDECODE .lockflight/bin/lockflight redgreen start > /dev/null
  ) > "$w.setup.log" 2>&1
}

# A copy of the crew plugin whose test author runs on <model>: the shipped agent has no `model` line, so it inherits the
# session's model; the copy adds one to its frontmatter.
crew_with_model() { # crew_with_model <model> -> plugin dir
  local copy="$out/plugins/lockflight-crew-$1"
  if [ ! -d "$copy" ]; then
    mkdir -p "$out/plugins" && cp -R "$ROOT/plugins/lockflight-crew" "$copy" && rm -rf "$copy/evals"
    perl -0pi -e "s/^(name: test-author\n)/\${1}model: $1\n/m" "$copy/agents/test-author.md"
  fi
  printf '%s' "$copy"
}

prompt_for() { # prompt_for <arm> <slug>
  local base="This is a Lockflight project. The feature in specs/001-$2 has an approved spec, plan and tasks, red/green is started, and the stubs for User Story 1 are declared and committed. Do the red step of the /lockflight:feature skill for US1: write the failing tests for US1 and get '.lockflight/bin/lockflight redgreen red' accepted. Do only the red step: do not implement production code and do not run 'redgreen green'. When done, report the verdict line."
  if [ "${1%%-*}" = crew ]; then
    printf '%s The lockflight-crew plugin is installed: use its test author for the red step, as the feature skill describes.' "$base"
  else
    printf '%s' "$base"
  fi
}

run_one() { # run_one <story dir> <arm> <n> <run dir>
  local story="$1" arm="$2" n="$3" dir="$4" slug; slug="$(basename "$story")"
  local w="$dir/project"; mkdir -p "$dir"
  if ! setup_project "$story" "$w"; then echo "setup failed for $slug, see $w.setup.log" >&2; return 1; fi
  local plugins=(--plugin-dir "$ROOT/plugins/lockflight")
  case "$arm" in
    crew) plugins+=(--plugin-dir "$ROOT/plugins/lockflight-crew") ;;
    crew-*) plugins+=(--plugin-dir "$(crew_with_model "${arm#crew-}")") ;;
  esac
  local started; started="$(date +%s)"
  (
    cd "$w" || exit 1
    # Nested runs: unset CLAUDECODE so claude starts; it sets it again for its own tools.
    env -u CLAUDECODE claude -p "$(prompt_for "$arm" "$slug")" "${plugins[@]}" --model "$model" \
      --max-budget-usd "$run_budget" --max-turns 80 --output-format json --permission-mode acceptEdits \
      --allowedTools "Bash(.lockflight/bin/lockflight *)" "Bash(./.lockflight/bin/lockflight *)" "Bash(dotnet build*)" \
        "Bash(dotnet test*)" "Bash(ls*)" "Bash(cat *)" "Bash(grep *)" "Bash(find *)" "Bash(head *)" "Bash(tail *)" \
        "Bash(git status*)" "Bash(git diff*)" "Bash(git log*)" "Bash(cd *)" "Bash(pwd)" \
      < /dev/null > "$dir/out.json" 2> "$dir/err.txt"
  )
  echo "$(( $(date +%s) - started ))" > "$dir/seconds"
  (cd "$w" && env -u CLAUDECODE .lockflight/bin/lockflight redgreen status > "$dir/status.txt" 2>&1)
  (cd "$w" && git status --porcelain --untracked-files=all -- src > "$dir/production-changes.txt")
  (cd "$w" && git add -N -- src && git diff -- src > "$dir/production.diff")
  cp "$w"/.lockflight/features/*/redgreen.json "$dir/redgreen.json" 2>/dev/null
  cp "$w"/specs/*/spec.md "$dir/spec.md" 2>/dev/null
  cat "$w"/.lockflight/ledger/events-*.jsonl > "$dir/ledger.jsonl" 2>/dev/null
  local sid; sid="$(jq -r '.session_id // empty' "$dir/out.json" 2>/dev/null)"
  [ -n "$sid" ] && transcripts "$sid" "$dir"
  printf '%s %s %s\n' "$slug" "$arm" "$n" > "$dir/run"
  rm -rf "$w"/*/*/bin "$w"/*/*/obj
}

# One row per run, from the files a run left.
row() { # row <run dir>
  local dir="$1" slug arm n; read -r slug arm n < "$dir/run"
  local reds accepted_at compile rejected kinds verdict cost seconds rounds prod writes blocked all out ta_all ta_model
  local other traced untagged status skill
  reds="$(jq -r 'select(.signature | startswith("redgreen:red:")) | .signature' "$dir/ledger.jsonl" 2>/dev/null)"
  accepted_at="$(printf '%s\n' "$reds" | grep -n 'redgreen:red:accepted' | head -n 1 | cut -d: -f1)"
  compile="$(printf '%s\n' "$reds" | grep -c 'COMPILE_RED')"
  rejected="$(printf '%s\n' "$reds" | grep -c 'redgreen:red:rejected')"
  # The rejected event's summary lists the kinds ("<feature>: RED_IN_TEST,GREEN_BEFORE").
  kinds="$(jq -r 'select(.signature == "redgreen:red:rejected") | .summary | sub("^[^:]*: "; "")' "$dir/ledger.jsonl" 2>/dev/null | tr ',' '\n' | sort | uniq -c | awk '{ printf "%s%s×%s", sep, $2, $1; sep = " " }')"
  [ "$compile" -gt 0 ] && kinds="${kinds:+$kinds }COMPILE_RED×$compile"
  blocked="$(jq -r 'select(.type == "hook.blocked") | .type' "$dir/ledger.jsonl" 2>/dev/null | grep -c .)"
  if grep -q 'RED (test projects locked)' "$dir/status.txt"; then verdict="accepted (red ${accepted_at:-?})"; else verdict="no"; fi
  cost="$(jq -r '.total_cost_usd // 0' "$dir/out.json" 2>/dev/null || echo 0)"
  seconds="$(cat "$dir/seconds")"
  # Accepted tests: how many were RED_OTHER (accepted with a flag), which of the spec's requirements their traits name, and
  # how many carry no Requirement trait. Runs recorded before these files were kept show "-".
  other="-" traced="-" untagged="-"
  if [ -f "$dir/redgreen.json" ] && [ -f "$dir/spec.md" ]; then
    local frs; frs="$(grep -oE '^- \*\*FR-[0-9]+' "$dir/spec.md" | grep -oE 'FR-[0-9]+' | sort -u)"
    other="$(jq '[.cycles[].tests[] | select(.red == "RED_OTHER")] | length' "$dir/redgreen.json")"
    untagged="$(jq '[.cycles[].tests[] | select((.requirements | length) == 0)] | length' "$dir/redgreen.json")"
    traced="$(jq -r '.cycles[].tests[].requirements[]' "$dir/redgreen.json" | sort -u | grep -cxF -f <(printf '%s\n' "$frs"))/$(printf '%s\n' "$frs" | grep -c .)"
  fi
  # The main session's tool calls in order, one per line: "Agent <subagent type>" or "Bash <command>" or the tool name.
  local calls; calls="$(jq -r 'select(.type == "assistant") | .message.content[]? | select(.type == "tool_use")
    | if .name == "Agent" then "Agent \(.input.subagent_type // "")" elif .name == "Bash" then "Bash \(.input.command // "" | gsub("\n"; " "))"
      elif .name == "Skill" then "Skill \(.input.skill // "")" else .name end' "$dir/session.jsonl" 2>/dev/null)"
  # Whether the main session loaded the feature skill, the text that asks for the checks.
  if printf '%s\n' "$calls" | grep -q '^Skill .*feature'; then skill="yes"; else skill="no"; fi
  # Rounds: Agent calls naming the test author in the main session (the plain arm has none).
  rounds="$(printf '%s\n' "$calls" | grep -c '^Agent .*test-author')"
  # After each test-author round the skill wants `redgreen status` as the very next call: "first" when it was every time,
  # "late" when it came after other calls, "no" when it never followed the last round; "-" without a round.
  status="-"
  if [ "$rounds" -gt 0 ]; then
    status="$(printf '%s\n' "$calls" | awk '
      /^Agent .*test-author/ { pending = 1; next_is = 1; rounds++; next }
      next_is { if ($0 ~ /^Bash .*redgreen status/) first++; next_is = 0 }
      pending && /^Bash .*redgreen status/ { checked++; pending = 0 }
      END { if (pending) print "no"; else if (first == rounds) print "first"; else print "late" }')"
  fi
  prod="$(grep -c . "$dir/production-changes.txt")"
  local files=("$dir/session.jsonl"); [ -d "$dir/subagents" ] && files+=("$dir"/subagents/*.jsonl)
  # Production writes: Write/Edit calls aimed at src/ by any agent, refused or not.
  writes="$(cat "${files[@]}" 2>/dev/null | jq -r 'select(.type == "assistant") | .message.content[]? | select(.type == "tool_use" and (.name == "Write" or .name == "Edit" or .name == "MultiEdit")) | .input.file_path // ""' 2>/dev/null | grep -c '/src/')"
  read -r all out < <(tokens "${files[@]}")
  ta_all=0 ta_model="-"
  if [ -d "$dir/subagents" ]; then
    local ta='[ .[] | select(.type == "assistant" and .message.usage != null and ((.attributionAgent // "") | test("test-author"))) ]'
    ta_all="$(cat "$dir"/subagents/*.jsonl | jq -s -r "$ta"' | map({id: (.message.id // .uuid), u: .message.usage}) | group_by(.id) | map(.[-1].u) | map((.input_tokens // 0) + (.output_tokens // 0) + (.cache_creation_input_tokens // 0) + (.cache_read_input_tokens // 0)) | add // 0')"
    ta_model="$(cat "$dir"/subagents/*.jsonl | jq -s -r "$ta"' | map(.message.model) | unique | join(",")')"
    [ -n "$ta_model" ] || ta_model="-"
  fi
  printf '| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n' \
    "$slug" "$arm" "$n" "$verdict" "$compile" "$rejected" "${kinds:--}" "$other" "$traced" "$untagged" "$rounds" "$status" "$skill" \
    "$prod" "$writes" "$blocked" "$seconds" "$all" "$ta_all" "$ta_model" "$(printf '%.2f' "$cost")"
}

report() { # report <out dir>
  local o="$1" rows
  rows="$(for d in "$o"/runs/*/; do [ -f "$d/run" ] && row "${d%/}"; done)"
  {
    echo "| story | arm | run | red accepted | compile reds | other rejections | rejection kinds | accepted RED_OTHER | FRs traced | untagged tests | test-author rounds | status after test author | skill loaded | production files changed | production writes | guard blocks | wall s | tokens (all) | test-author tokens | test-author model | cost USD |"
    echo "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"
    printf '%s\n' "$rows"
    echo
    echo "| arm | runs | accepted on first red | accepted eventually | compile reds | RED_IN_TEST | GREEN_BEFORE | other kinds | accepted RED_OTHER tests | FRs traced | runs changing production | production writes | guard blocks | status not checked first | skill not loaded | mean wall s | mean tokens | total cost USD | cost per accepted red USD |"
    echo "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"
    printf '%s\n' "$rows" | awk -F'|' '
      function count(m) { sub(/.*×/, "", m); return m + 0 }
      function kind(field, name) { return match(field, name "×[0-9]+") ? count(substr(field, RSTART, RLENGTH)) : 0 }
      { for (i = 2; i <= NF; i++) gsub(/^ +| +$/, "", $i) }
      NF > 10 { a = $3; if (!(a in n)) order[++k] = a; n[a]++; if ($5 ~ /^accepted/) ok[a]++; if ($5 ~ /red 1\)/) first[a]++
        c[a] += $6; it[a] += kind($8, "RED_IN_TEST"); gb[a] += kind($8, "GREEN_BEFORE")
        ok_kinds = kind($8, "RED_IN_TEST") + kind($8, "GREEN_BEFORE") + kind($8, "COMPILE_RED"); total = 0
        tmp = $8; while (match(tmp, /×[0-9]+/)) { total += count(substr(tmp, RSTART, RLENGTH)); tmp = substr(tmp, RSTART + RLENGTH) }
        ok2[a] += total - ok_kinds
        if ($9 != "-") ro[a] += $9
        if (split($10, f, "/") == 2) { tc[a] += f[1]; tt[a] += f[2] }
        if ($13 == "no" || $13 == "late") sk[a]++; if ($14 == "no") nl[a]++
        if ($15 + 0 > 0) pr[a]++; pw[a] += $16; gbk[a] += $17
        s[a] += $18; t[a] += $19; usd[a] += $22 }
      END { for (i = 1; i <= k; i++) { a = order[i]
        printf "| %s | %d | %d | %d | %d | %d | %d | %d | %d | %s | %d | %d | %d | %s | %d | %.0f | %.0f | %.2f | %s |\n", a, n[a], first[a], ok[a], c[a], it[a], gb[a], ok2[a], ro[a], (tt[a] ? tc[a] "/" tt[a] : "-"), pr[a], pw[a], gbk[a], (a ~ /^crew/ ? sk[a] + 0 : "-"), nl[a], s[a] / n[a], t[a] / n[a], usd[a], (ok[a] ? sprintf("%.3f", usd[a] / ok[a]) : "-") } }'
  } > "$o/report.md"
  cat "$o/report.md"
}

if [ -n "$report_only" ]; then report "$report_only"; exit 0; fi
: "${ENGINE:?set ENGINE to a lockflight binary}"
if [ -n "$setup_only" ]; then
  setup_project "$HERE/stories/$setup_only" "$setup_dir" || { echo "setup failed, see $setup_dir.setup.log" >&2; exit 1; }
  echo "seeded $setup_only in $setup_dir"; exit 0
fi
[ -n "$out" ] || out="$(mktemp -d "${TMPDIR:-/tmp}/lockflight-crew-bench.XXXXXX")"
mkdir -p "$out/runs"
[ -n "$stories" ] || stories="$(cd "$HERE/stories" && ls | paste -sd, -)"
spent=0
echo "crew benchmark: model $model, claude $(claude --version 2>/dev/null | head -n 1), results in $out"
IFS=',' read -r -a story_list <<< "$stories"
IFS=',' read -r -a arm_list <<< "$arms"
for slug in "${story_list[@]}"; do
  for n in $(seq 1 "$runs"); do
    for arm in "${arm_list[@]}"; do
      if awk -v s="$spent" -v r="$run_budget" -v t="$total_budget" 'BEGIN { exit !(s + r > t) }'; then
        echo "stopping: spent $spent USD, one more run could pass the $total_budget USD budget" >&2; break 3
      fi
      dir="$out/runs/$slug-$arm-$n"
      echo "run $slug $arm $n"
      run_one "$HERE/stories/$slug" "$arm" "$n" "$dir" || continue
      spent="$(awk -v s="$spent" -v c="$(jq -r '.total_cost_usd // 0' "$dir/out.json" 2>/dev/null || echo 0)" 'BEGIN { printf "%.4f", s + c }')"
      echo "  $(row "$dir")"
    done
  done
done
echo "spent $spent USD"
report "$out"
