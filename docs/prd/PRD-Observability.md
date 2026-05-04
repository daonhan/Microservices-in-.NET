# PRD: Observability Polish

## Problem Statement

The platform already emits OpenTelemetry traces to Jaeger and exposes a Prometheus scrape endpoint per service, but the observability story has obvious rough edges that make it unreliable for day-two operations. Only the Order service is scraped by Prometheus; the other services (Basket, Product, Auth, Inventory, Gateway) expose `/metrics` but nobody collects them. Only Order emits custom business metrics — the rest have zero domain signal. There is no log pipeline at all: logs go to the container stdout and disappear. There are no dashboards, no alerts, no health probes for Kubernetes to act on, no sampling (every span is exported), no environment / version / instance metadata on telemetry, no infrastructure-level metrics (RabbitMQ queue depth, Redis hit rate, SQL waits), and the `AddConsoleExporter()` calls flood container logs in every environment. When a problem happens, an operator can open Jaeger for one trace and that is it. This PRD closes those gaps without rewriting the architecture.

## Solution

Extend the existing `ECommerce.Shared.Observability` library and the `observability/` compose/K8s assets to deliver a coherent "three pillars" stack: **metrics** (Prometheus + pre-provisioned Grafana dashboards + Alertmanager rules), **logs** (OpenTelemetry Logs → OTLP → Loki, queryable in Grafana), and **traces** (Jaeger, with head-based sampling and resource attributes). Every microservice gets health checks (`/health/live`, `/health/ready`) with real dependency probes, business metrics parity with Order, and Kubernetes scrape annotations so a single `prometheus` deployment discovers them automatically. Infrastructure exporters (RabbitMQ, Redis, SQL, node) are added so dependencies are visible alongside services. Console exporters are gated behind `ASPNETCORE_ENVIRONMENT=Development` to keep production logs clean. The shared library gains a single `AddPlatformObservability(serviceName)` extension that wires tracing, metrics, logs, resource attributes, sampling, and the Prometheus endpoint in one line per service.

## User Stories

### Operator Perspective

1. As an operator, I want every microservice scraped by Prometheus automatically, so that I don't have to edit `prometheus-config.yaml` each time a service is added.
2. As an operator, I want pre-provisioned Grafana dashboards per service, so that I can see request rate, error rate, and latency without building panels from scratch.
3. As an operator, I want a platform overview dashboard, so that I can see the health of all services on one screen.
4. As an operator, I want infrastructure metrics (RabbitMQ queue depth, consumer count, Redis ops/sec, SQL active connections) on dashboards, so that I can diagnose dependency issues.
5. As an operator, I want alert rules firing on high error rate, high latency, and queue backlog, so that I'm paged before customers complain.
6. As an operator, I want structured logs from every service centralized in Loki, so that I can query by `service.name`, `trace_id`, or `user_id` across the whole platform.
7. As an operator, I want to pivot from a trace in Jaeger to the matching logs in Grafana/Loki, so that I can debug a single request end to end.
8. As an operator, I want Kubernetes liveness and readiness probes backed by real dependency checks, so that unhealthy pods are restarted and not-ready pods don't receive traffic.
9. As an operator, I want telemetry tagged with `deployment.environment`, `service.version`, and `service.instance.id`, so that I can filter metrics/logs/traces by environment and rollout.
10. As an operator, I want trace sampling configurable per environment, so that production cost stays bounded while Development keeps 100% traces.
11. As an operator, I want Console exporters off outside Development, so that container logs aren't flooded with telemetry noise.

### Developer Perspective

12. As a developer, I want a single `AddPlatformObservability(serviceName)` call in each `Program.cs`, so that wiring is uniform and low-ceremony.
13. As a developer, I want `ILogger` output automatically exported via OTLP with `trace_id` / `span_id` correlation, so that logs line up with traces without extra code.
14. As a developer, I want business metrics for Basket (baskets updated, items per basket), Product (prices updated, products created), Inventory (stock movements, reservation latency), and Auth (login success/failure), so that every service has first-class domain signal like Order.
15. As a developer, I want health check classes for SQL, RabbitMQ, and Redis in the shared library, so that each service registers only the probes it depends on.
16. As a developer, I want tests that assert `/metrics` exposes the expected series names, so that a renamed meter is caught in CI.
17. As a developer, I want tests that assert `/health/live` and `/health/ready` return the correct status for healthy and unhealthy dependencies, so that K8s probe contracts don't silently break.
18. As a developer, I want unit tests around `MetricFactory` and any new meter wrappers, so that counter/histogram shapes are stable.
19. As a developer, I want Docker Compose to bring up the full observability stack locally (Jaeger, Prometheus, Alertmanager, Grafana, Loki, exporters), so that I can verify dashboards and alerts on my machine.
20. As a developer, I want Kubernetes manifests with the same stack plus `prometheus.io/scrape` annotations, so that cluster deploys mirror local.
21. As a developer, I want the shared library to stay a single NuGet package, so that services pull one versioned dependency for observability.

## Implementation Decisions

### Shared Library (`ECommerce.Shared.Observability`)

- Add a top-level `AddPlatformObservability(serviceName)` extension that composes the existing `AddOpenTelemetryTracing` / `AddOpenTelemetryMetrics` with the new logs pipeline, resource attributes, and sampler, so individual services no longer wire each pillar separately.
- Extend `OpenTelemetryOptions` with: `OtlpExporterEndpoint` (existing), `SamplingRatio` (double, default 1.0 Development / 0.1 Production), `Environment`, `ServiceVersion`, `EnableConsoleExporters` (bool, default false).
- Add OpenTelemetry Logs provider registration: `ILoggingBuilder.AddOpenTelemetry()` with `IncludeFormattedMessage`, `IncludeScopes`, `ParseStateValues`, and OTLP exporter pointed at the same collector endpoint.
- Add `ResourceBuilder` attributes: `service.name`, `service.version`, `service.instance.id` (machine name or pod name via `HOSTNAME`), `deployment.environment`.
- Gate `AddConsoleExporter()` calls on `EnableConsoleExporters`, which is `true` only when env is Development.
- Add `ParentBasedSampler(TraceIdRatioBasedSampler(SamplingRatio))` to tracing.
- Add a `HealthChecks` subfolder with shared health-check registrations: `AddPlatformHealthChecks()` → `/health/live` (always 200 if process is up) and `/health/ready` (composite of registered probes). Provide opt-in helpers `AddSqlServerProbe(connectionString)`, `AddRabbitMqProbe(hostname)`, `AddRedisProbe(configuration)` using the `AspNetCore.HealthChecks.*` community packages.
- Add a `BusinessMetrics` helper per bounded-context style so each service file stays thin (names only, no counters declared in the library itself — metrics remain owned by their service).

### Per-Service Changes

- **Basket:** counters for `basket-updates`, `basket-products-added`, `basket-products-removed`; histogram `basket-size` (items per basket) with buckets [0, 1, 3, 5, 10, 25].
- **Product:** counters for `products-created`, `product-price-updates`.
- **Inventory:** counters for `stock-movements` (tagged by movement type: reserve/commit/release/restock), `stock-reservations-failed`; histogram `reservation-latency-ms` with buckets [5, 25, 100, 500, 2000].
- **Auth:** counters for `login-success`, `login-failure`.
- **Order:** keep existing `total-orders` counter and `products-per-order` histogram.
- **All services:** replace manual OTel wiring in `Program.cs` with `AddPlatformObservability(serviceName)` + `AddPlatformHealthChecks()` + relevant probe registrations. Keep custom-metrics callbacks where services declare their own meters.

### API Gateway

- Register health checks and expose `/health/live`, `/health/ready` (passthrough — gateway has no dependencies of its own beyond upstreams).
- Do not scrape gateway business metrics — it has none — but still emit platform traces + logs.

### Prometheus

- Replace the single `Order` job in `observability/prometheus-config.yaml` with:
  - `kubernetes_sd_configs` block (for K8s target) using `prometheus.io/scrape`, `prometheus.io/path`, `prometheus.io/port` annotations.
  - Static `static_configs` block (for Compose target) listing each service container by DNS name.
- Add `alerting:` section pointing at an Alertmanager service.
- Add a `rule_files:` entry pointing to `observability/alerts.yaml` with initial rules: `HighHttpErrorRate` (>5% 5xx for 5m), `HighHttpLatencyP95` (>1s 5m), `RabbitMqQueueBacklog` (>1000 messages 5m), `ServiceDown` (`up == 0` 2m), `LowStockAlert` piggy-backing on Inventory metric.

### Alertmanager

- Add as a new Compose service + K8s Deployment. Single default receiver (webhook or null) — wiring to real channels (Slack, email) is out of scope.

### Grafana + Loki

- Add Grafana and Loki as Compose services and K8s manifests.
- Ship a `observability/grafana/` folder with provisioning configs: datasources (Prometheus, Loki, Jaeger) and dashboards (per-service RED dashboard, platform overview, RabbitMQ dashboard, SQL dashboard).
- Configure Jaeger → Loki trace-to-logs correlation using the `trace_id` label exported by OpenTelemetry Logs.
- OTLP ingestion path: services → OTel Collector (new) → fan out: traces → Jaeger, metrics → Prometheus remote-write (or keep pull model and let services expose `/metrics`), logs → Loki.
- Decision: keep Prometheus pull-based for metrics (simpler, matches current model); introduce OTel Collector only for logs fan-out to Loki. Traces continue to go direct to Jaeger OTLP. This minimizes moving parts.

### Infrastructure Exporters

- `rabbitmq_exporter` (kbudde/rabbitmq-exporter) → scraped by Prometheus.
- `redis_exporter` (oliver006/redis-exporter) → scraped by Prometheus.
- SQL: use the built-in `mssql-exporter` (awaragi/prometheus-mssql-exporter).
- `node_exporter` only in Kubernetes manifests (not Compose).

### Kubernetes

- Add `prometheus.io/scrape: "true"`, `prometheus.io/path: "/metrics"`, `prometheus.io/port: "8080"` annotations to every service Deployment pod template.
- Add `livenessProbe` (HTTP `/health/live`) and `readinessProbe` (HTTP `/health/ready`) with sensible initial delay / period to every microservice Deployment.
- Add `HOSTNAME` env var (auto-populated by K8s) surfaced as `service.instance.id`.
- Add `DEPLOYMENT_ENV` env var wired into `OpenTelemetry__Environment`.
- Add new manifests: `grafana.yaml`, `loki.yaml`, `alertmanager.yaml`, `otel-collector.yaml`, exporters.

### Configuration Surface

- Each service `appsettings.json` gains an `OpenTelemetry` section with `OtlpExporterEndpoint`, `SamplingRatio`, `Environment`, `ServiceVersion`, `EnableConsoleExporters`.
- Defaults chosen to match prior behavior in Development so existing workflows don't regress.

## Testing Decisions

Good tests verify external behavior: the HTTP response a K8s probe would see, the exact series names a dashboard will query, the fact that a counter increments when the domain event happens. They do not assert on internal OTel builder state, log formatters, or which exporter instance was registered.

- **Unit tests (`*.Tests` projects):**
  - `MetricFactory` and new business-metric helpers: given a meter name, when a counter/histogram is created, then it carries the expected name and unit.
  - Health-check probe classes: given an unhealthy dependency stub, when `CheckHealthAsync` runs, then status is `Unhealthy` with the expected description.
- **Integration tests (`WebApplicationFactory<Program>`):**
  - `GET /health/live` returns 200 when the process is up, regardless of dependencies.
  - `GET /health/ready` returns 200 with all dependencies healthy, 503 with any dependency faulted (using a test-double connection string / stubbed probe).
  - `GET /metrics` returns text/plain Prometheus exposition containing the expected series for that service (e.g., Basket test asserts `basket_updates_total` and `basket_size_bucket` appear after a basket update request).
- **Prior art:** existing `Basket.Tests/Endpoints`, `Order.Tests`, `Product.Tests`, `Inventory.Tests` projects already use `WebApplicationFactory<Program>` with `appsettings.Tests.json` and `IAsyncLifetime` cleanup — the new tests follow the same pattern. Business-metric tests piggy-back on existing endpoint tests (hit the endpoint, then scrape `/metrics`, then assert).
- **Modules tested:** Basket, Order, Product, Inventory, Auth — each gets health + metrics-exposure coverage. Gateway gets health-endpoint coverage only.
- **Not tested automatically:** Grafana dashboards, Alertmanager rules, log shipping to Loki, and RabbitMQ/Redis/SQL exporter presence are verified manually via the Compose stack — these are configuration assets without meaningful unit-test surface.

## Out of Scope

- Real alerting channels (Slack/email/PagerDuty integration). Alertmanager will ship with a null receiver.
- Log retention policies, Loki storage backends beyond local filesystem, Prometheus long-term storage / remote write.
- Custom SLO / SLI calculation rules and error-budget burn alerts.
- Tracing-based sampling strategies beyond head-based `ParentBased(TraceIdRatioBased)` (no tail sampling, no adaptive sampling).
- Synthetic monitoring / blackbox probing.
- Per-tenant or per-user cardinality controls.
- Frontend RUM (real user monitoring).
- Cost/usage dashboards for cloud providers.
- Rewriting any existing business logic; only observability wiring and test additions are expected.
- Production secrets for Grafana admin password — a default dev password is fine at this stage.

## Further Notes

- Keep the shared library backward compatible: `AddOpenTelemetryTracing` and `AddOpenTelemetryMetrics` stay public so in-flight branches don't break; `AddPlatformObservability` is the new recommended entry point.
- OTel Collector is introduced only to fan logs out to Loki; if Loki is dropped later, the collector can be removed without touching service code.
- Grafana datasources and dashboards live under `observability/grafana/` and are mounted read-only into the container — edits in the Grafana UI are intentionally ephemeral; dashboards are source-controlled.
- Prior PRDs (`PRD.md`, `PRD-Inventory.md`) established the pattern of one shared NuGet + per-service Dockerfile + Compose entry + K8s manifest. This PRD follows the same conventions and adds no new deployment primitives.
- Sampling ratio default of 0.1 in Production is a starting point; can be tuned once trace volume is observed.
- The `service.instance.id` attribute is critical for multi-replica K8s deployments (future scaling work) — adding it now costs nothing and unblocks later scale-out.
