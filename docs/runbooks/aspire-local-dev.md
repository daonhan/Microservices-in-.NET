# .NET Aspire Local-Dev Runbook

The Aspire AppHost is a **local-only** overlay for the inner dev loop (decision: [ADR-0011](../adr/0011-aspire-local-dev-overlay.md)). It never runs in Azure. Docker Compose remains the AKS-parity path and the supported path for smoke/QA and the saga regression suite — see the AppHost-vs-Compose matrix below.

## Entry point

```bash
dotnet run --project apphost/Nhamnhi.AppHost
```

This starts SQL Server, Redis, and RabbitMQ as containers, then all 8 services + `ApiGateway` as Aspire resources, and launches the dashboard. F5 in the IDE on `apphost/Nhamnhi.AppHost` does the same with the debugger attached.

No edits to any service's `appsettings.Development.json` are required: connection strings and cross-service URLs flow from the Aspire app model into the services as environment variables.

## Dashboard URL and ports

| What | URL / port |
| --- | --- |
| Aspire dashboard | `http://localhost:18888` (launches automatically) |
| AppHost launch endpoint (DCP) | `http://localhost:15888` |
| Dashboard OTLP ingest | `http://localhost:18889` |
| Resource service endpoint | `http://localhost:18890` |

Ports are set in `apphost/Nhamnhi.AppHost/Properties/launchSettings.json`. To change the dashboard port, edit `ASPNETCORE_URLS` in the `http` profile; to change the launch endpoint, edit `applicationUrl`. Aspire is pinned at **9.x** (currently `9.5.2`) in `apphost/Nhamnhi.AppHost/Nhamnhi.AppHost.csproj`.

## AppHost vs. Docker Compose — decision matrix

| Use **AppHost** (`dotnet run --project apphost/Nhamnhi.AppHost`) | Use **Docker Compose** (`docker compose up --build`) |
| --- | --- |
| Iterating on a cross-service change and want one dashboard for traces/metrics/logs/console | AKS-shape parity run (manifests mirror the Compose topology) |
| IDE-attached debugging of one or more services without hand-starting dependencies | Local smoke runs, QA scenarios, the Phase-4 saga regression path |
| Fast inner loop on touched code without a full Compose rebuild | Exercising the OTEL Collector → Jaeger/Prometheus/Loki/Grafana stack ([ADR-0009](../adr/0009-otel-jaeger-prometheus-loki-grafana.md)) |
| | Azure Service Bus emulator path (`docker compose --profile asb up`) |
| | Anything that must match what staging/prod runs |

When in doubt for anything that ships, prefer Compose. The OTEL stack ([ADR-0009](../adr/0009-otel-jaeger-prometheus-loki-grafana.md)) is unchanged by the AppHost; the Aspire dashboard is an additional local-only view, not a replacement for Grafana/Jaeger.

## Troubleshooting

### Port conflicts

Symptom: AppHost fails to start, or the dashboard/launch endpoint does not come up.

```bash
# See what holds the dashboard / DCP / OTLP / resource-service ports
ss -ltnp | grep -E ':18888|:15888|:18889|:18890'
```

Free the offending process, or change the port in `apphost/Nhamnhi.AppHost/Properties/launchSettings.json` (`ASPNETCORE_URLS` for the dashboard, `applicationUrl` for the launch endpoint) and re-run.

### DCP (Developer Control Plane) failures

Symptom: resources stay `Starting`/`FailedToStart`, or errors mention `Aspire.Hosting.Dcp`.

- Confirm Docker Desktop is running — DCP launches the SQL/Redis/RabbitMQ containers.
- Raise log detail: in `apphost/Nhamnhi.AppHost/appsettings.json` set `"Aspire.Hosting.Dcp": "Debug"` and re-run to see why a resource failed.
- A service that fails its own startup (bad connection string, missing migration) surfaces in the dashboard's resource detail and console — open that resource's logs first.

### Container reset

Symptom: stale SQL/Redis/RabbitMQ container state from a previous AppHost run.

```bash
# Stop the AppHost (Ctrl+C), then remove the Aspire-managed containers
docker ps --filter "label=aspire" -q | xargs -r docker rm -f
# Optionally prune dangling volumes from those containers
docker volume prune -f
```

Re-run `dotnet run --project apphost/Nhamnhi.AppHost` to recreate them clean.

## Standalone Aspire Dashboard container (future Compose-driven setups)

The dashboard can also run as a standalone container, decoupled from the AppHost — useful if a future Compose-driven setup wants the single-pane dashboard without adopting the full Aspire app model:

```bash
docker run --rm -it \
  -p 18888:18888 -p 18889:18889 \
  mcr.microsoft.com/dotnet/aspire-dashboard:9.5
```

Point services' OTLP exporter at `http://localhost:18889` and open `http://localhost:18888`. This is documented for future use only — the current supported local paths are the AppHost and Docker Compose.
