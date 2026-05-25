# API Gateway — service notes

Gateway compiles both YARP and Ocelot. `Gateway:Provider` (env `Gateway__Provider`) = `Yarp` (default) or `Ocelot`; unknown values fail fast. Routes/port/auth/health/metrics identical across both.

Also hosts the DLQ poller + operator API. See root [CLAUDE.md](../CLAUDE.md#dlq--operator-api) for the cross-cutting contract.
