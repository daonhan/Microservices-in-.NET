# PRD: QA Scenario — Dead-Letter Queue (DLQ) Operator Flow

> Status: draft (plan mode).

## Context
Following the completion of the baseline QA dataset (Issue #72), the platform requires a deterministic scenario to verify the Dead-Letter Queue (DLQ) and Operator Replay flow defined in ADR-0004. This ensures that transient infrastructure failures are recoverable without causing duplicate side-effects across the choreographed saga.

Per project conventions, this scenario prioritizes an automated "AFK" (Away From Keyboard) implementation via `scripts/local-smoke-test.ps1` first, followed by manual Human-in-the-Loop (HITL) verification using Bruno and Markdown runbooks.

## Problem Statement
The current QA dataset covers business-level scenarios (happy path, stock shortage, payment decline), but fails to exercise the platform's resilience mechanisms. QA engineers and CI pipelines have no deterministic way to verify that a failed integration event lands in the DLQ, and that an operator can successfully replay it via the gateway's Operator API once the underlying outage is resolved.

## Solution
Introduce a new QA scenario specifically targeting the DLQ and Operator API:
1. **Persona:** Seed a new `operator@qa.test` user with the `operator` role claim so the `/operator/api` endpoints can be accessed.
2. **AFK Automation:** Extend `scripts/local-smoke-test.ps1` with a `-Scenario dlq` flag. This script will simulate a transient outage by stopping a database container, triggering the DLQ, starting the database, and calling the Replay API.
3. **HITL Tools:** Provide a Bruno collection folder (`05-dlq-operator`) containing the Operator API requests, and a detailed Markdown runbook (`docs/qa/scenarios/05-dlq-operator.md`) for manual verification.

*Note: The Blazor UI is out of scope for this scenario. All operator actions will be driven via the REST API.*

## User Stories

1. As a QA engineer, I want a pre-seeded `operator@qa.test` persona so I can authenticate against the protected `/operator/api` endpoints without manually tweaking the database.
2. As a release manager, I want the `scripts/local-smoke-test.ps1` to support a `-Scenario dlq` flag so the CI and AFK agents can automatically assert the DLQ logic is sound.
3. As a QA engineer, I want a Bruno collection folder with pre-wired requests for listing failures and triggering replays.
4. As a QA engineer, I want a step-by-step runbook for the DLQ scenario so I can deterministically simulate a transient outage (e.g., stopping a container) and recover from it.
5. As an operator, I want to verify that replaying the DLQ message only targets the initially failing service, ensuring no duplicate events are processed by other services (targeted replay).

## Implementation Decisions

### Triggering the Failure
To simulate a transient failure without requiring "poison pill" code changes, the scenario will utilize container orchestration:
1. Submit an order.
2. Stop the shipping database (`docker compose stop sql-shipping`).
3. The Shipping consumer will fail to process the `OrderConfirmedEvent`, exhaust its Polly retries, and route the message to `ecommerce-dlq`.
4. Start the database (`docker compose start sql-shipping`).
5. Replay the message via the API.

### Modules Modified
- `ECommerce.Shared/Qa/QaPersonas.cs`: Add `Operator` static GUID.
- `Auth.Service/Infrastructure/Data/EntityFramework/Configurations/UserConfiguration.cs`: Seed `operator@qa.test` with the `operator` role.
- `scripts/local-smoke-test.ps1`: Add `-Scenario dlq` logic including `docker compose stop/start` orchestration.
- `qa/bruno/05-dlq-operator`: New folder with `GET /operator/api/failures` and `POST /operator/api/failures/{id}/replay`.
- `docs/qa/scenarios/05-dlq-operator.md`: The HITL runbook.

### Tooling
- The Blazor UI is explicitly excluded. The flow relies entirely on the API Gateway's minimal API endpoints (`/operator/api/failures`).

## Testing Decisions

A good test here verifies **observable resilience and targeted replay**:
- Verify that before stopping the database, the DLQ is empty.
- Verify that after the retry budget is exhausted, exactly one message appears in the DLQ with `OriginalQueue` pointing to the Shipping service.
- Verify that calling the Replay endpoint returns a `202 Accepted`.
- Verify that after replay, the Shipping service successfully creates the Shipment.
- Verify that other saga participants (like Inventory and Basket) do not process the event a second time.

## Out of Scope
- Blazor Operator UI.
- Poison pill payload injection (replay is byte-for-byte, so a payload bug cannot be fixed by replay anyway).
- Automatic replay schedulers (replay remains a manual/explicit operator action).

## Further Notes
- The AFK agent should be instructed to implement the `local-smoke-test.ps1` updates first, ensuring the infrastructure primitives work before the documentation and Bruno collections are finalized for HITL.
