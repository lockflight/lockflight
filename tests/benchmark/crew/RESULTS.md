# Crew benchmark results

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
