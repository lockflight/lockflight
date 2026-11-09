| 0 | PASS; also a gate run whose steps were all N/A (for example `edit` with no changed C# lines) |
| 1 | FAIL |
| 2 | blocked: a hook decision, or a human-only command run by the agent |
| 3 | ERROR: the engine or a tool it ran broke (including a tool that is not on PATH). This is not a verdict about the code |
| 4 | configuration: invalid command line (unknown option, missing value), manifest or target |
| 5 | NOT-EARNED: nothing to judge, for example zero tests. Never a pass |
