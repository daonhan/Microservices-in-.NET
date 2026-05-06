# Plan: Repository `CONTEXT.md` + `docs/adr/`

> Source PRD: `docs/prd/PRD-Context.md` — GitHub issue: https://github.com/daonhan/Microservices-in-.NET/issues/54

## Architectural decisions

Durable decisions that apply across all phases:

- **File location**: `CONTEXT.md` lives at the repository root. ADRs live under `docs/adr/`. Screenshots live under `docs/assets/`.
- **Single canonical CONTEXT file**: One `CONTEXT.md`. The TL;DR + share-block at the top is the social-media-friendly slice; the body below is the long form for Substack and on-repo readers. No separate short/long variants.
- **Voice**: First person for narrative ("I chose…", "I learned…"). Neutral third-person for the glossary and the ADRs.
- **Body section order**: Share-block (TL;DR, badges, pitch, links) · Why I built it · What it is · Domain glossary · Architecture at a glance · Architectural decisions index · AI workflow · What I learned · Link tree · Footer.
- **ADR format (MADR-lite)**: Each ADR has `Title`, `Status`, `Date`, `Context`, `Decision`, `Consequences`. No code snippets — links to source folders only.
- **ADR numbering**: Zero-padded four-digit prefix (`0001-…` … `0009-…`) so directory listings sort naturally. Append-only — superseded decisions get a new ADR with a `Supersedes ADR-XXXX` header.
- **Initial ADR set** (all marked `Accepted` on creation):
  1. ADR-0001 — API Gateway provider switch: YARP default with Ocelot fallback
  2. ADR-0002 — Transactional Outbox per publishing service
  3. ADR-0003 — RS256 JWT issuance with `/jwks` discovery
  4. ADR-0004 — RabbitMQ fanout exchange with dead-letter queue and operator API
  5. ADR-0005 — `ECommerce.Shared` distributed as a NuGet package via a local feed
  6. ADR-0006 — One `.slnx` solution per service; no root `.sln`
  7. ADR-0007 — EF Core with one database per service
  8. ADR-0008 — Saga choreography (no central orchestrator) for Order/Inventory/Payment/Shipping
  9. ADR-0009 — OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack
- **Glossary scope**: Saga, Outbox, Dead-Letter Queue (DLQ), Integration Event, Reservation, Backorder, Authorize, Capture, Refund, JWKS, Fanout exchange, YARP, Ocelot, Choreography vs Orchestration, Minimal API, `.slnx`, OTEL Collector.
- **Living doc rule**: `CONTEXT.md` is updated in the same PR as any change that invalidates a section.
- **No tests**: No automated link-checking or other automation. Quality is enforced at PR review time.
- **README change**: A single new line near the top of `README.md` pointing readers at `CONTEXT.md`. No other README content moves.
- **Cross-linking**: `CONTEXT.md` deep-links to each ADR by number and title. Each ADR links back to the relevant wiki page and source folder.

---

## Phase 1: Tracer — root file + ADR scaffold + README pointer

**User stories**: 4, 10, 11, 26, 27, 28, 29

### What to build

A minimal but renderable `CONTEXT.md` at the repo root, the `docs/adr/` directory with its template and empty index, and a single pointer line in `README.md`. The body of `CONTEXT.md` may use stub headings ("Coming in phase N") for sections not yet written, but the share-block at the top must be complete and self-contained — a reader landing on the repo or copy-pasting the top into LinkedIn must get a coherent, finished pitch from this phase alone.

### Acceptance criteria

- [ ] `CONTEXT.md` exists at the repo root and renders on the GitHub repo landing page.
- [ ] The top of `CONTEXT.md` contains a complete share-block: project title, badges (build status if available, license, .NET 10 target), one-paragraph pitch, three to five "what's interesting here" bullets, and links out to `README.md`, the wiki home, my LinkedIn, and my Substack.
- [ ] `CONTEXT.md` ends with a footer linking to my LinkedIn (`linkedin.com/in/daonhan`) and Substack (`substack.com/@daonhan`).
- [ ] Body sections appear as headings in the agreed order, with stubs where content lands in later phases.
- [ ] `docs/adr/README.md` exists as a one-page index, currently listing the nine planned ADR titles with their numbers and a status of "Planned".
- [ ] `docs/adr/0000-template.md` exists with the MADR-lite headers (`Title`, `Status`, `Date`, `Context`, `Decision`, `Consequences`).
- [ ] `README.md` has one new line near the top pointing to `CONTEXT.md`. No other README content changes.

---

## Phase 2: Decisions backbone — nine ADRs + decisions index in CONTEXT.md

**User stories**: 3, 14, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25

### What to build

Write the nine accepted ADRs that capture the load-bearing decisions already implemented in the codebase. Each ADR follows the template and is linked from a new "Architectural decisions" section in `CONTEXT.md`. After this phase, every key "why" question about the platform has a stable, citable answer.

### Acceptance criteria

- [ ] Nine ADR files exist under `docs/adr/` with names `0001-…` through `0009-…`, each marked `Status: Accepted` and dated `2026-05-06`.
- [ ] Each ADR follows the MADR-lite template — no code snippets — and links to the source folder(s) that implement the decision and to the relevant wiki page.
- [ ] The `docs/adr/README.md` index lists all nine with status `Accepted` (no longer "Planned").
- [ ] `CONTEXT.md` has an "Architectural decisions" section that lists each ADR by number and title and links to its file.
- [ ] Every link from `CONTEXT.md` to `docs/adr/` resolves.

---

## Phase 3: Glossary + Architecture-at-a-glance

**User stories**: 1, 2

### What to build

Fill the domain glossary in `CONTEXT.md` with the full term set, each entry being one to three sentences in business or platform language (not implementation detail). Add an "Architecture at a glance" section that reuses the top-level mermaid diagram from `README.md` and gives a short orienting paragraph. After this phase, an AI agent or new reader has the vocabulary needed to navigate the rest of the repo.

### Acceptance criteria

- [ ] The glossary section in `CONTEXT.md` defines all terms in the agreed scope: Saga, Outbox, Dead-Letter Queue (DLQ), Integration Event, Reservation, Backorder, Authorize, Capture, Refund, JWKS, Fanout exchange, YARP, Ocelot, Choreography vs Orchestration, Minimal API, `.slnx`, OTEL Collector.
- [ ] Each glossary entry is one to three sentences, in business or platform terms, and avoids file paths or code references.
- [ ] The "Architecture at a glance" section embeds the mermaid diagram from `README.md` and includes a short paragraph orienting the reader to the seven services, the gateway, and the saga participants.
- [ ] Every glossary term appears in at least one ADR or service description elsewhere in `CONTEXT.md` or the wiki (so terms aren't orphaned).

---

## Phase 4: Narrative — Why / What / AI workflow / What I learned

**User stories**: 6, 7, 8, 12

### What to build

Write the first-person narrative sections of `CONTEXT.md`: why I built the project, what it is, how I worked with Claude Code Pro and GitHub Copilot Pro+, and what I learned. After this phase, a recruiter or developer friend can read `CONTEXT.md` top-to-bottom and come away with a clear sense of the project's scope, my role, and the AI-pair-programming workflow that produced it.

### Acceptance criteria

- [ ] "Why I built it" tells the personal story in the first person, naming the learning goals and the portfolio motivation.
- [ ] "What it is" gives a one-screen summary of the platform — services, datastores, gateway, observability, deployment targets — without duplicating the README.
- [ ] "AI workflow" explains concretely which tasks were paired with Claude Code Pro versus GitHub Copilot Pro+, references how PRDs and plans (`docs/prd/`, `docs/plans/`) acted as the contract between human and agent, and notes which boundaries (security review, deployment, schema migrations) I kept under direct human control.
- [ ] "What I learned" lists five to seven concrete takeaways covering at minimum: saga choreography vs orchestration, outbox semantics, JWT/JWKS issuance, OpenTelemetry wiring, dual-gateway switching, and NuGet-vs-project-reference shared libraries.
- [ ] All four sections are in first person. Tone is consistent with the share-block at the top.

---

## Phase 5: Polish — screenshots, badges, link tree, footer

**User stories**: 5, 9, 13, 15

### What to build

The shareable polish layer: a Grafana dashboard screenshot and an API Gateway combined Swagger UI screenshot under `docs/assets/`, embedded in `CONTEXT.md`; resolved badges (build status if a workflow exists, otherwise omitted; license; .NET 10 target); a full link tree pointing to every wiki page, every PRD in `docs/prd/`, every plan in `docs/plans/`, the Kubernetes manifests, and key runbooks; and the verified footer linking to LinkedIn and Substack. After this phase, `CONTEXT.md` is ready to be linked from a LinkedIn post or used as a Substack lede.

### Acceptance criteria

- [ ] `docs/assets/` contains at least two PNG screenshots: one of a Grafana dashboard and one of the API Gateway combined Swagger UI.
- [ ] Both screenshots are embedded in `CONTEXT.md` via relative paths and render on GitHub.
- [ ] Badges in the share-block resolve: license badge points to the actual license; .NET version badge reflects net10.0; build-status badge is included only if a GitHub Actions workflow is present (otherwise omitted, not faked).
- [ ] The "Link tree" section lists and links every wiki page under `docs/wiki/`, every PRD under `docs/prd/`, every plan under `docs/plans/`, the Kubernetes manifests folder, and key runbooks.
- [ ] The footer's LinkedIn and Substack links resolve.
- [ ] Reading the share-block at the top in isolation still reads as a complete, self-contained pitch suitable for copy-paste to LinkedIn.
