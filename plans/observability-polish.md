# Plan: Observability Polish

> Source PRD: [docs/prd/PRD-Observability.md](../docs/prd/PRD-Observability.md) · [Issue #1](https://github.com/daonhan/Microservices-in-.NET/issues/1)

## Architectural decisions

Durable decisions that apply across all phases:

- **Shared entry point**: every service wires observability via a single `AddPlatformObservability(serviceName)` extension in `ECommerce.Shared.Observability`. The existing `AddOpenTelemetryTracing` / `AddOpenTelemetryMetrics` remain public for backward compatibility.
- **Telemetry transport**:
  - Traces → direct OTLP to Jaeger (unchanged).
  - Metrics → Prometheus pull model against each service's `/metrics` (unchanged).
  - Logs → OTLP to a new OTel Collector, which forwards to Loki.
- **Resource attributes** (on every signal): `service.name`, `service.version`, `service.instance.id` (pod/host name via `HOSTNAME`), `deployment.environment`.
- **Sampling**: `ParentBased(TraceIdRatioBased(SamplingRatio))`; default `1.0` in Development, `0.1` in Production, configurable via `OpenTelemetry__SamplingRatio`.
- **Console exporters**: gated on `EnableConsoleExporters` (true only when `ASPNETCORE_ENVIRONMENT=Development`).
- **Health endpoints**: `/health/live` (process liveness only) and `/health/ready` (composite of registered dependency probes). Probes available: SQL Server, RabbitMQ, Redis.
- **Prometheus discovery**:
  - Compose: static targets listing each service container by DNS name.
  - Kubernetes: `kubernetes_sd_configs` relabelling off pod annotations `prometheus.io/scrape=true`, `prometheus.io/path=/metrics`, `prometheus.io/port=8080`.
- **Metric naming**: counters end in `_total`, histograms expose `_bucket` / `_sum` / `_count`. Service meter name equals service name (e.g. `Basket.Service`). Business-metric names (per PRD): `basket-updates`, `basket-products-added`, `basket-products-removed`, `basket-size`, `products-created`, `product-price-updates`, `stock-movements`, `stock-reservations-failed`, `reservation-latency-ms`, `login-success`, `login-failure`.
- **Config surface**: each service `appsettings.json` gets an `OpenTelemetry` section: `OtlpExporterEndpoint`, `SamplingRatio`, `Environment`, `ServiceVersion`, `EnableConsoleExporters`. K8s deployments additionally set `DEPLOYMENT_ENV` and surface `HOSTNAME`.
- **Observability assets layout**: `observability/prometheus-config.yaml`, `observability/alerts.yaml`, `observability/alertmanager.yaml`, `observability/otel-collector.yaml`, `observability/grafana/` (datasources + dashboards), `observability/loki/`.
- **K8s probes**: every microservice Deployment gets `livenessProbe` on `/health/live` and `readinessProbe` on `/health/ready`.
- **Test strategy**: unit tests for metric/probe helpers; integration tests via `WebApplicationFactory<Program>` asserting `/health/*` HTTP status and `/metrics` series names. Dashboards, alert rules, and log shipping verified manually via the Compose stack.

---

## Phase 1: Platform Observability Wrapper + Resource Attributes

**User stories**: 9, 10, 11, 12, 21

### What to build

Introduce `AddPlatformObservability(serviceName)` in `ECommerce.Shared.Observability` that composes tracing + metrics registration, applies resource attributes (`service.version`, `service.instance.id`, `deployment.environment`), installs the `ParentBased(TraceIdRatioBased)` sampler, and gates Console exporters on `EnableConsoleExporters`. Extend `OpenTelemetryOptions` with `SamplingRatio`, `Environment`, `ServiceVersion`, `EnableConsoleExporters`. Replace per-service manual OTel wiring in every `Program.cs` (Basket, Order, Product, Auth, Inventory, Gateway) with the new one-liner. Wire the new env vars into Docker Compose and Kubernetes manifests. The existing `AddOpenTelemetryTracing` / `AddOpenTelemetryMetrics` extensions remain callable so nothing breaks mid-migration.

### Acceptance criteria

- [ ] `AddPlatformObservability(serviceName)` exists in the shared library and is called from every microservice `Program.cs`.
- [ ] Traces viewed in Jaeger carry `service.version`, `service.instance.id`, and `deployment.environment` attributes.
- [ ] Setting `OpenTelemetry__SamplingRatio=0.1` visibly reduces exported spans; setting `1.0` restores full volume.
- [ ] With `ASPNETCORE_ENVIRONMENT=Production`, container stdout is free of OTel Console exporter output; Development still shows it.
- [ ] Docker Compose and all Kubernetes Deployments pass the new `OpenTelemetry__*` and `DEPLOYMENT_ENV` environment variables.
- [ ] Services still build and run against the existing Jaeger/Prometheus setup (no regression).

---

## Phase 2: Health Checks End-to-End

**User stories**: 8, 15, 17

### What to build

Add a `HealthChecks` area to `ECommerce.Shared.Observability` exposing `AddPlatformHealthChecks()` plus opt-in probe helpers `AddSqlServerProbe`, `AddRabbitMqProbe`, `AddRedisProbe` backed by the `AspNetCore.HealthChecks.*` community packages. Each service maps `/health/live` (always 200 while the process is up) and `/health/ready` (composite of registered probes, returning 503 when any is unhealthy). Register only the probes each service actually depends on. Wire K8s Deployments to use `/health/live` as `livenessProbe` and `/health/ready` as `readinessProbe` with reasonable initial delay and period. Add integration tests in each `*.Tests` project that exercise both endpoints against a healthy factory and against a factory configured with a bad dependency endpoint.

### Acceptance criteria

- [ ] `GET /health/live` returns 200 for every service regardless of dependency state.
- [ ] `GET /health/ready` returns 200 when all registered probes are healthy and 503 when any probe fails (demonstrated via a stubbed/misconfigured dependency in tests).
- [ ] Every microservice Kubernetes Deployment has `livenessProbe` and `readinessProbe` pointing at the new endpoints.
- [ ] Integration tests asserting probe behavior exist for Basket, Order, Product, Inventory, Auth, and Gateway (Gateway passthrough only — no dependency probes).
- [ ] Killing/stopping the dependent SQL Server container causes the corresponding service's readiness probe to flip to 503 within its configured period.

---

## Phase 3: Prometheus Scrape Coverage + Infra Exporters

**User stories**: 1, 4, 20

### What to build

Rewrite `observability/prometheus-config.yaml` so Prometheus scrapes every microservice: a `static_configs` block for Compose and a `kubernetes_sd_configs` block keyed off `prometheus.io/scrape`, `prometheus.io/path`, `prometheus.io/port` pod annotations for Kubernetes. Annotate every microservice Deployment with those labels. Add RabbitMQ, Redis, and MSSQL exporters as Compose services and as Kubernetes manifests; add `node_exporter` only in Kubernetes. Ensure each exporter is also scraped by Prometheus. This phase does not yet build dashboards — it just makes sure every target shows up as `up == 1` in Prometheus's targets UI.

### Acceptance criteria

- [ ] Prometheus `/targets` in Compose shows every microservice plus RabbitMQ, Redis, and MSSQL exporters with status `UP`.
- [ ] Prometheus `/targets` in Kubernetes shows the same, plus `node_exporter`, discovered purely via pod annotations.
- [ ] Adding a new microservice with the standard annotations is picked up automatically in Kubernetes without editing `prometheus-config.yaml`.
- [ ] Prometheus ad-hoc queries return non-zero values for representative infra series (`rabbitmq_queue_messages_ready`, `redis_commands_processed_total`, `mssql_connections`).

---

## Phase 4: Business Metrics Parity

**User stories**: 14, 16, 18

### What to build

Add the business counters and histograms specified in the PRD to Basket, Product, Inventory, and Auth so every service has domain-level signal matching Order. Each service declares its own meters (using `MetricFactory` / the shared pattern) and increments them from the endpoint handlers / event handlers that own the corresponding business event. Extend unit tests around `MetricFactory` and any new meter helper shapes. Add integration tests per service that hit an endpoint and then scrape `/metrics` to assert the expected series names appear (with non-zero values where applicable).

### Acceptance criteria

- [ ] Basket exposes `basket-updates`, `basket-products-added`, `basket-products-removed` counters and `basket-size` histogram (buckets 0, 1, 3, 5, 10, 25).
- [ ] Product exposes `products-created` and `product-price-updates` counters.
- [ ] Inventory exposes `stock-movements` (tagged with movement type) and `stock-reservations-failed` counters plus `reservation-latency-ms` histogram (buckets 5, 25, 100, 500, 2000).
- [ ] Auth exposes `login-success` and `login-failure` counters.
- [ ] Order's existing `total-orders` counter and `products-per-order` histogram still emit unchanged.
- [ ] Integration tests for each of the above services trigger the relevant endpoint/event and then assert the series appear on `/metrics`.
- [ ] Unit tests cover `MetricFactory` counter/histogram naming and any new meter-wrapper shape.

---

## Phase 5: Logs Pipeline → Loki via OTel Collector

**User stories**: 6, 13

### What to build

Extend `AddPlatformObservability` to register the OpenTelemetry logs provider on `ILoggingBuilder` with `IncludeFormattedMessage`, `IncludeScopes`, `ParseStateValues`, and an OTLP log exporter pointed at a new OTel Collector. Add the OTel Collector as a Compose service and a Kubernetes Deployment, configured to receive OTLP logs and export them to Loki. Add Loki as a Compose service and Kubernetes Deployment with local filesystem storage. Verify that emitted log records carry `trace_id` and `span_id` when written inside an active span and that `service.name` is queryable.

### Acceptance criteria

- [ ] Every microservice emits logs via OTLP to the OTel Collector; no code change beyond the shared library entry point is required per service.
- [ ] Loki contains log records from all six services tagged with `service.name`.
- [ ] Log records emitted inside an HTTP request contain the matching `trace_id` / `span_id` of the surrounding span.
- [ ] Compose and Kubernetes both bring up the Collector + Loki successfully and remain healthy.
- [ ] Removing Loki/Collector from Compose does not break services (logs still go to stdout; OTLP export fails gracefully).

---

## Phase 6: Grafana + Dashboards + Trace↔Log Correlation

**User stories**: 2, 3, 7

### What to build

Add Grafana as a Compose service and Kubernetes Deployment. Provision datasources (Prometheus, Loki, Jaeger) and dashboards via files under `observability/grafana/` mounted read-only. Ship an initial dashboard set: a per-service RED (Rate / Errors / Duration) dashboard templated by `service.name`, a platform overview showing all services on one screen, a RabbitMQ dashboard, and a SQL dashboard — all sourced from the metrics produced in phases 3 and 4. Configure Jaeger↔Loki correlation so clicking a span in Jaeger opens the matching logs in Grafana/Loki via `trace_id`.

### Acceptance criteria

- [ ] Grafana starts with all three datasources pre-provisioned and reachable.
- [ ] The per-service RED dashboard loads for every microservice and shows non-zero data under load.
- [ ] The platform overview dashboard renders tiles for every service plus infra dependencies.
- [ ] Clicking a span in Jaeger pivots into Grafana/Loki and shows logs for the same `trace_id`.
- [ ] Dashboards are source-controlled under `observability/grafana/` and reload on container restart (UI edits are intentionally ephemeral).

---

## Phase 7: Alerting — Rules + Alertmanager

**User stories**: 5

### What to build

Add `observability/alerts.yaml` with the initial rule set (`HighHttpErrorRate` >5% 5xx for 5m, `HighHttpLatencyP95` >1s for 5m, `RabbitMqQueueBacklog` >1000 messages for 5m, `ServiceDown` `up == 0` for 2m, `LowStockAlert` piggy-backing on the Inventory metric) and reference it from Prometheus via `rule_files`. Add Alertmanager as a Compose service and a Kubernetes Deployment with a single null/webhook receiver — real channel integrations remain out of scope. Point Prometheus's `alerting:` section at Alertmanager. Verify each rule can fire by manually inducing the condition (e.g. spamming failing auth requests, stopping a service, killing RabbitMQ consumers).

### Acceptance criteria

- [ ] Prometheus `/rules` page lists all five alert rules as loaded and healthy.
- [ ] Stopping a microservice causes `ServiceDown` to transition to `FIRING` within 2 minutes.
- [ ] Generating >5% 5xx traffic against a service causes `HighHttpErrorRate` to fire.
- [ ] Alertmanager `/alerts` surfaces the firing alerts routed from Prometheus.
- [ ] The default receiver is a null/webhook — no real notification channel is wired.
