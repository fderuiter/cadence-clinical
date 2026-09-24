# Deid Package

## Overview

De-identification utilities package

---

## Architectural Boundaries

The `deid` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/deid/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- CADENCE_DICOM_UID_ROOT
- ComplianceProfile
- DEFAULT_DATE_SHIFT_DAYS
- DeidAirGapVault
- DeidDetector
- DetectionResult
- DetectorCategory
- DicomDeidCertificate

---

## Dependencies

This package relies on the following standard dependencies:
- `packages-security`
- `pydantic>=2.6.0`
- `python-dateutil>=2.8.2`
- `pyyaml>=6.0.1`
- `rapidfuzz>=3.14.5`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/deid
```
