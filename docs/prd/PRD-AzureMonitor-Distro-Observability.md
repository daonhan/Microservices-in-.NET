# PRD: Azure Monitor Distro for Production Observability

## Problem Statement

Every service already emits OpenTelemetry traces, metrics, and logs through the shared `AddPlatformObservability()` seam. Locally that flows over OTLP to the vendor-neutral stack (Jaeger / Prometheus / Loki / Grafana). In the cloud, the AKS manifests for **dev, staging, and prod** flip `OpenTelemetry:Exporter=AzureMonitor` and ship the same three signals to a single workspace-based Application Insights resource per environment.

But the cloud path uses the **raw `Azure.Monitor.OpenTelemetry.Exporter`** — a data-path-only exporter. That means operators looking at App Insights get none of the things that make App Insights worth using:

- **No Live Metrics** — there is no real-time stream of request/dependency/failure rates while watching a deploy.
- **No Application Map** — there is no automatic service topology, so an operator can't see Order → Product → Inventory at a glance.
- **No automatic dependency collection** — the manual pipeline never registered HttpClient or Redis instrumentation, so outbound HTTP calls (Order→Product, gateway→services) and Basket's Redis calls are invisible as dependencies. Only SQL is instrumented, and only on the seven SQL services.

So in production an operator can pivot from a request to its SQL calls and that is roughly it — the same thin experience the local Jaeger view already gives, minus the topology and the live view that are the whole reason to pay for App Insights.

Separately, the cloud metrics story is muddled: the Prometheus scrape endpoint and `prometheus.io/scrape` annotations are still emitted in every cloud manifest even though the intended cloud sink is App Insights, and the five Prometheus alert rules in `observability/alerts.yaml` only exist for the local stack.

## Solution

Adopt the **Azure Monitor OpenTelemetry Distro** (`Azure.Monitor.OpenTelemetry.AspNetCore`) on the existing `Exporter=AzureMonitor` branch of `AddPlatformObservability()`. One `UseAzureMonitor()` call replaces the three manual Azure Monitor exporters and brings the full App Insights APM experience: Live Metrics, automatic Application Map, and auto-collected request/dependency telemetry for AspNetCore + HttpClient + SqlClient — so the topology and outbound HTTP/SQL dependencies appear without any per-service wiring.

The change is **config-gated and surgical**. The default (`Otlp`) branch stays byte-for-byte identical, so local dev and every existing test are untouched. The distro path layers the platform's custom Activity sources (RabbitMQ / DeadLetter / Outbox), custom Meters, and the per-service tracing/metrics lambdas on top of `UseAzureMonitor()`, while suppressing the double SQL instrumentation the distro would otherwise duplicate.

Because the distro owns metrics in the cloud, in-cluster Prometheus is dropped there and **metrics go to App Insights only**. To avoid leaving production unalerted, the five Prometheus alert rules are re-created as **Azure Monitor alerts in Bicep**: three map cleanly to App Insights telemetry, ServiceDown maps to Container Insights, and the RabbitMQ queue-backlog rule is deferred to a follow-up that first needs an app-level queue-depth metric.

The whole change lands in **one production code file** — the shared `OpenTelemetryStartupExtensions` composition — plus the package swap, new Bicep alert modules, and manifest cleanup. No `Program.cs` is edited.

## User Stories

### Operator perspective

1. As an operator, I want a Live Metrics stream in App Insights while I watch a production rollout, so that I can spot a spike in failures or latency within seconds and halt the deploy.
2. As an operator, I want an automatic Application Map of the services and their dependencies, so that I can understand the topology and locate the failing hop without reading code.
3. As an operator, I want outbound HTTP calls (Order→Product, gateway→services) shown as dependencies, so that I can tell whether a slow request is the service or a downstream call.
4. As an operator, I want SQL calls shown as dependencies with their durations, so that I can attribute latency to the database.
5. As an operator, I want request and dependency telemetry collected automatically for every service, so that a new service is observable in App Insights without per-service instrumentation work.
6. As an operator, I want production traces sampled at the configured ratio while Live Metrics stays unsampled, so that ingestion cost stays bounded but I never miss the live picture.
7. As an operator, I want all services reporting into one workspace-based App Insights resource per environment, distinguished by role name, so that I have a single pane per environment.
8. As an operator, I want a high-HTTP-error-rate alert (>5% 5xx over 5 minutes, per service) in Azure Monitor, so that I'm paged when a service starts failing.
9. As an operator, I want a high-latency alert (p95 > 1s over 5 minutes, per service) in Azure Monitor, so that I'm paged on latency regressions.
10. As an operator, I want a low-stock / reservation-failure alert sourced from Inventory's custom metric, so that I learn about stock problems from telemetry rather than from customers.
11. As an operator, I want a service-down alert backed by Container Insights pod restart / not-ready signals, so that a crashed or stuck pod pages me even for event-driven services that serve no HTTP.
12. As an operator, I want alerts wired to an action group with an email distribution, so that notifications reach the on-call contacts.
13. As an operator, I want alerts provisioned only for staging and production, so that dev noise doesn't drown real signals.
14. As an operator, I want the dead `prometheus.io/scrape` annotations removed from the cloud manifests, so that the deployment descriptors reflect what actually scrapes (nothing, in the cloud).

### Developer perspective

15. As a developer, I want the cloud-vs-local exporter choice to stay a single `OpenTelemetry:Exporter` config flag, so that I flip behavior by configuration, not by code branch or environment name.
16. As a developer, I want the local OTLP → Jaeger/Prometheus/Grafana experience completely unchanged, so that my day-to-day debugging and the existing dashboards keep working.
17. As a developer, I want the platform's custom Activity sources (RabbitMQ, DeadLetter, Outbox) and Meters to still flow to App Insights on the distro branch, so that broker, outbox, and DLQ spans/metrics aren't lost when we switch exporters.
18. As a developer, I want my per-service tracing/metrics customizations (SQL instrumentation, the Saga source/meter, YARP source, histogram views) to keep working on the distro branch, so that adopting the distro doesn't regress any service's signal.
19. As a developer, I want SQL spans to appear exactly once on the distro branch, so that the Application Map and dependency durations aren't doubled by the distro and the per-service `WithSqlInstrumentation()` both registering SQL.
20. As a developer, I want `ILogger` output captured and shipped to App Insights without a duplicate manual logging pipeline, so that logs aren't double-exported on the distro branch.
21. As a developer, I want the `/metrics` Prometheus endpoint to disappear on the distro branch, so that there's no dead endpoint advertising a pipeline that isn't registered.
22. As a developer, I want zero `Program.cs` edits across the nine services, so that the change is contained to the shared composition and the blast radius is small.
23. As a developer, I want the distro adoption to ride the normal shared-libs lockstep version bump and consumer sweep, so that the rollout follows the established versioning runbook.
24. As a developer, I want new tests that assert the distro branch's behavioral contracts (single SQL registration, no `/metrics`, no duplicate log pipeline), so that a future refactor can't silently reintroduce double telemetry.
25. As a developer, I want the Azure Monitor alert rules expressed in Bicep alongside the rest of the infrastructure, so that alerting is version-controlled and deployed with the environment.
26. As a developer, I want the deferred items (Redis dependency instrumentation, RabbitMQ queue-depth metric + its alert) explicitly recorded, so that the production monitoring gaps are tracked rather than forgotten.

## Implementation Decisions

### Modules

- **`AddPlatformObservability` composition seam** (the deep module). A single, stable interface — `AddPlatformObservability(serviceName, customTracing?, customMetrics?)` — that hides all exporter/instrumentation wiring and now branches internally on `OpenTelemetryOptions.UseAzureMonitor`. Callers (nine `Program.cs` files) are unchanged; the entire feature lives behind this interface in `OpenTelemetryStartupExtensions`.
- **`OpenTelemetryOptions`** (config contract, unchanged). `Exporter` (`Otlp` default vs `AzureMonitor`), `SamplingRatio`, `Environment`, `ServiceVersion`, and connection-string resolution (`AzureMonitorConnectionString` → `APPLICATIONINSIGHTS_CONNECTION_STRING`). No surface change.
- **Azure Monitor alerts module** (new Bicep deep module). Takes the App Insights resource id and an action-group id; emits the alert rules. Paired with an **action-group module** for email notifications. Wired into `main.bicep`, gated to staging/prod.

### Composition branch (distro vs OTLP)

- On the `AzureMonitor` branch, call `UseAzureMonitor()` once (connection string from `ResolveAzureMonitorConnectionString()`, `SamplingRatio` mapped to the distro's float). The distro owns the trace/metric/log providers, the Azure Monitor exporters, Live Metrics, and auto-instrumentation for AspNetCore + HttpClient + SqlClient.
- Layer **only** the platform's custom Activity sources (RabbitMQ / DeadLetter / Outbox), custom Meters (service name / DeadLetter / Outbox), and the per-service `customTracing`/`customMetrics` lambdas on top. Do **not** add AspNetCore instrumentation (distro owns it), the OTLP exporter, the Prometheus exporter, or a sampler (the distro owns sampling).
- The `Otlp` branch is preserved exactly as today (sampler, AspNetCore, custom sources, OTLP exporter, Prometheus exporter, console exporters).
- Delete the three conditional `AddAzureMonitor{Trace,Metric,Log}Exporter` calls — the distro replaces them.

### SQL double-instrumentation dedupe

- The distro auto-registers SqlClient instrumentation; per-service lambdas also call `WithSqlInstrumentation()`. To avoid duplicate SQL spans without editing six `Program.cs` files, set a thread-scoped ambient flag during the distro branch's synchronous tracing-lambda invocation. `WithSqlInstrumentation()` becomes a no-op when the flag is set; on the OTLP branch the flag is false, so behavior is unchanged.

### Logging and Prometheus endpoint

- On the distro branch, skip the manual OpenTelemetry logging pipeline entirely — the distro captures `ILogger` and exports to App Insights (avoids double log export). The OTLP branch keeps its logging block.
- `UsePrometheusExporter()` reads config and returns early when `UseAzureMonitor` is set, so `/metrics` is absent on the distro branch. No call-site edits.

### Sampling

- `OpenTelemetry:SamplingRatio` (0.1 in cloud) maps to the distro's fixed-rate sampler. Live Metrics is unsampled by design.

### Packages

- Add `Azure.Monitor.OpenTelemetry.AspNetCore`; remove `Azure.Monitor.OpenTelemetry.Exporter` (its only three callers are deleted; the distro carries the exporter transitively). Verify the distro version resolves against the pinned OpenTelemetry 1.9.0 family; if it forces a core bump, bump the OpenTelemetry packages together.

### Alerts (Bicep, gated to staging/prod)

- **HighHttpErrorRate** and **HighHttpLatencyP95** — scheduled-query (log) alerts over the App Insights `requests` table, grouped by `cloud_RoleName`.
- **LowStockAlert** — scheduled-query alert over `customMetrics` for Inventory's reservation-failure counter. The App Insights metric name is dash-cased (e.g. `stock-reservations-failed`), not the Prometheus `_total` form.
- **ServiceDown** — Container Insights pod restart / not-ready metric alert (uniform across all nine services, including event-driven ones). Requires the AKS Container Insights monitoring addon to be enabled.
- **RabbitMqQueueBacklog** — deferred; broker queue depth came from the RabbitMQ Prometheus exporter, which is dropped in the cloud. The durable fix is to emit queue depth as an app custom metric and alert on it.

### Rollout

- Bump the shared-libs lockstep `<Version>` (minor; no public-surface change), pack all ten nupkgs to the local feed, then sweep the nine consumers to the new version per the versioning runbook.
- Strip the dead `prometheus.io/{scrape,path,port}` annotations from the AKS manifests; keep the local OTLP/Prometheus stack and annotate `observability/alerts.yaml` as local-only (superseded in the cloud by the Bicep alerts).

## Testing Decisions

A good test here asserts **external behavior of the composition seam**, not the distro's internals: given a configuration, what does the pipeline register? Tests must not require a network or a real App Insights resource — use a syntactically valid fake connection string.

- **Preserved (must stay green):** the OTLP branch is unchanged, so `OpenTelemetryOptionsTests` (options binding and connection-string resolution) and the `Outbox` / `DeadLetter` platform observability tests (compose `AddPlatformObservability` on the OTLP path and drive a custom Activity through a recording processor) continue to pass untouched.
- **New (distro-branch contracts):**
  - SQL instrumentation is registered exactly once on the `AzureMonitor` branch — i.e. `WithSqlInstrumentation()` is a no-op when the ambient flag is set.
  - `UsePrometheusExporter()` does not map `/metrics` when `Exporter=AzureMonitor`.
  - The logging overload does not add the manual OpenTelemetry logging provider on the `AzureMonitor` branch.
- **Module under test:** the `AddPlatformObservability` composition seam (both branches). Bicep alert modules are validated by `bicep build` / deployment `what-if`, not unit tests.
- **Prior art:** `OutboxPlatformObservabilityTests` and `DeadLetterPlatformObservabilityTests` are the model for composing the seam and asserting what flows through it; `OpenTelemetryOptionsTests` is the model for options-binding assertions.

## Out of Scope

- **Redis dependency instrumentation** for Basket's app-map edge (the distro does not auto-instrument StackExchange.Redis) — deferred to a small isolated follow-up.
- **RabbitMqQueueBacklog alert** and the **app-level queue-depth metric** it depends on — deferred follow-up; tracked monitoring gap in the interim.
- Switching the broker to Azure Service Bus.
- Rebuilding the Grafana dashboards as Azure Workbooks/dashboards.
- Changing the local-dev OTLP stack or its docker-compose assets.
- Moving to per-service App Insights resources (stays one workspace-based resource per environment).

## Further Notes

- The `AzureMonitor` branch is **already live** in the dev/staging/prod manifests; this PRD upgrades what that branch does (raw exporter → distro), it does not introduce the cloud export.
- The connection string already flows Bicep output → Azure DevOps pipeline variable → `appinsights-secret` Kubernetes secret → `APPLICATIONINSIGHTS_CONNECTION_STRING`; no new secret plumbing is required.
- ADR-0009 explicitly named managed APM "out of scope — kept as a swap option since the OTEL Collector makes it a config change." This work realizes that swap for the cloud; a short ADR addendum recording the distro adoption and the cloud metrics/alerting move is warranted.
- Primary risk: distro vs pinned OpenTelemetry 1.9.0 compatibility at restore — resolve by bumping the OpenTelemetry family together if a conflict surfaces.
- The full implementation plan for this PRD is captured at `~/.claude/plans/sequential-sauteeing-kite.md` (Parts A–C: composition refactor, Bicep alerts, rollout).
