## Problem Statement

Since the `PRD-Docs-Refresh-2026-05` sweep (issues #138–#144, baseline as of 2026-05-19), several load-bearing changes have landed in the repo without a matching documentation pass:

- Clean Architecture + Vertical Slices was promoted to the **default service shape** for all services (ADR-0012), closing the migration across Order, Auth, Product, Basket, Inventory, Shipping, Payment, Saga, and API Gateway.
- `ECommerce.Shared` was decomposed into **nine capability packages plus an umbrella metapackage** (ADR-0013), with a dedicated messaging capability package landing separately (PR #294).
- Every service adopted **narrow shared-libs pinning** with a versioning runbook.
- `PATTERNS.md` (Clean Arch + VSA conventions) was added to the docs tree and is referenced from multiple CLAUDE.md files.
- A `/spec-pipeline` + custom-agents LLM authoring workflow shipped (PR #295).
- QA smoke gap closure (#292) wired saga-operator and shipping terminals into the Bruno smoke suite.

As a result, root public-facing files (`README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md`), the 27 in-tree wiki pages under `docs/wiki/`, the `docs/adr/README.md` index, and the ten grounding files (`<svc>-microservice/CLAUDE.md` ×9 + `shared-libs/CLAUDE.md`) collectively still describe the pre-cutover shape. The GitHub Wiki remote is one snapshot further behind because no manual push has occurred since the May refresh.

Developers relying on these surfaces for onboarding, architecture decisions, or LLM-assisted coding receive stale context, which leads to incorrect assumptions about service layout, package selection, and ADR provenance.

## Solution

Perform a targeted reconciliation pass covering every surface in the `PRD-Docs-Refresh-2026-05` catalog plus the per-service and shared-libs grounding files, patching only the deltas in the ~30-day window since 2026-04-27.

Key content changes:
- Reconcile all surfaces with Clean Arch + VSA as the default layout (ADR-0012 + PATTERNS.md link).
- Update the shared-libs catalog to the nine-package + umbrella model with narrow-pinning convention (ADR-0013 + `docs/runbooks/shared-libs-versioning.md` link).
- Add a "Delivered 2026-05" section to `Roadmap.md` and a short paragraph to `CONTEXT.md` acknowledging the spec-pipeline / AFK / custom-agents LLM workflow.
- Add ADR-0011, ADR-0012, and ADR-0013 rows to the `docs/adr/README.md` index and wire cross-references in `CONTEXT.md`.
- Ship `scripts/publish-wiki.ps1` + `scripts/publish-wiki.sh` (matching the `apply-local-k8s` / `build-local-images` sibling convention) and `docs/runbooks/wiki-publish.md` so the GitHub Wiki remote can be updated in a single manual step.
- Patch all ten grounding CLAUDE.md files to reflect the new default shape, narrow-package references, and ADR links.

Delivered as two PRs: (1) root files + `docs/wiki/` + GH Wiki publish tooling + one-shot wiki push; (2) per-service CLAUDE.md ×9 + `shared-libs/CLAUDE.md`.

## User Stories

1. As a new developer, I want `README.md` to describe Clean Architecture + Vertical Slices as the default service shape, so that I start with the correct mental model and do not scaffold features in the legacy layout.
2. As a new developer, I want `CONTEXT.md` to list all nine shared-libs capability packages with narrow-pinning guidance, so that I select the right NuGet package without reading the entire ADR trail.
3. As a new developer, I want `CONTEXT.md` to mention the spec-pipeline and AFK workflow, so that I know AI-assisted authoring is a first-class tool in this repo.
4. As a developer, I want `AGENTS.md` to reflect the ADR-0012 default layout, so that AI coding assistants generating scaffolding produce correctly structured vertical slices.
5. As a developer, I want `.github/copilot-instructions.md` to reference ADR-0012 and the nine-package catalog, so that GitHub Copilot suggestions align with current repo conventions.
6. As a developer, I want `docs/wiki/Architecture.md` to state Clean Arch + VSA as the default and link PATTERNS.md, so that the public wiki is an accurate onboarding reference.
7. As a developer, I want `docs/wiki/Shared-Library.md` to contain a nine-row package catalog table with narrow-pinning guidance, so that contributors can quickly identify which package to reference.
8. As a developer, I want `docs/wiki/Roadmap.md` to contain a "Delivered 2026-05" section listing ADR-0012, ADR-0013, narrow pinning, spec-pipeline, and QA smoke closure, so that the project history is legible.
9. As a developer, I want `docs/wiki/Home.md` to reflect the current tech-stack summary including the nine-package shared-libs model, so that the wiki landing page is accurate.
10. As a developer, I want each `docs/wiki/Service-<svc>.md` page to reference Clean Arch + VSA as the default layout, so that per-service wiki pages do not contradict root architecture docs.
11. As a developer, I want `docs/wiki/Contributing.md` to reference the spec-pipeline / AFK workflow and updated CLAUDE.md grounding guidance, so that contributors know about LLM-assisted authoring.
12. As a developer, I want `docs/wiki/_Sidebar.md` to surface any new navigation entries introduced by this refresh, so that the wiki sidebar stays consistent with the updated page set.
13. As a developer, I want `docs/adr/README.md` to include rows for ADR-0011, ADR-0012, and ADR-0013, so that the ADR index is a complete historical record.
14. As a developer, I want `CONTEXT.md` to cross-reference ADR-0011, ADR-0012, and ADR-0013 where relevant, so that readers can follow the decision trail without searching the `docs/adr/` directory.
15. As an operator, I want `docs/runbooks/wiki-publish.md` to document the prerequisites, clone strategy, and when to run the wiki publish script, so that any team member can execute the push without tribal knowledge.
16. As an operator, I want `scripts/publish-wiki.ps1` to clone the GitHub Wiki remote, rsync `docs/wiki/` into it, commit, and push in a single command, so that publishing is low-effort and repeatable.
17. As an operator, I want `scripts/publish-wiki.sh` to provide the same one-shot publish capability on Linux/macOS, so that the script is usable in any shell environment matching the sibling convention.
18. As a developer using LLM assistance, I want `basket-microservice/CLAUDE.md` to reference Clean Arch + VSA as the default and link ADR-0012, so that AI context is grounded in the current layout.
19. As a developer using LLM assistance, I want `order-microservice/CLAUDE.md` to reference ADR-0012/0013 and narrow-pinning guidance, so that AI suggestions for the Order service use the correct shared-libs packages.
20. As a developer using LLM assistance, I want `auth-microservice/CLAUDE.md` to reflect the default Clean Arch + VSA shape and current shared-libs model, so that AI context is accurate.
21. As a developer using LLM assistance, I want `product-microservice/CLAUDE.md` to reference ADR-0012/0013, so that AI-assisted work in the Product service starts from the correct baseline.
22. As a developer using LLM assistance, I want `inventory-microservice/CLAUDE.md` to reference ADR-0012/0013 and the narrow-pinning runbook, so that AI context is grounded correctly.
23. As a developer using LLM assistance, I want `shipping-microservice/CLAUDE.md` to reference ADR-0012/0013, so that AI context for the Shipping service is current.
24. As a developer using LLM assistance, I want `payment-microservice/CLAUDE.md` to reference ADR-0012/0013, so that AI context for the Payment service is current.
25. As a developer using LLM assistance, I want `saga-microservice/CLAUDE.md` to reference ADR-0012/0013, so that AI context for the Saga service is current.
26. As a developer using LLM assistance, I want `api-gateway/CLAUDE.md` to reference ADR-0012/0013, so that AI context for the API Gateway is current.
27. As a developer using LLM assistance, I want `shared-libs/CLAUDE.md` to list all nine capability packages, the narrow-pinning convention, and the ADR-0013 link, so that AI context for shared-libs work is grounded in the current package model.
28. As a developer, I want the existing drift gate (`scripts/check-doc-drift.*`) to remain green after all patches land, so that CI does not regress on the documentation health checks introduced in the prior refresh.
29. As a developer, I want the GitHub Wiki remote to match the updated `docs/wiki/` contents after PR #1 merges, so that external readers see current documentation.
30. As a project lead, I want both PRs to pass pre-commit (dotnet format + build + Basket tests) without modification, so that the docs-only diffs do not introduce build noise.

## Implementation Decisions

### Module 1 — Root public-facing docs
- Files in scope: `README.md`, `CONTEXT.md`, `AGENTS.md`, `.github/copilot-instructions.md`.
- Reconcile the service-layout description to Clean Arch + VSA default (ADR-0012 link + PATTERNS.md reference).
- Replace or supplement the shared-libs section with the nine-package catalog and narrow-pinning convention (ADR-0013 link + `docs/runbooks/shared-libs-versioning.md` link).
- Add one paragraph in `CONTEXT.md` describing the spec-pipeline / AFK / custom-agents LLM authoring workflow.
- Add one `Roadmap.md` "Delivered" bullet for the spec-pipeline / AFK / custom-agents feature set.
- Keep changes surgical: patch only the stale paragraphs/tables; do not restructure sections.

### Module 2 — `docs/wiki/` in-tree pages (27 files)
- `Shared-Library.md`: replace legacy shared-libs table with the nine-row capability package catalog; add narrow-pinning guidance row/note.
- `Architecture.md`: add/update core-design-rules section to state Clean Arch + VSA as default; link ADR-0012 and PATTERNS.md.
- `Roadmap.md`: add "Delivered 2026-05" section with bullets for ADR-0012, ADR-0013, narrow pinning (PR #294), spec-pipeline (PR #295), QA smoke gap closure (#292).
- `Home.md`: update tech-stack summary to reflect nine-package shared-libs.
- Per-service `Service-*.md` pages (one per service, 9 files): note Clean Arch + VSA as the default layout; add link to ADR-0012 where relevant.
- `Contributing.md`: reference spec-pipeline / AFK workflow and updated CLAUDE.md grounding approach.
- `_Sidebar.md`: add any navigation entries that become stale or missing; no new wiki pages introduced.
- ADR cross-links: wherever existing wiki pages reference the saga orchestrator ADR or prior ADRs, ensure ADR-0011/0012/0013 are also linked where relevant.
- Constraint: update pages in place; no new wiki pages; no sidebar restructuring beyond fixing staleness.

### Module 3 — `docs/adr/README.md` index
- Add a row for ADR-0011 (Order Clean Arch + VSA pilot), ADR-0012 (Clean Arch + VSA default), and ADR-0013 (shared-libs decomposition) to the index table.
- Add cross-references to these ADRs in `CONTEXT.md` where the relevant decisions are discussed.
- Do not modify the ADR bodies themselves.

### Module 4 — `docs/runbooks/wiki-publish.md` (new file)
- Document prerequisites: git CLI, access to the GH Wiki remote (`github.com/daonhan/Microservices-in-.NET.wiki.git`), PowerShell or Bash, write access.
- Document clone strategy: shallow clone of wiki remote into a temp directory; rsync/copy of `docs/wiki/` contents; commit with a standard message; push.
- Document when to run: after PR #1 merges to main; not wired to CI.
- Match Markdown style of existing runbooks in `docs/runbooks/`.

### Module 5 — `scripts/publish-wiki.ps1` + `scripts/publish-wiki.sh` (new files)
- PowerShell script (`publish-wiki.ps1`): clone wiki remote to a temp dir, copy `docs/wiki/` contents, commit with a timestamped message, push. Accept optional `-DryRun` switch.
- Bash script (`publish-wiki.sh`): equivalent functionality; POSIX-compatible; accept `--dry-run` flag.
- Match naming and header convention of `scripts/apply-local-k8s.ps1` / `scripts/apply-local-k8s.sh` and `scripts/build-local-images.ps1` / `scripts/build-local-images.sh`.
- No CI wiring; scripts are invoked manually as documented in the runbook.

### Module 6 — Per-service CLAUDE.md ×9 + `shared-libs/CLAUDE.md`
- Services in scope: basket, order, auth, product, inventory, shipping, payment, saga, api-gateway (nine `<svc>-microservice/CLAUDE.md` or `api-gateway/CLAUDE.md`) plus `shared-libs/CLAUDE.md`.
- Each per-service file: add/update the service-layout paragraph to name Clean Arch + VSA as default and link ADR-0012; add narrow-package reference guidance linking ADR-0013 and the versioning runbook.
- `shared-libs/CLAUDE.md`: update the package catalog section to list all nine capability packages plus the umbrella metapackage; update the narrow-pinning guidance; add ADR-0013 link.
- Keep changes surgical: only patch paragraphs/tables that are stale; do not rewrite CLAUDE.md sections not affected by the delta window.

### PR sequencing
- PR #1 covers Modules 1–5 (root files + wiki + publish tooling). After PR #1 merges, the operator manually runs `scripts/publish-wiki.ps1` or `.sh` to push the refreshed wiki to the GH Wiki remote.
- PR #2 covers Module 6 (grounding files). PR #2 is independent and can be reviewed in parallel but merges after PR #1 to avoid reviewer context-switching.

### Drift gate
- The existing `scripts/check-doc-drift.*` must remain green. Banned phrases stay banned; the services table must list all services at correct ports; markdown link check must pass.
- CleanArch+VSA enforcement is already handled by NetArchTest + the layout analyzer; no new drift-gate rules are added.

## Testing Decisions

**What makes a good test for this PRD**: documentation tests should verify observable output rather than prose content. For this docs-only PRD, the primary test harness is the existing drift gate (`scripts/check-doc-drift.*`) which checks banned phrases, the services table, and markdown link validity. New links introduced by this PRD (ADR cross-links, PATTERNS.md references, runbook links) must all resolve.

**Surfaces to verify:**
- `scripts/check-doc-drift.*` passes green over all touched files after each PR.
- Markdown link checker (as invoked by the drift gate) resolves all new internal links to ADR files, runbooks, and PATTERNS.md.
- `scripts/publish-wiki.ps1 -DryRun` and `scripts/publish-wiki.sh --dry-run` execute without error in a repo with git CLI available, producing the expected clone + copy + commit steps without pushing.
- Pre-commit hook (`dotnet format --verify-no-changes` + `dotnet build --no-restore` + Basket tests) passes unmodified because the PRD diff is docs-only.

**Prior art:**
- Existing drift gate tests in `scripts/check-doc-drift.*` are the canonical model for documentation health checks in this repo.
- `scripts/apply-local-k8s.ps1` and `scripts/build-local-images.ps1` serve as the structural prior art for the new publish scripts.

**Not tested:**
- Prose accuracy of wiki narrative content (human review at PR time).
- Actual GitHub Wiki remote push (manual operator step post-merge, not automated).

## Out of Scope

- Wiki autosync GitHub Action (deferred; this round is manual push only).
- Any change to the existing drift gate — banned phrases, allowlist, service-table check, link checker, and PR template all stay as shipped.
- Code changes under `*-microservice/`, `api-gateway/` source code, `shared-libs/` source code, `kubernetes/`, or `scripts/check-doc-drift.*`.
- Rewriting historical PRDs, plans, or phase summaries.
- ADR-0013 body content edits (this PRD links from the ADR index; the ADR owns its own body).
- Wiki sidebar / page-taxonomy restructuring (existing pages updated in place; no new wiki pages added).
- New per-package wiki pages (`Shared-Library.md` carries the nine-row package catalog in place).
- A dedicated wiki page for the LLM/AFK workflow (CONTEXT.md and Roadmap.md mention only; full wiki page deferred).
- QA smoke suite changes (already shipped in #292; this PRD documents the outcome, does not re-open it).

## Further Notes

- The prior baseline is `PRD-Docs-Refresh-2026-05` (issues #138–#144). Only deltas from the ~30-day window (2026-04-27 to 2026-05-27) need patching; do not redo the saga-cutover documentation.
- The GitHub Wiki remote is `github.com/daonhan/Microservices-in-.NET.wiki.git`. The one-shot push overwrites stale pages but must only be executed after PR #1 has been reviewed and merged to main.
- ADR-0011 documents the Order Clean Arch + VSA pilot; ADR-0012 promotes the pattern to the repo default; ADR-0013 documents the shared-libs decomposition. All three are currently missing from `docs/adr/README.md`.
- `PATTERNS.md` lives at `infrastructure-deployment/docs/PATTERNS.md` and is already referenced from several CLAUDE.md files; wiki and root-doc links should point to its canonical in-repo location.
- The spec-pipeline / AFK / custom-agents workflow shipped in PR #295. The CONTEXT.md paragraph and Roadmap bullet should be brief (2–4 sentences / one bullet); a full wiki page is deferred.
- Two-PR sequencing is a deliberate reviewer-ergonomics choice: public narrative (PR #1) is easier to review without nine CLAUDE.md diffs mixed in.
