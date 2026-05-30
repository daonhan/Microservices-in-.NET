# PRD — Scope & Non-Goals consolidation + CI/CD doc reconcile

> Status: draft. Tracked as [#313](https://github.com/daonhan/Microservices-in-.NET/issues/313). Synthesized from a session that started as "what is important out of scope for this repo?" and surfaced both a missing consolidated scope statement and a stale CI/CD claim.

## Problem Statement

As someone landing on this repo — a recruiter, a developer friend, or an AI agent reading the grounding docs — I cannot find a single, authoritative answer to "what is this project, and just as importantly, what is it *not*?" The scope is real and deliberate (a learning/portfolio backend that walks the hard distributed-systems paths, not a feature-complete commerce product), but that framing is implicit. The actual out-of-scope decisions are scattered across a dozen PRDs, ADRs, and runbooks: Cosmos DB migration is deferred in one PRD, public NuGet publishing is excluded in a runbook, Ocelot removal is deferred in another PRD, Auth request validation is punted in yet another. There is no consolidated "Scope & Non-Goals" section in the two canonical entry points (`CONTEXT.md`, `README.md`), so a reader has to reverse-engineer the boundaries from the whole `docs/` tree.

Worse, one cross-cutting claim is now factually wrong. `CLAUDE.md`, `AGENTS.md`, and `.github/copilot-instructions.md` all state **"GitHub Actions is not used."** That was true once, but the repo now ships two active GitHub Actions workflows — `.github/workflows/docker-build.yml` (build verification) and `.github/workflows/smoke-test.yml` (end-to-end QA smoke). `CONTEXT.md` even renders status badges pointing at those workflows, and multiple PRDs (`PRD-Smoke-Test-Saga-Hardening`, `PRD-Smoke-Test-Bruno-Cli`) explicitly build GitHub Actions CI. The blanket statement contradicts the codebase and confuses every AI agent that reads the grounding files before touching CI. The accurate framing already exists in exactly one place — `infrastructure-deployment/docs/PATTERNS.md` says "GitHub Actions is not the deployment path for this repo" — but it has not propagated to the canonical instruction files.

## Solution

Give the repo one authoritative, human-readable statement of scope and non-goals, and make the CI/CD story internally consistent.

1. Add a concise **"Scope & Non-Goals"** section to `CONTEXT.md` as the single source of truth, consolidating the implicit framing (learning/portfolio backend; no frontend; simulated payments; depth-over-breadth) and the explicit out-of-scope decisions currently scattered across PRDs/ADRs/runbooks, each linking back to its authoritative ADR/PRD/runbook rather than re-litigating the decision. Link to that section from `README.md` so both entry points converge on one place.

2. Correct the CI/CD claim everywhere it appears. Replace the blanket "GitHub Actions is not used" with the accurate split: **GitHub Actions runs build verification and QA smoke checks (CI gates); Azure Pipelines is the deployment path.** Align `CLAUDE.md`, `AGENTS.md`, and `.github/copilot-instructions.md` with the already-correct wording in `infrastructure-deployment/docs/PATTERNS.md`.

After this, a reader gets the boundaries of the project in under a minute, and no grounding doc tells an agent something the codebase contradicts.

## User Stories

1. As a recruiter skimming the repo, I want a short "what this is / what this is not" statement up front, so that I can judge the author's scoping judgment without reading the whole `docs/` tree.
2. As a developer friend evaluating the project, I want to know it is a learning/portfolio backend rather than a production storefront, so that I calibrate my expectations about frontend, real payments, and feature breadth.
3. As a developer friend, I want the deliberate non-goals listed in one place, so that I do not file "missing feature" issues for things that were intentionally excluded.
4. As an AI agent reading `CONTEXT.md` before a task, I want an explicit non-goals list, so that I do not propose work that the author has already ruled out (e.g. building a frontend, integrating a real payment processor, publishing to public NuGet).
5. As an AI agent, I want each non-goal to link to its authoritative ADR/PRD/runbook, so that I can read the rationale and the conditions under which it might come back into scope.
6. As the repo owner, I want one canonical scope section, so that when a boundary changes I update a single place instead of hunting through scattered docs.
7. As a reader, I want to learn that there is no frontend/UI and the surface is the API Gateway plus Swagger, so that I do not look for a web client that does not exist.
8. As a reader, I want to learn that the Payment service simulates authorize/capture/void/refund rather than calling a real processor, so that I understand the payment flow is a pattern demonstration.
9. As a reader, I want the "depth over breadth" intent stated, so that I understand why there are eight services with deep patterns rather than a wide but shallow catalog of commerce features.
10. As a reader, I want to see that Cosmos DB migration is deferred and Azure SQL is the current datastore, so that I do not assume a multi-model persistence story that is not there yet.
11. As a reader, I want to see that shared libraries publish only to a local NuGet feed and public-feed publishing is out of scope, so that I understand the distribution model.
12. As a reader, I want to see that Ocelot removal, runtime/per-route gateway provider switching, and gateway hot-reload are out of scope, so that I understand the dual-gateway switch is a boot-time choice by design.
13. As a reader, I want to see that per-slice request validation in Auth is a deferred follow-up, so that I do not read its absence as an oversight.
14. As a reader, I want to see that microservice code consolidation is out of scope (services deploy as-is), so that I understand the monorepo's one-solution-per-service boundary is intentional.
15. As an AI agent about to touch CI, I want the grounding docs to correctly say GitHub Actions runs build + smoke checks, so that I do not delete or distrust the existing workflows believing they should not exist.
16. As an AI agent about to touch deployment, I want the grounding docs to say Azure Pipelines is the deployment path, so that I add deploy steps in the right place.
17. As an AI agent, I want the CONTEXT badges that point at GitHub Actions workflows to be consistent with the prose, so that the README does not look self-contradictory to a reader.
18. As a contributor reading `.github/copilot-instructions.md`, I want the CI/CD line to match reality, so that Copilot suggestions about CI are grounded in the actual setup.
19. As a maintainer, I want the corrected CI/CD wording to be identical across `CLAUDE.md`, `AGENTS.md`, and `copilot-instructions.md`, so that the three agent-facing files do not drift again.
20. As a future reader, I want the scope section written so that adding a new non-goal is a one-line append with a link, so that the section stays cheap to maintain and resists rot.

## Implementation Decisions

- **Single source of truth for scope.** The consolidated "Scope & Non-Goals" content lives in `CONTEXT.md` (the human-narrated entry point). `README.md` links to it rather than duplicating it, to avoid two copies drifting.
- **Two content groupings inside the section.** (a) *What this is* — one-paragraph framing already implied by the existing "Why I built it" / "What it is" sections, restated as an explicit scope statement. (b) *Non-goals* — a bulleted list, each item one line with a link to the authoritative ADR/PRD/runbook that owns the decision.
- **Non-goals are pointers, not re-decisions.** This PRD does not change any existing scope decision. It surfaces and links: no frontend/UI; simulated payments; depth-over-breadth feature scope; single-author portfolio/learning intent; Cosmos DB migration deferred (Azure SQL current); local-feed-only NuGet (no public feed); Ocelot removal deferred; runtime/per-route gateway switch + hot reload deferred; Auth per-slice request validation deferred; microservice code consolidation excluded; promoting gateway-internal JWT claim helpers to the shared library deferred.
- **CI/CD reconcile = wording alignment, not behavior change.** No workflow files are added, removed, or edited. The change replaces the inaccurate blanket statement with the accurate split already expressed in `infrastructure-deployment/docs/PATTERNS.md`: GitHub Actions = build verification + QA smoke (CI gates); Azure Pipelines (per-service `azure-pipelines.yml`) = deployment path.
- **Files whose CI/CD line is corrected:** `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`. The corrected sentence is identical across all three to prevent future drift.
- **Badges stay.** The GitHub Actions status badges in `CONTEXT.md` are correct (the workflows exist) and remain; the prose is brought into line with them, not the reverse.
- **No new abstractions.** This is a documentation consolidation. There are no software modules, schema changes, or API contracts. The "modules" are two documentation artifacts: the scope section (content) and the CI/CD wording correction (cross-file edit).

## Testing Decisions

- **What makes a good test here:** for a docs-only change, verification means a reader/agent can find the scope boundaries in one place and no grounding doc states something the codebase contradicts. There is no external program behavior to assert, so automated tests would only couple to prose and provide little value.
- **Verification method (manual review):** confirm the "Scope & Non-Goals" section renders in `CONTEXT.md` and is linked from `README.md`; confirm every non-goal link resolves to a real ADR/PRD/runbook; confirm the three agent-facing files (`CLAUDE.md`, `AGENTS.md`, `copilot-instructions.md`) carry the identical corrected CI/CD sentence; confirm the CONTEXT badges still resolve to the existing workflows.
- **Modules tested:** none via automated tests, per the decision above and confirmed with the author. This matches existing prior art: documentation PRDs in this repo (`PRD-Context`, `PRD-Wiki`) verify via rendering/link-resolution checks in their plans rather than unit tests, and CI-plumbing PRDs (`PRD-Smoke-Test-Bruno-Cli`) explicitly decline to unit-test plumbing that would only couple to internals.

## Out of Scope

- Changing any existing scope decision. This PRD documents and links the boundaries; it does not move them.
- Editing, adding, or removing any GitHub Actions workflow or Azure Pipelines definition. The CI/CD work is wording-only.
- Reworking `CONTEXT.md` beyond adding the scope section and a `README.md` cross-link (no restructuring of "Why I built it", "What I learned", "Link tree", etc.).
- Adding a markdown link-checker or badge-consistency CI check. (Considered and explicitly declined in favor of manual review for this change; could be a separate follow-up if doc drift recurs.)
- Per-service or per-doc scope sections. Scope lives once at the repo level; per-service divergences continue to live in each service's `CLAUDE.md`.

## Further Notes

- The accurate CI/CD framing to mirror is `infrastructure-deployment/docs/PATTERNS.md`: "GitHub Actions is not the deployment path for this repo." The defect is that the stronger, incorrect claim "GitHub Actions is not used" propagated into the three agent-grounding files while the workflows were added.
- Source decisions to link from the non-goals list, for the author's convenience: ADR-0001 (gateway provider switch is boot-time; per-route selection + hot reload out of scope), ADR-0005 / `docs/runbooks/shared-libs-versioning.md` (local feed only), `docs/prd/azure-infrastructure-deployment.md` (Cosmos DB migration deferred), `docs/prd/PRD-Auth-CleanArch-VSA-Pilot.md` (Auth validation deferred), `docs/prd/PRD-ApiGateway-Yarp.md` (Ocelot removal deferred), `docs/prd/PRD-Aks-Deployment.md` (no microservice code consolidation), `docs/prd/PRD-ApiGateway-CleanArch-VSA-Pilot.md` (gateway-internal JWT helper not promoted to shared lib).
