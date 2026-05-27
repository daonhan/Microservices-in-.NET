# Plan: Docs + Wiki Refresh — 2026-05 Late Window

> Source PRD: [docs/prd/PRD-Docs-Wiki-Refresh-2026-05-Late.md](../prd/PRD-Docs-Wiki-Refresh-2026-05-Late.md)

Two PRs total:

- **PR #1** = Phases 1–5 (root files + `docs/wiki/` + publish tooling + one-shot wiki push)
- **PR #2** = Phase 6 (per-service CLAUDE.md ×9 + `shared-libs/CLAUDE.md`)

PR #2 is independent and can be reviewed in parallel but merges after PR #1.

## Architectural decisions

Durable across all phases.

- **Default service layout**: Clean Architecture + Vertical Slices (`Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`) as standardised in ADR-0012. Every surface must name this as the default; do not describe it as the Order-only pilot.
- **Shared-libs model**: nine capability packages (`ECommerce.Shared.Kernel`, `.EventBus`, `.RabbitMq`, `.AzureServiceBus`, `.Messaging`, `.DeadLetter`, `.Platform`, `.Contracts`, `.Testing.Qa`) plus one umbrella metapackage (`ECommerce.Shared`). Narrow-pinning: production services reference only the direct capability packages they use. ADR-0013 is the source of record; `docs/runbooks/shared-libs-versioning.md` is the runbook.
- **ADR linkage**: ADR-0011 (Order Clean Arch + VSA pilot), ADR-0012 (Clean Arch + VSA repo default), ADR-0013 (shared-libs decomposition) are all missing from `docs/adr/README.md` and must be added. Cross-references from `CONTEXT.md` must be wired.
- **PATTERNS.md location**: `docs/PATTERNS.md` (canonical in-repo path). All new links point here.
- **LLM authoring workflow**: spec-pipeline / AFK / custom-agents shipped in PR #295. A brief mention (2–4 sentences / one bullet) is sufficient in `CONTEXT.md` and `Roadmap.md`; a full wiki page is deferred.
- **Wiki publish scripts**: named `scripts/publish-wiki.ps1` and `scripts/publish-wiki.sh`, following the header and parameter-convention of `scripts/apply-local-k8s.ps1` / `scripts/apply-local-k8s.sh`. Wiki remote: `github.com/daonhan/Microservices-in-.NET.wiki.git`. Scripts accept `-DryRun` / `--dry-run` flags. No CI wiring; manually invoked per runbook.
- **Drift gate**: `scripts/check-doc-drift.*` must remain green after each phase. No new drift-gate rules are added; no changes to those scripts.
- **Surgical constraint**: patch only stale paragraphs/tables in each file. Do not restructure sections, rename headings, or touch prose that is not affected by the ~30-day delta window since 2026-04-27.
- **Pre-commit**: all PRs are docs-only diffs; `dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests must pass unmodified.

---

## Phase 1: Root public-facing docs reconciliation

**User stories**: 1, 2, 3, 4, 5, 13, 14

### What to build

Patch the four root public-facing files — `README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md` — so they reflect the delta from the past 30 days. Specifically:

- Update the service-layout description in each file to name Clean Architecture + Vertical Slices as the repo default, link ADR-0012, and reference `docs/PATTERNS.md`.
- Replace or supplement the shared-libs section in each file with the nine-package capability catalog and narrow-pinning convention, linking ADR-0013 and `docs/runbooks/shared-libs-versioning.md`.
- Add one paragraph to `CONTEXT.md` (2–4 sentences) describing the spec-pipeline / AFK / custom-agents LLM authoring workflow (PR #295).
- Add ADR-0011, ADR-0012, and ADR-0013 rows to `docs/adr/README.md` index.
- Wire cross-references to ADR-0011, ADR-0012, and ADR-0013 in `CONTEXT.md` where the relevant decisions are discussed.

Changes are surgical: only stale paragraphs and tables are patched; section structure is preserved.

### Acceptance criteria

- [ ] `README.md`, `AGENTS.md`, and `.github/copilot-instructions.md` each describe Clean Arch + VSA as the default layout and link ADR-0012.
- [ ] `CONTEXT.md` describes Clean Arch + VSA as the default, links ADR-0012 and `docs/PATTERNS.md`, and includes the nine-package shared-libs catalog with narrow-pinning guidance linking ADR-0013 and `docs/runbooks/shared-libs-versioning.md`.
- [ ] `CONTEXT.md` contains a 2–4 sentence paragraph on the spec-pipeline / AFK / custom-agents workflow.
- [ ] `CONTEXT.md` cross-references ADR-0011, ADR-0012, and ADR-0013 in the relevant discussion sections.
- [ ] `docs/adr/README.md` index table has rows for ADR-0011, ADR-0012, and ADR-0013 in numeric order.
- [ ] All new internal links (ADR files, PATTERNS.md, runbook) resolve as relative paths.
- [ ] `scripts/check-doc-drift.*` passes green over all touched files.

---

## Phase 2: `docs/wiki/` core pages — architecture + shared-libs + home

**User stories**: 6, 7, 9, 12

### What to build

Update the three wiki pages that carry the primary architecture narrative:

- `docs/wiki/Architecture.md`: add or update the core-design-rules section to state Clean Arch + VSA as the default service layout; link ADR-0012 and `docs/PATTERNS.md`. Where existing text references the Order-only pilot or the pre-ADR-0012 state, patch to the current default.
- `docs/wiki/Shared-Library.md`: replace the legacy shared-libs table with a nine-row capability package catalog (one row per package: name, purpose, key extension methods / types it provides). Add a narrow-pinning guidance note referencing `docs/runbooks/shared-libs-versioning.md` and ADR-0013.
- `docs/wiki/Home.md`: update the tech-stack summary section to reflect the nine-package shared-libs model and the Clean Arch + VSA default layout. No topology diagram changes needed unless the existing diagram is factually wrong about the current stack.

Changes are in-place; no new wiki pages are created.

### Acceptance criteria

- [ ] `docs/wiki/Architecture.md` states Clean Arch + VSA as the repo default, links ADR-0012 and `docs/PATTERNS.md`.
- [ ] `docs/wiki/Shared-Library.md` contains a nine-row package catalog table with narrow-pinning guidance.
- [ ] `docs/wiki/Home.md` tech-stack summary reflects the nine-package model and the Clean Arch + VSA default.
- [ ] No new wiki pages created; `docs/wiki/_Sidebar.md` unchanged unless an existing entry is stale (fix only what is stale).
- [ ] All new internal links resolve.
- [ ] `scripts/check-doc-drift.*` passes green.

---

## Phase 3: `docs/wiki/` per-service pages + contributing + roadmap + ADR cross-links

**User stories**: 8, 10, 11, 12, 28

### What to build

Update the remaining wiki pages that are stale relative to the delta window:

- Per-service `docs/wiki/Service-*.md` pages (nine files: API-Gateway, Auth, Basket, Inventory, Order, Payment, Product, Saga, Shipping): in each, note Clean Arch + VSA as the default layout for that service and link ADR-0012. Where an existing page references an old layout or the pre-ADR-0012 pilot description, patch only that paragraph.
- `docs/wiki/Contributing.md`: reference the spec-pipeline / AFK workflow and updated CLAUDE.md grounding approach. Keep the addition brief (one paragraph or a bullet in the relevant section).
- `docs/wiki/Roadmap.md`: add a "Delivered 2026-05" section with bullets for ADR-0012 (Clean Arch + VSA default), ADR-0013 / narrow pinning (PR #294), spec-pipeline / AFK / custom-agents (PR #295), and QA smoke gap closure (#292).
- Wherever existing wiki pages reference prior ADRs and ADR-0011/0012/0013 are relevant, add the missing cross-link. Do not rewrite existing ADR references — add only.
- `docs/wiki/_Sidebar.md`: fix any navigation entries that became stale from this refresh. No new entries needed (no new pages introduced).

### Acceptance criteria

- [ ] All nine `Service-*.md` pages reference Clean Arch + VSA as the default and link ADR-0012.
- [ ] `docs/wiki/Contributing.md` mentions the spec-pipeline / AFK workflow.
- [ ] `docs/wiki/Roadmap.md` has a "Delivered 2026-05" section with the four bullets above.
- [ ] ADR-0011, ADR-0012, and ADR-0013 cross-links are present where relevant in the updated pages.
- [ ] `docs/wiki/_Sidebar.md` has no stale entries.
- [ ] All new internal links resolve.
- [ ] `scripts/check-doc-drift.*` passes green over all touched files.

---

## Phase 4: `docs/runbooks/wiki-publish.md` (new runbook)

**User stories**: 15

### What to build

Create `docs/runbooks/wiki-publish.md` documenting the manual wiki push procedure. Content:

- Prerequisites: git CLI, write access to the GitHub Wiki remote (`github.com/daonhan/Microservices-in-.NET.wiki.git`), PowerShell 7+ or Bash.
- Clone strategy: shallow clone the wiki remote into a temp directory; copy `docs/wiki/` contents into it; commit with a standard timestamped message; push.
- When to run: after PR #1 merges to main; not wired to CI; the operator triggers it manually.
- Script invocation examples for both PowerShell (`./scripts/publish-wiki.ps1`) and Bash (`./scripts/publish-wiki.sh`), including dry-run usage.
- Match the Markdown style (heading levels, code block language tags, note/warning callout style) of existing runbooks in `docs/runbooks/`.

### Acceptance criteria

- [ ] `docs/runbooks/wiki-publish.md` exists with prerequisites, clone strategy, timing guidance, and script invocation examples.
- [ ] Markdown style matches existing runbooks (e.g. `docs/runbooks/shared-libs-versioning.md`).
- [ ] Links to the publish scripts and wiki remote are present and syntactically correct.
- [ ] `scripts/check-doc-drift.*` passes green.

---

## Phase 5: `scripts/publish-wiki.ps1` + `scripts/publish-wiki.sh` (new scripts)

**User stories**: 16, 17, 29, 30

### What to build

Create two equivalent publish scripts following the header convention of `scripts/apply-local-k8s.ps1` / `scripts/apply-local-k8s.sh` and `scripts/build-local-images.ps1` / `scripts/build-local-images.sh`:

- `scripts/publish-wiki.ps1` (PowerShell 7+): synopsis/description/parameter/example header block; clone wiki remote to a temp dir; copy `docs/wiki/` contents into it; commit with a timestamped message including the HEAD commit SHA; push. Accept `-DryRun` switch: when set, print the steps that would execute but do not push.
- `scripts/publish-wiki.sh` (Bash, POSIX-compatible): equivalent functionality; `--dry-run` flag; same clone-copy-commit-push sequence.

Both scripts:
- Exit non-zero on any git failure.
- Print progress messages matching the verbosity style of sibling scripts.
- Do not modify any files under `docs/wiki/` on disk; work entirely in the temp clone.

After both scripts are created and PR #1 merges, the operator manually runs one of them to push the refreshed wiki to the GitHub Wiki remote — completing story 29.

### Acceptance criteria

- [ ] `scripts/publish-wiki.ps1` exists with the standard synopsis/description/parameter/example header block.
- [ ] `scripts/publish-wiki.sh` exists and is POSIX-compatible.
- [ ] Both scripts accept a dry-run flag and print expected steps without pushing when it is set.
- [ ] `./scripts/publish-wiki.ps1 -DryRun` executes without error in a repo with git CLI available.
- [ ] `./scripts/publish-wiki.sh --dry-run` executes without error in a repo with git CLI available.
- [ ] Both scripts exit non-zero on any git failure.
- [ ] PR #1 (Phases 1–5) passes pre-commit (`dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests) unmodified.
- [ ] `scripts/check-doc-drift.*` passes green over the full PR #1 diff.

---

## Phase 6: Per-service CLAUDE.md ×9 + `shared-libs/CLAUDE.md` (PR #2)

**User stories**: 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 30

### What to build

Patch the ten grounding files — `basket-microservice/CLAUDE.md`, `order-microservice/CLAUDE.md`, `auth-microservice/CLAUDE.md`, `product-microservice/CLAUDE.md`, `inventory-microservice/CLAUDE.md`, `shipping-microservice/CLAUDE.md`, `payment-microservice/CLAUDE.md`, `saga-microservice/CLAUDE.md`, `api-gateway/CLAUDE.md`, and `shared-libs/CLAUDE.md` — to reflect the ~30-day delta.

Per-service CLAUDE.md files (nine): for each, add or update the service-layout paragraph to name Clean Architecture + Vertical Slices as the default and link ADR-0012; add a narrow-package reference note linking ADR-0013 and `docs/runbooks/shared-libs-versioning.md`. Changes are surgical — only the stale paragraphs or tables are patched; sections unaffected by the delta window are left untouched.

`shared-libs/CLAUDE.md`: the package catalog section already reflects the nine-package model and narrow-pinning guidance. Verify it includes the ADR-0013 link and the versioning runbook link. If either is missing, add it. No other rewrites.

All ten files are in a single PR (#2) for reviewer-ergonomics isolation from the public narrative changes in PR #1.

### Acceptance criteria

- [ ] All nine per-service CLAUDE.md files name Clean Arch + VSA as the default layout and link ADR-0012.
- [ ] All nine per-service CLAUDE.md files include a narrow-package reference note linking ADR-0013 and `docs/runbooks/shared-libs-versioning.md`.
- [ ] `shared-libs/CLAUDE.md` links ADR-0013 and `docs/runbooks/shared-libs-versioning.md` (already present; confirm no regression).
- [ ] No CLAUDE.md section unrelated to the delta window is modified.
- [ ] PR #2 passes pre-commit (`dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests) unmodified.
- [ ] `scripts/check-doc-drift.*` passes green over the full PR #2 diff.
