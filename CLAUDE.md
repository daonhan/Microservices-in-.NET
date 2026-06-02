# CLAUDE.md

Guidance for Claude Code in this repo. See [README.md](README.md), [CONTEXT.md](CONTEXT.md), [.claude/CLAUDE.md](.claude/CLAUDE.md) (behavioral rules).

If prior work may be relevant, use the `load-session-context` skill to search the local QMD index of prior sessions (and, after Phase 4, repo docs) and load only the relevant past context before continuing.

**Per-service details live next to the service.** When working in `<svc>-microservice/`, that directory's `CLAUDE.md` auto-loads with the local divergences. This root file covers only cross-cutting concerns.

## Repo shape

.NET microservices monorepo, **net10.0**, no root solution. Each `*-microservice/`, `api-gateway/`, `shared-libs/` is its own `.slnx`.

| Service     | Port | Datastore | Notes file                                                             |
|-------------|------|-----------|------------------------------------------------------------------------|
| basket      | 8000 | Redis     | [basket-microservice/CLAUDE.md](basket-microservice/CLAUDE.md)         |
| order       | 8001 | SQL+Redis | [order-microservice/CLAUDE.md](order-microservice/CLAUDE.md)           |
| product     | 8002 | SQL       | [product-microservice/CLAUDE.md](product-microservice/CLAUDE.md)       |
| auth        | 8003 | SQL       | [auth-microservice/CLAUDE.md](auth-microservice/CLAUDE.md)             |
| api-gateway | 8004 | —         | [api-gateway/CLAUDE.md](api-gateway/CLAUDE.md)                         |
| inventory   | 8005 | SQL       | [inventory-microservice/CLAUDE.md](inventory-microservice/CLAUDE.md)   |
| shipping    | 8006 | SQL       | [shipping-microservice/CLAUDE.md](shipping-microservice/CLAUDE.md)     |
| payment     | 8007 | SQL       | [payment-microservice/CLAUDE.md](payment-microservice/CLAUDE.md)       |
| saga        | 8008 | SQL       | [saga-microservice/CLAUDE.md](saga-microservice/CLAUDE.md)             |

Shared libraries (NuGet, local feed, narrow package rules, packing flow + lazy broker rule): [shared-libs/CLAUDE.md](shared-libs/CLAUDE.md). Shared-libs ships nine capability packages plus the `ECommerce.Shared` umbrella compatibility metapackage on lockstep `<Version>`; production services should reference only the direct capabilities they use. See [docs/runbooks/shared-libs-versioning.md](docs/runbooks/shared-libs-versioning.md) for package selection and bump-and-publish workflow.

## Build / test / run

Per-solution from the service directory.

```bash
cd <svc>-microservice && dotnet build
cd <svc>-microservice && dotnet test
dotnet test --filter "FullyQualifiedName~OrderEndpointTests"   # class
dotnet test --filter "DisplayName~Given_X_When_Y_Then_Z"       # single
dotnet format --verify-no-changes --verbosity minimal          # mirrors pre-commit
docker compose up --build                                       # full stack
docker compose up sql rabbitmq redis -d                         # infra only
```

`Directory.Build.props` sets `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`; its `NoWarn` list documents intentional exemptions.

## Pre-commit (Husky.Net)

Activate once: `dotnet tool restore && dotnet husky install`. Hook runs `dotnet format --verify-no-changes`, `dotnet build --no-restore`, then Basket tests only — **run other suites manually before pushing cross-service changes**.

Sandbox blockers (WSL / virtiofs / Docker, `MSB3248`, hard prohibitions): see [docs/runbooks/sandbox-precommit.md](docs/runbooks/sandbox-precommit.md).

## Service layout — default: Clean Architecture + Vertical Slices

Default shape: `Features/<Slice>/`, `Domain/`, `Contracts/Integration/`, `Infrastructure/`. Boundaries enforced per service by NetArchTest (`<Svc>.Tests/Architecture/LayoutTests.cs`) + a Roslyn `<Svc>.Service.LayoutAnalyzer`.

ADRs: [0011](docs/adr/0011-order-cleanarch-vsa-pilot.md) (original Order pilot), [0012](docs/adr/0012-clean-arch-vsa-default-service-shape.md) (promoted to default). Runbook for new slices: [adding-a-new-slice.md](docs/runbooks/adding-a-new-slice.md).

Every service in the monorepo is on this layout; api-gateway closed out the migration. Per-service file documents only its divergences:

| Service     | Outbox seam | Domain/ | Contracts/ | Key divergence                                                 |
|-------------|-------------|---------|------------|----------------------------------------------------------------|
| Order       | yes         | yes     | yes        | original pilot                                                 |
| Product     | yes         | yes     | yes        | none documented                                                |
| Basket      | no          | yes     | yes        | no integration events; no CQRS-lite read split                 |
| Auth        | no          | yes     | no         | no cross-service payloads                                      |
| Inventory   | no          | yes     | yes        | inline events per slice; saga commands from Shared             |
| Shipping    | no          | yes     | yes        | inline events; carrier adapters; per-state HTTP slices         |
| Payment     | yes         | yes     | yes        | re-adopts seam; multi-producer convention; gateway in Domain   |
| Saga        | no          | yes     | yes        | two-level `Features/<Saga>/<Trigger>/`; transition runner; reaper |
| ApiGateway  | no          | no      | no         | no aggregate; no integration events; proxy + poller as Infrastructure |

## Cross-service architecture

Read together: each service's `Program.cs` (composition root) + [shared-libs/CLAUDE.md](shared-libs/CLAUDE.md). New cross-cutting concerns belong in the matching shared-libs capability package.

**Saga (orchestrator-only):** Saga service owns the order saga end-to-end. Starts from `OrderCreatedEvent`, persists saga state, drives participants exclusively with commands: `ReserveStockCommand`/`CommitStockCommand`/`ReleaseStockCommand` (Inventory), `AuthorizePaymentCommand`/`CapturePaymentCommand`/`VoidPaymentCommand`/`RefundPaymentCommand` (Payment), `ConfirmOrderCommand`/`CancelOrderCommand` (Order), `CreateShipmentCommand`/`CancelShipmentCommand` (Shipping). Participants reply with integration events carrying `CausationId`/`SagaId`. Cutover completed 2026-05-18 (issue #132). Runbook: [saga-orchestrator-strangler.md](docs/runbooks/saga-orchestrator-strangler.md). ADR: [0010](docs/adr/0010-saga-orchestrator-supersedes-choreography.md).

## DLQ + operator API

Retry-exhausted messages dead-letter to `ecommerce-dlq` exchange. API Gateway poller persists them (plus failed outbox rows from each service's `/internal/outbox/failed`, gated by `RequireService`) into gateway-owned `dead_letter_messages`. Operator endpoints under `/operator/api/failures*` (Bearer + `Operator` claim): list/detail/single replay/batch replay/discard. Replay re-publishes to `OriginalQueue`; `dlq.replay` spans carry original `CorrelationId`. Counters: `dlq_messages_total`, `dlq_replays_total`, `dlq_discards_total`.

When changing event payloads, retry policy, or queue routing, audit both consumer side **and** the DLQ poller.

## Authentication

RS256 user JWTs from Auth (`POST /login`); `client_credentials` service tokens (`POST /token`). Resources validate via `AddJwtAuthentication()` (fetches+caches `/jwks` — no shared secret). `/internal/*` gated by `RequireService` policy (`scope=service`); user tokens cannot reach. Dev keys: `auth-microservice/Auth.Service/dev-keys/`.

## CI/CD

GitHub Actions runs build verification (`docker-build.yml`) and QA smoke checks (`smoke-test.yml`) as CI gates; Azure Pipelines (per-service `azure-pipelines.yml`) is the deployment path. Bicep provisions AKS/ACR/Azure SQL/Service Bus/App Insights. Deploys to `ecommerce-{dev,staging,prod}` namespaces.

## Conventions

- File-scoped namespaces, `var` preferred, usings outside namespace (`.editorconfig`).
- Test names `Given_When_Then` with underscores (`CA1707` suppressed).
- `**/Migrations/*.cs` marked `generated_code = true` — don't hand-edit style.
- Per-service `IDesignTimeDbContextFactory` so `dotnet ef migrations add` works without running `Program.cs`.
- Integration tests use `WebApplicationFactory<Program>`; each service has `public partial class Program { }` at end of `Program.cs`.

## Behavioral

Apply `.claude/CLAUDE.md` (think first, simplicity, surgical changes, goal-driven). Make only changes the user asked for; match existing style; prefer smallest correct change; push back on over-engineering; state a brief plan + success criteria for non-trivial work.
