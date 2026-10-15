# Acme workspace

This shell holds the specs and the AI setup for the Acme services. Each service is its own repository, cloned beneath it:

| Path | Service | Stack |
|---|---|---|
| `services/orders` | Orders API | .NET 8, xUnit v2 |
| `services/billing` | Billing | .NET 10, xUnit v3 |
| `web/admin` | Admin UI | TypeScript |

Run Claude Code from this directory. Specs live in `specs/` and may span services.
