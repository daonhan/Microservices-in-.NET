# Plan: Local-dev workflow guide and Messaging:Provider config knob across services

> Source PRD: `docs/prd/PRD-Messaging-LocalDev-Docs.md`
> GitHub issue: https://github.com/daonhan/Microservices-in-.NET/issues/82
> Depends on: `docs/plans/messaging-adopt-addplatform.md`, `docs/plans/messaging-asb-emulator-local.md`, `docs/plans/messaging-dlq-provider-abstraction.md`

## Implementation Issue Index

| Phase | Issue | Blocked by |
|-------|-------|------------|
| Phase 1: Explicit `Messaging:Provider` config knob across services | [#108](https://github.com/daonhan/Microservices-in-.NET/issues/108) | None |
| Phase 2: Local-dev guide skeleton with four scenarios | [#109](https://github.com/daonhan/Microservices-in-.NET/issues/109) | None |
| Phase 3: Saga verification checklist + troubleshooting | [#110](https://github.com/daonhan/Microservices-in-.NET/issues/110) | [#109](https://github.com/daonhan/Microservices-in-.NET/issues/109) |
| Phase 4: Cross-doc references and CI gate statement | [#111](https://github.com/daonhan/Microservices-in-.NET/issues/111) | [#109](https://github.com/daonhan/Microservices-in-.NET/issues/109) |

## Architectural decisions

Durable decisions that apply across all phases:

- **Doc-only PRD**: no code changes beyond explicit `Messaging:Provider` config defaults. No new tests, no changes to `MessagingStartupExtensions`, adapters, or DLQ capture.
- **Default provider stays RabbitMQ**: `docker compose up`, `dotnet test`, and the Phase-4 smoke job are unchanged. ASB paths are opt-in.
- **Single canonical guide path**: `docs/local-dev/messaging.md`. README and `CONTEXT.md` link to it; existing `docs/qa/asb-emulator-local.md` stays focused on the emulator-only procedure and is cross-linked from the guide rather than rewritten.
- **Scenario taxonomy**: the guide covers exactly four scenarios — (1) default Compose Rabbit, (2) F5 + ASB emulator, (3) F5 + shared dev namespace, (4) Compose `--profile asb`. Each scenario has its own subsection with env-var examples and a verification checklist.
- **Config surface**: every service's `appsettings.json` carries `"Messaging": { "Provider": "RabbitMq" }`. The gateway already has it and is the reference shape. `appsettings.Development.json` overrides are documented in the guide but not committed by default.
- **Env-var convention**: ASCII double-underscore form (e.g. `Messaging__Provider`, `AzureServiceBus__ConnectionString`) is the canonical wire-up shown in the guide and Compose comments, matching what `Microsoft.Extensions.Configuration.EnvironmentVariables` consumes.
- **Saga verification scope**: the doc checklist covers Order → Inventory → Payment → Shipping success and at least one compensation path (insufficient stock or failed payment). It is a manual procedure, not an automated test.
- **Phase-4 contract**: docs state explicitly that the Phase-4 smoke workflow remains RabbitMQ-only. No ASB profile is added to that gate.
- **Real-environment guidance**: deployment docs state which provider each environment expects (dev / staging / prod). ASB topology stays Bicep-owned; auto-provision `Auto` is documented as emulator-only behavior.
- **No translations, no Bicep changes, no automation**: out of scope per PRD.

---

## Phase 1: Explicit `Messaging:Provider` config knob across services

**User stories**: 2

### What to build

Surface the messaging provider switch in every service's `appsettings.json` so it is greppable, discoverable at the default config layer, and obvious in code review. The gateway already carries the canonical `"Messaging": { "Provider": "RabbitMq" }` block. Add the same block (default `RabbitMq`) to each of the seven other services. No behavior change: services already default to RabbitMQ when the key is absent.

### Acceptance criteria

- [ ] `auth-microservice/Auth.Service/appsettings.json` contains `"Messaging": { "Provider": "RabbitMq" }`.
- [ ] `basket-microservice/Basket.Service/appsettings.json` contains the same block.
- [ ] `product-microservice/Product.Service/appsettings.json` contains the same block.
- [ ] `order-microservice/Order.Service/appsettings.json` contains the same block.
- [ ] `inventory-microservice/Inventory.Service/appsettings.json` contains the same block.
- [ ] `shipping-microservice/Shipping.Service/appsettings.json` contains the same block.
- [ ] `payment-microservice/Payment.Service/appsettings.json` contains the same block.
- [ ] API Gateway `appsettings.json` retains its existing `Messaging:Provider` value unchanged.
- [ ] `grep -r "Messaging" --include=appsettings.json` returns one hit per service.
- [ ] Each service's `WebApplicationFactory<Program>` boot test in `*.Tests` still passes (proves no startup regression from the explicit default).
- [ ] No service changes default broker behavior: `docker compose up` against this branch still routes events through RabbitMQ.

---

## Phase 2: Local-dev guide skeleton with four scenarios

**User stories**: 1, 3, 4, 7, 9

### What to build

New file `docs/local-dev/messaging.md` is the single discoverable entry point for choosing a local messaging path. The guide covers the four scenarios from the architectural decisions, each with copy-pasteable env-var examples and a short "when to use this" note. README and `CONTEXT.md` link to it so newcomers find it without hunting. The guide cross-links to `docs/qa/asb-emulator-local.md` rather than restating the emulator procedure.

### Acceptance criteria

- [ ] `docs/local-dev/messaging.md` exists and is the canonical local-dev messaging entry point.
- [ ] The guide has four scenario subsections: default Compose Rabbit, F5 + ASB emulator, F5 + shared dev namespace, Compose `--profile asb`.
- [ ] Each scenario lists required env vars in `Name=Value` form, including `Messaging__Provider`, `AzureServiceBus__ConnectionString` (where relevant), `AzureServiceBus__AdministrationConnectionString` (where relevant), `AzureServiceBus__TopicName`, `AzureServiceBus__AutoProvisionTopology`, and `EventBus__QueueName`.
- [ ] Each scenario includes an `appsettings.Development.json` snippet for F5-driven runs where applicable.
- [ ] The guide includes a "when to choose this" comparison covering trade-offs: emulator (offline, fast loop, limited ASB feature coverage) vs real namespace (full fidelity, costs money, requires secrets).
- [ ] `README.md` links to `docs/local-dev/messaging.md` from its messaging or local-dev section.
- [ ] `CONTEXT.md` links to `docs/local-dev/messaging.md` from a discoverable section.
- [ ] The guide cross-links to `docs/qa/asb-emulator-local.md` for the emulator-only verification and teardown procedure.
- [ ] No automated tests are added or modified.
- [ ] Manual smoke: a clean clone reader can follow scenario 1 (default Compose Rabbit) end to end using only the guide.

---

## Phase 3: Saga verification checklist + troubleshooting

**User stories**: 6, 10

### What to build

Extend the guide with a manual end-to-end saga verification checklist and a troubleshooting section. The saga checklist walks through placing an order via the gateway, confirms each downstream event lands (`StockReserved`, `OrderConfirmed`, `StockCommitted`, `PaymentAuthorized` / `PaymentCaptured`, `ShipmentCreated`), and covers at least one compensation branch (insufficient stock or failed payment) so the developer sees the saga's cancel path. The troubleshooting section enumerates the failures a developer is most likely to hit on each provider.

### Acceptance criteria

- [ ] `docs/local-dev/messaging.md` contains a "Verify the saga" section.
- [ ] The saga checklist is identical in shape for RabbitMQ and ASB and lists each saga event explicitly: `OrderCreatedEvent`, `StockReserved` / `StockReservationFailed`, `OrderConfirmed` / `OrderCancelled`, `StockCommitted`, `PaymentAuthorized`, `PaymentCaptured` (or `PaymentFailed`), `ShipmentCreated`.
- [ ] The checklist names the HTTP call(s) needed to start the saga (e.g. `POST /order`) and the inspection step(s) used to confirm each leg landed (DB row, log line, or operator endpoint).
- [ ] The checklist includes at least one compensation walkthrough: an insufficient-stock order produces `StockReservationFailed` and `OrderCancelled`.
- [ ] A "Troubleshooting" section covers: (a) missing ASB emulator EULA / SQL acceptance env vars, (b) missing or wrong topology when `AutoProvisionTopology=Never`, (c) wrong connection string (cloud vs emulator), (d) `Messaging__Provider` typo or unknown value failing fast at startup, (e) port collision on `5672` between Rabbit and the emulator.
- [ ] Each troubleshooting entry lists the symptom, likely cause, and resolution in a fixed shape (e.g. table or `**Symptom / Cause / Fix**` triplet).
- [ ] Guide tells the developer how to read the existing `dlq_messages_total` / `dlq_replays_total` counters or the operator API to confirm an end-to-end failure was captured (for compensation verification).
- [ ] No automated tests are added.

---

## Phase 4: Cross-doc references and CI gate statement

**User stories**: 5, 8

### What to build

Close the loop with the surrounding documentation so the new guide is not an island. Update `docs/wiki/Architecture.md` and `Infrastructure - Deployment/docs/TECH_STACK.md` to reflect dual-provider local-dev support and to state the production / staging / dev provider expectations. Add documentation comments in `docker-compose.yaml` referencing the `asb` profile and the `Messaging__Provider` override env var so a code-search of the Compose file surfaces the dual-broker story. Add an explicit doc statement that `dotnet test` and the Phase-4 smoke workflow stay RabbitMQ-only.

### Acceptance criteria

- [ ] `docs/wiki/Architecture.md` references dual-provider local-dev support and links to `docs/local-dev/messaging.md`.
- [ ] `Infrastructure - Deployment/docs/TECH_STACK.md` states which provider is expected per environment (dev, staging, prod) and notes that ASB topology stays Bicep-owned.
- [ ] `docker-compose.yaml` has documentation comments near the `servicebus-emulator` / `servicebus-sql` services pointing to the `asb` profile and the `Messaging__Provider=AzureServiceBus` override pattern.
- [ ] `docker-compose.yaml` default service environment blocks are unchanged: no default Compose env var flips a service from RabbitMQ to ASB.
- [ ] `docs/local-dev/messaging.md` contains an explicit "CI and smoke contract" subsection stating that `dotnet test` and the Phase-4 smoke workflow remain RabbitMQ-only and that contributors must not add ASB to the Phase-4 gate without a separate PRD.
- [ ] Final code-search confirms (a) no default Compose env var sets `Messaging__Provider=AzureServiceBus`, (b) no Phase-4 pipeline file references the `asb` profile, (c) the new guide is reachable from `README.md`, `CONTEXT.md`, and `docs/wiki/Architecture.md`.
- [ ] Manual review: a clean-clone developer can find the local-dev guide from `README.md` within two clicks.
