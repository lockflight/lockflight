## Lockflight rules (they take precedence over the rules above)

This project checks tasks.md with `dotnet lockflight phase check tasks`. Tasks that break these rules are rejected, so follow them
even where the text above says otherwise:

- **Tests are MANDATORY and come first.** Disregard "Tests are OPTIONAL" above. Every user story gets test tasks, and they come
  before the story's implementation tasks. Stub declarations may come before the tests.
- **Every user-story task cites the requirements it serves**, right after the story label:
  `- [ ] T012 [P] [US1] (FR-001, FR-003) Description with file path`. Cite only IDs that spec.md defines. Setup, foundational
  and polish tasks (no story label) need no requirement IDs.
- **Red/green (.NET).** When a story adds public members, its first task declares them as stubs whose bodies are
  `throw new NotImplementedException();`. The test tasks then write xUnit v3 tests tagged `[Trait("Requirement", "FR-001")]`
  against the stubs. `dotnet lockflight redgreen red` must accept the red before the implementation tasks start, and
  `dotnet lockflight redgreen green` closes the story.
- After writing tasks.md, run `dotnet lockflight phase check tasks` and fix every item that fails.
