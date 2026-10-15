# ADR-2515: Step-Up Tokens Are Single Use Across Instances and Restarts

- **Status:** Accepted
- **Date:** 2026-10-15
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter (pre-deployment containment brief, 2026-10-15)
- **Requirements:** SRS-SYS-005 (e-signature step-up, single use), SRS-EDC-003 (Part 11 signature manifest),
  PRD-TRN-003 (training electronic signatures)
- **Issues:** #5311, #5361 (signing routes only)

---

## 1. Context & Problem Statement

An e-signature needs a fresh step-up token (`X-Sig-Token`, contract v3, 60 seconds) that signs once
([Signature Token Cryptographic Contract](../SDLC/Signature_Token_Cryptographic_Contract.md)). Inspection of the code
at `bee6f37` found that the promise did not hold, and executed tests on PostgreSQL confirmed it:

- **Batch PI sign-off** (`/api/v1/execution/batch-sign-off` and its `signatures/` alias) skipped token verification
  when no token was sent, and signed every manifest `"mock-sig"` when the gateway secret was missing. A request with no
  token returned 200 and approved the forms.
- **Replay.** Execution built a new `SigTokenVerifier`, with a new in-memory replay cache, per request, so the same
  token signed again (a second request returned 200, on the same instance and on a second one; 7 of 8 concurrent
  requests succeeded).
- **Replay window.** Where the cache was shared, it forgot a token at `exp`, but the JWT handler accepted it until
  `exp + 5s` of clock skew, so the token could be replayed in those 5 seconds.
- **Delegation-log sign-off** (`/api/v1/training/delegations/bulk-signoff`) accepted any signed-in caller with a
  non-empty `password` string, took the signer and role from the body, sealed with an unkeyed SHA-256, and invented
  completed, PI-signed records for ids that did not exist.

A process-local cache cannot give single use when a host runs more than one instance or restarts inside a token's
window. Proving that needs a test across two hosts on one database, not an in-process test.

## 2. Decision Drivers & Constraints

- Every signing route verifies a token bound to the signer, the route, its semantic action and, for batches, the exact
  set of targets. There is no tokenless or placeholder path.
- The authority to sign comes from the verified caller (subject and roles), never the request body.
- Single use is claimed only as far as the use store is actually shared.
- Pre-deployment (2026-10-15): no cutover or compatibility for old clients is needed.

## 3. Options Considered

### Option 1: One shared verifier with a process-local cache

- **Overview:** register the verifier once per process so every request shares one cache.
- **Rejected:** it fixes replay only within one process. A restart or a second instance accepts the token again.

### Option 2: A durable use store behind an interface (selected)

- **Overview:** the verifier records each token's first use through `ISigTokenUseStore`. Hosts with a database
  register a store whose unique key makes the first use atomic everywhere that database is shared.

### Option 3: A distributed cache (Redis)

- **Rejected:** a new piece of infrastructure the platform does not otherwise need, for a guarantee PostgreSQL already
  gives.

## 4. Decision Outcome

### 4.1 Acceptance window

`SigTokenVerifier` checks lifetime itself on the injected `IClock` instead of the JWT handler's wall clock. A token is
accepted only while `iat - 5s <= now < exp + 5s` (and `now >= nbf - 5s`), and only if `exp - iat <= 60s`. The 5 seconds
(`SigTokenVerifier.ClockSkew`) absorbs drift between the issuing gateway and the verifying service.

### 4.2 Single use

The verifier consumes the token's `jti` through `ISigTokenUseStore.TryRecordFirstUseAsync(jti, exp + 5s)`, only after
every other check passes. The record is kept until the end of the acceptance window, so it can never be forgotten while
the token is still accepted.

- **Execution** registers `PostgresSigTokenUseStore`: an insert into `execution.consumed_signature_tokens`, whose
  primary key on the token id makes check-and-record one atomic step across instances and restarts. Expired rows are
  deleted on the next use.
- **Every other host** keeps the process-local `ReplayCache`, registered once as a singleton by `AddCadenceSecurity()`.
  That is single use within one process only. Those services gain the durable store when they fold into the single
  host (#5315); until then a restart or second instance of, for example, knowledge or eConsent can accept a token again
  inside its 70-second window. This is a known gap, not a guarantee.

### 4.3 Signing routes

- Batch PI sign-off requires a token bound to `/api/v1/execution/batch-sign-off`, `execution.form.approve` and
  `batch_id = sha256(study:TYPE:sortedIds:reason)`. The service also checks that the token's subject is the signer and
  its batch is the request's, and keys each manifest with the gateway secret, throwing when it is missing.
- Delegation-log sign-off requires `delegation_log:sign` (principal investigator, authorized ER physician and lead
  investigator only) and a token bound to `/api/v1/training/delegations/bulk-signoff`, `knowledge.delegation.sign_off`
  and `batch_id = sha256(sortedIds joined with ",")`. The signer is the verified caller, an unknown id fails the whole
  request with 404, and the seal is an HMAC keyed with the gateway secret. The body no longer carries a password, signer
  name or role.

## 5. Consequences & Trade-offs

- Clients must obtain a step-up token for each batch with the matching `semantic_action` and `batch_id`.
- A replayed or concurrent second use of an Execution token fails with 401 on any instance, which the PostgreSQL
  integration tests show across two hosts sharing one database.
- Other services' single use remains process-local until #5315. Docker-based multi-instance runs were not executed.
- Single-form approval (`/approve`) still expects the client to send an HMAC keyed with the gateway secret, which no
  client can hold. It is out of this decision and tracked as a follow-up.

## 6. Implementation & Verification

- `packages/security`: `SigTokenVerifier.VerifyAndConsumeAsync`, `ISigTokenUseStore`, `ReplayCache`,
  `ElectronicSignatureEndpointFilter` (binding resolved from the request) and `HttpContext.VerifiedSigToken()`.
- `apps/execution`: `PostgresSigTokenUseStore`, the `consumed_signature_tokens` migration, and the batch sign-off route
  and service.
- `apps/knowledge`: the delegation sign-off route; `PermissionMatrix` grants `delegation_log:sign`.
- Tests: `Cadence.IntegrationTests.Execution.BatchSignOffSignatureTests` (PostgreSQL, two hosts on one database),
  `Cadence.UnitTests.Execution.Endpoints.BatchSignOffEndpointsTests`,
  `Cadence.UnitTests.Knowledge.DelegationSignOffAuthorityTests` and `Cadence.UnitTests.Packages.SignatureTokenTests`.
