## Lockflight rules (they take precedence over the rules above)

This project checks tasks.md with `.lockflight/bin/lockflight phase check tasks`. Tasks that break these rules are rejected,
so follow them even where the text above says otherwise:

- **Tests are MANDATORY and come first.** Disregard "Tests are OPTIONAL" above. Every user story gets test tasks, and they come
  before the story's implementation tasks. Stub declarations may come before the tests.
- **Make each task's role visible.** A task counts as a test task when it sits under a `### Tests for User Story N` heading,
  starts with test wording ("Write tests …", "Spec for …", "Cover … with tests") or names the test file it writes
  (`*.spec.ts`, `*.test.ts`, `*Tests.cs`, a `*.Tests/` project). A stub task says "stub", "skeleton", "declare signatures" or
  `NotImplementedException`. Anything else in a story counts as implementation and must come after the story's last test task.
- **Every user-story task cites the requirements it serves**, preferably right after the story label:
  `- [ ] T012 [P] [US1] (FR-001, FR-003) Description with file path`. An `FR-xxx` anywhere on the line counts. Cite only IDs that spec.md defines. Setup, foundational
  and polish tasks (no story label) need no requirement IDs.
- **Red/green (.NET).** When a story adds public members, its first task declares them as stubs whose bodies are
  `throw new NotImplementedException();`. The test tasks then write xUnit v3 tests tagged `[Trait("Requirement", "FR-001")]`
  against the stubs. `.lockflight/bin/lockflight redgreen red` must accept the red before the implementation tasks start, and
  `.lockflight/bin/lockflight redgreen green` closes the story.
- After writing tasks.md, run `.lockflight/bin/lockflight phase check tasks` and fix every item that fails. A failure names
  each task, its line and why it was classified as it was.
