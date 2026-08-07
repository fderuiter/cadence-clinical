# Challenge Report — Milestone M1: Foundational Core Utilities Migration

## Verdict: APPROVE

## Challenge Summary

**Overall risk assessment**: LOW

## Challenges

### [Low] Challenge 1: Stale Bytecode in `packages/core-models/__pycache__`
- **Assumption challenged**: Removing `.py` source files completely purges Python compiled bytecode.
- **Attack scenario**: Stale `.pyc` files remaining in `packages/core-models/__pycache__` (`audit.cpython-*.pyc`, `datetime_helpers.cpython-*.pyc`, `signature.cpython-*.pyc`) could theoretically cause import shadowing if sourceless import mechanisms or legacy Python import loaders are invoked.
- **Empirical stress test**: Executed empirical Python import tests targeting `packages.core_models.audit`, `core_models.audit`, and bare `import audit`. PEP 3147 standard import resolution correctly rejected sourceless pycache imports, raising `ModuleNotFoundError`.
- **Blast radius**: None at present runtime.
- **Mitigation**: Clean `__pycache__` directory during final M5 directory eradication.

## Stress Test Results

1. **Import Shadowing Verification**:
   - **Scenario**: Attempt importing `audit`, `signature`, `datetime_helpers`, `storage` from `packages.core_models` or top-level `sys.path`.
   - **Expected behavior**: `ModuleNotFoundError` raised.
   - **Actual behavior**: `ModuleNotFoundError` raised cleanly.
   - **Result**: PASS

2. **Static Analysis & Linting**:
   - **Scenario**: Run `uv run ruff check .` and `uv run ruff format --check .`.
   - **Expected behavior**: 0 lint errors, 0 unformatted files.
   - **Actual behavior**: 0 lint errors, 681 files checked and correctly formatted.
   - **Result**: PASS

3. **Code Duplication Check**:
   - **Scenario**: Run `python3 scripts/detect_duplication.py`.
   - **Expected behavior**: 0 duplicate blocks above 15-line threshold.
   - **Actual behavior**: SUCCESS, 0 duplicate blocks found.
   - **Result**: PASS

4. **Pydantic v2 Type & Validation Harness**:
   - **Scenario**: Instantiate `AwareDatetime`, `Part11AuditMixin`, `SignatureManifestation`, and `DocumentMetadataResponse` with valid and invalid (naive datetime) inputs.
   - **Expected behavior**: Naive datetimes rejected with `ValidationError`; UTC datetimes serialized with trailing `'Z'`.
   - **Actual behavior**: Naive inputs rejected; Z-serialization verified (`{"created_at":"2026-08-07T12:00:00Z"}`).
   - **Result**: PASS

5. **Full Pytest Suite**:
   - **Scenario**: Run `uv run pytest -n auto`.
   - **Expected behavior**: 169/169 tests pass.
   - **Actual behavior**: 169 passed in 26.24s.
   - **Result**: PASS

## Unchallenged Areas

- Domain models remaining in `packages/core-models/` (`eligibility`, `organization_domain`, `protocol_authoring`, `sdtm`, etc.) — Out of scope for M1; scheduled for migration in M2 and M3.
