# Transactional outbox

At-least-once publish. Domain write + event row commit in one DB transaction. Background poller drains. See [ADR-0002](https://github.com/daonhan/Microservices-in-.NET/blob/main/docs/adr/0002-transactional-outbox-per-publishing-service.md).

## Write path (sync, inside HTTP request)

```mermaid
flowchart LR
    R[/HTTP request/] --> H[Endpoint handler]
    H --> TX{{DB Transaction}}
    TX --> D[(Domain table<br/>Orders / Stock / ...)]
    TX --> O[(OutboxEvents<br/>status=Pending)]
    TX --> C[Commit]
    C --> RES[/200 OK/]

    classDef tx fill:#1e3a8a,stroke:#3b82f6,color:#fff
    classDef db fill:#0f172a,stroke:#64748b,color:#fff
    class TX,C tx
    class D,O db
```

## Publish path (async, OutboxBackgroundService)

```mermaid
flowchart LR
    T([PeriodicTimer<br/>PublishIntervalInSeconds]) --> Q[GetUnpublishedOutboxEvents]
    Q --> L{rows?}
    L -- no --> T
    L -- yes --> F[foreach row]
    F --> LOCK[SQL transaction + pending-row update lock]
    LOCK --> PUB[eventBus.PublishAsync + broker confirmation]
    PUB -->|ok| M[Set Sent = true]
    PUB -->|throw| E[Record failure<br/>Attempts++]
    M --> COMMIT[Commit dispatch transaction]
    COMMIT --> F
    E --> CHK{Attempts >= MaxAttempts?}
    CHK -- no --> COMMIT
    CHK -- yes --> DEAD[(status=Failed<br/>visible at /internal/outbox/failed)]
    DEAD --> COMMIT
    DEAD --> POLL[Gateway DLQ poller]
    POLL --> OPS[(dead_letter_messages<br/>operator API)]

    classDef ok fill:#14532d,stroke:#22c55e,color:#fff
    classDef bad fill:#7f1d1d,stroke:#ef4444,color:#fff
    classDef ext fill:#1e293b,stroke:#0ea5e9,color:#fff
    class M,PUB ok
    class E,DEAD bad
    class POLL,OPS ext
```

## Failure recovery loop

```mermaid
sequenceDiagram
    autonumber
    participant Svc as Service
    participant DB as OutboxEvents
    participant GW as ApiGateway poller
    participant OPS as Operator
    Svc->>DB: status=Failed (Attempts >= Max)
    GW->>Svc: GET /internal/outbox/failed (RequireService)
    Svc-->>GW: failure rows
    GW->>GW: persist to dead_letter_messages
    OPS->>GW: GET /operator/api/failures
    GW-->>OPS: list
    OPS->>GW: POST /operator/api/failures/{id}/replay
    GW->>Svc: re-publish to OriginalQueue
```

## Source

- [`shared-libs/.../Outbox/OutboxBackgroundService.cs`](https://github.com/daonhan/Microservices-in-.NET/blob/main/shared-libs/ECommerce.Shared.EventBus/Impl/Outbox/OutboxBackgroundService.cs) — poller loop
- [`shared-libs/.../Outbox/OutboxContext.cs`](https://github.com/daonhan/Microservices-in-.NET/blob/main/shared-libs/ECommerce.Shared.EventBus/Impl/Outbox/OutboxContext.cs) — EF schema
- [`api-gateway/.../OutboxPolling/OutboxFailurePoller.cs`](https://github.com/daonhan/Microservices-in-.NET/blob/main/api-gateway/ApiGateway/Infrastructure/Polling/OutboxFailurePoller.cs) — gateway-side ingest

## Replica coordination and deployment

SQL dispatch rechecks each candidate under an update lock held through publish and
state commit; competing replicas skip owned rows. RabbitMQ publishes are persistent,
mandatory, and confirmed. Delivery remains at least once. See the
[reliability deployment runbook](../runbooks/outbox-delivery-reliability.md) for
rollback/retry behavior, topology compatibility, rollout ordering, and tests.
