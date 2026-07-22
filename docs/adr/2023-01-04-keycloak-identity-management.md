# ADR 2023-01-04: Keycloak Identity Management

## Status
Accepted

## Context
Cadence Clinical requires a secure, unified authentication and authorization mechanism for clinical users. The system must support OIDC auth routing, integration with the API gateway (`apps/gateway`), and complex role-based access controls without forcing each downstream microservice to implement redundant credential checking logic.

## Decision
We decided to adopt Keycloak as the central Identity and Access Management (IAM) provider. Keycloak will handle OIDC authentication flows, issue JWTs, and maintain unified user session states across the platform.

## Alternatives Considered
- **Managed SaaS Providers (Auth0 / Okta):** These are easier to maintain and integrate. However, they introduce vendor lock-in and potential regulatory/data residency challenges for sensitive healthcare operations that mandate strict self-hosting capabilities.
- **Custom Authentication Service:** Developing a proprietary IAM service is extremely resource-intensive, highly prone to critical security vulnerabilities, and diverts focus from core clinical business logic.

## Trade-offs
- **Positive:** Keycloak is open-source, heavily battle-tested, and fully self-hostable (preventing vendor lock-in). It provides native support for OIDC, SAML, and sophisticated identity brokering out of the box.
- **Negative:** Keycloak is operationally complex to deploy, configure, and upgrade. It demands substantial infrastructure overhead (e.g., its own dedicated database and memory allocation) compared to lightweight custom solutions.
