# Architecture Decision Records

This directory captures the load-bearing architectural decisions for the platform in MADR-lite format. ADRs are **append-only**: a superseded decision gets a new ADR with a `Supersedes ADR-XXXX` header rather than an in-place rewrite.

New ADRs start from [`0000-template.md`](0000-template.md).

## Index

| #                                                                            | Title                                                                            | Status   |
| ---------------------------------------------------------------------------- | -------------------------------------------------------------------------------- | -------- |
| [ADR-0001](0001-api-gateway-yarp-default-ocelot-fallback.md)                 | API Gateway provider switch: YARP default with Ocelot fallback                   | Accepted |
| [ADR-0002](0002-transactional-outbox-per-publishing-service.md)              | Transactional Outbox per publishing service                                      | Accepted |
| [ADR-0003](0003-rs256-jwt-with-jwks-discovery.md)                            | RS256 JWT issuance with `/jwks` discovery                                        | Accepted |
| [ADR-0004](0004-rabbitmq-fanout-with-dlq-and-operator-api.md)                | RabbitMQ fanout exchange with dead-letter queue and operator API                 | Accepted |
| [ADR-0005](0005-ecommerce-shared-as-nuget-via-local-feed.md)                 | `ECommerce.Shared` distributed as a NuGet package via a local feed               | Accepted |
| [ADR-0006](0006-one-slnx-solution-per-service.md)                            | One `.slnx` solution per service; no root `.sln`                                 | Accepted |
| [ADR-0007](0007-ef-core-database-per-service.md)                             | EF Core with one database per service                                            | Accepted |
| [ADR-0008](0008-saga-choreography-no-central-orchestrator.md)                | Event-driven saga coordination for Order/Inventory/Payment/Shipping (original)   | Superseded by ADR-0010 |
| [ADR-0009](0009-otel-jaeger-prometheus-loki-grafana.md)                      | OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack         | Accepted |
| [ADR-0010](0010-saga-orchestrator-supersedes-choreography.md)                | Saga orchestrator owns Order/Inventory/Payment/Shipping (supersedes ADR-0008)    | Accepted |
| [ADR-0011](0011-order-cleanarch-vsa-pilot.md)                                | Order service Clean Architecture + Vertical Slice pilot                          | Accepted |
| [ADR-0012](0012-clean-arch-vsa-default-service-shape.md)                     | Clean Architecture + Vertical Slices is the default service shape                | Accepted |
| [ADR-0013](0013-shared-libs-multi-package-split.md)                          | `ECommerce.Shared` split into nine capability packages + umbrella metapackage    | Accepted |
| [ADR-0014](0014-env-gated-qa-runtime-seeders-for-operator-and-dlq.md)        | Env-gated runtime seeders for the QA Operator credential and DLQ fixtures        | Accepted |
| [ADR-0015](0015-terraform-as-azure-iac-option.md)                            | Terraform as a second Azure IaC lane owning the `sbx2` environment               | Accepted |
