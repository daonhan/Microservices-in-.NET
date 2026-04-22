# PRD — Repository Wiki

## Problem Statement

As a developer encountering this repository — whether a new contributor, an external learner studying microservices in .NET, or an operator deploying the platform — I currently have to stitch together knowledge from the `README.md`, four existing PRDs in `docs/prd/`, four implementation plans in `docs/plans/`, Kubernetes manifests, observability config, and source code to understand how the system fits together. There is no single navigable entry point that answers questions like "what does the Basket service expose?", "how do integration events flow through the saga?", "how do I run this locally?", "how do I roll the API Gateway back from YARP to Ocelot?", and "how is auth enforced at the gateway?".

The `README.md` gives a great overview but is intentionally short. Deep detail is scattered or implicit in code. Onboarding cost is higher than it needs to be, and the repository's value as a reference implementation is under-realized because its architecture story is not discoverable from outside the codebase.

## Solution

Publish a GitHub Wiki at `https://github.com/daonhan/Microservices-in-.NET/wiki` that is the canonical, navigable reference for the platform. The wiki will be organized around how readers actually approach the system: overview → architecture → getting started → per-service pages → cross-cutting concerns (shared library, events, observability, testing, deployment) → contributing and troubleshooting.

The wiki will be generated from markdown files checked into `docs/wiki/` in the main repo so the content is reviewable alongside code changes, then published to the `Microservices-in-.NET.wiki` git repository. The `docs/wiki/` directory is the source of truth; pushes to the wiki repo are the distribution step.

## User Stories

1. As a new contributor, I want a Wiki **Home** page that summarizes the platform and links to every other page, so that I have one entry point.
2. As a new contributor, I want a **Getting Started** page that tells me exactly which commands to run to bring the full stack up locally, so that I can reproduce a working environment in minutes.
3. As a new contributor, I want a **Contributing** page that documents coding conventions (Minimal APIs, DTO vs domain separation, `Given_When_Then` test naming, Outbox usage), so that my first PR matches the house style.
4. As an external learner, I want an **Architecture** page with the high-level mermaid diagram, the per-service/per-datastore rule, the event-driven communication pattern, and the saga flow, so that I can study the design decisions.
5. As an external learner, I want a **Shared Library** page describing `ECommerce.Shared` — JWT, EventBus, Outbox, Observability, Health Checks — so that I can understand what code is reused vs service-specific.
6. As an external learner, I want an **Integration Events Catalog** that lists every event, its publisher, its subscribers, and its payload fields, so that I can trace cross-service interactions without reading every handler.
7. As an operator, I want a **Kubernetes Deployment** page describing the manifests, the apply order, and the service-discovery DNS names, so that I can deploy the stack to a cluster.
8. As an operator, I want an **Observability** page that enumerates the signals (traces → Jaeger, metrics → Prometheus, logs → Loki), the dashboards, and the alerts (`HighHttpErrorRate`, `HighHttpLatencyP95`, `RabbitMqQueueBacklog`, `ServiceDown`, `LowStockAlert`), so that I know what to watch in production.
9. As an operator, I want a **Troubleshooting / FAQ** page covering common failure modes (RabbitMQ connection flap, outbox backlog, JWT clock skew, SQL cold-start, YARP→Ocelot rollback), so that I have a runbook.
10. As a developer integrating with the platform, I want an **API Reference** page listing every endpoint per service (method, route, auth requirement, purpose), so that I don't have to read endpoint files.
11. As a developer, I want a **Basket** service page describing its Redis cache, its endpoints, and how it subscribes to `OrderCreatedEvent` / `ProductPriceUpdatedEvent`, so that I can modify it safely.
12. As a developer, I want an **Order** service page describing the Outbox flow, the saga coordination with Inventory, and the `OrderCreatedEvent` / `OrderConfirmedEvent` / `OrderCancelledEvent` lifecycle, so that I can reason about order state transitions.
13. As a developer, I want a **Product** service page describing the catalog CRUD, the admin-role write protection, and the `ProductCreatedEvent` / `ProductPriceUpdatedEvent` emissions, so that I understand the downstream impact of catalog changes.
14. As a developer, I want an **Auth** service page describing JWT issuance, HMAC-SHA256 signing, and the `user_role` claim, so that I can reason about authentication end-to-end.
15. As a developer, I want an **Inventory** service page describing stock reservations, backorders, low-stock monitoring, and the seven events it publishes, so that I can extend inventory behavior.
16. As a developer, I want an **API Gateway** service page describing the YARP-default / Ocelot-fallback toggle, the routes, and the role-based rules, so that I can add a new upstream without breaking auth.
17. As a developer, I want a **Testing** page describing xUnit + NSubstitute for units, `WebApplicationFactory<Program>` + `IAsyncLifetime` for integration, and RabbitMQ publish/subscribe verification patterns, so that I write tests the same way the rest of the repo does.
18. As a maintainer, I want a **Roadmap** page that reflects the state of existing plans in `docs/plans/` and known next steps, so that intent is visible to the community.
19. As any reader, I want a **_Sidebar** that mirrors the page hierarchy on every wiki page, so that navigation is consistent.
20. As any reader, I want a **_Footer** that points back to the repo and to the license, so that provenance is always visible.
21. As a maintainer, I want the wiki content to live in `docs/wiki/` in the main repo and be pushed to the wiki git remote, so that wiki edits go through code review.
22. As an AI agent or automation script, I want stable wiki page names (`Service-Basket`, `Integration-Events`, etc.) and cross-links using those names, so that future automated edits don't break links.

## Implementation Decisions

- **Source of truth**: The `docs/wiki/` directory in the main repo. The GitHub Wiki (`Microservices-in-.NET.wiki.git`) is a downstream publishing target.
- **Publishing mechanism**: Clone the wiki git repo, mirror the contents of `docs/wiki/` into it, commit with a descriptive message, and push. This is documented in `docs/wiki/Contributing.md` and is runnable locally.
- **Page set (initial)**: `Home`, `Architecture`, `Getting-Started`, `Service-Basket`, `Service-Order`, `Service-Product`, `Service-Auth`, `Service-Inventory`, `Service-API-Gateway`, `Shared-Library`, `Integration-Events`, `API-Reference`, `Testing`, `Observability`, `Kubernetes-Deployment`, `Contributing`, `Troubleshooting`, `Roadmap`, plus `_Sidebar` and `_Footer`.
- **Naming**: Dash-separated page names so GitHub Wiki URLs are readable (`/wiki/Service-Basket`).
- **Depth**: Medium — every page explains the concept, embeds a mermaid diagram where it adds value, and links into the repo (`README.md`, `docs/prd/*`, `docs/plans/*`, specific source folders) for authoritative detail. No code snippets or file-line citations that drift (file paths only, where they are stable).
- **Diagrams**: Mermaid only (rendered natively by GitHub Wiki). Reuse the top-level diagram from `README.md` on `Home`/`Architecture`; add a saga sequence diagram on `Service-Order` and `Integration-Events`.
- **Cross-links**: Every service page links to its siblings via the `_Sidebar`. The `Integration-Events` page deep-links into service pages. The `API-Reference` page deep-links into service pages.
- **No duplicated data**: Service pages link to the shared `Integration-Events` catalog rather than re-listing schemas. The `Home` page links to the `README.md` for the quickstart rather than forking it.
- **PRD lifecycle**: This PRD is saved at `docs/prd/PRD-Wiki.md` (matching repo convention) **and** submitted as a GitHub issue for visibility.
- **Gateway provider**: Pages describe YARP as the default and Ocelot as the runtime-switchable fallback, consistent with `README.md` and `docs/prd/PRD-ApiGateway-Yarp.md`.
- **API reference accuracy**: The `API-Reference` page is generated from the current endpoint classes under `{service}-microservice/{Service}.Service/Endpoints/` at the time of writing. A note on each page reminds maintainers to update the wiki in the same PR as endpoint changes.

## Testing Decisions

Good tests exercise external behavior, not implementation details. For a wiki the "behavior" is link correctness and content accuracy. Implementation-detail tests (e.g., "the file has a heading named X") are brittle and will not be written.

- **What is tested**: The wiki itself is markdown content, so tests are limited to link/reference checks that catch real breakage.
- **What is not tested**: Wording, heading structure, diagram rendering. These are reviewed in PR.
- **Out-of-repo publishing**: No automated test is written for `git push` to the wiki remote; the publish step is a documented manual command in `docs/wiki/Contributing.md`.
- **Prior art**: The existing repo has no markdown-linting infrastructure. We are not introducing one as part of this PRD; if link-checking is desired later it can be added as a separate PR.

## Out of Scope

- Auto-generating API reference or event catalogs from source at build time.
- Markdown lint or dead-link CI.
- Translating the wiki into other languages.
- Video walkthroughs or animated diagrams.
- Migrating `README.md` content into the wiki (`README.md` remains the repo quickstart).
- Changing any microservice behavior, endpoint, or config.

## Further Notes

- The wiki git remote is `https://github.com/daonhan/Microservices-in-.NET.wiki.git`. It is a separate git repository initialized automatically when the first wiki page is created on GitHub.
- Future enhancement: add a short GitHub Action that rsyncs `docs/wiki/` to the wiki remote on push to `main`. Explicitly deferred to a follow-up PRD.
