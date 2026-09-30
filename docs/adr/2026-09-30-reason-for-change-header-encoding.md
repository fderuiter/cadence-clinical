# ADR-2495: One Encoding for the Reason-for-Change Header

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

The reason for change travels on the `X-Change-Reason` request header, and every regulated write stores it in
the audit trail (21 CFR 11.10(e)). The header had no declared encoding (#4895):

- **Browsers refuse it:** fetch accepts a header value only when every character is at most U+00FF. A reason with a
  typographic dash or quote (`—`, `’`), which autocorrect inserts, or in a non-Latin script throws before the
  request is sent. The user sees a generic failure on a data change, site activation or subject state change.
- **Servers guess:** Latin-1 letters such as `é` do get through, but the gateway, the signed-identity check and
  Execution read the raw header without an encoding, so the audit trail may not hold what the user typed.
- **Proxy hops:** a non-ASCII value cannot be forwarded by the gateway's HTTP client either.

## 2. Decision Drivers & Constraints

- The audit entry stores exactly the text the user typed, in any script.
- A header value on the wire is ASCII.
- Existing plain ASCII reasons (scripts, tests, integrations) keep working unchanged.
- One decoder on the server, used by every reader.

## 3. Options Considered

### Option 1: Move the reason into the request body

- **Cons:**
  - ❌ Every mutating route and contract changes, including `DELETE` routes that have no body.
  - ❌ The gateway signs the reason with the caller's identity; it would have to read and rewrite bodies.

### Option 2: Percent-encode every reason

- **Cons:**
  - ❌ Ambiguous for existing clients: a plain reason such as `Fixed 5% error` would be decoded wrongly or rejected.

### Option 3: Plain ASCII, or the RFC 8187 extended form (Selected)

- **Overview:**
  - A reason that is printable ASCII (U+0020 to U+007E) and does not start with `UTF-8'` travels unchanged.
  - Any other reason travels as `UTF-8''` followed by the percent-encoded UTF-8 bytes of the text, the
    `ext-value` of RFC 8187 (as in `Content-Disposition: filename*=`). The charset name is case-insensitive and an
    optional language tag between the quotes is ignored.
  - A value that starts with `UTF-8'` must be well-formed: every `%` starts a two-digit hex escape, every other
    character is visible ASCII, and the bytes are valid UTF-8. A malformed value is refused, never stored.
  - `Cadence.Security.Authentication.ChangeReasonHeader` (`Encode`, `TryDecode`, `Read`) is the only server-side
    codec. `apps/web/src/api/changeReason.ts` (`encodeChangeReason`) is the browser's encoder.
  - The gateway decodes the browser's value, signs the decoded text with the caller's identity, and forwards the
    canonical encoding. The signed-identity check decodes before verifying, so `Principal.ChangeReason` and the
    audit interceptor see the typed text. A malformed value gets `400` at the gateway, and a request that reaches
    a service with one fails authentication.
- **Pros:**
  - ✅ Any reason can be sent from a browser, and the audit trail stores it exactly.
  - ✅ Plain ASCII reasons are unchanged on the wire.
  - ✅ A published standard with a known shape, not a private scheme.
- **Cons:**
  - ❌ A reason typed literally starting with `UTF-8'` is always sent encoded.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** It fixes the browser failure and the server's guess without changing any route or contract,
  and keeps every existing ASCII caller working.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - A reason in any language reaches the audit trail as typed, through the gateway and its signature.
- **Negative Impact / Technical Debt:**
  - Every new client that sets `X-Change-Reason` must encode it with `encodeChangeReason` (web) or
    `ChangeReasonHeader.Encode` (.NET). A raw non-ASCII value still fails in the browser.
  - Every server reader must decode with `ChangeReasonHeader`; reading `Request.Headers["X-Change-Reason"]`
    directly is a defect.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/security/src/Authentication/ChangeReasonHeader.cs`, `GatewayIdentity.cs`, `GatewayAuthenticationHandler.cs`
  - `apps/gateway/src/GatewayIdentityForwarding.cs`, `apps/gateway/src/Program.cs`
  - `apps/execution/src/Infrastructure/Security/AmbientAuditContextAccessor.cs`,
    `apps/execution/src/Endpoints/Filters/ChangeReasonHeaderFilter.cs`, `apps/execution/src/Endpoints/SubjectEndpoints.cs`
  - `apps/web/src/api/changeReason.ts` and every web client that sets the header
  - `packages/testing/src/Security/TestSecurityContext.cs`
- **Verification Plan:**
  - `tests/Cadence.UnitTests/Packages/ChangeReasonHeaderTests.cs` checks the encoding, round trips in several scripts, and rejection of
    malformed values, with the same strings as `apps/web/tests/api/changeReason.test.js`.
  - `tests/Cadence.IntegrationTests/Execution/ChangeReasonEncodingTests.cs` sends a reason with an em dash and Japanese as a signed
    caller and checks that the audit entry stores exactly that text, for both header readers.
  - `tests/Cadence.UnitTests/Gateway/GatewayAuthenticationTests.cs` checks that the gateway forwards the canonical encoding, signs the
    decoded text, and answers `400` to a malformed value.
