# ADR-0009 — OpenTelemetry + Jaeger + Prometheus + Loki + Grafana observability stack

- **Status**: Accepted
- **Date**: 2026-05-06
- **Addendum**: 2026-06-10 — cloud APM swapped to the Azure Monitor Distro; cloud metrics/alerting moved to App Insights + Bicep alerts (see the [Addendum](#addendum-2026-06-10--cloud-apm-azure-monitor-distro) below).

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

## Addendum (2026-06-10) — Cloud APM: Azure Monitor Distro

This ADR named managed APM out of scope but "kept as a swap option since the OTEL Collector makes it a config change." That swap is now realized **for the cloud only**. Per [PRD #332](../prd/PRD-AzureMonitor-Distro-Observability.md) (plan: [`azuremonitor-distro-observability.md`](../plans/azuremonitor-distro-observability.md)), the `OpenTelemetry:Exporter=AzureMonitor` branch of `AddPlatformObservability()` adopts the **Azure Monitor OpenTelemetry Distro** (`Azure.Monitor.OpenTelemetry.AspNetCore`): one `UseAzureMonitor()` call replaces the three manual `AddAzureMonitor{Trace,Metric,Log}Exporter` calls and brings Live Metrics, the automatic Application Map, and auto-collected request/dependency telemetry (AspNetCore + HttpClient + SqlClient).

The default `Otlp` branch — and therefore the entire local Jaeger/Prometheus/Loki/Grafana stack described above — is unchanged. This is a config-gated, cloud-only change; local development is untouched.

Because the distro owns metrics in the cloud, in-cluster Prometheus is dropped there (metrics flow to App Insights only) and the five local Prometheus rules in [`observability/alerts.yaml`](../../observability/alerts.yaml) (now marked local-only) are re-created as Azure Monitor alerts in Bicep: `HighHttpErrorRate` / `HighHttpLatencyP95` / `LowStockAlert` over App Insights telemetry, and `ServiceDown` over Container Insights.

Rollout is tracked as children of PRD #332: composition refactor (#333), action group (#335), and the alert slices (#336–#338).

### Deferred production-monitoring gaps

Two signals the local Prometheus stack provided do not survive the cloud move. They are **deferred, not dropped** — recorded here so they are tracked rather than forgotten:

1. **Redis dependency instrumentation (Basket's app-map edge).** The distro auto-instruments AspNetCore, HttpClient, and SqlClient, but **not** `StackExchange.Redis`. Until a follow-up adds Redis instrumentation, Basket's Redis calls will not appear as dependencies on the Application Map.
2. **`RabbitMqQueueBacklog` alert + its prerequisite queue-depth metric.** Broker queue depth came from the RabbitMQ Prometheus exporter, which is dropped in the cloud. The durable fix is to emit queue depth as an **app-level custom metric** and alert on it in Azure Monitor; both the metric and the alert are deferred follow-ups, so there is no cloud alert on broker backlog in the interim.
