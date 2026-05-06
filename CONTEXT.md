# E-Commerce Microservices Platform — Project Context

[![Docker Build](https://github.com/daonhan/Microservices-in-.NET/actions/workflows/docker-build.yml/badge.svg)](https://github.com/daonhan/Microservices-in-.NET/actions/workflows/docker-build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

> **TL;DR — share-block.** I built this repo to learn and demonstrate microservices in .NET 10 end-to-end, paired with **Claude Code Pro** and **GitHub Copilot Pro+** as my coding partners. It's a seven-service e-commerce platform with a YARP/Ocelot-switchable gateway, a transactional outbox, an event-driven saga across Order/Inventory/Payment/Shipping, RS256 JWT auth with `/jwks` discovery, an OpenTelemetry stack (Jaeger + Prometheus + Loki + Grafana), Kubernetes manifests, and a full GitHub Wiki sourced from `docs/wiki/`. This file is the single grounded entry point for AI agents, developer friends, and recruiters.

### What's interesting here

- **Dual-gateway switch.** The same gateway service compiles both **YARP** (default) and **Ocelot** behind a `Gateway:Provider` flag — same routes, same auth, same metrics, swap at boot.
- **Transactional outbox + RabbitMQ fanout + DLQ operator API.** Publishers never write straight to the broker; a poller drains the outbox, and dead letters surface through a gateway-fronted operator endpoint.
- **Choreographed saga across four services** — Order → Inventory → Payment → Shipping, no central orchestrator, all coordination via integration events.
- **`ECommerce.Shared` as a real NuGet package** against a local feed (`local-nuget-packages/`) instead of project references — closer to how real shared libraries propagate.
- **One `.slnx` per service, no root `.sln`.** Each service has an independent build/test boundary; `Directory.Build.props` enforces `TreatWarningsAsErrors`.
- **AI-first development workflow.** PRDs in `docs/prd/`, plans in `docs/plans/`, and `CLAUDE.md` / `.github/copilot-instructions.md` act as the contract between me and the agents.

### Links

- Code-first reference: [README.md](README.md)
- Wiki home: [docs/wiki/Home.md](docs/wiki/Home.md)
- LinkedIn: [linkedin.com/in/daonhan](https://www.linkedin.com/in/daonhan)
- Substack: [substack.com/@daonhan](https://substack.com/@daonhan)

---

## Why I built it

_Coming in phase 4._

## What it is

_Coming in phase 4._

## Domain glossary

_Coming in phase 3._ Will define: Saga, Outbox, Dead-Letter Queue (DLQ), Integration Event, Reservation, Backorder, Authorize, Capture, Refund, JWKS, Fanout exchange, YARP, Ocelot, Choreography vs Orchestration, Minimal API, `.slnx`, OTEL Collector.

## Architecture at a glance

_Coming in phase 3._ Will reuse the mermaid diagram from [README.md](README.md) with a short orienting paragraph.

## Architectural decisions

The load-bearing decisions live as MADR-lite ADRs under [docs/adr/](docs/adr/README.md). Each is `Accepted` and links to the source folder(s) that implement it.

1. [ADR-0001](docs/adr/0001-api-gateway-yarp-default-ocelot-fallback.md) — API Gateway provider switch: YARP default with Ocelot fallback
2. [ADR-0002](docs/adr/0002-transactional-outbox-per-publishing-service.md) — Transactional Outbox per publishing service
3. [ADR-0003](docs/adr/0003-rs256-jwt-with-jwks-discovery.md) — RS256 JWT issuance with `/jwks` discovery
4. [ADR-0004](docs/adr/0004-rabbitmq-fanout-with-dlq-and-operator-api.md) — RabbitMQ fanout exchange with dead-letter queue and operator API
5. [ADR-0005](docs/adr/0005-ecommerce-shared-as-nuget-via-local-feed.md) — `ECommerce.Shared` distributed as a NuGet package via a local feed
6. [ADR-0006](docs/adr/0006-one-slnx-solution-per-service.md) — One `.slnx` solution per service; no root `.sln`
7. [ADR-0007](docs/adr/0007-ef-core-database-per-service.md) — EF Core with one database per service
8. [ADR-0008](docs/adr/0008-saga-choreography-no-central-orchestrator.md) — Saga choreography (no central orchestrator) for Order/Inventory/Payment/Shipping
9. [ADR-0009](docs/adr/0009-otel-jaeger-prometheus-loki-grafana.md) — OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack

## AI workflow

_Coming in phase 4._ How Claude Code Pro and GitHub Copilot Pro+ were used, and which boundaries I kept under direct human control.

## What I learned

_Coming in phase 4._

## Link tree

_Coming in phase 5._ Will index every wiki page, PRD, plan, runbook, and Kubernetes manifest folder.

---

_Built by Paul Nhan Nguyen Dao — [LinkedIn](https://www.linkedin.com/in/daonhan) · [Substack](https://substack.com/@daonhan)._
