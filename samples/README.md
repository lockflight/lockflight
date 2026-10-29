# Samples

Small, synthetic projects the cross-component suites run Lockflight on. They are fixtures for contributors, not templates
for your own repository, and none of them contains client data.

| Sample | What it is | Used by |
|---|---|---|
| [`dotnet-single`](dotnet-single/) | A minimal ASP.NET Core API (`Shop.Api`) with an xUnit v3 test project (`Shop.Tests`). `ContainerSmokeTests` needs Docker. | [`tests/e2e/lifecycle.sh`](../tests/e2e/lifecycle.sh) copies it and runs the whole lifecycle on the copy. |
| [`dotnet-nunit`](dotnet-nunit/) | A class library (`Inventory`) with an NUnit 4 test project (`Inventory.Tests`) on VSTest, one `[TestCase]` row ignored. | [`tests/e2e/lifecycle-nunit.sh`](../tests/e2e/lifecycle-nunit.sh) runs red/green and trace on a copy, through VSTest and with NUnit's runner on Microsoft.Testing.Platform. |
| [`workspace-shell`](workspace-shell/) | A synthetic workspace shell: `shell/` holds `CLAUDE.md`, the team's own `.claude/` (hooks including a format-on-edit and a protected-file guard, an agent, a custom skill), stock Spec Kit 1.1.0 with a customised constitution and plan template, and three existing features (two share the number 002); `children/` holds `services/orders` (.NET 8, xUnit v2 on VSTest, `.sln`), `services/billing` (.NET 10, xUnit v3 on MTP, `.slnx`) and `web/admin` (TypeScript). | [`tests/workspace/make-shell.sh`](../tests/workspace/make-shell.sh) materializes it with a git repo per child; the workspace suites run on it. |

For one repository with several services, see [monorepos](../docs/guides/getting-started.md#monorepos).

## Next

- [Tests](../tests/README.md): the suites that run on these samples
- [Contributing](../CONTRIBUTING.md): build, test and open a pull request
