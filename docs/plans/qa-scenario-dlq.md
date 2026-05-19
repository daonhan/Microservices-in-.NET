# Plan: QA Scenario — DLQ & Operator Replay Flow

> Source PRD: `docs/prd/PRD-QA-Scenario-DLQ.md`

## Architectural decisions

Durable decisions that apply across all phases:

- **Authentication / Authorization**: The scenario relies on a pre-seeded `operator@qa.test` persona that holds the `operator` role claim, granting access to the protected Gateway Operator APIs.
- **Routes**: Interactions occur exclusively via the API Gateway Minimal API endpoints (`/operator/api/failures` and `/operator/api/failures/{id}/replay`). The Blazor UI is out of scope.
- **Failure Simulation Orchestration**: Transient failures are deterministically simulated via Docker container orchestration (stopping and starting `sql-shipping` container) rather than modifying application logic to ingest poison pills.
- **Targeted Replay**: A replay from the DLQ targets only the originally failed service, ensuring that previously successful saga steps (like Basket checkout or Payment) are not inadvertently duplicated.

---

## Phase 1: Identity & Persona Seeding

**User stories**:
- 1. As a QA engineer, I want a pre-seeded `operator@qa.test` persona so I can authenticate against the protected `/operator/api` endpoints without manually tweaking the database.

### What to build

Establish the `operator` persona definition in the shared QA library and seed the identity data within the Auth Service's EF Core configuration. This ensures the environment boots up with the operator credentials immediately available for authentication.

### Acceptance criteria

- [ ] `Operator` static GUID is added to `ECommerce.Shared/Qa/QaPersonas.cs`.
- [ ] `UserConfiguration.cs` in the Auth Service seeds the `operator@qa.test` user and associates it with the `operator` role.
- [ ] A valid JWT containing the `operator` role claim can be retrieved for `operator@qa.test` when running the application.

---

## Phase 2: Automated Smoke Test Scenario (AFK)

**User stories**:
- 2. As a release manager, I want the `scripts/local-smoke-test.ps1` to support a `-Scenario dlq` flag so the CI and AFK agents can automatically assert the DLQ logic is sound.
- 5. As an operator, I want to verify that replaying the DLQ message only targets the initially failing service, ensuring no duplicate events are processed by other services (targeted replay).

### What to build

Implement automated validation of the DLQ routing and Replay mechanics directly within the PowerShell smoke test script. The script will orchestrate a deterministic failure, wait for DLQ routing, restore the service, and verify the replay logic succeeds and resumes the business flow successfully.

### Acceptance criteria

- [ ] `scripts/local-smoke-test.ps1` is updated to support the `-Scenario dlq` argument.
- [ ] The script successfully places an order and immediately stops the `sql-shipping` container using `docker compose`.
- [ ] The script polls and verifies that a failure record appears in the DLQ via `GET /operator/api/failures`.
- [ ] The script restarts the `sql-shipping` container and successfully triggers the replay via `POST /operator/api/failures/{id}/replay`.
- [ ] The script asserts that the shipping service processes the replay and the final Shipment record is successfully created.

---

## Phase 3: HITL Verification Assets (Bruno & Docs)

**User stories**:
- 3. As a QA engineer, I want a Bruno collection folder with pre-wired requests for listing failures and triggering replays.
- 4. As a QA engineer, I want a step-by-step runbook for the DLQ scenario so I can deterministically simulate a transient outage (e.g., stopping a container) and recover from it.

### What to build

Deliver the required manual execution assets to empower QA engineers to verify the process through Human-In-The-Loop methods. This includes ready-to-run API requests and detailed markdown documentation for the simulation flow.

### Acceptance criteria

- [ ] A new `05-dlq-operator` folder is added to the project's Bruno collections (`qa/bruno/05-dlq-operator`).
- [ ] Bruno requests are created for `GET /operator/api/failures` and `POST /operator/api/failures/{id}/replay`.
- [ ] A step-by-step markdown runbook is created at `docs/qa/scenarios/05-dlq-operator.md`.
- [ ] The runbook clearly documents the manual container orchestration steps and the expected Bruno requests needed to complete the scenario.
