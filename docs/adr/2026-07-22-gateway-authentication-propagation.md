# ADR 2026-07-22: Centralized API Gateway Authentication and Header Propagation

## Status
Accepted

## Context
Downstream microservices (e.g., Designer and Execution) previously did not independently validate OpenID Connect (OIDC) tokens or extract user roles, leaving them vulnerable to unauthorized access if they were exposed or if internal routing bypassed checks. However, duplicating full OIDC validation, JWKS fetching, and cryptographic token verification across every microservice introduces significant latency, operational overhead, and code duplication.

## Decision
We decided to centralize authentication and OIDC validation at the API Gateway level. The API Gateway will intercept all incoming requests, validate OIDC tokens against the Keycloak identity provider, and block invalid or missing tokens. 

To securely propagate the user's identity to downstream services without requiring them to re-verify the token, the Gateway will:
1. Extract the `sub` claim (as `X-User-Id`) and `realm_access` roles (as `X-User-Roles`).
2. Inject these into the HTTP headers of the proxied request.
3. Append an `X-Gateway-Timestamp` to prevent replay attacks.
4. Generate an HMAC-SHA256 signature using a shared `GATEWAY_SECRET` and append it as `X-Gateway-Signature`.

Downstream services will use lightweight middleware to quickly verify the HMAC signature and timestamp, establishing trust in the injected identity headers.

## Alternatives Considered
- **Decentralized OIDC Validation:** Validating tokens independently in each microservice. This was rejected due to the added latency (fetching JWKS), operational overhead, and code duplication.
- **Mutual TLS (mTLS) for Internal Routing:** Relying solely on network-level security between the Gateway and microservices. Rejected because it doesn't solve the need to securely propagate user identity and role information for application-level authorization.

## Trade-offs
- **Positive:** Downstream services remain lightweight and decoupled from the identity provider and OIDC complexities.
- **Positive:** Performance is improved since cryptographic verification of JWTs is done once at the edge. Downstream services only perform a fast HMAC signature check, satisfying low-latency constraints (< 15ms).
- **Positive:** Security is enhanced as centralized edge protection prevents unauthorized access. The HMAC signature prevents spoofing of identity headers even if an attacker manages to route directly to a downstream service.
- **Negative:** Both the gateway and all downstream services must securely share and manage the `GATEWAY_SECRET`.
- **Negative:** Any changes to the header propagation format (`X-User-Id`, `X-User-Roles`) must be synchronized across the gateway and all consuming microservices.
