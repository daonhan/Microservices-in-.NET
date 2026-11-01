# Outbox delivery reliability (shared packages 3.2.1)

## Delivery contract

Delivery is **at least once**. A database transaction cannot atomically commit
with RabbitMQ's acknowledgement. A process/database failure after the broker
accepts a message but before `Sent` commits can publish that message again.
Consumers must deduplicate by the payload's `Event.Id`; RabbitMQ `MessageId` now
contains that same ID. Publisher confirms do not mean a consumer processed the
message, and a fanout routing success proves at least one binding, not that every
intended subscriber is present.

## Order write path

`OrderContext.ExecuteAsync` owns its transaction. It snapshots domain events,
translates the whole batch before writing, and retains the mapped event IDs
through EF execution-strategy retries. Once command preparation succeeds, retries
repeat tracked database writes and outbox inserts, rather than mutating aggregates
again. The callback should stage tracked changes, not call `SaveChanges`, send
external requests, or perform other irreversible side effects. An exception during
preparation can still cause EF to invoke it again; discard the scoped context if
preparation fails rather than reusing partially mutated aggregates.

`SaveChangesAsync(false)` leaves writes pending. `AcceptAllChanges` and removal of
only the captured domain-event instances happen after `TransactionScope.Dispose`
returns successfully, not after `Complete` merely votes to commit. An enclosing
ambient or explicit EF transaction is rejected before invoking work, because an
inner scope cannot acknowledge an outer transaction's eventual commit.

Outbox insertion checks persisted rows and detaches its inserted row after each
save. A row left in EF's identity map by an earlier ambient rollback must not
suppress the next insertion. The stable event ID makes repeated inserts into an
already committed outbox idempotent. This does not provide business-command
idempotency: an ambiguous business transaction commit may still surface a duplicate
key/error on retry. Reconcile using the business ID before retrying the command
from a new request.

These changes cover `OrderContext.ExecuteAsync` and the shared outbox store and
dispatcher. Other callers of `IOutboxUnitOfWork` remain responsible for replay-safe
callbacks and their own DbContext/domain-event acknowledgement lifecycle; it does
not restore caller-owned EF tracking state after rollback. In particular, Payment's
separate context and Order's saga command handlers require their own transaction
lifecycle audit before claiming the same aggregate retry guarantees.

## Dispatch across replicas

The SQL Server store implements the optional `IOutboxDispatchStore` capability.
Polling reads at most 100 pending rows without tracking and skips locked rows.
Each candidate is then rechecked in its own transaction using `UPDLOCK`,
`READPAST`, and `READCOMMITTEDLOCK`. Only the lock owner publishes and changes
`Sent` or increments failure state. Ownership lasts through broker confirmation
and the database commit; a crashed process releases its lock through rollback.
There are no clocks, expiring leases, new status values, or new schema columns.
`READCOMMITTEDLOCK` also supports databases with `READ_COMMITTED_SNAPSHOT ON`.

This deliberately holds a database transaction open while publishing. Monitor SQL
lock waits, transaction duration, dispatch lag, and broker latency. SQL can still
block at page/table locks, and ordering across replicas is not guaranteed. Each
RabbitMQ confirmation wait is bounded to 10 seconds; configured transport retries
can extend the total dispatch time. Cancellation rolls back ownership without
counting a shutdown as a failed publish. Failed polling/state persistence is logged
and retried on the next tick instead of terminating the hosted service.

Existing custom `IOutboxStore` implementations continue to use the legacy dispatch
path. They must implement `IOutboxDispatchStore` with equivalent ownership semantics
before using concurrent dispatchers. Do not use a process-local mutex as a replica
coordination mechanism.

## RabbitMQ compatibility

The publisher uses confirms, persistent messages, JSON content type, stable message
IDs, and mandatory routing. Nacks, confirm timeouts, connection errors, and returned
unroutable messages propagate to outbox failure tracking; they never acknowledge
an outbox row as sent. Confirm selection precedes publish, and returned messages
are checked after the broker's confirmation.

The existing `ecommerce-exchange` declaration remains **fanout, non-durable,
non-auto-delete**. Consumer queues and the DLQ remain durable. Changing an existing
exchange's durability in place causes RabbitMQ `PRECONDITION_FAILED` channel
closures, so this patch does not silently redeclare it as durable. Persistent
messages routed into durable queues can survive broker restarts, but the transient
exchange/bindings still need consumer startup to restore them. Durability alone is
not a guarantee against disk failure; broker/queue replication is an operational
choice. Dead-letter replay uses a separate publisher and is not changed here.

For a future durable exchange cutover, provision a differently named durable
exchange, bind durable subscriber queues, coordinate publisher/consumer configuration
changes, drain in-flight messages, and retire the old exchange only after verification.
Do not delete/recreate a live exchange or its queues as part of deploying this patch.

## Deployment and rollback

1. Pack all ten shared capability/umbrella packages together at `3.2.1`, then
   restore and rebuild consumers. All nine service projects pin the new lockstep
   version; do not overwrite an existing published `3.2.0` package with new binaries.
   Assembly identities remain `3.2.0.0` for this patch because outbox rows persist
   assembly-qualified type names; package and informational versions identify the patched release.
2. No EF migration is added. Existing initial outbox and failure-tracking migrations
   must already be applied to every publishing service's database. The existing
   `(Sent, Status)` index is reused. No new database permissions are required beyond
   normal reads, updates, and transactions.
3. Ensure expected subscriber queues/bindings exist before enabling new publishers.
   An empty fanout exchange now causes retries/failure rows instead of silently
   losing messages. Verify `/health/ready` and broker bindings after restart.
4. Stop or drain **all old dispatchers for a given database** before starting the
   new replicas. An old poller can read a row before a new owner locks it and later
   publish it, so mixed old/new replicas do not have the new exclusion guarantee.
   Pending rows remain compatible; the application API can remain available if
   dispatchers are operated independently.
5. Watch failed rows and `Attempts`/`LastError`, publish confirms/returns, SQL locks,
   consumer duplicate handling, and the gateway operator API during rollout.
6. Rollback needs no schema or RabbitMQ changes. Rebuild/redeploy `3.2.0` consumers
   together, accepting the previous publisher/dispatch guarantees. Keep existing
   rows and queues; never truncate the outbox to recover a deploy.

## Regression checks

Run from the repository root with .NET 10 and Docker available:

```bash
dotnet pack shared-libs/ECommerce.Shared.slnx -c Release -o local-nuget-packages
dotnet test shared-libs/tests/ECommerce.Shared.EventBus.Tests
dotnet test shared-libs/tests/ECommerce.Shared.RabbitMq.Tests
dotnet test shared-libs/tests/ECommerce.Shared.Messaging.Tests
dotnet test shared-libs/tests/ECommerce.Shared.DeadLetter.Tests
dotnet test order-microservice/Order.Service.slnx --filter 'FullyQualifiedName~OrderContextReliabilityTests|FullyQualifiedName~DomainEventOutboxInterceptorTests'
```

SQL Server integration regressions exercise real transactions, rollback at scope
disposal, retained Order events/tracked states, stable IDs through an injected EF
retry, two competing contexts, RCSI polling, lock release on cancellation, failed
publish accounting, and reinsertion after ambient rollback. RabbitMQ regressions
exercise persistent confirmed delivery against existing topology, mandatory returns,
and failure propagation. Unit tests additionally cover translation validation,
selective event acknowledgement, and recovery after a failed database poll.
