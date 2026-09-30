# ADR-2261: Native C# De-Identification Engine and Cryptographic Canonical Signing Foundation

Also known as: ADR-20260929 (former number)

- **Status:** Accepted
- **Date:** 2026-09-29
- **Authors:** @fderuiter
- **Deciders:** Architecture Review Board

---

## 1. Context & Problem Statement

To complete the decommissioning of Python execution runtime dependencies in favor of a native .NET 10 monorepo architecture, clinical data de-identification and DICOM privacy capabilities must run natively in `Cadence.Deid`. Previously, clinical DICOM header scrubbing, burned-in pixel masking, text transforms, surrogate air-gap vaulting, and signed audit manifests relied on Python services (`packages/deid`). This implementation satisfies HIPAA Safe Harbor and DICOM PS 3.15 Annex E requirements under PRD-SYS-051 and PRD-IMG-001.

## 2. Decision Drivers & Constraints

- **Driver 1:** 100% functional parity with Python `packages/deid` test suites across DICOM header sanitization, burned-in pixel masking, text transforms, surrogate air-gap token vaulting, and signed audit manifests (PRD-SYS-051, PRD-IMG-001).
- **Driver 2:** High-performance in-flight DICOM processing using `fo-dicom` with sub-10ms execution latency.
- **Driver 3:** Compliance with HIPAA Safe Harbor and DICOM PS 3.15 Annex E regulations without raw PHI leakage in audit logs or manifest metadata.
- **Driver 4:** Cryptographic integrity for redaction manifests and certificates via symmetric HMAC-SHA256 and asymmetric RSA-PSS/ECDSA signatures supported natively in `Cadence.Security` (PRD-SYS-053).

## 3. Options Considered

### Option 1: Sidecar Python De-identification Service

- **Overview:** Maintain Python `packages/deid` as an out-of-process gRPC/HTTP service.
- **Pros:**
  - ✅ Reuses existing Python code.
- **Cons:**
  - ❌ Requires active Python runtime in production deployments.
  - ❌ Increases inter-service latency and memory footprint.

### Option 2: Native .NET 10 C# De-identification Engine with fo-dicom (Selected)

- **Overview:** Fully port `packages/deid` to C# in `Cadence.Deid` leveraging `fo-dicom` 5.x and expanding `Cadence.Security` with canonical serialization and asymmetric RSA/ECDSA signing.
- **Pros:**
  - ✅ Eliminates external Python execution runtime dependencies.
  - ✅ In-process sub-millisecond execution for text transforms and DICOM streams.
  - ✅ Zero raw PHI leakage in signed redaction manifests and certificates.
- **Cons:**
  - ❌ Requires maintaining C# port parity with clinical regulatory rules.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing a native C# engine in `Cadence.Deid` provides optimal throughput, security, and full compliance while completing the runtime decommissioning of Python dependencies (PRD-SYS-051).

## 5. Consequences & Trade-offs

- **Positive Impact:** All DICOM and text de-identification runs natively in .NET 10 without inter-process calls.
- **Negative Impact / Technical Debt:** Requires maintenance of regex detector patterns and DICOM header tag lists directly in C#.
- **Mitigation Strategy:** Enforce unit test coverage in `Cadence.UnitTests` covering all 14 detector categories and DICOM sanitization rules.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `Cadence.Deid` (`packages/deid/src/`), `Cadence.Security` (`packages/security/src/`), `Cadence.UnitTests` (`tests/Cadence.UnitTests/Packages/DeidEngineUnitTests.cs`).
- **Verification Plan:** Unit tests in `Cadence.UnitTests` verify 100% pass rate across text transforms, DICOM header stripping, pixel masking, surrogate vaulting, and signed manifests.
