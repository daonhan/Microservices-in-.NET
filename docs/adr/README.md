# Architecture Decision Records

This directory captures the load-bearing architectural decisions for the platform in MADR-lite format. ADRs are **append-only**: a superseded decision gets a new ADR with a `Supersedes ADR-XXXX` header rather than an in-place rewrite.

New ADRs start from [`0000-template.md`](0000-template.md).

## Index

| #        | Title                                                                                | Status  |
| -------- | ------------------------------------------------------------------------------------ | ------- |
| ADR-0001 | API Gateway provider switch: YARP default with Ocelot fallback                       | Planned |
| ADR-0002 | Transactional Outbox per publishing service                                          | Planned |
| ADR-0003 | RS256 JWT issuance with `/jwks` discovery                                            | Planned |
| ADR-0004 | RabbitMQ fanout exchange with dead-letter queue and operator API                     | Planned |
| ADR-0005 | `ECommerce.Shared` distributed as a NuGet package via a local feed                   | Planned |
| ADR-0006 | One `.slnx` solution per service; no root `.sln`                                     | Planned |
| ADR-0007 | EF Core with one database per service                                                | Planned |
| ADR-0008 | Saga choreography (no central orchestrator) for Order/Inventory/Payment/Shipping     | Planned |
| ADR-0009 | OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack             | Planned |
