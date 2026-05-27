# API Gateway — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)); API Gateway applies it to `Features/Operator/...`, `Infrastructure/Proxy/`, `Infrastructure/Polling/`, and `Infrastructure/Auth/` without `Domain/` or local `Contracts/`.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.DeadLetter`.

Gateway compiles both YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) = `Yarp` (default) or `Ocelot`; unknown values fail fast. Routes/port/auth/health/metrics identical across both.

Also hosts the DLQ poller + operator API. See root [CLAUDE.md](../CLAUDE.md#dlq--operator-api) for the cross-cutting contract.
