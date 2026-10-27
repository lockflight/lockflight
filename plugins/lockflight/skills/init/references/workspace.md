# Workspace shells

Read this when the project is a workspace shell: a root repository with child folders that hold their own `.git` (the
service repositories).

## Before `init`

The plugin must be enabled at **user scope**. Claude Code loads project settings only from the directory a session starts
in, so without it nothing guards a session started inside a service. `scripts/dev-install.sh` already does this; otherwise
ask the user to run `claude plugin install lockflight@lockflight --scope user`. It changes their own settings, so never run it
yourself. Tell them to start sessions at the shell root, where their own hooks load too.

## After `init`

`init` lists the services under `units:` and adds the child repositories to `.gitignore`.

Run `.lockflight/bin/lockflight workspace sync`. It writes into each .NET service repository its CI policy:
`lockflight.unit.yaml`, the engine pin, the service's baselines and the workflow. Tell the user to review and commit in each
service repository: a service's PR is judged by these files from its base branch. Suggest a CODEOWNERS entry in each service
for `lockflight.unit.yaml`, the pin and `.github/workflows/`.

## Verify and hand over

In a workspace, `doctor` also checks that each unit is cloned, has no `lockflight.yaml` of its own, is ignored by the shell
and carries the current unit CI policy; and that the plugin is enabled at user scope and no SDK pin keeps the engine from
running.

- The Stop gate builds and tests only the changed services, at most two at a time. `doctor` names services that are slow at
  Stop; for those, set `units[].test.stopFilter`.
- Known-failing tests: the user runs `.lockflight/bin/lockflight baseline tests --path <unit>` once per service, in a separate
  terminal window, and merges it in its own pull request.
- A feature's tasks should name their services with `[unit:services/orders]`, so red/green builds only those.
