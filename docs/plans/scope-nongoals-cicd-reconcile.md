# Plan: Scope & Non-Goals consolidation + CI/CD doc reconcile

> Source PRD: [PRD-Scope-NonGoals.md](../prd/PRD-Scope-NonGoals.md) — tracked as [#313](https://github.com/daonhan/Microservices-in-.NET/issues/313)

## Context

Why this change: the repo has no single authoritative answer to "what is this project, and what is it *not*?" The scope (learning/portfolio backend, depth-over-breadth, no frontend, simulated payments) is real but implicit, and the explicit out-of-scope decisions are scattered across a dozen PRDs/ADRs/runbooks. A reader has to reverse-engineer the boundaries from the whole `docs/` tree.

Second, one grounding claim is now factually wrong. `CLAUDE.md`, `AGENTS.md`, and `.github/copilot-instructions.md` all say **"GitHub Actions is not used"** — but the repo ships two active workflows (`docker-build.yml`, `smoke-test.yml`) and `CONTEXT.md` renders badges pointing at them. The accurate framing already exists in `infrastructure-deployment/docs/PATTERNS.md` ("GitHub Actions is not the deployment path for this repo") but never propagated to the agent-facing files.

Outcome: a reader gets the project boundaries in under a minute from one place, and no grounding doc tells an agent something the codebase contradicts.

This is a **documentation-only** change — no code, schema, API, or workflow files are added/removed/edited. Verification is manual review (rendering + link resolution + cross-file consistency), matching prior doc-PRD practice (`PRD-Context`, `PRD-Wiki`).

## Architectural decisions

Durable decisions that apply across all phases:

- **Single source of truth.** The "Scope & Non-Goals" content lives once in `CONTEXT.md` (human-narrated entry point). `README.md` links to it, does not duplicate.
- **Section anchor.** New `## Scope & Non-Goals` heading in `CONTEXT.md` → GitHub anchor `#scope--non-goals`. README cross-link targets that anchor.
- **Placement.** Section sits between `## What I learned` (CONTEXT.md:227) and `## Link tree` (CONTEXT.md:243): flow becomes Why → What → Learned → Scope/Non-Goals → Links.
- **Two groupings inside the section.** (a) *What this is* — one paragraph restating the implicit framing as explicit scope. (b) *Non-goals* — bulleted list, each item one line + link to the authoritative ADR/PRD/runbook that owns the decision. New non-goal = one-line append.
- **Non-goals are pointers, not re-decisions.** No existing scope decision changes. Each bullet links to its source:
  | Non-goal | Link target |
  |----------|-------------|
  | Gateway provider switch is boot-time; per-route selection + hot reload out of scope | `docs/adr/0001-api-gateway-yarp-default-ocelot-fallback.md` |
  | Ocelot removal deferred (dual-gateway by design) | `docs/prd/PRD-ApiGateway-Yarp.md` |
  | Local NuGet feed only; no public-feed publishing | `docs/adr/0005-ecommerce-shared-as-nuget-via-local-feed.md`, `docs/runbooks/shared-libs-versioning.md` |
  | Cosmos DB migration deferred (Azure SQL current) | `docs/prd/azure-infrastructure-deployment.md` |
  | Auth per-slice request validation deferred | `docs/prd/PRD-Auth-CleanArch-VSA-Pilot.md` |
  | No microservice code consolidation (services deploy as-is) | `docs/prd/PRD-Aks-Deployment.md` |
  | Gateway-internal JWT claim helper not promoted to shared lib | `docs/prd/PRD-ApiGateway-CleanArch-VSA-Pilot.md` |
  | No frontend/UI (surface is API Gateway + Swagger); simulated payments; depth-over-breadth | framing prose, no external link |
- **CI/CD reconcile = wording alignment, not behavior change.** Replace blanket "GitHub Actions is not used" with the accurate split: **GitHub Actions runs build verification + QA smoke (CI gates); Azure Pipelines (per-service `azure-pipelines.yml`) is the deployment path.** Corrected sentence identical across `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md` to prevent re-drift.
- **Files NOT touched.** No workflow YAML, no `azure-pipelines.yml`, no `PATTERNS.md`, no restructuring of other CONTEXT.md sections. No new abstractions, no link-checker CI.

---

## Phase 1: Scope & Non-Goals section + README cross-link + badge label fix

**User stories**: 1–14, 17, 20

### What to build

Add a new `## Scope & Non-Goals` section to `CONTEXT.md` (between `What I learned` and `Link tree`) with two groupings: a *What this is* framing paragraph and a *Non-goals* bullet list where each bullet links to its authoritative source (per the table above). Add a short cross-link in `README.md` (a `## Scope & Non-Goals` callout pointing to `CONTEXT.md#scope--non-goals`, placed after `## Key Patterns` at README.md:241) — link, do not duplicate content. Fix the mislabeled badge at `CONTEXT.md:4`: it reads `[![Docker Build]...]` but points at `smoke-test.yml`; relabel to **QA Smoke Test** to match the workflow's `name:` (story 17, badge/prose consistency).

### Acceptance criteria

- [ ] `## Scope & Non-Goals` renders in `CONTEXT.md` with a *What this is* paragraph and a *Non-goals* bullet list.
- [ ] *What this is* states: learning/portfolio backend, no frontend (surface = API Gateway + Swagger), simulated payments, depth-over-breadth, single-author intent.
- [ ] Every non-goal bullet that has a source links to a real, resolving ADR/PRD/runbook path (all 8 targets confirmed to exist).
- [ ] `README.md` links to `CONTEXT.md#scope--non-goals` and does not duplicate the content.
- [ ] `CONTEXT.md:4` badge label reads "QA Smoke Test" and still points at `smoke-test.yml`.
- [ ] No other CONTEXT.md / README.md section is restructured.

---

## Phase 2: CI/CD wording reconcile across agent-facing files

**User stories**: 15, 16, 18, 19

### What to build

Replace the inaccurate "GitHub Actions is not used" claim in the three agent-grounding files with the accurate split already expressed in `infrastructure-deployment/docs/PATTERNS.md`. The corrected sentence is **identical** across all three files. Fit it to each file's existing format:
- `CLAUDE.md:87` — `## CI/CD` paragraph (bold sentence today).
- `AGENTS.md:87` — same paragraph, mirror exactly.
- `.github/copilot-instructions.md:41` — inline bullet under "Tech stack (actual)".

Corrected framing: GitHub Actions runs build verification (`docker-build.yml`) and QA smoke checks (`smoke-test.yml`) as CI gates; Azure Pipelines (per-service `azure-pipelines.yml`) is the deployment path.

### Acceptance criteria

- [ ] None of the three files contains the string "GitHub Actions is not used".
- [ ] All three carry an identical corrected CI/CD sentence (build + smoke = GitHub Actions CI gates; Azure Pipelines = deploy path).
- [ ] No workflow file, `azure-pipelines.yml`, or `PATTERNS.md` is edited.
- [ ] `dotnet format`-irrelevant (docs only); no build impact.

---

## Verification (manual review — no automated tests)

Per PRD Testing Decisions: docs-only, automated tests would only couple to prose. Verify end-to-end by:

1. **Render check** — open `CONTEXT.md`; confirm `## Scope & Non-Goals` renders between `What I learned` and `Link tree` with both groupings.
2. **Link resolution** — click/verify every non-goal link resolves to an existing file (8 targets); verify README link jumps to the CONTEXT.md anchor (`#scope--non-goals`).
3. **Cross-file consistency** — grep all three agent files for "GitHub Actions is not used" → zero hits; grep the new corrected sentence → identical in all three. Quick command:
   ```powershell
   Select-String -Path CLAUDE.md, AGENTS.md, .github/copilot-instructions.md -Pattern "GitHub Actions"
   ```
4. **Badge** — confirm `CONTEXT.md:4` badge label = "QA Smoke Test", URL still `smoke-test.yml`; both badges still resolve to existing workflows.
5. **No drift** — `git diff --stat` touches only `CONTEXT.md`, `README.md`, `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md`, and `docs/plans/scope-nongoals-cicd-reconcile.md`. No workflow/pipeline/PATTERNS files.
