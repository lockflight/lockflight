# Crew benchmark results

## 2026-10-27: hard stories, test author against plain

- **Setup:** as below (Claude Code 2.1.309, session model `sonnet`, macOS arm64, .NET 10), `--max-budget-usd 1.50` per
  run. Four new stories built to provoke the failure modes the test author's brief targets (`loyalty-points`,
  `gift-cards`, `receipt-lines`, `checkout-totals`; see the README) and the earlier `discount-rules`, 3 runs each.
- **Arms:** `plain` and `crew` (test author on the session model), then `crew-haiku`. The feature skill now makes the
  `redgreen status` check after the test author a numbered step that must come next.
- **A change during the run:** on `receipt-lines` the test author wrote tests for the scenarios it could compile, ran
  `redgreen red` and got a partial red accepted in 3 of 3 runs. That locks the test projects, so the width scenarios
  could only be added after a person's `redgreen reset`. Its brief now says to stop before `redgreen red` when a stub is
  missing; `receipt-lines` was run again with the fixed brief (`crew (fixed brief)` below) and `crew-haiku` ran with it.
- **Spend:** 7.86 USD `total_cost_usd` in all: plain 2.59, crew 2.34, crew with the fixed brief 0.61, crew-haiku 1.96,
  and a 0.36 pilot. Under the 20 USD cap.
- **Commands:** `run.sh --stories loyalty-points,gift-cards,receipt-lines,checkout-totals,discount-rules --runs 3
  --arms plain,crew --max-budget-usd 1.50`, then `--stories receipt-lines --arms crew`, then the five stories with
  `--arms crew-haiku`.

### Per arm

| arm | runs | accepted on first red | accepted eventually | compile reds | RED_IN_TEST | GREEN_BEFORE | other kinds | accepted RED_OTHER tests | FRs traced | runs changing production | production writes | guard blocks | status not checked first | skill not loaded | mean wall s | mean tokens | total cost USD | cost per accepted red USD |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| plain | 15 | 12 | 14 | 0 | 0 | 3 | 0 | 0 | 73/78 | 3 | 3 | 0 | - | 0 | 43 | 217743 | 2.59 | 0.185 |
| crew | 15 | 13 | 15 | 0 | 0 | 2 | 0 | 0 | 69/78 | 0 | 0 | 0 | 4 | 4 | 49 | 182255 | 2.34 | 0.156 |
| crew (fixed brief), `receipt-lines` only | 3 | 3 | 3 | 0 | 0 | 0 | 0 | 0 | 12/12 | 3 | 3 | 0 | 0 | 0 | 70 | 333144 | 0.61 | 0.203 |
| crew-haiku (fixed brief) | 15 | 12 | 13 | 0 | 0 | 2 | 0 | 0 | 69/78 | 2 | 2 | 0 | 2 | 2 | 84 | 339529 | 1.96 | 0.151 |

The four hard stories alone (12 runs per arm; `crew` counts `receipt-lines` with the fixed brief):

| arm | accepted on first red | accepted eventually | FRs traced | production changes | mean wall s | total cost USD | cost per accepted red USD |
|---|---|---|---|---|---|---|---|
| plain | 12 | 12 | 69/69 | 3 (the missing stub, declared) | 43 | 2.14 | 0.178 |
| crew | 12 | 12 | 69/69 | 3 (the missing stub, declared by the main session) | 57 | 2.09 | 0.174 |
| crew, original brief | 12 | 12 | 63/69 | 0 | 50 | 1.90 | 0.158 |
| crew-haiku | 11 | 11 | 63/69 | 2 (the missing stub, declared by the main session) | 82 | 1.60 | 0.145 |

### What it shows

- **A tie, not a win, for the test author.** On the hard stories both arms got every red accepted on the first try with
  every requirement traced. The test author was slower (about 14 s more per story) at the same cost per accepted red.
  On `discount-rules` it again did slightly better (3 of 3 accepted against 2 of 3). That gap is one run, as in the first
  benchmark.
- **The traps did not fire.** In 63 runs there were no compile reds, no RED_IN_TEST, no accepted RED_OTHER and no guard
  blocks. Sonnet and Haiku both read the declared signatures before writing tests: the `ValueTask`, `required` members,
  the new namespace and the struct id. Both faked the clock with a `TimeProvider` subclass and fixed or avoided the
  seeded broken test helper before running red. On `gift-cards`, every arm noticed that `Money` already rejects -5.00 and
  tested only the zero amount. One `crew` run instead built a negative `Money` through reflection, but it said so.
  Tagging across four classes was right in every run: no untagged tests and no test tagged with another class's
  requirement.
- **The one real difference was a loss, now fixed.** With the missing overload, the plain session declared it as a stub
  (step 6.1 of the skill) and covered all four requirements. The test author, told never to touch production code,
  locked a partial red instead (2 of 4 requirements). With the fixed brief it stops, the main session declares the stub
  and starts a second round, and the result matches plain, about 35 s slower. One Haiku run stopped to ask the user
  instead.
- **Skipped status checks come from not loading the skill.** With the firmer wording, the main session ran
  `redgreen status` as its very next call in every run that loaded `/lockflight:feature` (27 of 27). Every skip (4 of 15
  `crew`, 2 of 15 `crew-haiku`) was a run where the main session called the test author directly, without loading the
  skill, so it never saw the instruction. Wording cannot reach those runs. A check that does not depend on the model
  (for example a hook that runs `redgreen status` when the test author returns) would.
- **Haiku** was cheapest per accepted red (0.145 USD on the hard stories), about twice as slow as `crew` on sonnet,
  and stopped in 2 of 15 runs.

### Every run

| story | arm | run | red accepted | compile reds | other rejections | rejection kinds | accepted RED_OTHER | FRs traced | untagged tests | test-author rounds | status after test author | skill loaded | production files changed | production writes | guard blocks | wall s | tokens (all) | test-author tokens | test-author model | cost USD |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| checkout-totals | crew | 1 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 46 | 204633 | 55512 | claude-sonnet-5-5 | 0.16 |
| checkout-totals | crew | 2 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 69 | 225178 | 73373 | claude-sonnet-5-5 | 0.19 |
| checkout-totals | crew | 3 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 75 | 224998 | 73319 | claude-sonnet-5-5 | 0.19 |
| checkout-totals | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 0 | - | yes | 0 | 0 | 0 | 45 | 211385 | 0 | - | 0.19 |
| checkout-totals | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 0 | - | yes | 0 | 0 | 0 | 62 | 264965 | 0 | - | 0.20 |
| checkout-totals | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 0 | - | yes | 0 | 0 | 0 | 48 | 249118 | 0 | - | 0.22 |
| discount-rules | crew | 1 | accepted (red 1) | 0 | 0 | - | 0 | 2/3 | 0 | 1 | no | no | 0 | 0 | 0 | 43 | 131432 | 46436 | claude-sonnet-5-5 | 0.13 |
| discount-rules | crew | 2 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 2/3 | 0 | 1 | first | yes | 0 | 0 | 0 | 39 | 197309 | 47786 | claude-sonnet-5-5 | 0.15 |
| discount-rules | crew | 3 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 2/3 | 0 | 1 | first | yes | 0 | 0 | 0 | 57 | 199453 | 47845 | claude-sonnet-5-5 | 0.16 |
| discount-rules | plain | 1 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 2/3 | 0 | 0 | - | yes | 0 | 0 | 0 | 50 | 224929 | 0 | - | 0.16 |
| discount-rules | plain | 2 | no | 0 | 1 | GREEN_BEFORE×1 | 0 | 0/3 | 0 | 0 | - | yes | 0 | 0 | 0 | 34 | 184989 | 0 | - | 0.14 |
| discount-rules | plain | 3 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 2/3 | 0 | 0 | - | yes | 0 | 0 | 0 | 48 | 224165 | 0 | - | 0.15 |
| gift-cards | crew | 1 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 1 | first | yes | 0 | 0 | 0 | 50 | 203834 | 54764 | claude-sonnet-5-5 | 0.16 |
| gift-cards | crew | 2 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 1 | first | yes | 0 | 0 | 0 | 58 | 207433 | 55309 | claude-sonnet-5-5 | 0.18 |
| gift-cards | crew | 3 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 1 | first | yes | 0 | 0 | 0 | 57 | 207043 | 55063 | claude-sonnet-5-5 | 0.18 |
| gift-cards | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 0 | - | yes | 0 | 0 | 0 | 41 | 228746 | 0 | - | 0.17 |
| gift-cards | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 0 | - | yes | 0 | 0 | 0 | 49 | 236332 | 0 | - | 0.18 |
| gift-cards | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 0 | - | yes | 0 | 0 | 0 | 38 | 236934 | 0 | - | 0.18 |
| loyalty-points | crew | 1 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 1 | no | no | 0 | 0 | 0 | 37 | 126271 | 41493 | claude-sonnet-5-5 | 0.14 |
| loyalty-points | crew | 2 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 1 | no | no | 0 | 0 | 0 | 43 | 126017 | 41242 | claude-sonnet-5-5 | 0.14 |
| loyalty-points | crew | 3 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 1 | no | no | 0 | 0 | 0 | 37 | 125430 | 40667 | claude-sonnet-5-5 | 0.14 |
| loyalty-points | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 0 | - | yes | 0 | 0 | 0 | 41 | 230223 | 0 | - | 0.19 |
| loyalty-points | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 0 | - | yes | 0 | 0 | 0 | 35 | 193032 | 0 | - | 0.17 |
| loyalty-points | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 0 | - | yes | 0 | 0 | 0 | 37 | 166227 | 0 | - | 0.17 |
| receipt-lines | crew | 1 | accepted (red 1) | 0 | 0 | - | 0 | 2/4 | 0 | 1 | first | yes | 0 | 0 | 0 | 46 | 183931 | 35016 | claude-sonnet-5-5 | 0.14 |
| receipt-lines | crew | 2 | accepted (red 1) | 0 | 0 | - | 0 | 2/4 | 0 | 1 | first | yes | 0 | 0 | 0 | 37 | 183961 | 34978 | claude-sonnet-5-5 | 0.14 |
| receipt-lines | crew | 3 | accepted (red 1) | 0 | 0 | - | 0 | 2/4 | 0 | 1 | first | yes | 0 | 0 | 0 | 39 | 186909 | 35554 | claude-sonnet-5-5 | 0.14 |
| receipt-lines | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 0 | - | yes | 1 | 1 | 0 | 42 | 230099 | 0 | - | 0.17 |
| receipt-lines | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 0 | - | yes | 1 | 1 | 0 | 42 | 222409 | 0 | - | 0.15 |
| receipt-lines | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 0 | - | yes | 1 | 1 | 0 | 34 | 162589 | 0 | - | 0.15 |
| receipt-lines | crew (fixed brief) | 1 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 2 | first | yes | 1 | 1 | 0 | 77 | 269855 | 51683 | claude-sonnet-5-5 | 0.20 |
| receipt-lines | crew (fixed brief) | 2 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 2 | first | yes | 1 | 1 | 0 | 84 | 379481 | 51975 | claude-sonnet-5-5 | 0.21 |
| receipt-lines | crew (fixed brief) | 3 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 2 | first | yes | 1 | 1 | 0 | 50 | 350096 | 61326 | claude-sonnet-5-5 | 0.20 |
| checkout-totals | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 74 | 253916 | 103897 | claude-haiku-5-5 | 0.11 |
| checkout-totals | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 92 | 288318 | 135816 | claude-haiku-5-5 | 0.12 |
| checkout-totals | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 0 | 8/8 | 0 | 1 | first | yes | 0 | 0 | 0 | 72 | 259757 | 107052 | claude-haiku-5-5 | 0.12 |
| discount-rules | crew-haiku | 1 | no | 0 | 1 | GREEN_BEFORE×1 | 0 | 0/3 | 0 | 2 | first | yes | 0 | 0 | 0 | 110 | 399628 | 141797 | claude-haiku-5-5 | 0.15 |
| discount-rules | crew-haiku | 2 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 2/3 | 0 | 2 | first | yes | 0 | 0 | 0 | 118 | 444493 | 145649 | claude-haiku-5-5 | 0.16 |
| discount-rules | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 0 | 2/3 | 0 | 1 | first | yes | 0 | 0 | 0 | 61 | 263752 | 112412 | claude-haiku-5-5 | 0.11 |
| gift-cards | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 2 | first | yes | 0 | 0 | 0 | 101 | 471942 | 214152 | claude-haiku-5-5 | 0.15 |
| gift-cards | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 1 | first | yes | 0 | 0 | 0 | 65 | 312002 | 162130 | claude-haiku-5-5 | 0.11 |
| gift-cards | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 0 | 5/5 | 0 | 2 | first | yes | 0 | 0 | 0 | 100 | 432054 | 178170 | claude-haiku-5-5 | 0.15 |
| loyalty-points | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 1 | first | yes | 0 | 0 | 0 | 86 | 257152 | 104245 | claude-haiku-5-5 | 0.12 |
| loyalty-points | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 1 | no | no | 0 | 0 | 0 | 82 | 218448 | 133251 | claude-haiku-5-5 | 0.10 |
| loyalty-points | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 0 | 6/6 | 0 | 2 | first | yes | 0 | 0 | 0 | 86 | 390685 | 139693 | claude-haiku-5-5 | 0.14 |
| receipt-lines | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 2 | first | yes | 1 | 1 | 0 | 80 | 395867 | 105802 | claude-haiku-5-5 | 0.15 |
| receipt-lines | crew-haiku | 2 | no | 0 | 0 | - | 0 | 0/4 | 0 | 1 | no | no | 0 | 0 | 0 | 34 | 172719 | 58225 | claude-haiku-5-5 | 0.10 |
| receipt-lines | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 0 | 4/4 | 0 | 2 | first | yes | 1 | 1 | 0 | 97 | 532200 | 139106 | claude-haiku-5-5 | 0.17 |

## 2026-10-27: test author, four arms

- **Setup:** Claude Code 2.1.309, session model `sonnet` (claude-sonnet-5-5) in every arm, engine built from this
  repository, macOS arm64, .NET 10. Five seeded stories × 3 runs × 4 arms = 60 runs, `--max-budget-usd 4` per run.
- **Arms:** `plain`, the core plugin only (the main session writes the tests); `crew`, the core and crew plugins with the
  test author inheriting the session model; `crew-opus` and `crew-haiku`, the same with the test author's model set to
  `opus` or `haiku` in a generated copy of the agent file.
- **Spend:** 9.41 USD `total_cost_usd` in all (plain 2.11, crew 2.20, crew-opus 3.42, crew-haiku 1.69).
- **Command:** `run.sh --stories shipping,coupons,bulk-pricing,order-summary,discount-rules --runs 3 --arms plain,crew,crew-opus,crew-haiku --max-budget-usd 4 --total-budget-usd 70`

### Per arm

| arm | runs | accepted on first red | accepted eventually | compile reds | other rejections | mean rounds | mean wall s | mean tokens | total cost USD | cost per accepted red USD |
|---|---|---|---|---|---|---|---|---|---|---|
| crew | 15 | 13 | 15 | 0 | 2 | 1.0 | 48 | 175428 | 2.20 | 0.147 |
| crew-haiku | 15 | 12 | 13 | 0 | 3 | 1.0 | 56 | 238216 | 1.69 | 0.130 |
| crew-opus | 15 | 13 | 15 | 0 | 2 | 1.0 | 59 | 198861 | 3.42 | 0.228 |
| plain | 15 | 12 | 14 | 0 | 3 | 0.0 | 36 | 191736 | 2.11 | 0.151 |

Split by story difficulty:

| stories | arm | runs | accepted on first red | accepted eventually | mean wall s | mean tokens | total cost USD | cost per accepted red USD |
|---|---|---|---|---|---|---|---|---|
| shipping, coupons, bulk-pricing | plain | 9 | 9 | 9 | 34 | 183435 | 1.25 | 0.139 |
| shipping, coupons, bulk-pricing | crew | 9 | 9 | 9 | 46 | 163839 | 1.27 | 0.141 |
| shipping, coupons, bulk-pricing | crew-opus | 9 | 9 | 9 | 55 | 192971 | 1.99 | 0.221 |
| shipping, coupons, bulk-pricing | crew-haiku | 9 | 9 | 9 | 51 | 229640 | 0.98 | 0.109 |
| order-summary, discount-rules | plain | 6 | 3 | 5 | 39 | 204189 | 0.86 | 0.172 |
| order-summary, discount-rules | crew | 6 | 4 | 6 | 50 | 192811 | 0.93 | 0.155 |
| order-summary, discount-rules | crew-opus | 6 | 4 | 6 | 65 | 207697 | 1.43 | 0.238 |
| order-summary, discount-rules | crew-haiku | 6 | 3 | 4 | 64 | 251080 | 0.71 | 0.177 |

### What it shows

- **No clear win for the test author on sonnet.** It matched the plain agent on the three plain stories (9 of 9 first-try
  reds each, the same cost per accepted red) and did a little better on the two harder ones (6 of 6 accepted against 5 of
  6), at about 12 s more wall time per story for the extra agent. The difference rests on one run.
- **Only `discount-rules` separated the arms.** It changes an existing method, so two of its scenarios already pass:
  a test for them is `GREEN_BEFORE`. Every arm wrote those tests first. Accepted runs then dropped them and reported that
  FR-001 has no red/green test; the runs counted as "no" stopped and asked the user whether to drop the tests or change
  the plan. Both are defensible; neither arm edited production code to force a red.
- **Haiku is cheapest per accepted red on the plain stories** (0.109 against 0.141 USD) but used more tokens and stopped
  on the judgement story in 2 of 3 runs. **Opus** matched sonnet's results at about 1.6 times the cost.
- **No compile reds, no production edits and no guard blocks** in any of the 60 runs: the seeded stories with declared
  stubs are easy for every arm, so they cannot show the failure modes the brief targets (compile reds, tests that throw in
  their own code). Harder stories are needed before this benchmark can decide.
- **The skill's own check:** in 5 of the 45 crew runs the main session did not run `redgreen status` after the agent
  returned, although the feature skill says to. The engine judged those reds anyway; the result columns come from the
  ledger, not from the session.

### Every run

| story | arm | run | red accepted | compile reds | other rejections | rejection kinds | test-author rounds | production files changed | guard blocks | wall s | tokens (all) | test-author tokens | test-author model | cost USD |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| bulk-pricing | crew | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 49 | 185514 | 37006 | claude-sonnet-5-5 | 0.15 |
| bulk-pricing | crew | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 51 | 185498 | 36958 | claude-sonnet-5-5 | 0.15 |
| bulk-pricing | crew | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 45 | 187252 | 36402 | claude-sonnet-5-5 | 0.15 |
| bulk-pricing | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 72 | 244451 | 96037 | claude-haiku-5-5 | 0.11 |
| bulk-pricing | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 58 | 236292 | 87219 | claude-haiku-5-5 | 0.11 |
| bulk-pricing | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 45 | 214266 | 64868 | claude-haiku-5-5 | 0.11 |
| bulk-pricing | crew-opus | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 62 | 202941 | 53164 | claude-opus-5-5 | 0.24 |
| bulk-pricing | crew-opus | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 70 | 202851 | 51199 | claude-opus-5-5 | 0.22 |
| bulk-pricing | crew-opus | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 52 | 203034 | 51470 | claude-opus-5-5 | 0.23 |
| bulk-pricing | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 38 | 188717 | 0 | - | 0.14 |
| bulk-pricing | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 40 | 191895 | 0 | - | 0.16 |
| bulk-pricing | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 33 | 191199 | 0 | - | 0.15 |
| coupons | crew | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 36 | 183878 | 36066 | claude-sonnet-5-5 | 0.14 |
| coupons | crew | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 34 | 184493 | 36205 | claude-sonnet-5-5 | 0.14 |
| coupons | crew | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 52 | 121264 | 36534 | claude-sonnet-5-5 | 0.13 |
| coupons | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 52 | 260363 | 109084 | claude-haiku-5-5 | 0.11 |
| coupons | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 49 | 235567 | 84114 | claude-haiku-5-5 | 0.11 |
| coupons | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 45 | 222737 | 71280 | claude-haiku-5-5 | 0.11 |
| coupons | crew-opus | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 42 | 167516 | 48782 | claude-opus-5-5 | 0.20 |
| coupons | crew-opus | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 64 | 198632 | 49188 | claude-opus-5-5 | 0.21 |
| coupons | crew-opus | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 65 | 168964 | 49836 | claude-opus-5-5 | 0.21 |
| coupons | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 23 | 183116 | 0 | - | 0.13 |
| coupons | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 27 | 187517 | 0 | - | 0.14 |
| coupons | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 55 | 221927 | 0 | - | 0.15 |
| discount-rules | crew | 1 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 54 | 208250 | 59171 | claude-sonnet-5-5 | 0.16 |
| discount-rules | crew | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 41 | 187263 | 36569 | claude-sonnet-5-5 | 0.15 |
| discount-rules | crew | 3 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 58 | 199763 | 48635 | claude-sonnet-5-5 | 0.16 |
| discount-rules | crew-haiku | 1 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 67 | 270209 | 121371 | claude-haiku-5-5 | 0.11 |
| discount-rules | crew-haiku | 2 | no | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 66 | 263370 | 74731 | claude-haiku-5-5 | 0.14 |
| discount-rules | crew-haiku | 3 | no | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 71 | 269034 | 82390 | claude-haiku-5-5 | 0.13 |
| discount-rules | crew-opus | 1 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 86 | 243449 | 93822 | claude-opus-5-5 | 0.25 |
| discount-rules | crew-opus | 2 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 1 | 0 | 0 | 59 | 214108 | 62226 | claude-opus-5-5 | 0.24 |
| discount-rules | crew-opus | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 50 | 148632 | 62927 | claude-opus-5-5 | 0.20 |
| discount-rules | plain | 1 | no | 0 | 1 | GREEN_BEFORE×1 | 0 | 0 | 0 | 32 | 186527 | 0 | - | 0.14 |
| discount-rules | plain | 2 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 0 | 0 | 39 | 256946 | 0 | - | 0.16 |
| discount-rules | plain | 3 | accepted (red 2) | 0 | 1 | GREEN_BEFORE×1 | 0 | 0 | 0 | 45 | 188725 | 0 | - | 0.14 |
| order-summary | crew | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 53 | 188020 | 36452 | claude-sonnet-5-5 | 0.16 |
| order-summary | crew | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 53 | 185806 | 36973 | claude-sonnet-5-5 | 0.15 |
| order-summary | crew | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 42 | 187763 | 36962 | claude-sonnet-5-5 | 0.15 |
| order-summary | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 55 | 248609 | 97055 | claude-haiku-5-5 | 0.11 |
| order-summary | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 47 | 242090 | 90764 | claude-haiku-5-5 | 0.11 |
| order-summary | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 79 | 213166 | 93730 | claude-haiku-5-5 | 0.11 |
| order-summary | crew-opus | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 64 | 203142 | 53671 | claude-opus-5-5 | 0.24 |
| order-summary | crew-opus | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 63 | 231979 | 80979 | claude-opus-5-5 | 0.27 |
| order-summary | crew-opus | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 69 | 204870 | 52628 | claude-opus-5-5 | 0.23 |
| order-summary | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 41 | 221393 | 0 | - | 0.14 |
| order-summary | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 32 | 153879 | 0 | - | 0.13 |
| order-summary | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 43 | 217663 | 0 | - | 0.15 |
| shipping | crew | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 78 | 186356 | 35236 | claude-sonnet-5-5 | 0.15 |
| shipping | crew | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 40 | 119904 | 35159 | claude-sonnet-5-5 | 0.13 |
| shipping | crew | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 30 | 120392 | 35750 | claude-sonnet-5-5 | 0.13 |
| shipping | crew-haiku | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 38 | 209913 | 58427 | claude-haiku-5-5 | 0.11 |
| shipping | crew-haiku | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 55 | 270892 | 85601 | claude-haiku-5-5 | 0.12 |
| shipping | crew-haiku | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 45 | 172275 | 87140 | claude-haiku-5-5 | 0.09 |
| shipping | crew-opus | 1 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 48 | 188197 | 38821 | claude-opus-5-5 | 0.24 |
| shipping | crew-opus | 2 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 50 | 202632 | 51597 | claude-opus-5-5 | 0.22 |
| shipping | crew-opus | 3 | accepted (red 1) | 0 | 0 | - | 1 | 0 | 0 | 46 | 201972 | 49820 | claude-opus-5-5 | 0.22 |
| shipping | plain | 1 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 35 | 150401 | 0 | - | 0.12 |
| shipping | plain | 2 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 25 | 151271 | 0 | - | 0.12 |
| shipping | plain | 3 | accepted (red 1) | 0 | 0 | - | 0 | 0 | 0 | 32 | 184868 | 0 | - | 0.14 |

## 2026-10-27: pilot, two arms

Same setup, three stories (shipping, coupons, bulk-pricing) × 2 runs × 2 arms, `--max-budget-usd 2.50`; 1.65 USD in all.
Every run had its red accepted on the first try, with no compile reds, rejections, production edits or guard blocks.

| arm | runs | accepted on first red | mean wall s | mean tokens | mean cost USD |
|---|---|---|---|---|---|
| plain | 6 | 6 | 37 | 176782 | 0.14 |
| crew | 6 | 6 | 43 | 143240 | 0.14 |

Before the pilot, a smoke run showed the test author giving up after a Bash heredoc was refused: the refusal also
cancelled the engine command sent in the same message, and the agent read that as "Bash is not available". The brief now
says to write files with Write/Edit and to run each engine command as a Bash call of its own.
