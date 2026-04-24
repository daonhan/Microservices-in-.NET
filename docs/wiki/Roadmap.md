# Roadmap

A snapshot of where the platform is heading. Intent here is aspirational; authoritative status lives in [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans) and in open issues/PRs.

## Delivered

- ✅ Six-service decomposition with per-service datastores
- ✅ RabbitMQ fanout + Transactional Outbox
- ✅ YARP API Gateway (with Ocelot retained as runtime-switchable fallback) — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-ApiGateway-Yarp.md)
- ✅ Inventory service with reservations, movements, backorders — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Inventory.md)
- ✅ Order ↔ Inventory saga
- ✅ Shipping service: post-confirmation fulfillment, carrier integration, event-driven state machine — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Shipping.md)
- ✅ Full observability stack (Jaeger, Prometheus, Alertmanager, Grafana, Loki) with alerts — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Observability.md)
- ✅ Kubernetes manifests for services + infra + observability
- ✅ Public Wiki (this wiki) — [PRD](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/prd/PRD-Wiki.md)

## In flight

Tracked under [`docs/plans/`](https://github.com/daonhan/Microservices-in-.NET/tree/main/docs/plans):

- `observability-polish.md` — dashboard coverage, SLO burn alerts
- `inventory.md` — ongoing inventory enhancements

## Candidate future work

These are ideas, not commitments.

- **Auto-sync wiki from `main`** — GitHub Action to mirror `docs/wiki/` → wiki remote on merge, removing the manual publish step from [Contributing](Contributing#editing-the-wiki).
- **Payment service** — close the checkout loop with a payment microservice participating in the saga.
- **OpenAPI aggregation at the Gateway** — surface a combined Swagger UI.
- **Dead-letter queue + replay UI** — operator tool for failed events.
- **Identity Server / OIDC** instead of the hand-rolled JWT issuer.
- **Multi-tenancy** — tenant-scoped datastores and row-level auth.

## How to propose new work

Open a PRD under `docs/prd/PRD-<Feature>.md` following the pattern of existing PRDs and submit it as a GitHub issue for discussion. See [Contributing § PRD / Plan workflow](Contributing#prd--plan-workflow).
