---
type: regex
target: { source: file, path: specs/001-overdue-fines/spec.md }
flags: i
pattern: '### User Story 1\s*[-:–—][^\n]*\n(?:(?!##)[^\n]*\n)*?[^\n]*\*\*Given\*\*[^\n]*\*\*When\*\*[^\n]*\*\*Then\*\*'
---
