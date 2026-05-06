# ADR-0007 — EF Core with one database per service

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

Services that own state (Auth, Order, Product, Inventory, Payment, Shipping) need persistence. Two well-known anti-patterns to avoid: (a) one shared database with foreign keys crossing service boundaries, and (b) ad-hoc per-service ORMs that make it impossible to reason about migrations and schema lifecycle uniformly. The platform also explicitly demonstrates database-per-service ownership, which is the canonical microservices data pattern.

Implemented inside each service's `Infrastructure/Data/` and `Migrations/` folders, with `IDesignTimeDbContextFactory` wired so `dotnet ef migrations add ...` works without booting `Program.cs`. See also the wiki page [`Architecture.md`](../wiki/Architecture.md).

## Decision

Every stateful service uses **EF Core** against its **own database** (SQL Server in development; Basket uses Redis as it is a cache, not a relational store). Schemas are not shared across services. Cross-service consistency is achieved through the saga (ADR-0008) and the outbox (ADR-0002), not through cross-database joins or shared tables.

## Consequences

- Schema changes in one service never block another; migrations run independently.
- Reporting that needs cross-service data must do so through events or a downstream read model — joining at the database layer is not an option.
- One ORM (EF Core) across services means one mental model for migrations, one set of conventions for `IDesignTimeDbContextFactory`, and consistent test patterns.
- Out of scope: read replicas, CQRS read-model stores, cross-cutting analytics warehouse.
