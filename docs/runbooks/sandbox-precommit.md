# Sandbox pre-commit policy (WSL / virtiofs / Docker)

Activate Husky.Net once per clone:

```bash
dotnet tool restore && dotnet husky install
```

Hook runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, then Basket tests only — **run other suites manually before pushing cross-service changes**.

## Known sandbox failure

`MSB3248 No such device` on `dotnet build --no-restore` (or on `ECommerce.Shared.Tests` reading a freshly built shared DLL) caused by root-owned or sandbox-created `bin`/`obj`. **Not a regression.**

## Mandatory order before any commit in sandbox

1. Clean + restore + rerun hook:
   ```bash
   find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
   dotnet tool restore
   dotnet restore && dotnet husky run --group pre-commit
   ```
2. If still `MSB3248`, retry once more after `dotnet restore --force`.
3. If hook still fails: **STOP. Do not commit.** Report blocker to user with the exact failing command + error. User commits from host.

## Hard prohibitions

No exceptions, no "sandbox-only" escape hatch:

- No `--no-verify`, no `-c core.hooksPath=`, no skipping `dotnet format` / `dotnet build` / tests.
- No `Hooks-Deferred:` / `Validation-Deferred:` / similar commit-message footer.
- No "passed clean in sandbox, defer remainder to host" partial commits.
- No closing the issue / marking task done while validation is deferred.

## Rationale

A commit with deferred validation pollutes history, blocks downstream automation, and shifts unfinished work onto the user without their consent. The correct sandbox outcome when hooks cannot pass is **handoff, not commit**.
