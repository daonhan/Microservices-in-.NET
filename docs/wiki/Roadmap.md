# Roadmap

A snapshot of where the platform is heading. Intent here is aspirational; authoritative status lives in [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans) and in open issues/PRs.

## Delivered

- ✅ Seven-service decomposition with per-service datastores
- ✅ RabbitMQ fanout + Transactional Outbox
- ✅ YARP API Gateway (with Ocelot retained as runtime-switchable fallback) — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-Yarp.md)
- ✅ Inventory service with reservations, movements, backorders — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Inventory.md)
- ✅ Order ↔ Inventory ↔ Payment ↔ Shipping saga (authorize on stock-reserved; capture on shipment-dispatch; payment-driven `OrderConfirmedEvent`)
- ✅ Shipping service: post-confirmation fulfillment, carrier integration, event-driven state machine — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Shipping.md)
- ✅ Payment service: authorize/capture/refund with pluggable `IPaymentGateway` (in-memory default), admin refund + manual capture, ownership-checked reads — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Payment.md)
- ✅ Combined Swagger UI at the gateway (OpenAPI aggregation, dev/staging only) — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-OpenApi-Aggregation.md)
- ✅ Full observability stack (Jaeger, Prometheus, Alertmanager, Grafana, Loki) with alerts — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Observability.md)
- ✅ Kubernetes manifests for services + infra + observability
- ✅ Azure cloud deployment — Bicep IaC (VNet, AKS, ACR, SQL, Redis, Key Vault, Monitor, Service Bus), per-service Azure Pipelines (build + multi-env deploy), Dev/Staging/Prod AKS manifests with Ingress, Azure Service Bus adapter for `IEventBus`, Application Insights via Azure Monitor OTel exporter — [Epic #33](https://github.com/daonhan/Microservices-in-.NET/issues/33) · [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/azure-infrastructure-deployment.md) · [Plan](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/plans/azure-infrastructure-deployment-plan.md) · wiki: [Azure-Deployment](Azure-Deployment)
- ✅ Public Wiki (this wiki) — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Wiki.md)

### Delivered 2026-05

- ✅ **Clean Architecture + Vertical Slices default** — [ADR-0012](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0012-clean-arch-vsa-default-service-shape.md) promoted the Order pilot to the repo-wide service shape, with [docs/PATTERNS.md](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/PATTERNS.md) as the implementation guide.
- ✅ **Shared-libs narrow pinning** — [ADR-0013](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0013-shared-libs-multi-package-split.md) split shared-libs into nine direct capability packages plus the umbrella compatibility package; PR #294 completed the messaging extraction and production narrow-package convergence.
- ✅ **Spec-pipeline / AFK / custom agents** — PR #295 made PRD-to-plan-to-issue authoring and AFK execution explicit repo workflows.
- ✅ **QA smoke gap closure** — #292 wired saga-operator and shipping terminal coverage into the Bruno smoke suite.
- ✅ **Saga orchestrator cutover** — Saga service owns end-to-end order saga state; participants driven by commands (Reserve/Commit/Release stock, Authorize/Capture/Void/Refund payment, Confirm/Cancel order, Create/Cancel shipment); legacy event-driven saga handlers removed — [#132](https://github.com/daonhan/Microservices-in-.NET/issues/132) · [#136](https://github.com/daonhan/Microservices-in-.NET/issues/136) · [#137](https://github.com/daonhan/Microservices-in-.NET/issues/137) · [Strangler runbook](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/runbooks/saga-orchestrator-strangler.md)
- ✅ **Refund saga** — `RefundSaga` state machine + `RefundRequestedEvent` for post-capture refund flow — [#130](https://github.com/daonhan/Microservices-in-.NET/issues/130)
- ✅ **Saga reaper + per-step timeouts** — background reaper drives stuck saga instances to compensation when participants miss step deadlines — [#126](https://github.com/daonhan/Microservices-in-.NET/issues/126)
- ✅ **Compensation matrix** — reverse-command paths defined per saga state, exercised by the orchestrator state machine — [#125](https://github.com/daonhan/Microservices-in-.NET/issues/125)
- ✅ **Operator saga API** — list/detail/retry/abort endpoints (Bearer + `Operator` claim) — [#128](https://github.com/daonhan/Microservices-in-.NET/issues/128)
- ✅ **StockItem aggregate** — atomic reserve/commit/release via `Evaluate`/`ApplyHold`; one-shot levels lookup — [#117](https://github.com/daonhan/Microservices-in-.NET/issues/117) · [#118](https://github.com/daonhan/Microservices-in-.NET/issues/118)
- ✅ **Messaging provider abstraction** — `AddPlatform*` wiring across services, `Messaging:Provider` selects RabbitMQ (default) or Azure Service Bus — [#79](https://github.com/daonhan/Microservices-in-.NET/issues/79) · [#88](https://github.com/daonhan/Microservices-in-.NET/issues/88)
- ✅ **Provider-agnostic DLQ poller** — gateway dead-letter capture/replay works on both RabbitMQ and ASB — [#81](https://github.com/daonhan/Microservices-in-.NET/issues/81) · [#100](https://github.com/daonhan/Microservices-in-.NET/issues/100)

## In flight

Tracked under [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans):

- `observability-polish.md` — dashboard coverage, SLO burn alerts
- `inventory.md` — ongoing inventory enhancements
- **Dead-letter queue + replay UI** — operator tool for failed events. Phases 1–3 (capture, store, replay endpoint), 5a–5b (outbox failure tracking + per-service `/internal/outbox/failed`), 5c (Origin column + `?origin=` filter, gateway pull-cache aggregation [#53](https://github.com/daonhan/Microservices-in-.NET/issues/53)), and Phase 4 admin API (`/operator/api/failures/{id}` detail, `/discard`, `/replay-batch` — see [API-Reference](API-Reference#operator--dlq-admin-api-gateway-hosted)) are landed. Phase 6 observability ([#47](https://github.com/daonhan/Microservices-in-.NET/issues/47)) — `Event.CorrelationId`, header round-trip, replay span linking, shared OTEL pipeline registration, and the three Prometheus counters with consistent label sets — is landed; remaining is the Blazor operator UI ([#45](https://github.com/daonhan/Microservices-in-.NET/issues/45)) which surfaces the correlation id and trace link. Parent: [PRD #36](https://github.com/daonhan/Microservices-in-.NET/issues/36).

## Candidate future work

These are ideas, not commitments.

- **Auto-sync wiki from `main`** — GitHub Action to mirror `docs/wiki/` → wiki remote on merge, removing the manual publish step from [Contributing](Contributing#editing-the-wiki).
- **Real PSP integration** — slot a Stripe/Adyen implementation behind `IPaymentGateway` (the in-memory gateway is the v1 default; see [Service-Payment](Service-Payment)).
- **Identity Server / OIDC** instead of the hand-rolled JWT issuer.
- **Multi-tenancy** — tenant-scoped datastores and row-level auth.

## How to propose new work

Open a PRD under `docs/prd/PRD-<Feature>.md` following the pattern of existing PRDs and submit it as a GitHub issue for discussion. See [Contributing § PRD / Plan workflow](Contributing#prd--plan-workflow).
