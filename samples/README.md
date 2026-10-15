# Samples

| Sample | Used by |
|---|---|
| [`dotnet-single`](dotnet-single/) | A minimal ASP.NET Core API (`Shop.Api`) with an xUnit v3 test project (`Shop.Tests`). [`tests/e2e/lifecycle.sh`](../tests/e2e/lifecycle.sh) copies it and runs the whole lifecycle on the copy. `ContainerSmokeTests` needs Docker. |

| [`workspace-shell`](workspace-shell/) | A synthetic workspace shell: `shell/` holds `CLAUDE.md`, the team's own `.claude/` (hooks including a format-on-edit and a protected-file guard, an agent, a custom skill), stock Spec Kit 1.1.0 with a customised constitution and plan template, and three existing features (two share the number 002); `children/` holds `services/orders` (.NET 8, xUnit v2 on VSTest, `.sln`), `services/billing` (.NET 10, xUnit v3 on MTP, `.slnx`) and `web/admin` (TypeScript). [`tests/workspace/make-shell.sh`](../tests/workspace/make-shell.sh) materializes it with a git repo per child. No client data. |

The monorepo sample arrives with that topology.
