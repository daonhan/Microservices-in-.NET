# Scenario 05: Saga Operator Abort

Start from a clean stack:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collection in `qa/bruno/saga-operator` with the `qa-local` environment. The orchestrator opens a saga for every order; for this scenario a deterministic Running `Order` saga is seeded by `20260526100000_SeedQaPhase2_Saga` (gated by `IsQaSeedingEnabled`) so the operator endpoints have a stable target on a freshly booted stack.

## 1. Confirm the seeded operator saga

The seeded saga has `SagaId = e0000000-0000-0000-0000-000000000001`, `SagaType = Order`, `Status = Running`, `CurrentStep = PaymentAuthorizing`, and a synthetic `OrderId = e0000000-0000-0000-0000-000000000002` no other scenario references.

SQL:

```sql
SELECT si.SagaId, si.SagaType, si.CurrentStep, si.Status, os.OrderId
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
WHERE si.SagaId = 'e0000000-0000-0000-0000-000000000001';
```

Event/log: no runtime action — the fixture is migration-seeded.

## 2. Issue a service token

HTTP: `POST http://localhost:8003/token` with `grant_type=client_credentials`, `client_id=api-gateway`, and `client_secret=dev-api-gateway-secret` returns `200` with a service-role JWT.

SQL: not required.

Event/log: Auth logs a service-token success metric.

Jaeger: find an Auth span for `POST /token`.

## 3. List and inspect the saga

HTTP: `GET http://localhost:8008/operator/api/sagas?type=Order&status=Running` returns the seeded saga in the `items` array. `GET http://localhost:8008/operator/api/sagas/{sagaId}` returns the saga detail, including `transitions`.

SQL:

```sql
SELECT CurrentStep, Status, LastCommandId, NextTimeoutAt
FROM Saga.dbo.SagaInstances
WHERE SagaId = 'e0000000-0000-0000-0000-000000000001';

SELECT FromStep, ToStep, TriggerKind, Error
FROM Saga.dbo.SagaTransitions
WHERE SagaId = 'e0000000-0000-0000-0000-000000000001'
ORDER BY Timestamp, Id;
```

Event/log: no operator action should have been recorded yet.

Jaeger: the saga detail call should appear as a Saga service HTTP span.

## 4. Retry the in-flight step

HTTP: `POST http://localhost:8008/operator/api/sagas/{sagaId}/retry` returns `202` and the same `currentStep`.

SQL:

```sql
SELECT LastCommandId, RetryCount, NextTimeoutAt
FROM Saga.dbo.SagaInstances
WHERE SagaId = 'e0000000-0000-0000-0000-000000000001';

SELECT TOP 5 Id, EventType, Sent, Status
FROM Saga.dbo.OutboxEvents
WHERE Id = 'e0000000-0000-0000-0000-000000000003';
```

Event/log: the seeded outbox row for `LastCommandId` is set back to `Sent=0` with `Status=Pending`, and a `TriggerKind=OperatorAction` transition records the retry.

Jaeger: follow the next publish attempt for the requeued command.

## 5. Abort the saga

HTTP: `POST http://localhost:8008/operator/api/sagas/{sagaId}/abort` returns `202` with `status=Compensating` and the compensation `currentStep`.

SQL:

```sql
SELECT CurrentStep, Status, LastCommandId
FROM Saga.dbo.SagaInstances
WHERE SagaId = 'e0000000-0000-0000-0000-000000000001';

SELECT FromStep, ToStep, TriggerKind, Error
FROM Saga.dbo.SagaTransitions
WHERE SagaId = 'e0000000-0000-0000-0000-000000000001'
ORDER BY Timestamp, Id;
```

Event/log: Saga publishes the first reverse-step command and records an operator-action transition with `Operator abort started saga compensation.`

Jaeger: the compensation command publish should share the saga correlation context.
