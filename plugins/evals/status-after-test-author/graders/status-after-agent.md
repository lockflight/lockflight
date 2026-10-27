---
type: tool_order
before: { tool: Agent, input_match: 'lockflight-crew:test-author' }
after: { tool: Bash, input_match: 'lockflight(\.exe)?\s+(?:\S+\s+)*redgreen\s+status' }
arm: both
---
