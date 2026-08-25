# ADR-2218: Extract Core Workspace Package and Isolate Datetime Primitives

* **Status:** Accepted
* **Date:** 2026-08-25
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Previously, datetime validation primitives (`AwareDatetime`, `serialize_utc_z`, `validate_timezone_aware_datetime`) were housed within `packages/security/datetime_helpers.py`. This created an unnatural dependency where `packages/database` had to depend on `packages/security` solely for timezone-aware Pydantic validation helpers. To eliminate circular or unnecessary cross-package dependencies and maintain strict hexagonal boundary isolation across microservices and workspace packages, fundamental data primitives need to be extracted into a dedicated foundational core package (`packages/core`).

## 2. Decision Drivers & Constraints

* Eliminate direct dependency from `packages/database` on `packages/security`.
* Maintain 100% backward compatibility for downstream imports via re-exports from `packages.security` and `packages.database`.
* Enforce strict ISO-8601 UTC Z timezone serialization and validation across all Pydantic models (PRD-SYS-001).
* Maintain workspace package registration and dependency structure across `pyproject.toml` and `uv.lock`.

## 3. Options Considered

1. **Option A (Selected): Extract `packages/core` package.** Create a lightweight `packages/core` workspace package containing baseline datetime primitives (`AwareDatetime`, `serialize_utc_z`, `validate_timezone_aware_datetime`) and re-export them in legacy modules for backward compatibility.
2. **Option B: Duplicate datetime helpers across security and database packages.** Leads to code drift, violation of DRY principles, and potential scanner flags.

## 4. Decision Outcome

Chosen option: **Option A**. Extracting `packages/core` provides a clean workspace hierarchy where low-level domain primitives reside in `packages/core`, allowing both `packages/security` and `packages/database` to depend on `packages/core` without relying on each other for fundamental types.

## 5. Consequences & Trade-offs

* **Positive:** Uncouples `packages/database` from `packages/security`; enforces clean dependency hierarchy (`core` -> `database`/`security` -> `apps`).
* **Positive:** Re-exports guarantee 100% backward compatibility for legacy imports across all microservices.
* **Negative:** Introduces an extra package directory (`packages/core`) to manage in `pyproject.toml` workspace sources.

## 6. Implementation & Verification

* Package created at `packages/core` with `pyproject.toml` (`packages-core`).
* Registered `packages-core` as a workspace package in root `pyproject.toml` and as a dependency in `packages/database` and `packages/security`.
* Baseline datetime logic moved to `packages/core/datetime_helpers.py` and re-exported from `packages.security.datetime_helpers` and `packages.database.datetime_helpers`.
* Verified with automated unit tests in `packages/core/tests/test_datetime_helpers.py`.
