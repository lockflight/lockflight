---
type: regex
target: { source: file, path: specs/001-overdue-fines/tasks.md }
flags: m
pattern: '^- \[ \] T\d{3,}\s+(\[P\]\s+)?\[US1\][^\n]*FR-00\d[\s\S]*^- \[ \] T\d{3,}\s+(\[P\]\s+)?\[US2\][^\n]*FR-00\d'
---
