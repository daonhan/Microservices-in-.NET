# Plan: Documentation refresh — Saga orchestrator cutover + drift gate (2026-05)

> Source PRD: [docs/prd/PRD-Docs-Refresh-2026-05.md](../prd/PRD-Docs-Refresh-2026-05.md) · GitHub issue [#138](https://github.com/daonhan/Microservices-in-.NET/issues/138)

Two PRs total:

- **Reconciliation PR** = Phases 1–4 (lands first, clears drift so the gate doesn't self-fail)
- **Gate PR** = Phases 5–6 (lands second, prevents regression)

## Architectural decisions

Durable across all phases.

- **Saga service identity**: name `saga-microservice`, port `8008`, datastore SQL Server, responsibility "owns order saga state; drives Order/Inventory/Payment/Shipping via commands".
- **Canonical saga sequence (orchestrator-led)**: `OrderCreatedEvent` (Order → Saga) → `ReserveStockCommand` (Saga → Inventory) → `StockReserved` (Inventory → Saga) → `AuthorizePaymentCommand` (Saga → Payment) → `PaymentAuthorized` (Payment → Saga) → `ConfirmOrderCommand` (Saga → Order) → `OrderConfirmed` (Order → Saga) → `CommitStockCommand` (Saga → Inventory) → `StockCommitted` (Inventory → Saga) → `CreateShipmentCommand` (Saga → Shipping) → `ShipmentCreated`/`ShipmentDispatched` (Shipping → Saga) → `CapturePaymentCommand` (Saga → Payment) → `PaymentCaptured` (Payment → Saga) → `ShipmentDelivered` (Shipping → Saga). Compensation path: `ReleaseStockCommand`/`VoidPaymentCommand`/`CancelShipmentCommand`/`CancelOrderCommand`/`RefundPaymentCommand` depending on last completed step.
- **Command catalog (saga-driven)**: `ReserveStockCommand`, `CommitStockCommand`, `ReleaseStockCommand`, `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RefundPaymentCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `CreateShipmentCommand`, `CancelShipmentCommand`.
- **ADR linkage**: ADR-0008 status flips to `Superseded by ADR-0010`; ADR-0010 references back to ADR-0008. Reasoning preserved verbatim in both.
- **Allowlist policy**: literal file `scripts/doc-drift-allowlist.txt`, one path per line. Initial entries: `docs/adr/0008-saga-choreography-no-central-orchestrator.md`, `docs/adr/0010-saga-orchestrator-supersedes-choreography.md`, `docs/runbooks/saga-orchestrator-strangler.md`, `docs/prd/PRD-Saga-Orchestrator.md`, `docs/prd/PRD-Docs-Refresh-2026-05.md`, `docs/plans/docs-refresh-2026-05.md`.
- **Banned phrases** (case-insensitive): `choreograph`, `no central orchestrator`, `no orchestrator`, `saga choreography`.
- **CI placement**: new `azure-pipelines.docs.yml` at repo root, independent of per-service pipelines. Triggers: `**/*.md`, `docs/**`, `docker-compose.yaml`, `scripts/check-doc-drift.*`.
- **Test naming**: `Given_When_Then` with underscores (repo convention).
- **Reconciliation/gate ordering**: reconciliation lands first; gate would otherwise fail its own first run.

---

## Phase 1: ADR + runbook history alignment

**User stories**: 7, 8

### What to build

End-to-end pass making the architectural-decision history readable. Mark ADR-0008 as superseded with reasoning preserved verbatim, ensure ADR-0010 references back to it, add ADR-0010 row to the ADR index, link ADR-0010 from CONTEXT.md's ADR list, link the strangler runbook from CONTEXT.md's runbooks list. No diagram or catalog work — that's later.

### Acceptance criteria

- [ ] `docs/adr/0008-saga-choreography-no-central-orchestrator.md` header reads `Status: Superseded by ADR-0010` with original reasoning intact.
- [ ] `docs/adr/0010-saga-orchestrator-supersedes-choreography.md` references ADR-0008 in its "Supersedes" header.
- [ ] `docs/adr/README.md` index has a row for ADR-0010.
- [ ] `CONTEXT.md` ADR list includes ADR-0010 in numeric order.
- [ ] `CONTEXT.md` runbooks list includes `saga-orchestrator-strangler.md`.
- [ ] Every link in the two updated ADRs and CONTEXT.md resolves (manual click-through).

---

## Phase 2: Service catalog + Service-Saga page

**User stories**: 1, 3, 6, 9, 11

### What to build

Make every service catalog list eight services with port 8008 for Saga, and produce a real `Service-Saga.md` wiki page. Saga row is written once (`| Saga | 8008 | SQL Server | Owns order saga state; drives Order/Inventory/Payment/Shipping via commands |`) and copy-pasted verbatim into every surface that catalogs services. Service-Saga page content lifted from PRD-Saga-Orchestrator + the strangler runbook + reading `saga-microservice/Saga.Service/` (Program.cs composition root, state model, command handlers).

`.github/copilot-instructions.md` is also rewritten in this phase to match CLAUDE.md's `AddPlatform*` extension names and to drop the Order↔Inventory-only saga description in favor of the orchestrator shape.

No diagram changes in this phase — only catalog + Service-Saga page + Copilot rewrite. Catalog topology mermaid in `README.md`, `CONTEXT.md`, `docs/wiki/Home.md` updated to add a Saga node and orchestrator edges; the *sequence* diagrams stay in Phase 3.

### Acceptance criteria

- [ ] Services table in `README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md`, `CLAUDE.md` all list eight services with matching ports.
- [ ] `README.md` project tree includes `saga-microservice/`.
- [ ] `README.md` tech-stack and pattern tables reference Saga where applicable.
- [ ] Topology mermaid in `README.md`, `CONTEXT.md`, `docs/wiki/Home.md` includes a Saga node at `:8008` with edges to Order/Inventory/Payment/Shipping.
- [ ] `docs/wiki/Service-Saga.md` exists and covers: responsibility, state model (saga instance + typed payload), command catalog, participant interaction, reaper/timeout behavior, operator API, links to PRD-Saga-Orchestrator and the strangler runbook.
- [ ] `docs/wiki/_Sidebar.md` Services group lists Saga.
- [ ] `docs/wiki/Home.md` "Where to go next" table links Service-Saga.
- [ ] `.github/copilot-instructions.md` Cross-service section references `AddPlatformEventBus`/`AddPlatformEventPublisher`/`AddPlatformSubscriberService` (no `AddRabbitMq*` names) and describes saga as orchestrator-driven.
- [ ] No structural prose still claims seven services anywhere in scope.

---

## Phase 3: Saga diagrams + command catalog

**User stories**: 2, 4, 5, 6

### What to build

One canonical orchestrator-led mermaid sequence diagram, written once and reused verbatim in `docs/wiki/Diagram-Saga.md`, `docs/wiki/Architecture.md` saga section, and `docs/wiki/Integration-Events.md` saga-and-fulfillment-sequence section. Add a saga command catalog table to `Integration-Events.md` alongside the existing event catalog. Update CONTEXT.md "what it is" prose to describe orchestration-led coordination. Update Service-Order/Inventory/Payment/Shipping wiki pages to reference the orchestrator-driven flow where they currently claim "reacts to events from peers" or similar.

This is the phase where every banned phrase ("choreography", "no orchestrator", "saga choreography", "no central orchestrator") leaves the non-allowlisted docs.

### Acceptance criteria

- [ ] Canonical sequence diagram present and identical in `Diagram-Saga.md`, `Architecture.md`, `Integration-Events.md`.
- [ ] Compensation flow diagram in `Diagram-Saga.md` shows orchestrator-issued reverse commands.
- [ ] `Integration-Events.md` event ⇄ service matrix updated: Saga is a subscriber to reply events; participants are no longer cross-subscribed for saga steps.
- [ ] `Integration-Events.md` includes a command catalog table covering all eleven commands listed under Architectural decisions.
- [ ] `CONTEXT.md` "what it is" prose describes orchestration-led saga.
- [ ] `docs/wiki/Service-Order.md`, `Service-Inventory.md`, `Service-Payment.md`, `Service-Shipping.md` describe each service's role under orchestration (command receiver + reply event publisher).
- [ ] Grep over non-allowlisted docs returns zero hits for `choreograph`, `no central orchestrator`, `no orchestrator`, `saga choreography`.

---

## Phase 4: Roadmap delta + PRD index

**User stories**: 10

### What to build

Append a `Delivered (2026-05)` subsection to `docs/wiki/Roadmap.md` covering the saga orchestrator cutover (#132/#136/#137), refund saga (#130), reaper (#126), compensation matrix (#125), operator saga API (#128), StockItem aggregate (#117/#118), and messaging provider abstraction (RabbitMQ default + ASB switch + provider-agnostic DLQ poller). Add a row for `PRD-Docs-Refresh-2026-05` to `docs/prd/PRD.md`. Cross-link the strangler runbook from the new Roadmap subsection.

Reconciliation PR opens at end of this phase.

### Acceptance criteria

- [ ] `docs/wiki/Roadmap.md` "Delivered (2026-05)" subsection exists with the eight items above, each linking the corresponding issue.
- [ ] `docs/prd/PRD.md` index lists `PRD-Docs-Refresh-2026-05`.
- [ ] Reconciliation PR (Phases 1–4) opened with the eight surfaces touched, passes pre-commit, and references issue #138.
- [ ] Manual smoke: render the wiki locally or in a preview; verify topology + sequence diagrams.

---

## Phase 5: Drift-gate script + tests (no CI wiring)

**User stories**: 14, 16

### What to build

Two equivalent scripts: `scripts/check-doc-drift.ps1` (PowerShell 7+) and `scripts/check-doc-drift.sh` (bash). Both consume `scripts/doc-drift-allowlist.txt` (literal one-path-per-line). Behavior: walk the working tree, run two checks, exit non-zero with a numbered `file:line` list on failure.

Check 1 — banned-phrase grep: case-insensitive search for the four banned phrases over all `*.md` files, minus allowlisted paths.

Check 2 — service-table sync: parse `docker-compose.yaml` `services:` keys matching `*-microservice|api-gateway`, extract host port from each `ports:` block, then verify each `(service, port)` pair appears in the services tables in `README.md`, `CONTEXT.md` Architecture topology, `AGENTS.md` services line, `.github/copilot-instructions.md` services table, and `CLAUDE.md` services line.

Tests: Pester fixtures (`.ps1`) and bats fixtures (`.sh`) covering: banned phrase outside allowlist → fail; banned phrase inside allowlist → pass; full eight-service table → pass; missing `:8008` row → fail; clean fixture → pass. Test names follow `Given_When_Then`.

No CI wiring in this phase. Demoable by running `./scripts/check-doc-drift.ps1` locally and exiting 0 (because Phases 1–4 cleared drift).

### Acceptance criteria

- [ ] `scripts/check-doc-drift.ps1`, `scripts/check-doc-drift.sh`, `scripts/doc-drift-allowlist.txt` exist.
- [ ] Both scripts exit 0 against HEAD after Phases 1–4.
- [ ] Both scripts exit non-zero with a numbered list when a banned phrase is reintroduced in a non-allowlisted file.
- [ ] Both scripts exit non-zero when a service is added to `docker-compose.yaml` without being added to all five catalog surfaces.
- [ ] Pester/bats tests pass against the fixture directory.
- [ ] Test names follow `Given_When_Then`.
- [ ] Allowlist file documented at the top with a comment explaining the policy.

---

## Phase 6: Azure Pipelines wiring + markdown link check + PR template

**User stories**: 12, 13, 15, 17

### What to build

New `azure-pipelines.docs.yml` at repo root, triggered on changes to `**/*.md`, `docs/**`, `docker-compose.yaml`, `scripts/check-doc-drift.*`. Two steps:

1. `Verify documentation drift` — runs the bash script from Phase 5.
2. `Verify markdown links` — runs `lychee --offline --no-progress` or `markdown-link-check` (choice deferred to implementation based on Azure Pipelines image availability) over `README.md`, `CONTEXT.md`, `AGENTS.md`, and `docs/**/*.md`. Offline mode: relative paths + anchors are in-scope, external `http(s)://` links are skipped.

Both steps fail the build on non-zero exit.

`.github/PULL_REQUEST_TEMPLATE.md` gains a "Docs updated?" checklist with one box per surface: README, CONTEXT, AGENTS, Copilot instructions, wiki Home, Architecture, Diagram-Saga, Integration-Events, Service-* pages, Roadmap. Authors tick N/A where appropriate. Template is advisory; CI is enforcing.

AFK feedback-loop hook: document that `scripts/check-doc-drift.sh` is the AFK self-check command (story 17). Add a one-line reference in the AFK task prompt template at `.github/prompts/afk-task.prompt.md`.

Gate PR opens at end of this phase.

### Acceptance criteria

- [ ] `azure-pipelines.docs.yml` exists with both steps and the documented trigger filter.
- [ ] Deliberate test branch with a banned phrase reintroduced in `README.md` fails the `Verify documentation drift` step.
- [ ] Deliberate test branch with a broken relative link in `docs/wiki/Home.md` fails the `Verify markdown links` step.
- [ ] HEAD passes both steps.
- [ ] `.github/PULL_REQUEST_TEMPLATE.md` has the "Docs updated?" checklist with at least the ten checkboxes above.
- [ ] `.github/prompts/afk-task.prompt.md` references `scripts/check-doc-drift.sh` as part of the AFK self-check.
- [ ] Gate PR (Phases 5–6) opened, references issue #138, and CI passes.
