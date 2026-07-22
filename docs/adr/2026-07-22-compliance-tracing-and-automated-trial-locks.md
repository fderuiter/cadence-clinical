# ADR-014: 2026-07-22 Compliance Tracing and Automated Trial Locks

## Status
Accepted
* **Date:** 2026-07-22
* **Authors:** @google-labs-jules
* **Deciders:** @fderuiter

---

## Context
To bring the platform into complete alignment with global clinical regulations (21 CFR Part 11 and EU Annex 11), we need to establish database-level audit protections, multi-party cryptographic key splitting, automatic key rotation, and automated read-only trial locks upon safety compromises. This guarantees a zero-loss, tamper-proof audit trail and secure blinding mechanics.

## 2. Decision Drivers & Constraints
* **Driver 1:** Regulatory Compliance (21 CFR Part 11, EU Annex 11) requiring tamper-proof audit trails.
* **Driver 2:** Security and Blinding Mechanics requiring no single point of failure in treatment allocation.
* **Driver 3:** Patient Safety requiring immediate data freezing upon compromise.

## Alternatives Considered
### Option 1: Application-level auditing and soft locks
* **Overview:** Rely on application logic and soft-delete flags for auditing, with application-level checks for trial locking.
* **Pros:** 
  * ✅ Easier to implement.
* **Cons:** 
  * ❌ Susceptible to bypass via direct database access or bugs.
  * ❌ Does not fully satisfy strict regulatory requirements for tamper-proof logs.

### Option 2: Database-level audit protection with threshold cryptography and global locks
* **Overview:** Hard-delete blocks at the database flush listener level. Multi-party threshold cryptography for blinding keys. Immediate global read-only lock with multi-channel alerting.
* **Pros:** 
  * ✅ Meets stringent regulatory standards for immutable audit logs.
  * ✅ Eliminates single point of failure for unblinding.
  * ✅ Ensures rapid response to safety compromises.
* **Cons:** 
  * ❌ Increased complexity in database transaction management and cryptographic operations.

## Decision
* **Chosen Option:** Option 2
* **Justification:** Only Option 2 provides the necessary guarantees to meet 21 CFR Part 11 and EU Annex 11 compliance, ensuring that no manual intervention or application bug can bypass the audit trail or unblinding mechanisms.

## Trade-offs
* **Positive Impact:** Full regulatory compliance and enhanced security posture.
* **Negative Impact / Technical Debt:** Added complexity to database operations and key management.
* **Mitigation Strategy:** Extensive integration and unit testing to ensure smooth operations. Use of established cryptographic algorithms (Shamir's Secret Sharing) to minimize custom cryptography risks.

## 6. Implementation & Verification
* **Affected Repositories / Services:** Execution Service (`apps/execution`)
* **Verification Plan:** Unit and integration tests in `tests/test_cryptography.py` and `tests/test_trial_lock.py` to validate hard-delete blocks, key splitting, and read-only locks.
