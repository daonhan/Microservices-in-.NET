# ADR-0009 — OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

A distributed system that publishes through an outbox, fans out via RabbitMQ, and chains four services in a saga is impossible to debug without first-class observability. Three signals are needed: traces (to follow a request across the saga), metrics (to alert on rates and latencies, including DLQ outcomes), and logs (to see what each service actually did). The platform also wants to model a vendor-neutral observability story rather than locking into one APM SaaS.

Implemented in [`observability/`](../../observability/) and wired into every service via the shared library's `AddPlatformObservability()`. See also the wiki page [`Observability.md`](../wiki/Observability.md).

## Decision

Every service emits **OpenTelemetry** signals through an OTEL Collector. The collector fans out: traces → **Jaeger**, metrics → **Prometheus**, logs → **Loki**. **Grafana** is the single pane of glass for all three. Trace context propagates across HTTP and through RabbitMQ message headers (so a saga's spans stitch into one trace).

## Consequences

- Adding a new backend means changing the collector, not every service.
- Saga debugging is tractable: a single trace ID follows a request from Order → Inventory → Payment → Shipping, including the broker hops.
- The stack adds containers (collector, Jaeger, Prometheus, Loki, Grafana) to the local development environment; Docker Compose handles them.
- Out of scope: managed APM (Application Insights, Datadog) — kept as a swap option since the OTEL Collector makes it a config change.
