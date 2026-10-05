# ADR-2809: User Onboarding Preferences Persistence and Client Synchronization

- **Status:** Accepted
- **Date:** 2026-10-02
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

User onboarding tour state and persona-level preferences were previously stored exclusively in client-side `localStorage`. Consequently, user onboarding progress was reset across devices or browsers, leading to repetitive onboarding tours and inconsistent user experiences. A hybrid persistence and synchronization model was required to store onboarding preferences in the Org microservice backend while providing immediate, zero-flicker client startup. Reference system requirement PRD-SYS-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** User onboarding progress must persist across user sessions and devices.
- **Driver 2:** Zero-flicker initial render must be preserved on web app startup by reading local state before backend HTTP requests resolve.
- **Driver 3:** Cross-device conflict resolution between client state and server state must be deterministic using timestamp comparison (`PRD-SYS-001`).

## 3. Options Considered

### Option 1: Hybrid Client-Server Sync with Debounced Backend Persistence (Selected)

- **Overview:** Store onboarding preferences in `org.user_onboarding_preferences` via EF Core in `apps/org`. On web client startup, load local state immediately from persona-scoped storage, fetch remote preferences asynchronously, and merge based on `remoteTime > localTime`. Push debounced local updates to `PUT /api/v1/org/preferences/onboarding/{persona}`.
- **Pros:**
  - ✅ Satisfies requirement PRD-SYS-001 with cross-device persistence.
  - ✅ Preserves instant UI startup with zero visual flicker.
  - ✅ Network failures during debounced sync operate silently without blocking the user interface.
- **Cons:**
  - ❌ Requires a new EF Core model and migration in `apps/org`.

### Option 2: Pure Server-Side Synchronous Store Loading

- **Overview:** Block frontend store initialization until `GET /api/v1/org/preferences/onboarding/{persona}` resolves.
- **Pros:**
  - ✅ Single source of truth.
- **Cons:**
  - ❌ Causes visible UI latency and layout flicker on cold application boot.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Chosen because it provides cross-device persistence (`PRD-SYS-001`) while maintaining instant zero-flicker frontend rendering and graceful offline/network resilience.

## 5. Consequences & Trade-offs

- **Positive Impact:** Seamless user experience across devices with debounced sync and deterministic conflict resolution (`PRD-SYS-001`).
- **Negative Impact / Technical Debt:** Additional REST endpoints and database table in `apps/org`.
- **Mitigation Strategy:** Automated xUnit unit tests in `tests/Cadence.UnitTests/Org/UserOnboardingPreferenceEndpointsTests.cs` and Vitest store tests in `apps/web/tests/stores/onboarding_sync.spec.ts`.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/org`, `apps/web`
- **Verification Plan:** Unit tests in `Cadence.UnitTests` and Vitest suite in `apps/web/tests/stores/onboarding_sync.spec.ts`.
