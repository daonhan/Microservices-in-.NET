# Troubleshooting

Common failure modes and their fixes. If none of these match, check [Observability](Observability) — the trace usually points at the culprit within seconds.

## Docker Compose: services crash-loop at startup

**Symptom**: `docker compose up` shows services restarting repeatedly.

**Likely causes**:
- SQL Server not yet ready when a service tries to migrate.
- RabbitMQ not yet listening when a service tries to declare the exchange.

**Fix**: bring infra up first.

```bash
docker compose up sql rabbitmq redis -d
# wait ~20s
docker compose up
```

EF Core `EnableRetryOnFailure` and Polly retries for RabbitMQ cover transient failures once everything is up.

## Docker API named pipe not found (`npipe://.../docker-<id>`)

**Symptom**: local runs fail with errors like:

```text
failed to connect to the docker API at npipe://\\.\pipe\docker-<id>
open \\.\pipe\docker-<id>: The system cannot find the file specified.
```

This usually appears when using local Kubernetes tooling (`kubectl`, Minikube docker-env,
or scripts that build images) after Docker Desktop restarted, and the calling shell/process
still points to a stale Docker endpoint.

**Fix (Windows / PowerShell)**:

```powershell
# 1) Clear stale process-level overrides
Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue
Remove-Item Env:DOCKER_CONTEXT -ErrorAction SilentlyContinue

# 2) Re-select a known-good context
docker context use desktop-linux

# 3) Validate daemon connectivity
docker version
docker context ls
```

If you use Minikube's Docker daemon, re-run docker-env in the same shell before rebuilding images:

```powershell
minikube docker-env | Invoke-Expression
./scripts/build-local-images.ps1
./scripts/apply-local-k8s.ps1
```

If the error persists, restart Docker Desktop (or `com.docker.service`) and reopen the terminal,
because parent processes can keep stale environment values.

## Migrations don't apply

**Symptom**: 500s with SQL errors about missing tables.

**Fix**: run migrations from the service project.

```bash
cd order-microservice/Order.Service
dotnet ef database update
```

Repeat per service.

## JWT rejected by downstream service

**Symptom**: Gateway accepts the token but the service returns 401.

**Likely cause**: `Jwt:Issuer` / signing key drift between Auth and the downstream service, or clock skew.

**Fix**: ensure every service's `Jwt:*` and `AuthMicroserviceBaseAddress` config match. In Kubernetes, source them from the same ConfigMap/Secret.

## Events published but never received

**Symptom**: Order stuck in `Created`; Inventory never reserves.

**Diagnosis checklist**:
1. RabbitMQ Management UI (http://localhost:15672) — does the exchange exist? Are queues bound? Is the depth growing?
2. Outbox table — are rows being written but not marked published?
3. Service logs — any handler exceptions?
4. Jaeger — does the publish span appear? Does the consume span appear?

**Common fixes**:
- Restart the subscriber to re-bind queues.
- Check the `OutboxBackgroundService` is registered in `Program.cs`.
- Verify the handler is registered via `AddEventHandler<TEvent, THandler>()`.

## Outbox backlog

**Symptom**: Outbox row count climbing; events lag.

**Likely cause**: Broker unreachable or slow; handler throughput too low.

**Fix**: Check RabbitMQ health, increase polling cadence in the Outbox config, or scale the publisher.

## Alert: `RabbitMqQueueBacklog` firing

See [Observability § Alerts](Observability#alerts). Usually a slow or crashed subscriber. Check the queue's consumer count in the RabbitMQ UI.

## Gateway returns 502

**Symptom**: `GET /product/1` returns 502 through the Gateway but works against the Product service directly.

**Fix**: check the Gateway startup log for the active provider (`ApiGateway starting with provider=...`). If YARP routes are misconfigured, roll back to Ocelot while you debug:

```bash
Gateway__Provider=Ocelot docker compose up api-gateway
```

See [Service-API-Gateway](Service-API-Gateway).

## Integration tests hang

**Symptom**: Order or Inventory integration tests wait for an event that never arrives.

**Fix**: ensure `sql`, `rabbitmq`, and `redis` are running (`docker compose up sql rabbitmq redis -d`). Check `IntegrationTestBase` bindings match the event the code publishes.

## Kubernetes pods `CrashLoopBackOff`

**Fix sequence**:

```bash
kubectl describe pod <pod>
kubectl logs <pod> --previous
```

Most commonly: missing ConfigMap key, wrong `Jwt:Key`, or SQL service not ready. Apply infra manifests first (see [Kubernetes-Deployment](Kubernetes-Deployment)).
