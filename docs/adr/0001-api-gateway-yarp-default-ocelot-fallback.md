# ADR-0001 — API Gateway provider switch: YARP default with Ocelot fallback

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

The platform needs a single ingress that fronts every service, terminates auth, and exposes operator endpoints. Two reasonable .NET reverse proxies exist — YARP (modern, Microsoft-maintained, performance-focused) and Ocelot (older, configuration-heavy, widely cited in microservices tutorials). Picking one outright would have meant either losing the chance to demonstrate Ocelot or losing YARP's better defaults. The gateway is also a teaching surface: the README and wiki specifically highlight that you can swap proxies without changing routes, auth, or metrics.

Implemented in [`api-gateway/`](../../api-gateway/). See also the wiki page [`Service-API-Gateway.md`](../wiki/Service-API-Gateway.md).

## Decision

The gateway service compiles **both** YARP and Ocelot. A single `Gateway:Provider` configuration value (env `Gateway__Provider`) selects which one wires up at startup. `Yarp` is the default; `Ocelot` is the documented fallback; any other value fails fast at boot. Routes, auth, health checks, and Prometheus metrics are identical across both providers, so no client- or service-side changes are needed when switching.

## Consequences

- The gateway carries two proxy stacks. Build size and dependency graph are larger than a single-provider gateway would be.
- Route configuration has to be maintained in two formats; a regression test boots both providers to keep them honest.
- Switching proxy at runtime is **not** supported — the choice is taken once at process start. Any change requires a restart, which is acceptable given how rarely the switch is exercised.
- Out of scope: per-route provider selection, hot reload of the provider switch.
