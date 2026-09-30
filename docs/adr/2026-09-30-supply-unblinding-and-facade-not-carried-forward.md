# ADR-2520: The Supply Unblinding Engine and Facade Are Not Carried Forward

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SUB-005 (emergency unblinding), PRD-SCM-001 (multi-echelon supply hierarchy)
- **Issue:** #5046 (part of #4789)

---

## 1. Context & Problem Statement

`packages/engine_core/supply/` is a Python behavioural reference for four pieces. Each needs a tested C# port, or a
record that it is not carried forward, before it can be deleted:

- `hierarchy.py`: validation and roll-up of the multi-echelon depot network.
- `engine.py`: the `PredictiveSupplyEngine` facade.
- `unblinding.py`: the `EmergencyUnblindingEngine`.
- `models.py`: the models behind them.

The hierarchy is ported as `MultiEchelonHierarchyEngine` (#5046). DTP dispatch and MKT (`DTPLogisticsEngine`) and
forecasting (`PredictiveDemandForecastingEngine`) were already ported. That leaves the facade and the unblinding
engine.

`EmergencyUnblindingEngine.execute_unblinding` authorised whatever role string the request carried, against a list of
six roles. It accepted a 10-character reason, read the wall clock, and returned a SHA-256 of the request fields
called a "digital seal". Nothing signed that seal, and anyone holding the fields could compute it.

Emergency unblinding already exists in Execution. It is the `UNBLINDED` subject state change. After #4982 (#5003) it
takes the role from the caller's verified claims, admits only the principal investigator and the authorised ER
physician (PRD-SUB-005), requires a reason of at least 50 characters, and records who broke the blind, why and when.
The kernel's target is stricter still (spec §7.4 and Appendix A.1). Unblinding becomes an Attestation-bearing event
plus an RTSM Assertion. Authorisation checks the `enrollment.unblind` capability in the kernel command pipeline
(K-AUTH-1), and blinding is a condition, not a role (K-AUTH-4).

## 2. Decision Drivers & Constraints

- There is one way to break the blind, and it follows PRD-SUB-005 and #4982.
- Roles come from verified claims, never from a request body (AGENTS.md §5).
- An integrity value must be verifiable against a key. A bare hash of the request fields is not a signature.
- K-TIME-3: no wall-clock reads.
- K-AUTH-1 and K-AUTH-4 are the target for unblinding authorisation.

## 3. Options Considered

### Option 1: Port `EmergencyUnblindingEngine` as written

- **Overview:** Add a C# engine with the same six roles, the 10-character reason and the SHA-256 seal.
- **Pros:**
  - ✅ Nothing in the Python reference is lost.
- **Cons:**
  - ❌ It creates a second unblinding path that admits four roles PRD-SUB-005 forbids and reasons that #4982 rejects.
  - ❌ It trusts the role in the request.
  - ❌ It presents an unsigned hash as a seal.

### Option 2: Port the rules into Execution's unblinding

- **Overview:** Merge the Python role list and seal into the existing state change.
- **Pros:**
  - ✅ There is still one path.
- **Cons:**
  - ❌ The Python rules are weaker than the ones Execution enforces now, so merging them would loosen #4982.

### Option 3: Do not carry the unblinding engine or the facade forward (selected)

- **Overview:** Execution's `UNBLINDED` state change stays the only emergency unblinding. The kernel Attestation and
  RTSM Assertion (spec Appendix A.1) replace it. The Python engine and the facade are not ported. The unused C#
  `EmergencyUnblindingRequest` and `EmergencyUnblindingResult` shapes are marked `[Obsolete]` so that nothing new
  builds on them.
- **Pros:**
  - ✅ There is one unblinding path, and it enforces PRD-SUB-005.
  - ✅ No unsigned "seal" and no role taken from a request.
- **Cons:**
  - ❌ The Python role list is not kept anywhere. It contradicted PRD-SUB-005, so that loss is intended.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** Porting the engine, or merging its rules, would weaken the control that #4982 fixed. The facade
  (`PredictiveSupplyEngine`) only forwarded calls to the four engines. C# callers use `MultiEchelonHierarchyEngine`,
  `PredictiveDemandForecastingEngine` and `DTPLogisticsEngine` directly, and a static class that forwards to other
  static classes adds nothing to test.

## 5. Consequences & Trade-offs

- **Positive Impact:** `supply/engine.py`, `supply/hierarchy.py`, `supply/models.py` and `supply/unblinding.py` can
  move to "Ported, deletion pending" in the Python reference inventory.
- **Negative Impact / Technical Debt:** The obsolete unblinding shapes remain in `SupplyModels.cs` until the Python
  files are deleted. Removing them needs the maintainer's approval, like the file deletion.
- **Mitigation Strategy:** `[Obsolete]` points any new use at this ADR, and warnings are errors, so code that uses
  the shapes does not build. The kernel Attestation work (#4719) replaces Execution's state change.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/engine_core` (`Supply/MultiEchelonHierarchyEngine.cs`,
  `Supply/SupplyModels.cs`) and `docs/PYTHON_REFERENCE_INVENTORY.md`.
- **Verification Plan:** `MultiEchelonHierarchyEngineTests` pins the hierarchy port to `hierarchy.py`. Execution's
  existing unblinding tests (#4982) cover the path that stays.
