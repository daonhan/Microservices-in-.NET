# PRD: Local-dev workflow guide and Messaging:Provider config knob across services

> GitHub issue: [#82](https://github.com/daonhan/Microservices-in-.NET/issues/82)
> Part of the RabbitMQ → Azure Service Bus local-dev migration. **Depends on PRDs A, B, C** (`PRD-Messaging-Adopt-AddPlatform.md`, `PRD-Messaging-AsbEmulator-Local.md`, `PRD-Messaging-DLQ-Provider-Abstraction.md`).

## Implementation Issue Index

| Slice | Issue |
|-------|-------|
| Provider defaults | [#108](https://github.com/daonhan/Microservices-in-.NET/issues/108) |
| Four-scenario guide | [#109](https://github.com/daonhan/Microservices-in-.NET/issues/109) |
| Saga verification and troubleshooting | [#110](https://github.com/daonhan/Microservices-in-.NET/issues/110) |
| Cross-docs and CI contract | [#111](https://github.com/daonhan/Microservices-in-.NET/issues/111) |

## Problem Statement

Even after PRDs A–C land, a new contributor has no clear instructions on how to choose between Rabbit and ASB locally, what env vars to set, what the emulator profile is for, or how to verify the saga end-to-end on each broker. `Messaging:Provider` lives only in the shared library — services do not surface it in their `appsettings*.json` or in the Compose env. This is a docs and config-defaults gap that turns into surprise the first time someone tries to "use ASB locally" without context.

## Solution

Add explicit `Messaging:Provider` config to each service's `appsettings.json` (default `RabbitMq`) so the knob is discoverable through code search. Add a developer-facing guide under `docs/` covering: F5 against ASB Emulator, F5 against a real namespace, opt-in Compose ASB profile from PRD B, expected env vars per option, and how to verify the saga (Order → Inventory → Payment → Shipping) end-to-end on each provider. Confirm that Phase-4 smoke tests stay Rabbit-only.

## User Stories

1. As a developer, I want a single doc under `docs/` that walks me through Rabbit and ASB local setups, so that I can pick a path and run.
2. As a developer, I want each service's `appsettings.json` to show `Messaging:Provider`, so that I can grep for it and see the default.
3. As a developer, I want example `appsettings.Development.json` snippets for the ASB Emulator, so that I can copy-paste and run F5.
4. As a developer, I want explicit env-var examples for the Compose ASB profile, so that I do not guess at variable names.
5. As an operator, I want Azure deployment docs to state which provider is expected per environment, so that I do not deploy a Rabbit-only service into an ASB namespace.
6. As a developer, I want a checklist for verifying the saga on each provider (order placed → stock reserved → payment authorized → shipment created), so that I have a quick smoke procedure.
7. As a maintainer, I want the README to point at the new local-dev guide, so that newcomers find it without hunting.
8. As a CI gatekeeper, I want documentation to state explicitly that the Phase-4 smoke job stays on Rabbit, so that future contributors do not break the contract by trying to add ASB to that gate.
9. As a developer, I want the doc to explain the trade-offs between emulator and real-namespace local dev, so that I pick the right option for what I am doing.
10. As a developer onboarding, I want a "Troubleshooting" section covering common failures (missing emulator EULA env, missing topology, wrong connection string), so that I can unblock myself.

## Implementation Decisions

- Add `"Messaging": { "Provider": "RabbitMq" }` to every service's `appsettings.json` (auth, basket, product, order, inventory, shipping, payment, gateway).
- Update `docker-compose.yaml` with documentation comments referencing the ASB profile and the `Messaging__Provider` env var. Default Rabbit env stays unchanged.
- New file `docs/local-dev/messaging.md` (or equivalent path) covering four scenarios: (1) default Compose Rabbit, (2) F5 + ASB Emulator, (3) F5 + shared dev namespace, (4) Compose `--profile asb`.
- Update `README.md` and `CONTEXT.md` to link the new guide.
- Update `docs/wiki/Architecture.md` and `Infrastructure - Deployment/docs/TECH_STACK.md` to reflect dual-provider local-dev support.
- Confirm in docs that `dotnet test` and the Phase-4 smoke workflow stay Rabbit-only.

## Testing Decisions

- Doc-only PRD; no automated tests added.
- A smoke verification that each service still boots with the new explicit `Messaging:Provider` default — covered by existing `WebApplicationFactory<Program>` integration tests in each `*.Tests` project.
- Manual verification: run each scenario against a clean clone and confirm outcomes match the doc.

## Out of Scope

- Code changes to `MessagingStartupExtensions`, the adapters, or DLQ capture (covered by PRDs A, B, C).
- Bicep / production deployment updates.
- Translations or non-English docs.

## Further Notes

- Depends on PRDs A, B, C landing.
- Keep the doc terse — link out to ADRs and existing PRDs rather than restating.
