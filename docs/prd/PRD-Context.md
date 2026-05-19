# PRD — Repository `CONTEXT.md` + `docs/adr/`

## Problem Statement

I built this repo to learn and demonstrate microservices in .NET 10 — using GitHub Copilot Pro+ and Claude Code Pro as my pair programmers. Over time it has grown to seven services, a YARP/Ocelot-switchable gateway, a transactional outbox, a saga across Order/Inventory/Payment/Shipping, an OpenTelemetry stack, Kubernetes manifests, twelve PRDs in `docs/prd/`, and a full GitHub Wiki sourced from `docs/wiki/`.

That depth is also the problem. The `README.md` is a 20 KB reference, the wiki is multi-page, and the *why* behind decisions (why YARP plus Ocelot, why an outbox, why RS256 + JWKS, why a NuGet shared lib instead of project references, why `.slnx` per service) is implicit in code or scattered across PRDs.

Three readers all hit this wall:

- **AI agents (Claude Code, Copilot, Cursor)** opening the repo cold need the domain language and key decisions in one grounded place so their edits don't fight the design.
- **Friends and other developers** browsing my GitHub need a one-shot way to grasp what the project is and why it's interesting.
- **Recruiters and visitors from my LinkedIn (`linkedin.com/in/daonhan`)** need a portfolio-grade landing page that shows the system, the choices, and how I worked with AI tools to build it.

No single file serves all three today.

## Solution

Add `CONTEXT.md` at the repo root — a single, long, first-person, evergreen narrative. The top of the file is a **TL;DR + share-block** (badges, a one-paragraph pitch, key links) designed to be copy-pasted into a LinkedIn post or a Substack lede at `substack.com/@daonhan`. Below it: the full domain glossary, a decisions index that links to a new `docs/adr/` folder, an AI-workflow showcase explaining how Claude Code Pro and GitHub Copilot Pro+ were used, what I learned, and a link tree pointing to every other doc, PRD, plan, and wiki page.

Alongside `CONTEXT.md`, bootstrap a new `docs/adr/` directory with nine numbered Architecture Decision Records (MADR-lite format) capturing the load-bearing choices already implemented in the codebase. `CONTEXT.md` indexes them; each ADR is the canonical "why" for one decision.

`CONTEXT.md` is a living document — updated in the same PR as any change that invalidates a section. ADRs are append-only: superseded decisions get a new ADR rather than an in-place rewrite.

## User Stories

1. As an AI coding agent (Claude Code, Copilot, Cursor) opening this repo for the first time, I want a single root-level `CONTEXT.md` containing the domain language and key decisions, so that my suggestions are grounded in the project's vocabulary instead of generic .NET conventions.
2. As an AI coding agent, I want every domain term used in the codebase (Saga, Outbox, DLQ, Integration Event, Reservation, Backorder, Authorize, Capture, Refund, JWKS, Fanout exchange, YARP, Ocelot) defined in one glossary, so that I can resolve ambiguous references without re-reading service folders.
3. As an AI coding agent, I want each load-bearing architectural decision to live in its own ADR file under `docs/adr/`, so that I can cite a stable identifier (for example "ADR-0002") when explaining or modifying the related code.
4. As a developer friend opening my GitHub profile, I want a `CONTEXT.md` link rendered prominently on the repo landing page next to `README.md`, so that I see a human-narrated entry point before I drown in the file tree.
5. As a developer friend, I want the top of `CONTEXT.md` to be a scannable TL;DR with badges, a one-paragraph pitch, and a "what's interesting here" list, so that I can decide in 30 seconds whether to keep reading.
6. As a recruiter or hiring manager visiting from my LinkedIn (`linkedin.com/in/daonhan`), I want `CONTEXT.md` to make the scope and ambition of the project obvious without requiring me to read code, so that I can evaluate the work quickly.
7. As a recruiter, I want a clear "AI workflow" section explaining which tasks were paired with Claude Code Pro versus GitHub Copilot Pro+, so that I understand the candidate's competency with modern AI-assisted development.
8. As any reader, I want a "What I learned" section listing five to seven concrete takeaways (orchestration vs event-driven saga coordination, outbox semantics, JWT/JWKS issuance, OpenTelemetry wiring, dual-gateway switching, NuGet-vs-project-reference shared libraries), so that the document has lasting reference value.
9. As any reader, I want screenshots of the Grafana dashboards and the API Gateway Swagger UI embedded in `CONTEXT.md`, so that the system feels real even before I run it locally.
10. As any reader, I want badges at the top of `CONTEXT.md` for build status, license, and .NET 10 target, so that the project's health is immediately visible.
11. As any reader, I want a footer linking back to my LinkedIn profile and Substack page, so that the document doubles as a personal landing page.
12. As a future version of myself returning to this repo after a long gap, I want `CONTEXT.md` written in the first person ("I chose YARP because…"), so that the document carries the design intent in the same voice I would use to explain it to a teammate.
13. As a maintainer, I want `CONTEXT.md` to be a living document updated in the same pull request as any major architectural change, so that it never silently goes stale.
14. As a maintainer, I want ADRs to be append-only — a superseded decision gets a new ADR with a "Supersedes ADR-XXXX" header — so that the historical record is preserved.
15. As an external learner, I want `CONTEXT.md` to link out to the `README.md`, `docs/wiki/`, every PRD in `docs/prd/`, every plan in `docs/plans/`, and the Kubernetes manifests, so that it functions as a single index for the whole repository.
16. As an external learner, I want each ADR to follow the same lightweight structure (Title, Status, Context, Decision, Consequences, Date), so that I can predict the layout on every page.
17. As an external learner, I want ADR-0001 to capture the YARP-default + Ocelot-fallback gateway switch so that I understand why two reverse proxies coexist.
18. As an external learner, I want ADR-0002 to capture the transactional outbox pattern so that I understand why publishers don't write straight to RabbitMQ.
19. As an external learner, I want ADR-0003 to capture RS256 JWT signing with `/jwks` discovery so that I understand the auth contract between services.
20. As an external learner, I want ADR-0004 to capture the RabbitMQ fanout exchange + DLQ so that I understand the messaging topology and dead-letter operator flow.
21. As an external learner, I want ADR-0005 to capture `ECommerce.Shared` distributed as a NuGet package against a local feed (instead of project references) so that I understand the shared-library workflow.
22. As an external learner, I want ADR-0006 to capture the choice of one `.slnx` per service with no root `.sln` so that I understand the per-service build/test boundary.
23. As an external learner, I want ADR-0007 to capture EF Core with one database per service so that I understand the data-isolation invariant.
24. As an external learner, I want ADR-0008 to capture event-driven saga coordination (no dedicated orchestrator) for the Order/Inventory/Payment/Shipping flow so that I understand why coordination is event-driven.
25. As an external learner, I want ADR-0009 to capture the OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack so that I understand how signals are exported, stored, and visualized.
26. As a contributor proposing a new architectural decision, I want a clear template at `docs/adr/0000-template.md` so that the format is consistent across ADRs.
27. As a maintainer, I want `docs/adr/README.md` to be a one-page index listing every ADR by number, title, and status, so that the directory is self-navigating.
28. As a social-media reader landing on a LinkedIn or Substack post, I want the share-block content at the top of `CONTEXT.md` to be self-contained — readable without scrolling further — so that it works as the lede of a published article.
29. As any reader, I want the PRD that produced `CONTEXT.md` saved at `docs/prd/PRD-Context.md` and also submitted as a GitHub issue, so that the planning artifact lives alongside every other PRD in this repo.

## Implementation Decisions

- **File location**: `CONTEXT.md` lives at the repository root, sitting next to `README.md` so it renders on the GitHub repo landing page.
- **Single file**: One canonical `CONTEXT.md`. No separate short/long variants. The TL;DR + share-block at the top is the social-media-friendly slice; the body is the long form for Substack and on-repo readers.
- **Voice**: First person ("I chose…", "I learned…"). Neutral third-person prose is reserved for the glossary and the ADRs.
- **Audiences served**: AI agents (primary), human developers and friends, recruiters arriving from LinkedIn. The structure is ordered to serve all three from the top down.
- **Top-of-file share-block** contains: project title, badges (build status, license, .NET 10 target), one-paragraph pitch, three to five "what's interesting here" bullets, links to README, wiki home, live demo (if any), and to my LinkedIn and Substack.
- **Body sections** (in order): Why I built it · What it is · Domain glossary · Architecture at a glance (reuses the mermaid diagram from `README.md`) · Architectural decisions index (links to each ADR) · AI workflow (Claude Code Pro + Copilot Pro+ — what each was used for) · What I learned · Link tree (wiki pages, PRDs, plans, runbooks) · Footer (LinkedIn + Substack).
- **Glossary scope**: full set — Saga, Outbox, Dead-Letter Queue (DLQ), Integration Event, Reservation, Backorder, Authorize, Capture, Refund, JWKS, Fanout exchange, YARP, Ocelot, Orchestration vs event-driven coordination, Minimal API, `.slnx`, OTEL Collector. Each entry is one to three sentences in business or platform terms — not implementation detail.
- **Living doc**: `CONTEXT.md` is updated in the same PR as any change that invalidates a section (new service, new ADR, removed component, changed glossary term).
- **No tests**: per the user, no link-check or other automation is added in this PRD's scope. Review at PR time is the quality gate.
- **ADR directory**: a new `docs/adr/` folder with `README.md` (index), `0000-template.md` (template), and ADRs 0001 through 0009 listed below.
- **ADR format (MADR-lite)**: Each ADR has the headers `Title`, `Status` (Accepted on creation), `Date`, `Context`, `Decision`, `Consequences`. No code snippets — link to the source folders that implement the decision.
- **ADR numbering**: zero-padded four-digit prefix (`0001-…`, `0002-…`) so directory listings sort naturally.
- **Initial ADR set** (titles, all marked `Accepted` on creation):
  1. ADR-0001 — API Gateway provider switch: YARP default with Ocelot fallback
  2. ADR-0002 — Transactional Outbox per publishing service
  3. ADR-0003 — RS256 JWT issuance with `/jwks` discovery
  4. ADR-0004 — RabbitMQ fanout exchange with dead-letter queue and operator API
  5. ADR-0005 — `ECommerce.Shared` distributed as a NuGet package via a local feed
  6. ADR-0006 — One `.slnx` solution per service; no root `.sln`
  7. ADR-0007 — EF Core with one database per service
  8. ADR-0008 — Event-driven saga coordination (no dedicated orchestrator) for Order/Inventory/Payment/Shipping
  9. ADR-0009 — OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack
- **ADRs explicitly out of the initial set**: Minimal APIs vs MVC (deferred — not load-bearing enough to warrant an ADR yet).
- **Cross-linking**: `CONTEXT.md` deep-links to each ADR by number and title. Each ADR links back to the relevant wiki page and source folder. `README.md` is updated only with a single new line near the top pointing readers at `CONTEXT.md` — no other README content moves.
- **PRD lifecycle**: This PRD is saved at `docs/prd/PRD-Context.md` (matching the convention used by every other PRD in this repo) and submitted as a GitHub issue at `github.com/daonhan/Microservices-in-.NET/issues`.
- **Screenshots**: PNG assets stored under `docs/assets/` (new folder if it does not already exist) and referenced by relative path. Two minimum: a Grafana dashboard view and the API Gateway combined Swagger UI.
- **Badges**: shields.io for license and .NET version; GitHub Actions badge for build status if a workflow is present, otherwise omitted rather than faked.

## Testing Decisions

No automated tests are written for this PRD. `CONTEXT.md` is human-narrative markdown with no executable behavior; ADRs are append-only documents. The user explicitly chose to skip tests for this work. Quality is enforced at PR review time:

- Reviewers verify that every link resolves and every glossary term is referenced from at least one ADR or service description.
- Reviewers verify that the TL;DR + share-block stands alone as a self-contained pitch.
- Reviewers verify that every ADR follows the template structure.

A future PRD may add a markdown link-checker to CI; that is out of scope here.

## Out of Scope

- A separate `CONTEXT-short.md` or any second variant — the share-block at the top of the single file replaces this.
- Auto-generation of `CONTEXT.md` from source code or PRDs.
- Translation of `CONTEXT.md` into other languages.
- A demo GIF or screencast (still screenshots only).
- The Substack post itself — `CONTEXT.md` is the source; publishing is a manual downstream step.
- Migrating existing PRDs into ADRs — PRDs and ADRs coexist; PRDs describe planned work, ADRs describe accepted decisions.
- An ADR for Minimal APIs vs MVC controllers (deferred; may be added later).
- Adding markdown link-checking to CI.
- Modifying the `README.md` beyond a single pointer line near the top.

## Further Notes

- The AI-workflow section is a deliberate part of the value of this document. Concrete examples to surface: which services were scaffolded with Copilot Pro+ in-IDE versus shaped by Claude Code Pro through PRD-driven iteration; how PRDs and plans (`docs/prd/`, `docs/plans/`) were used as the contract between human and agent; how `CLAUDE.md` and `.claude/CLAUDE.md` shape behavioral guardrails for Claude Code; and which boundaries (security review, deployment, schema migrations) I kept under direct human control.
- This PRD is itself a demonstration of that workflow: an interview-driven `/write-a-prd` flow run inside Claude Code, producing a PRD checked into the repo and a tracking GitHub issue.
- Once `CONTEXT.md` exists, future PRDs in this repo should add a "Related ADR" line when they imply or invalidate an existing architectural decision.
