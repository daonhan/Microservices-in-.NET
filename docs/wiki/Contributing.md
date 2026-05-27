# Contributing

This page covers coding conventions and the wiki-publishing flow. Before opening a PR, skim [Architecture](Architecture) and [Testing](Testing).

## Coding conventions

| Area | Rule |
|---|---|
| API style | ASP.NET Core **Minimal APIs**. Route groups and handlers live in `Features/<Slice>/` per [ADR-0012](../adr/0012-clean-arch-vsa-default-service-shape.md). |
| DTOs | HTTP request/response shapes live with the vertical slice that owns them. Never expose `Domain/` entities directly. |
| Domain | Aggregates and invariants live in `Domain/`. EF configuration lives with the `DbContext` under `Infrastructure/Data/`. |
| Events | Service-owned payloads live in `Contracts/Integration/`; handlers and integration maps live in the owning `Features/<Slice>/`. Publish via `IEventBus` through the outbox. Subscribe via `IEventHandler<TEvent>` + `AddEventHandler<,>()`. |
| Cross-cutting | Prefer the matching [`ECommerce.Shared.*`](Shared-Library) capability package over copy-paste across services. |
| Config | Put env-var-overridable keys in `appsettings.json`. Secrets never in repo. |
| Migrations | `dotnet ef migrations add <Descriptive_Name>` from the service project. Check the SQL script before committing. |

## Tests

- New endpoint → at minimum an integration test through `WebApplicationFactory<Program>`.
- New event publish → a round-trip test that subscribes, triggers, and asserts receipt.
- Test names follow `Given_When_Then`.
- See [Testing](Testing) for the full guide.

### Local path and WSL setup

Prefer a no-space checkout path for local tooling. If the repo lives under a Windows
path with spaces, create a junction and run .NET commands from the junction:

```powershell
New-Item -ItemType Directory -Path C:\src -Force
New-Item -ItemType Junction -Path C:\src\nhamnhi -Target "D:\Preparing\Microservices in .NET\Nhamnhi"
cd C:\src\nhamnhi
```

When testing from WSL, avoid running this repo directly from `/mnt/d` if MSBuild fails
with `MSB3248` or `No such device`. Copy the checkout to the WSL native filesystem and
make sure WSL has a .NET SDK that supports `net10.0`:

```bash
dotnet --list-sdks

mkdir -p ~/src
rsync -a --delete "/mnt/d/Preparing/Microservices in .NET/Nhamnhi/" ~/src/Nhamnhi/
cd ~/src/Nhamnhi
dotnet restore
```

### EF migrations in the shared library

Run shared-library EF commands from the no-space path when assembly loading fails from
the original checkout path:

```powershell
cd C:\src\nhamnhi
dotnet restore
dotnet ef dbcontext list --project .\shared-libs\ECommerce.Shared.DeadLetter\ECommerce.Shared.DeadLetter.csproj
dotnet ef migrations add Add_DeadLetterMessage --context ECommerce.Shared.Infrastructure.DeadLetter.DeadLetterDbContext --project .\shared-libs\ECommerce.Shared.DeadLetter\ECommerce.Shared.DeadLetter.csproj
```

Verify migration/model consistency before committing:

```powershell
cd C:\src\nhamnhi
dotnet ef migrations has-pending-model-changes `
  --context ECommerce.Shared.Infrastructure.DeadLetter.DeadLetterDbContext `
  --project .\shared-libs\ECommerce.Shared.DeadLetter\ECommerce.Shared.DeadLetter.csproj
```

Expected clean output:

```text
No changes have been made to the model since the last migration.
```

### Pre-commit hooks

This repo uses Husky.Net. Install both the Git hook path and the actual pre-commit
hook file in a new clone:

```powershell
dotnet tool restore
dotnet husky install
dotnet husky add pre-commit -c "dotnet husky run --group pre-commit"
```

Verify the hook command before opening a PR:

```powershell
dotnet husky run --group pre-commit
```

If this fails in `dotnet format --verify-no-changes`, run `dotnet format` and review
the diff before committing. Existing line-ending problems appear as `ENDOFLINE` errors.

If Husky fails in the `dotnet build --no-restore` step with `MSB3248` (`No such device`)
on a virtiofs sandbox, treat it as an environment issue (commonly root-owned `bin/obj`
artifacts), not a reason to bypass hooks. Run the commit from a host where hooks pass,
or clean build outputs in a writable shell and retry:

```bash
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
dotnet restore
dotnet husky run --group pre-commit
```

## PRD / Plan workflow

Substantial changes start as a PRD under [`docs/prd/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/prd) and a phased plan under [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans). Prior art:

The `/spec-pipeline`, AFK, and custom-agent workflow turns PRDs and plans into GitHub issues that an agent can execute with the repo's feedback loops. Agents should load the root `CLAUDE.md`, the relevant service `CLAUDE.md`, and the issue/plan context before editing; those grounding files are the source of service-local divergences from the default layout.

- [`PRD-Inventory.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Inventory.md)
- [`PRD-Observability.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Observability.md)
- [`PRD-ApiGateway-Yarp.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-Yarp.md)
- [`PRD-Wiki.md`](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Wiki.md)


## Editing the Wiki

**The source of truth is `docs/wiki/` in the main repo.** Do not edit pages directly on GitHub — those edits bypass code review and will be overwritten.

Flow:

1. Open a PR that edits files under `docs/wiki/`.
2. After merge, publish to the wiki remote:

```bash
# Clone the wiki repo alongside the main repo (first time only)
git clone https://github.com/daonhan/Microservices-in-.NET.wiki.git

# Mirror and publish
cd Microservices-in-.NET.wiki
rm -f *.md
cp ../Microservices-in-.NET/docs/wiki/*.md .
git add -A
git commit -m "Sync wiki from docs/wiki/"
git push origin master
```

On Windows PowerShell:

```powershell
cd Microservices-in-.NET.wiki
Remove-Item *.md
Copy-Item ..\Microservices-in-.NET\docs\wiki\*.md .
git add -A
git commit -m "Sync wiki from docs/wiki/"
git push origin master
```

### Ralph automation

For agent-driven development and feedback loops, see the `ralph/` folder for Bash/PowerShell scripts and prompt design. These automate PRD, plan, and documentation workflows.

## Commit style

Short imperative subject, optional body explaining why. Reference PRD or issue numbers when applicable.
