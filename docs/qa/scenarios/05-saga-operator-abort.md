# Scenario 05: Saga Operator Abort

Start from a clean stack with the orchestrator fully enabled:

```powershell
docker compose down -v
docker compose up --build
```

Set the Saga service environment to `SAGA_ORCHESTRATOR_ENABLED=true` and `SAGA_ORCHESTRATOR_PERCENTAGE=100` before the run. Use the Bruno collection in `qa/bruno/saga-operator` with the `qa-local` environment.

## 1. Open an orchestrated order saga

Pause Payment before placing the order so the saga stays in an in-flight step long enough to operate on:

```powershell
docker compose stop payment
```

HTTP: use the happy-path customer flow to create an order for product `9001`. The Saga service should open an `Order` saga for the order and park at `PaymentAuthorizing` after stock reservation.

SQL:

```sql
SELECT si.SagaId, si.SagaType, si.CurrentStep, si.Status, os.OrderId
FROM Saga.dbo.SagaInstances si
JOIN Saga.dbo.OrderSagaStates os ON os.SagaId = si.SagaId
ORDER BY si.CreatedAt DESC;
```

Event/log: Saga logs show the order saga opening and dispatching the current in-flight command. Payment remains stopped until the abort is issued.

Jaeger: find the saga transition span for the new `SagaId`.

## 2. Issue a service token

HTTP: `POST http://localhost:8003/token` with `grant_type=client_credentials`, `client_id=api-gateway`, and `client_secret=dev-api-gateway-secret` returns `200` with a service-role JWT.

SQL: not required.

Event/log: Auth logs a service-token success metric.

Jaeger: find an Auth span for `POST /token`.

## 3. List and inspect the saga

HTTP: `GET http://localhost:8008/operator/api/sagas?type=Order&status=Running` returns the running saga. `GET http://localhost:8008/operator/api/sagas/{sagaId}` returns the saga detail, including `transitions`.

SQL:

```sql
SELECT CurrentStep, Status, LastCommandId, NextTimeoutAt
FROM Saga.dbo.SagaInstances
WHERE SagaId = '<sagaId>';

SELECT FromStep, ToStep, TriggerKind, Error
FROM Saga.dbo.SagaTransitions
WHERE SagaId = '<sagaId>'
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
WHERE SagaId = '<sagaId>';

SELECT TOP 5 Id, EventType, Sent, Status
FROM Saga.dbo.OutboxEvents
ORDER BY CreatedAt DESC;
```

Event/log: the outbox row for `LastCommandId` is pending again, and a `TriggerKind=OperatorAction` transition records the retry.

Jaeger: follow the next publish attempt for the requeued command.

## 5. Abort the saga

HTTP: `POST http://localhost:8008/operator/api/sagas/{sagaId}/abort` returns `202` with `status=Compensating` and the compensation `currentStep`.

SQL:

```sql
SELECT CurrentStep, Status, LastCommandId
FROM Saga.dbo.SagaInstances
WHERE SagaId = '<sagaId>';

SELECT FromStep, ToStep, TriggerKind, Error
FROM Saga.dbo.SagaTransitions
WHERE SagaId = '<sagaId>'
ORDER BY Timestamp, Id;
```

Event/log: Saga publishes the first reverse-step command and records an operator-action transition with `Operator abort started saga compensation.`

Jaeger: the compensation command publish should share the saga correlation context.

Restart Payment after the scenario:

```powershell
docker compose start payment
```
