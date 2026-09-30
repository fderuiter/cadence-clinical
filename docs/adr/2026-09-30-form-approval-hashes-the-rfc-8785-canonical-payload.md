# ADR-2513: Form Approval Hashes the RFC 8785 Canonical Payload

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** SRS-EDC-003 (PI approval of a form with a Part 11 signature); related: PRD-DOC-003 (a signature bound to the content hash, here for form payloads instead of files)

---

## 1. Context & Problem Statement

A PI approves a completed form by sending a signature manifest whose `payload_hash` must match the form payload
(`FormSubmissionApplicationService.ApproveAsync`). The service hashed the payload text read back from PostgreSQL. The
payload is stored as `jsonb`, which reorders keys (by length, then bytes), rewrites spacing and can change how a number
is spelled (`1.2e2` becomes `120`). A client that hashed the payload it submitted always got `INVALID_PAYLOAD_HASH`
(#5023); the only working client read the stored text back and hashed that, so the signature was bound to a storage
detail rather than to the form content.

## 2. Decision Drivers & Constraints

- **Client-computable:** a browser must be able to compute the hash from what it submitted, with no round trip.
- **Content-bound:** any change to a value, key or array element must change the hash.
- **Standard:** use a published canonical form, not a Cadence-specific one.
- **No regression for kernel hashes:** protocol, standard and library hashes (`ContentHash`) stay as they are.

## 3. Options Considered

### Option 1: Hash the stored text (status quo)

- **Cons:**
  - ❌ Clients cannot compute it without fetching the stored payload, and it changes if the storage format does.

### Option 2: Reuse `ContentHash` / `CanonicalSigner` canonical form

- **Cons:**
  - ❌ Keeps number text as written. `jsonb` rewrites some numbers, and `JSON.stringify` writes `1.50` as `1.5`, so the
    two sides can still disagree.

### Option 3: RFC 8785 (JCS) on both sides (Selected)

- **Overview:**
  - `Cadence.Kernel.Trust.JsonCanonicalization` implements JCS: members sorted by UTF-16 code units at every depth, no
    whitespace, `JSON.stringify` string escapes, numbers written as ECMAScript writes an IEEE 754 double. Duplicate
    member names and numbers outside the double range are refused.
  - The payload hash is the lower-case hex SHA-256 of the canonical form. The server canonicalizes the stored payload;
    the client canonicalizes what it submitted (`canonicalSerialize` in `packages/ui/signing.js` already produces JCS
    for parsed JSON).
- **Pros:**
  - ✅ Key order, spacing and number spelling no longer matter; content changes still do.
  - ✅ Verified against the RFC 8785 examples and against PostgreSQL.
- **Cons:**
  - ❌ Numbers beyond double precision canonicalize to their nearest double, as they would in a browser.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** It is the standard canonical form that a browser's `JSON.stringify` already matches, and it binds
  the signature to the form content instead of its storage.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Clients hash the payload they submitted; the integration tests no longer read the stored text back.
- **Negative Impact / Technical Debt:**
  - A client that hashed the raw stored text gets `INVALID_PAYLOAD_HASH` when that text differs from its canonical form.
    No such client exists outside the tests.
  - The kernel now has two canonical JSON forms. `ContentHash` stays for existing hashes; new hashes a client must
    reproduce use `JsonCanonicalization`.

## 6. Implementation & Verification

- **Affected code:**
  - `packages/kernel/src/Cadence.Kernel.Abstractions/Trust/JsonCanonicalization.cs`
  - `apps/execution/src/Application/Forms/FormSubmissionApplicationService.cs` (`ComputePayloadHash`)
- **Verification:**
  - `tests/Cadence.UnitTests/Kernel/JsonCanonicalizationTests.cs` checks the RFC 8785 number, string and sorting examples.
  - `tests/Cadence.IntegrationTests/Execution/ExecutionWriteAuthorizationTests.cs` approves a form on PostgreSQL with the
    hash of the submitted text, and refuses a hash of other content.
