# ADR-0003 — RS256 JWT issuance with `/jwks` discovery

- **Status**: Accepted
- **Date**: 2026-05-06

## Context

Every protected endpoint in the platform — Order, Basket, Payment, the operator DLQ API — needs to validate access tokens minted by the Auth service. A symmetric (HS256) shared-secret approach would force every service to know the signing secret, turning auth into a fan-out secret-management problem. The platform should also model how a real OIDC-style identity provider exposes its keys so that resource servers can rotate without redeployments.

Implemented in [`auth-microservice/`](../../auth-microservice/). See also the wiki page [`Service-Auth.md`](../wiki/Service-Auth.md).

## Decision

The Auth service signs JWTs with **RS256** using an asymmetric key pair. The public key is published as a JWKS document at `/jwks`. Every resource server is configured to validate tokens by fetching that JWKS document — no shared secret leaves the Auth service. Token issuer and audience claims are checked alongside the signature.

## Consequences

- Resource servers depend on the Auth service being reachable at startup (or until the JWKS is cached). Caching with periodic refresh keeps this from becoming a hard runtime dependency.
- Key rotation is possible without redeploying consumers: publish a new `kid` in JWKS, sign new tokens with it, retire the old `kid` after the longest token lifetime.
- Slightly heavier validation cost than HS256, accepted as the price of not having a shared secret.
- Out of scope: full OIDC discovery (`/.well-known/openid-configuration`), refresh-token rotation policy.
