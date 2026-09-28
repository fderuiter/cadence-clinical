# ADR-2241: Pin `uv` Python Preference to System for Python 3.14

- **Status:** Accepted
- **Date:** 2026-09-04
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Per PRD-SYS-053 (developer environment reproducibility), a clean checkout
must reach a working build/test loop deterministically in any supported
environment (local, Codespaces, devcontainer, CI, or an agent session).
ADR-2240 recorded, as accepted technical debt, that `cadence doctor` (and,
transitively, every `pydantic.BaseModel` in the workspace) crashed under
Python 3.14.0rc2 with a `pydantic`/`typing` incompatibility, and deferred a
fix pending "a `pydantic` version investigation." That investigation is now
done, and the incompatibility turned out to be environmental rather than a
version-compatibility gap that a dependency bump could close.

`packages/deid` crashes on import — reproducible with nothing more than
`python -m packages.deid.cli --help`, or even just defining a trivial
`class Foo(pydantic.BaseModel): value: str` — with:

```
TypeError: _eval_type() got an unexpected keyword argument 'prefer_fwd_module'
...
AssertionError
```

Tracing it down: `pydantic` 2.13's `_typing_extra._eval_type()` calls
`typing._eval_type(..., prefer_fwd_module=True)` on Python ≥ 3.14, matching
the `typing._eval_type` signature CPython 3.14 shipped with at general
availability. A pre-release build of CPython 3.14 (`3.14.0rc2`) had already
been downloaded into this workspace's `uv`-managed Python cache — from back
when `requires-python = ">=3.14"` was first pinned, ahead of the 3.14 GA
release — and that rc2 build's `typing._eval_type` does **not** accept
`prefer_fwd_module` (CPython changed that private signature between rc2 and
final). `uv`'s default `python-preference` ("managed") re-selects an
already-downloaded managed interpreter over a system one on every
subsequent `uv sync`/`uv run`, even when a newer, correct system Python
3.14 is later installed alongside it. So the stale rc2 build silently wins
forever, on every fresh session that inherits that cache, regardless of
which `pydantic` version is pinned — reproducing the exact
`cadence doctor` crash ADR-2240 flagged, on totally unrelated changes (a
docs-only PR triggered the report that led to this investigation).

Confirmed directly: `pydantic` 2.13.5 (current) crashes against a cached
`CPython 3.14.0rc2` build, and the identical `pydantic` 2.13.5 works
correctly against `CPython 3.14.6` (the current final release). No
dependency version in `uv.lock` needed to change.

## 2. Decision Drivers & Constraints

- The fix must be permanent: it must not depend on any single workspace's
  cache happening to be clean, since the same poisoned-cache condition can
  reappear on any machine or CI/agent container image that ever ran `uv`
  against Python 3.14 before it went final.
- Must not require bumping `pydantic` past what already works, since the
  currently pinned version is not the problem.
- A broken interpreter getting silently selected must become an
  immediate, actionable failure at setup time rather than a confusing
  `pydantic` stack trace surfacing deep inside an unrelated CI job or CLI
  invocation.
- Must keep working in environments with a real system Python 3.14 already
  on `PATH` (CI, via `actions/setup-python`) and in environments where one
  must first be installed (local/agent sandboxes).

## 3. Options Considered

### Option 1: Bump `pydantic` to a newer/older pin

- **Overview:** Try to find a `pydantic` release that tolerates the
  `prefer_fwd_module` mismatch.
- ***Pros:*** Looks like a one-line fix.
- ***Cons:*** ❌ Doesn't address the actual defect — the installed `pydantic`
  version already works fine against a real Python 3.14; the bug is which
  _interpreter_ `uv` hands it, not which `pydantic` is installed. Any pin
  chosen to paper over one broken pre-release interpreter would be wrong
  the moment the cache holds a different one, and does nothing to stop the
  cache from staying poisoned.

### Option 2: Pin `.python-version` to an exact patch (e.g. `3.14.6`)

- **Overview:** Replace the bare `3.14` pin with a concrete final version.
- ***Pros:*** Prevents `uv` from matching the cached rc2 build by version.
- ***Cons:*** ❌ Goes stale the moment a newer patch (`3.14.7`, ...) is the
  only one available on a given machine; every patch release would need a
  repo-wide edit. Doesn't stop the underlying cache-preference footgun from
  affecting any _other_ pinned version later downloaded pre-release.

### Option 3: `python-preference = "system"` + wizard self-heal (Selected)

- **Overview:** Tell `uv` to prefer a `PATH`-discovered system Python over
  any cached managed one (`pyproject.toml`), and make the wizard's Python
  stage confirm the interpreter `uv` actually resolved has
  `sys.version_info.releaselevel == "final"`. If it doesn't — no system
  Python 3.14 was available yet, so `uv` fell back to a managed download —
  install one via the OS package manager, evict the stale managed
  interpreter (`uv python uninstall`), and re-sync.
- ***Pros:***
  - ✅ Fixes the root cause (interpreter selection), not a symptom.
  - ✅ Self-healing: turns a silent, deferred failure into an immediate,
    diagnosable one, with a best-effort automatic fix.
  - ✅ Works unchanged as CPython 3.14 receives further patch releases —
    no version number to keep current in this repo.
  - ✅ CI already puts a correct final-release interpreter on `PATH` via
    `actions/setup-python@v7`; this change makes `uv` actually use it
    instead of any cache it (or a restored `actions/cache` entry) might
    hold.
- ***Cons:***
  - ❌ Relies on a system Python 3.14 existing or being installable
    (`apt`/`winget`/etc.); on a platform where neither is possible, the
    wizard now reports the failure loudly instead of silently succeeding
    with a broken environment — an intentional trade-off given the
    alternative was an undiagnosed crash later.

## 4. Decision Outcome

- **Chosen Option:** Option 3.
- **Justification:** It fixes the actual defect (uv resolving a poisoned,
  pre-release managed interpreter ahead of a correct system one) rather
  than working around one specific bad cached version, needs no future
  maintenance as Python 3.14 patch releases ship, and converts a deferred,
  hard-to-diagnose failure into an immediate one with automatic recovery
  where possible.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/deid`, `cadence doctor`, and every other
  `pydantic` model in the workspace import correctly again under Python
  3.14; the technical debt ADR-2240 recorded is resolved; future sessions
  that inherit a poisoned `uv`-managed Python cache self-heal during setup
  instead of failing deep inside an unrelated task.
- **Negative Impact / Technical Debt:** The self-heal step in
  `scripts/setup_wizard.sh` shells out to `apt-get` when available, adding
  a Debian/Ubuntu-specific remediation path; other platforms fall back to
  a clear warning rather than automatic recovery.
- **Mitigation Strategy:** The warning message names the exact remediation
  (install a system Python 3.14, or `uv python uninstall <bad-version>`)
  so a non-apt platform can still recover manually in one step.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `pyproject.toml` (`[tool.uv]
python-preference = "system"`), `scripts/setup_wizard.sh` (verify +
  self-heal the resolved Python 3.14 interpreter after `uv sync`).
- **Verification Plan:**
  - Reproduced the original crash: cached `CPython 3.14.0rc2` +
    `pydantic` 2.13.5 → `TypeError`/`AssertionError` on
    `python -m packages.deid.cli --help` and on a bare `BaseModel`.
  - Confirmed the same `pydantic` 2.13.5 works against real
    `CPython 3.14.6`.
  - Confirmed that with `python-preference = "system"` set, `uv sync
--python 3.14` resolves the real system interpreter even with a
    poisoned managed `3.14.0rc2` cached alongside it, and
    `uv run -m packages.deid.cli --help` / `uv run pytest packages/deid`
    (40 passed) succeed.
  - Exercised `scripts/setup_wizard.sh --dev --minimal`'s self-heal path
    against a simulated poisoned cache with no system Python 3.14 present:
    it installs one via `apt-get`, evicts the stale managed interpreter,
    and re-syncs to a final release; on a still-unrecoverable machine it
    reports a clear, actionable warning instead of leaving a silently
    broken environment.
