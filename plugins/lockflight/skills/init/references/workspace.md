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

Each service repository then needs the shell's policy in its own CI. The preferred way is the pointer: in each service
repository, the user runs `lockflight init --unit-of <shell url> --unit-path <its path in units:>`. It writes `shell:` into the
service's `lockflight.yaml` and a CI job that clones the shell read-only and judges with its policy. Tell the user that a
private shell needs a read-only token in the `LOCKFLIGHT_SHELL_TOKEN` CI secret (or a deploy key for an SSH URL), and to
review and commit in each service repository. Suggest a CODEOWNERS entry in each service for `lockflight.yaml` and the CI file.

A service that an older release set up with `workspace sync` (removed) holds `lockflight.unit.yaml`, which the engine no longer
reads. Tell the user to run `lockflight update --project` in that service, checked out inside the shell, in their own terminal:
it moves the service to the pointer. `doctor` in the shell names such services.

## Verify and hand over

In a workspace, `doctor` also checks that each unit is cloned, has no `lockflight.yaml` of its own other than a `shell:` pointer
to this shell, is ignored by the shell and holds no vendored `lockflight.unit.yaml`; and that the plugin is enabled at user scope and no SDK pin keeps the engine from
running.

- The Stop gate builds and tests only the changed services, at most two at a time. `doctor` names services that are slow at
  Stop; for those, set `units[].test.stopFilter`.
- Known-failing tests: the user runs `.lockflight/bin/lockflight baseline tests --path <unit>` once per service, in a separate
  terminal window, and merges it in its own pull request.
- A feature's tasks should name their services with `[unit:services/orders]`, so red/green builds only those.
