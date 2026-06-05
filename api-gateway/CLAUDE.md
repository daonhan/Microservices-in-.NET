# API Gateway — service notes

Clean Architecture + Vertical Slices is the default service shape ([ADR-0012](../docs/adr/0012-clean-arch-vsa-default-service-shape.md)); API Gateway applies it to `Features/Operator/...`, `Infrastructure/Proxy/`, `Infrastructure/Polling/`, and `Infrastructure/Auth/` without `Domain/` or local `Contracts/`.

Narrow-pins shared-libs per [ADR-0013](../docs/adr/0013-shared-libs-multi-package-split.md) and [shared-libs-versioning.md](../docs/runbooks/shared-libs-versioning.md): `ECommerce.Shared.Platform`, `ECommerce.Shared.Messaging`, and `ECommerce.Shared.DeadLetter`.

Gateway compiles both YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) = `Yarp` (default) or `Ocelot`; unknown values fail fast. Routes/port/auth/health/metrics identical across both.

Also hosts the DLQ poller + operator API. See root [CLAUDE.md](../CLAUDE.md#dlq--operator-api) for the cross-cutting contract.

`DeadLetterQaFixtureSeeder` seeds five `qa-operator` `dead_letter_messages` rows for the smoke suite, gated by the existing `IsDevelopment()` block (no `Qa:Seed`, no `ECommerce.Shared.Testing.Qa` dependency). Env-gated runtime seeders — not migrations — for the QA Operator/DLQ artifacts are an explicit decision: [ADR-0014](../docs/adr/0014-env-gated-qa-runtime-seeders-for-operator-and-dlq.md). **Never add `Qa__Seed` to a non-dev manifest.**
