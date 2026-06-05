# Scenario 06: DLQ Operator

Start from a clean stack:

```powershell
docker compose down -v
docker compose up --build
```

Use the Bruno collection in `qa/bruno/dlq-operator` with the `qa-local` environment (the Postman mirror is folder `06 DLQ Operator`). Two runtime seeders make the operator API exercisable black-box on a freshly booted stack:

- **Operator credential** — `AuthQaOperatorSeeder.SeedQaOperatorUser` inserts `operator@qa.test` with `Role = "Operator"`, gated by `IsQaSeedingEnabled` (Development OR `Qa:Seed=true`). `JwtTokenService` copies `user.Role` verbatim, so `POST /login` yields a token whose `user_role == "Operator"` with no token-service change.
- **DLQ fixture** — `DeadLetterQaFixtureSeeder.SeedQaDeadLetterFixture` inserts five `dead_letter_messages` rows (gateway-owned `DeadLetter` DB), gated by the gateway's existing `IsDevelopment()` block. The four mutating rows are reset to `Pending` on every boot, so a `RESET=0` rerun does not 409.

Both seeders are env-gated to local dev/QA and never run in any AKS namespace — see [ADR-0014](../../adr/0014-env-gated-qa-runtime-seeders-for-operator-and-dlq.md).

The five fixtures all seed `Status = Pending (0)`, `Origin = DeadLetter (0)`, `Service = "qa-operator"`, `EventType = "Qa.OperatorSmokeEvent"`, `OriginalQueue = "qa-dlq-replay-sink"` (an inert sink no service binds), `Payload = "{}"`:

| Fixture GUID | Env var | Role |
| --- | --- | --- |
| `f0000000-0000-0000-0000-000000000001` | `operatorDlqListId` | list + detail (never mutated) |
| `f0000000-0000-0000-0000-000000000002` | `operatorDlqReplayId` | single replay |
| `f0000000-0000-0000-0000-000000000003` | `operatorDlqBatchAId` | batch replay |
| `f0000000-0000-0000-0000-000000000004` | `operatorDlqBatchBId` | batch replay |
| `f0000000-0000-0000-0000-000000000005` | `operatorDlqDiscardId` | discard |

`DeadLetterStatus` serializes as a numeric enum (`0` Pending, `1` Replayed, `2` Discarded) — no `JsonStringEnumConverter` is registered, so the detail assertions match `0`/`1`/`2`.

## 1. Confirm the seeded fixtures

The five rows exist under `service = qa-operator`, all `Pending`.

SQL:

```sql
SELECT id, status, replayed_at, discarded_at, discard_reason
FROM DeadLetter.dbo.dead_letter_messages
WHERE id IN (
  'f0000000-0000-0000-0000-000000000001',
  'f0000000-0000-0000-0000-000000000002',
  'f0000000-0000-0000-0000-000000000003',
  'f0000000-0000-0000-0000-000000000004',
  'f0000000-0000-0000-0000-000000000005'
)
ORDER BY id;
```

Event/log: no runtime action — the fixture is seeded at gateway startup.

## 2. Log in as the Operator persona

HTTP: `POST http://localhost:8003/login` with `{ "username": "operator@qa.test", "password": "oKNrqkO7iC#G" }` returns `200` and a token whose decoded `user_role == "Operator"`. The Bruno request captures it as `operatorToken`.

SQL:

```sql
SELECT [Id], [Username], [Role]
FROM Auth.dbo.Users
WHERE [Username] = 'operator@qa.test';
```

Jaeger: find an Auth span for `POST /login`.

## 3. List and inspect a failure

HTTP: `GET http://localhost:8004/operator/api/failures?service=qa-operator` returns `200` with the five fixtures in the `items` array. `GET http://localhost:8004/operator/api/failures/{{operatorDlqListId}}` returns the detail with `message.service == "qa-operator"` and `message.status == 0` (Pending).

SQL:

```sql
SELECT id, status, replayed_at, discarded_at
FROM DeadLetter.dbo.dead_letter_messages
WHERE id = 'f0000000-0000-0000-0000-000000000001';
```

Event/log: no mutation — `…0001` stays Pending across the whole scenario.

Jaeger: the list/detail calls appear as gateway HTTP spans; counter `dlq_messages_total` is unaffected by reads.

## 4. Single replay

HTTP: `POST http://localhost:8004/operator/api/failures/{{operatorDlqReplayId}}/replay` returns `202` with `{ id, newMessageId }`. A detail re-GET on the same id then shows `message.status == 1` (Replayed) with a non-empty `replayedAt`.

Replay is side-effect-free here: `RabbitMqDeadLetterPublisher.Publish` uses the default exchange with `routingKey = OriginalQueue` and `mandatory: false`, so re-publishing to the inert `qa-dlq-replay-sink` queue (no subscriber) is silently dropped — it returns a `newMessageId` and never re-enters any consumer.

SQL:

```sql
SELECT id, status, replayed_at, replayed_by
FROM DeadLetter.dbo.dead_letter_messages
WHERE id = 'f0000000-0000-0000-0000-000000000002';
```

Event/log: `dlq_replays_total` increments; the `dlq.replay` span carries the original `CorrelationId`.

## 5. Batch replay

HTTP: `POST http://localhost:8004/operator/api/failures/replay-batch` with `{ "ids": ["{{operatorDlqBatchAId}}", "{{operatorDlqBatchBId}}"] }` returns `200` with `items` length 2, each item `status == "success"` (the handler-mapped string, not the enum) and a non-empty `newMessageId`.

SQL:

```sql
SELECT id, status, replayed_at
FROM DeadLetter.dbo.dead_letter_messages
WHERE id IN (
  'f0000000-0000-0000-0000-000000000003',
  'f0000000-0000-0000-0000-000000000004'
)
ORDER BY id;
```

Event/log: `dlq_replays_total` increments by two.

## 6. Discard

HTTP: `POST http://localhost:8004/operator/api/failures/{{operatorDlqDiscardId}}/discard` with `{ "reason": "qa smoke discard" }` returns `202` with the `reason` echoed. A detail re-GET then shows `message.status == 2` (Discarded), a non-empty `discardedAt`, and `discardReason == "qa smoke discard"`.

SQL:

```sql
SELECT id, status, discarded_at, discarded_by, discard_reason
FROM DeadLetter.dbo.dead_letter_messages
WHERE id = 'f0000000-0000-0000-0000-000000000005';
```

Event/log: `dlq_discards_total` increments.

## Rerun without `down -v`

Replay/discard succeed only on `Status == Pending` (else `409`). The gateway seeder resets the four mutating rows (`…0002`–`…0005`) back to `Pending` — clearing `replayed_at`/`discarded_at`/`discard_reason` — on every boot, so a `RESET=0` rerun is green without `docker compose down -v`.
