# PRD: Documentation refresh — Saga orchestrator cutover + drift gate (2026-05)

> Companion docs: [ADR-0010](../adr/0010-saga-orchestrator-supersedes-choreography.md) (saga orchestrator supersedes choreography), [PRD-Saga-Orchestrator](PRD-Saga-Orchestrator.md), [saga-orchestrator-strangler runbook](../runbooks/saga-orchestrator-strangler.md).

## Context

The 2026-05-18 saga orchestrator cutover (#132/#136/#137) removed every choreography handler from Order/Inventory/Payment/Shipping and replaced them with a dedicated `saga-microservice` on port 8008 that drives the order saga via commands. ADR-0010 was filed; ADR-0008 superseded; a strangler runbook landed. Earlier deep work in the same window — StockItem aggregate (#117/#118), messaging provider abstraction (RabbitMQ default, ASB switch, DLQ poller), provider-agnostic DLQ runbook, outbox/UoW deep seam, payment depth — also touched cross-cutting concerns. The code state is current; the docs are not.

`README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md`, every page under `docs/wiki/`, and the CONTEXT link tree still describe a choreographed seven-service platform. `Service-Saga.md` does not exist, port 8008 is unlisted, ADR-0010 is unlinked, the strangler runbook is unlinked, the Roadmap "Delivered" list stops before the cutover, the saga sequence diagrams still show service-to-service event hops instead of orchestrator-driven commands, and `Integration-Events.md` has no command catalog. Drift is uniformly historical; nothing in the docs is forward-looking.

This PRD covers two outcomes: (1) one reconciliation pass that brings every documented surface to HEAD-as-of-2026-05-19, and (2) a drift gate that prevents the same backlog from re-accumulating after the next architectural change.

## Problem Statement

As a developer, recruiter, or AI agent reading this repo cold, I am told the platform uses saga choreography with no central orchestrator. Every surface — root README, narrated CONTEXT, wiki Home, Architecture, Diagram-Saga, Integration-Events, the Roadmap, the ADR list, the AGENTS guidance, the Copilot instructions — confirms that mental model. The code says otherwise: the orchestrator has been live since 2026-05-18, every choreography handler is deleted, and a Saga service on port 8008 owns the workflow. The mismatch is not a minor stale line in one file; it is the load-bearing architectural claim of the entire repo, repeated across roughly fifteen documents.

Concretely, I cannot answer "what service owns the order saga?" by reading the docs. The diagrams show events flowing between Order, Inventory, Payment, and Shipping; in HEAD the only edges that exist run between those services and the orchestrator. I cannot find a Service-Saga page, because there is none. I cannot find the orchestrator port, because the services table tops out at 8007. I cannot follow a link from CONTEXT.md to ADR-0010, because the ADR list ends at 0009. If I am an AI agent grounded by CONTEXT.md plus CLAUDE.md, I will generate code that subscribes to events on the participant side and "completes" a saga handler — code that hasn't existed for a week. The doc-drift cost is therefore not aesthetic; it is generating incorrect changes, incorrect interview answers about the repo, and a misleading portfolio claim.

The second problem is that nothing in the repo's process catches this. Pre-commit runs `dotnet format` + build + Basket tests. Azure Pipelines build and deploy each service. There is no gate that says "if you removed a choreography handler, did you update the diagram?" or "does the services table in README.md include every service in docker-compose.yaml?" After this cutover, a reasonable forecast is that the next time a load-bearing decision is reversed — refund saga goes live, ASB becomes default, a service is renamed — the docs will drift the same way and the same backlog will need a one-shot refresh PRD. The drift gate is the part of this PRD that pays for itself the next time.

## Solution

Two parts.

**Part A — Reconciliation pass.** Walk every surface listed below in one PR set, replacing every "choreography / no orchestrator" claim with the orchestrator-led shape, adding the Saga service (port 8008, SQL Server) to every catalog, linking ADR-0010 and the strangler runbook, adding a new `docs/wiki/Service-Saga.md` page, refreshing `Diagram-Saga.md` and the saga sequence in `Architecture.md` and `Integration-Events.md` to show orchestrator-driven commands + reply events, marking ADR-0008 as `Superseded by ADR-0010`, and extending `Roadmap.md` "Delivered" with the saga work (#124–#137) plus the messaging provider abstraction and refund saga (#130). Where wording can be reused (a single Saga service row, a single canonical orchestrator sequence diagram, a single command catalog), it is written once and referenced from the other surfaces.

**Part B — Drift gate.** Add a PR-template checkbox section, a `scripts/check-doc-drift.ps1` (PowerShell on Windows host; bash equivalent for Linux CI) that greps banned phrases ("choreograph*", "no central orchestrator", "no orchestrator") outside an allowlist of historical-context docs (`docs/adr/0008-*.md`, this PRD, the strangler runbook), and checks that the services table in `README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md`, and `docker-compose.yaml` services list stay in sync (same count, same ports). Wire the script as an Azure Pipelines step in a new shared `doc-drift-pipeline.yml` invoked from each per-service pipeline (or a single root pipeline; decision deferred to plan). Add a markdown link checker (`lychee` or `markdown-link-check`) as a second Azure Pipelines step to catch unresolved references like the current ADR-0010 link from CONTEXT.md (which would have failed today). Both steps fail the build; neither runs in pre-commit (pre-commit stays fast).

The gate is opinionated by design: it does not try to parse architecture, just to keep specific load-bearing strings out of specific files. False positives are accepted (allowlist exists) over false negatives. The script outputs a numbered list of offending files + line numbers, matching the existing operator-API-style "API + tooling over HTML" preference for operator surfaces.

## User Stories

1. As a recruiter opening this repo for the first time, I want the README's services table to list all eight services that actually run, so that the headline architecture pitch matches what `docker compose up` starts.
2. As a recruiter, I want the CONTEXT.md "what it is" section to describe orchestration-led saga coordination, so that the repo's portfolio narrative matches its current architecture.
3. As a developer joining the project, I want a `docs/wiki/Service-Saga.md` page describing the saga service's responsibility, state model, command catalog, and how it interacts with the four participants, so that I have one place to read the saga workflow.
4. As a developer, I want `docs/wiki/Diagram-Saga.md` and the saga sequence in `Architecture.md` and `Integration-Events.md` to show the orchestrator driving commands and consuming reply events, so that the diagrams match the runtime topology.
5. As a developer, I want `Integration-Events.md` to include a saga command catalog (`ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`, `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `CreateShipmentCommand`, `CancelShipmentCommand`) alongside the existing event catalog, so that both halves of the orchestrator contract are documented.
6. As an AI agent grounded by CLAUDE.md + CONTEXT.md + AGENTS.md, I want consistent saga-orchestrator language across all three, so that I never generate code that subscribes a participant service to an event the orchestrator now handles.
7. As an architecture reader, I want ADR-0008 to be marked `Superseded by ADR-0010` with the reasoning preserved, and ADR-0010 to be linked from the CONTEXT ADR list and from the README pattern table, so that the decision history reads correctly.
8. As an operator, I want `CONTEXT.md`'s runbooks list to include `saga-orchestrator-strangler.md`, so that the rollback path is discoverable from the narrated entry point.
9. As a developer, I want `docs/wiki/Home.md`'s "Where to go next" table to include a link to the new Service-Saga page and the `_Sidebar.md` Services group to include it, so that wiki navigation matches the service catalog.
10. As a developer, I want `Roadmap.md` "Delivered" to include the saga orchestrator cutover (#132/#136/#137), refund saga (#130), saga reaper (#126), compensation matrix (#125), operator saga API (#128), StockItem aggregate (#117/#118), and messaging provider abstraction, so that the repo's narrative of what shipped is current.
11. As a developer, I want `.github/copilot-instructions.md` to reference the current `AddPlatform*` helper names (matching CLAUDE.md) and the orchestrator-led saga shape, so that Copilot's grounded context is consistent with Claude's.
12. As a maintainer, I want a CI step that fails the Azure Pipelines build when banned phrases reappear in non-allowlisted docs, so that an architectural reversal cannot ship without a doc update.
13. As a maintainer, I want a CI step that fails the build when a markdown link in `README.md`, `CONTEXT.md`, `docs/wiki/*`, `docs/adr/*`, or `docs/runbooks/*` 404s, so that ADR/runbook references stay live.
14. As a maintainer, I want the drift-gate script's allowlist to be a single file with explicit entries (not regex-by-convention), so that historical-context exemptions are obvious in PR review.
15. As a contributor opening a PR that changes a saga, gateway, messaging, or auth surface, I want the PR template to remind me of the eight doc surfaces to update, so that I cannot accidentally merge a code change that contradicts the diagrams.
16. As a developer, I want the drift-gate script to also fail when a new service is added to `docker-compose.yaml` without appearing in the README services table at the same port, so that the catalog drift seen in this PRD cannot recur from a single missed copy-paste.
17. As an AI agent invoked AFK against this repo, I want the drift gate to run during my feedback loop so that I cannot complete a task that introduced drift, so that the AFK workflow stays self-correcting.

## Implementation Decisions

**Doc surfaces in scope (one reconciliation pass)**

- Root: `README.md`, `CONTEXT.md`, `CLAUDE.md` (already current, audit only), `AGENTS.md`, `.github/copilot-instructions.md`.
- Wiki: `docs/wiki/Home.md`, `Architecture.md`, `Diagram-Saga.md`, `Integration-Events.md`, `Roadmap.md`, `_Sidebar.md`, `Service-Order.md`, `Service-Inventory.md`, `Service-Payment.md`, `Service-Shipping.md`, `Service-API-Gateway.md`, `Shared-Library.md`, plus new `Service-Saga.md`.
- ADRs: `docs/adr/0008-*.md` flip status to `Superseded by ADR-0010` with reasoning preserved verbatim, ensure ADR-0010 references back. `docs/adr/README.md` index gains ADR-0010 row.
- Runbooks: ensure `docs/runbooks/saga-orchestrator-strangler.md` is linked from CONTEXT.md and from `Roadmap.md`.
- PRD index: `docs/prd/PRD.md` adds row for this PRD and confirms PRD-Saga-Orchestrator is linked.

**Modules**

- *Saga-aware doc index* — one Markdown file (or table in the plan) that lists each doc + line-range owning a saga-shape claim. Single source of truth for the reconciliation pass and for the drift gate's allowlist.
- *Service catalog block* — one canonical Saga service row (`| Saga | 8008 | SQL Server | Owns order saga state; drives Order/Inventory/Payment/Shipping via commands |`) copy-pasted with verbatim wording into the five surfaces that catalog services.
- *Saga sequence diagram (orchestrator)* — one canonical mermaid sequence showing Order → orchestrator (OrderCreatedEvent) → ReserveStockCommand → StockReserved (reply) → AuthorizePaymentCommand → PaymentAuthorized (reply) → ConfirmOrderCommand → CommitStockCommand → CreateShipmentCommand → CapturePaymentCommand. Reused verbatim in Architecture.md, Diagram-Saga.md, Integration-Events.md.
- *Doc-drift gate* — `scripts/check-doc-drift.ps1` + sibling `scripts/check-doc-drift.sh`, plus `scripts/doc-drift-allowlist.txt`. Azure Pipelines step name: `Verify documentation drift`. Failure mode: exit non-zero with a numbered list of offending file:line entries.
- *Roadmap delta block* — appended bulleted "Delivered (2026-05)" subsection to `Roadmap.md`, listing the cutover and supporting issues.

**Drift gate banned phrases (initial set)**

- `choreograph` (case-insensitive, word-boundary on left)
- `no central orchestrator`
- `no orchestrator`
- `saga choreography`

Allowlisted files: `docs/adr/0008-saga-choreography-no-central-orchestrator.md`, `docs/adr/0010-saga-orchestrator-supersedes-choreography.md`, `docs/runbooks/saga-orchestrator-strangler.md`, `docs/prd/PRD-Saga-Orchestrator.md`, `docs/prd/PRD-Docs-Refresh-2026-05.md`. Allowlist is a literal file, one entry per line.

**Service-table drift check**

Parse `docker-compose.yaml` `services:` keys for `*-microservice` and `api-gateway` entries, extract host port from `ports:`, and verify each `(service, port)` pair appears in each of: `README.md` services table, `CONTEXT.md` "Services" mention, `AGENTS.md` "Services (port, datastore)" line, `.github/copilot-instructions.md` services table, `CLAUDE.md` "Services" line. Mismatch fails the build.

**Markdown link check**

Run `lychee --offline --no-progress` (or `markdown-link-check` if `lychee` unavailable in Azure Pipelines image) over `README.md`, `CONTEXT.md`, `AGENTS.md`, `docs/**/*.md`. Treat anchors and relative paths as in-scope; treat external `http(s)://` links as out-of-scope (offline mode). Failure fails the build.

**CI placement**

New `azure-pipelines.docs.yml` at repo root. Triggered by changes to `**/*.md`, `docs/**`, `docker-compose.yaml`, `scripts/check-doc-drift.*`. Independent of per-service pipelines so that doc-only PRs don't trigger eight service builds and so that a code-only PR that ships drift still runs this pipeline.

**PR template**

`.github/PULL_REQUEST_TEMPLATE.md` gains a "Docs updated?" checklist with one box per surface (README/CONTEXT/AGENTS/copilot/wiki Home/Architecture/Diagram-Saga/Integration-Events/Service-*). Author ticks N/A boxes as appropriate. Template is advisory; CI is enforcing.

**Out of decision in this PRD**

- Whether to add a separate `wiki-autosync` GitHub Action that mirrors `docs/wiki/` to the GitHub Wiki remote (already in Roadmap "Candidate future work"; not in scope here).
- Whether to enforce a maximum doc age via mtime (rejected as a false-positive generator).
- Whether to rewrite the historical PRDs/plans to match the new shape (no — historical docs stay as-is; `Status: Accepted` / `Status: Delivered` is sufficient).

## Testing Decisions

A good test for this PRD verifies external behavior — that the drift script flags real drift and ignores allowlisted historical-context docs — and never verifies prose quality (the reconciliation pass is reviewed by humans, not by tests).

**Tests for the drift-gate script**

- Fixture: a temporary directory containing five small markdown files: one with `choreography` outside the allowlist, one with `choreography` inside the allowlist, one with the full services table including `:8008`, one with the table missing `:8008`, one clean. Run the script against the fixture; assert exit code, expected file:line list, and that allowlisted entries are not flagged.
- Test names follow `Given_When_Then` with underscores per repo convention (`Given_Banned_Phrase_Outside_Allowlist_When_Script_Runs_Then_Exit_Non_Zero`).
- Test runner: PowerShell `Pester` for `.ps1`, `bats` for `.sh`. Pick one based on the chosen CI image; both is overkill.
- Prior art: `scripts/` is currently sparse; the closest analogues are the smoke-test Bruno scripts and the saga end-to-end Testcontainers tests — both follow a "spin up a fixture, assert observable output" shape, which this matches.

**Tests for the markdown link checker**

No bespoke tests. Treat `lychee`/`markdown-link-check` as a trusted tool; verify integration only by deliberately introducing a broken link in a throwaway branch and confirming the Azure Pipelines step fails.

**No tests for prose**

Reconciliation pass changes are reviewed manually. The drift gate is the long-term check; the reconciliation pass is the one-shot.

## Out of Scope

- Auto-syncing `docs/wiki/` to the GitHub Wiki remote (separate roadmap item).
- Rewriting historical PRDs, plans, or phase summaries to match the new architecture (those are dated artifacts).
- Changes to code under `*-microservice/`, `api-gateway/`, `shared-libs/`, or `kubernetes/` — this PRD is docs + CI only.
- The actual `docs/wiki/Service-Saga.md` content for refund/return sagas (covered when `RefundSaga` lands per PRD-Saga-Orchestrator).
- Renaming `AGENTS.md` to align with `.claude/CLAUDE.md` casing — orthogonal cleanup.
- Internationalisation / non-English doc surfaces — none exist.

## Further Notes

- Sequencing: file this PRD, file the implementation plan under `docs/plans/docs-refresh-2026-05.md` with tracer-bullet phases (Phase 1: reconciliation pass landing as one PR; Phase 2: drift gate landing as a separate PR with its tests; Phase 3: PR-template checkbox + allowlist polish), then implement each phase behind its own GitHub issue.
- The reconciliation pass should land before the drift gate to avoid a self-failing CI on the first PR.
- The drift-gate allowlist file is itself a doc surface — if a future architectural reversal happens, the gate must be honest about what is and isn't current. Reviewer checklist for any future PR touching the allowlist: "Are we exempting drift or recording history?"
- Recurrence reminder: when refund saga ships, when ASB becomes default, when an eighth participant joins, the same reconciliation pattern applies. The drift gate is the cheap insurance for those events.
