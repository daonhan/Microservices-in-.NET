# Plan: Azure Monitor Distro for Production Observability

> Source PRD: [docs/prd/PRD-AzureMonitor-Distro-Observability.md](../prd/PRD-AzureMonitor-Distro-Observability.md)

## Architectural decisions

Durable decisions that apply across all phases:

- **Single composition seam**: the whole feature lives behind `AddPlatformObservability(serviceName, customTracing?, customMetrics?)` in `ECommerce.Shared.Platform/Composition/OpenTelemetryStartupExtensions.cs` (`ECommerce.Shared.Observability` namespace). The nine `Program.cs` callers are unchanged — **zero `Program.cs` edits**.
- **Config-gated branch, not environment-named**: behavior is selected by the existing `OpenTelemetry:Exporter` flag (`Otlp` default vs `AzureMonitor`), surfaced as `OpenTelemetryOptions.UseAzureMonitor`. `OpenTelemetryOptions` keeps its current surface (no new properties).
- **OTLP branch is frozen**: the `Otlp` path (sampler, AspNetCore instrumentation, custom sources, OTLP exporter, Prometheus exporter, console exporters, manual logging pipeline) stays byte-for-byte identical so local dev and every existing test are untouched.
- **Distro owns the providers on the cloud branch**: one `UseAzureMonitor()` call (connection string from `ResolveAzureMonitorConnectionString()`, `SamplingRatio` mapped to the distro's fixed-rate sampler) owns trace/metric/log providers, the Azure Monitor exporters, Live Metrics, and auto-instrumentation for AspNetCore + HttpClient + SqlClient. The branch layers **only** the platform's custom Activity sources (RabbitMQ / DeadLetter / Outbox), custom Meters (service name / DeadLetter / Outbox), and the per-service `customTracing` / `customMetrics` lambdas on top. It does **not** add AspNetCore instrumentation, an OTLP exporter, a Prometheus exporter, or a sampler.
- **SQL appears exactly once**: the distro auto-registers SqlClient; the 7 SQL services (order, product, auth, inventory, shipping, payment, saga) also call `WithSqlInstrumentation()`. A thread-scoped ambient flag set during the distro branch's synchronous tracing-lambda invocation makes `WithSqlInstrumentation()` a no-op. On the OTLP branch the flag is false → unchanged.
- **Logging + `/metrics` on the distro branch**: skip the manual OpenTelemetry logging pipeline (distro captures `ILogger`); `UsePrometheusExporter()` reads config and returns early so `/metrics` is absent. No call-site edits.
- **Packages**: add `Azure.Monitor.OpenTelemetry.AspNetCore`; remove `Azure.Monitor.OpenTelemetry.Exporter` (its only three callers are deleted; the distro carries the exporter transitively). Resolve against the pinned OpenTelemetry 1.9.0 family; if the distro forces a core bump, bump the OpenTelemetry packages together.
- **Rollout via the versioning runbook**: ship through the shared-libs lockstep `<Version>` bump (minor; no public-surface change) → pack all ten nupkgs to the local feed → sweep the nine consumers. Per [docs/runbooks/shared-libs-versioning.md](../runbooks/shared-libs-versioning.md).
- **Alerts live in Bicep**, gated to staging/prod, wired into `infrastructure-deployment/bicep/main.bicep`. New deep module(s) take the App Insights resource id (`appinsights.bicep` already outputs `appInsightsId`) and an action-group id. Validated by `bicep build` / deployment `what-if`, not unit tests.
- **App Insights queries / metric names**: scheduled-query (log) alerts group by `cloud_RoleName`. Inventory's reservation-failure metric is dash-cased in App Insights (`stock-reservations-failed`), not the Prometheus `_total` form.
- **One workspace-based App Insights resource per environment** (unchanged); services distinguished by role name. The `AzureMonitor` branch is already live in dev/staging/prod manifests — this work upgrades what it does (raw exporter → distro). Connection string plumbing (Bicep output → pipeline var → `appinsights-secret` → `APPLICATIONINSIGHTS_CONNECTION_STRING`) already exists; no new secret.
- **Test contract**: assert external behavior of the composition seam (given config, what does the pipeline register?) using a syntactically valid fake connection string — never a network or a real App Insights resource. Models: `OutboxPlatformObservabilityTests` / `DeadLetterPlatformObservabilityTests` (compose the seam, drive a custom Activity through a recording processor) and `OpenTelemetryOptionsTests` (options binding).

---

## Phase 1: Distro composition refactor + rollout

**User stories**: 1, 2, 3, 4, 5, 6, 7, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24

### What to build

The entire production-code change plus the rollout that makes it live. On the `AzureMonitor` branch of `AddPlatformObservability`, replace the three conditional `AddAzureMonitor{Trace,Metric,Log}Exporter` calls with one `UseAzureMonitor()` call, then layer the platform's custom Activity sources, custom Meters, and the per-service tracing/metrics lambdas on top. Add the thread-scoped ambient flag so `WithSqlInstrumentation()` no-ops on the distro branch (SQL once). Skip the manual logging pipeline on the distro branch; make `UsePrometheusExporter()` return early when `UseAzureMonitor` is set. Leave the `Otlp` branch byte-for-byte identical. Swap the package (`Azure.Monitor.OpenTelemetry.AspNetCore` in, `Azure.Monitor.OpenTelemetry.Exporter` out), confirming restore against the pinned OpenTelemetry 1.9.0 family. Add the three new distro-branch contract tests and keep the preserved tests green. Then bump the shared-libs lockstep `<Version>`, pack all ten nupkgs to the local feed, and sweep the nine consumers per the versioning runbook.

### Acceptance criteria

- [ ] On `Exporter=AzureMonitor`, the pipeline is composed via `UseAzureMonitor()`; the three `AddAzureMonitor{Trace,Metric,Log}Exporter` calls are deleted.
- [ ] Custom Activity sources (RabbitMQ / DeadLetter / Outbox), custom Meters, and per-service `customTracing` / `customMetrics` lambdas still flow on the distro branch.
- [ ] SQL instrumentation registers exactly once on the `AzureMonitor` branch — `WithSqlInstrumentation()` is a no-op when the ambient flag is set; on the `Otlp` branch it still registers (flag false).
- [ ] `UsePrometheusExporter()` does not map `/metrics` when `Exporter=AzureMonitor`; it still maps on the `Otlp` branch.
- [ ] The logging overload does not add the manual OpenTelemetry logging provider on the `AzureMonitor` branch (no double log export).
- [ ] The `Otlp` branch is unchanged: `OpenTelemetryOptionsTests`, `OutboxPlatformObservabilityTests`, and `DeadLetterPlatformObservabilityTests` pass untouched.
- [ ] `OpenTelemetryOptions` has no new public properties; no `Program.cs` file is edited across the nine services.
- [ ] `Azure.Monitor.OpenTelemetry.AspNetCore` is added and `Azure.Monitor.OpenTelemetry.Exporter` removed; restore resolves against OpenTelemetry 1.9.0 (or the OpenTelemetry family is bumped together and documented).
- [ ] Shared-libs `<Version>` bumped (minor), ten nupkgs packed to the local feed, all nine consumers swept to the new version; every service still builds.
- [ ] In a staging deploy on `Exporter=AzureMonitor`: Live Metrics streams, the Application Map shows Order → Product → Inventory, outbound HTTP and SQL appear as dependencies, and SQL spans are not doubled.

---

## Phase 2: Cloud manifest cleanup

**User stories**: 14

### What to build

Remove the dead `prometheus.io/{scrape,path,port}` pod annotations from the cloud AKS manifests (`kubernetes/aks-{dev,staging,prod}-*.yml`, nine services × three environments) since nothing scrapes them in the cloud. Leave the local OTLP/Prometheus stack and its manifests intact. Annotate `observability/alerts.yaml` as local-only (superseded in the cloud by the Bicep alerts in later phases).

### Acceptance criteria

- [ ] No `prometheus.io/scrape`, `prometheus.io/path`, or `prometheus.io/port` annotation remains in any `kubernetes/aks-{dev,staging,prod}-*.yml`.
- [ ] Local/sandbox manifests and the `observability/` Compose stack are untouched and still scrape locally.
- [ ] `observability/alerts.yaml` carries a header comment marking it local-only and superseded by the Bicep alerts in the cloud.

---

## Phase 3: Action group + email notifications

**User stories**: 12, 13

### What to build

A new Bicep action-group module with an email distribution to the on-call contacts, wired into `main.bicep` and gated to staging/prod only (no dev provisioning). Email recipients are parameterized per environment, mirroring the existing budget-contact parameter pattern. This is the shared notification target consumed by Phases 4–6; its output `actionGroupId` feeds every alert module.

### Acceptance criteria

- [ ] An action-group module exists and emits an `actionGroupId` output.
- [ ] It is wired into `main.bicep` behind a staging/prod gate; a dev deployment provisions no action group.
- [ ] Email recipients are environment-parameterized (not hard-coded).
- [ ] `bicep build` succeeds and a staging `what-if` shows the action group being created.
- [ ] A test notification to the action group reaches the configured email distribution.

---

## Phase 4: HTTP error-rate + latency alerts

**User stories**: 8, 9

### What to build

A Bicep alert module emitting two scheduled-query (log) alerts over the App Insights `requests` table, grouped by `cloud_RoleName` (per service), wired to the Phase 3 action group and gated to staging/prod: `HighHttpErrorRate` (>5% 5xx over 5 minutes) and `HighHttpLatencyP95` (p95 > 1s over 5 minutes). Takes the App Insights resource id and the action-group id as inputs.

### Acceptance criteria

- [ ] `HighHttpErrorRate` and `HighHttpLatencyP95` scheduled-query alerts exist in Bicep, scoped to the App Insights resource, grouped by `cloud_RoleName`.
- [ ] Both are gated to staging/prod and wired to the action group.
- [ ] `bicep build` succeeds; a staging `what-if` shows both alert rules.
- [ ] Inducing >5% 5xx on one service fires `HighHttpErrorRate` for that `cloud_RoleName` and pages the email distribution.
- [ ] Inducing p95 > 1s on one service fires `HighHttpLatencyP95` for that service.

---

## Phase 5: Low-stock / reservation-failure alert

**User stories**: 10

### What to build

A scheduled-query alert over the App Insights `customMetrics` table for Inventory's reservation-failure counter, using the dash-cased metric name `stock-reservations-failed` (not the Prometheus `_total` form), wired to the action group and gated to staging/prod.

### Acceptance criteria

- [ ] A `LowStockAlert` scheduled-query alert exists in Bicep, querying `customMetrics` for `stock-reservations-failed`.
- [ ] It is gated to staging/prod and wired to the action group.
- [ ] `bicep build` succeeds; a staging `what-if` shows the rule.
- [ ] Driving Inventory reservation failures fires the alert and pages the email distribution.

---

## Phase 6: Service-down alert + Container Insights addon

**User stories**: 11

### What to build

First enable the AKS Container Insights monitoring addon (it is not currently configured on `modules/aks.bicep`) so pod restart / not-ready signals are available. Then a Container Insights metric alert — `ServiceDown` — uniform across all nine services including the event-driven ones that serve no HTTP, wired to the action group and gated to staging/prod.

### Acceptance criteria

- [ ] The AKS module enables the Container Insights monitoring addon, linked to the Log Analytics workspace.
- [ ] A `ServiceDown` metric alert backed by pod restart / not-ready signals exists in Bicep, covering all nine services.
- [ ] It is gated to staging/prod and wired to the action group.
- [ ] `bicep build` succeeds; a staging `what-if` shows the addon change and the alert rule.
- [ ] Crashing or wedging a pod (including an event-driven service) fires `ServiceDown` and pages the email distribution.

---

## Phase 7: ADR addendum + deferred-gap tracking

**User stories**: 26

### What to build

A short ADR addendum to [docs/adr/0009-otel-jaeger-prometheus-loki-grafana.md](../adr/0009-otel-jaeger-prometheus-loki-grafana.md) recording the distro adoption and the cloud metrics/alerting move (ADR-0009 named managed APM out of scope but kept as a swap option; this realizes that swap). Explicitly record the two deferred production-monitoring gaps so they are tracked rather than forgotten: Redis dependency instrumentation for Basket's app-map edge (the distro does not auto-instrument StackExchange.Redis), and the `RabbitMqQueueBacklog` alert plus the app-level queue-depth custom metric it depends on (broker queue depth came from the dropped RabbitMQ Prometheus exporter).

### Acceptance criteria

- [ ] ADR-0009 has an addendum recording the distro adoption and the cloud metrics/alerting move.
- [ ] The deferred Redis dependency instrumentation is recorded as a tracked gap.
- [ ] The deferred `RabbitMqQueueBacklog` alert and its prerequisite app-level queue-depth metric are recorded as a tracked gap.
