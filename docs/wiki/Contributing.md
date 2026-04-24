# Contributing

This page covers coding conventions and the wiki-publishing flow. Before opening a PR, skim [Architecture](Architecture) and [Testing](Testing).

## Coding conventions

| Area | Rule |
|---|---|
| API style | ASP.NET Core **Minimal APIs**. Route groups per service under `Endpoints/`. |
| DTOs | All HTTP request/response shapes live in `ApiModels/`. Never expose `Models/` entities directly. |
| Domain | Internal entities go in `Models/`. EF configuration lives alongside the `DbContext` in `Infrastructure/Data/`. |
| Events | New events are classes under `IntegrationEvents/` that derive from `Event`. Publish via `IEventBus` (through the Outbox). Subscribe via `IEventHandler<TEvent>` + `AddEventHandler<,>()`. Shipping events follow the same pattern. |
| Cross-cutting | Prefer an extension in [`ECommerce.Shared`](Shared-Library) over copy-paste across services. |
| Config | Put env-var-overridable keys in `appsettings.json`. Secrets never in repo. |
| Migrations | `dotnet ef migrations add <Descriptive_Name>` from the service project. Check the SQL script before committing. |

## Tests

- New endpoint → at minimum an integration test through `WebApplicationFactory<Program>`.
- New event publish → a round-trip test that subscribes, triggers, and asserts receipt.
- Test names follow `Given_When_Then`.
- See [Testing](Testing) for the full guide.

## PRD / Plan workflow

Substantial changes start as a PRD under [`docs/prd/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/prd) and a phased plan under [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans). Prior art:

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
