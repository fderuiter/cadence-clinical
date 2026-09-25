# 00. Phase 0 Validation Gate — Audit Report

**Document ID:** PHASE0-NET10-00-VALIDATION
**Auditor role:** Independent Principal Systems Forensic Auditor / Quality Gatekeeper
**Audit date:** 2026-09-03
**Method:** Every finding below was produced by a shell command executed in this session against the actual repository state (not inferred from the Phase 0 documents' own prose). Full commands and raw output are included per gate so every number is reproducible. Where a Phase 0 document's own methodology differs from this prompt's literal instructions, both results are shown and the discrepancy is explained rather than silently resolved in either direction.

---

## Audit Summary Scorecard

| Quality Gate | Status | Critical Findings / Metrics |
|---|---|---|
| Gate 1: Artifact Completeness | **PASS** | All 5 required files present, executable bit set on `04_scaffold_solution.sh`, zero placeholder tokens (`TODO`/`TBD`/`<replace_me>`/etc.) found in any of the 5 documents |
| Gate 2: Zero-Orphan Math Test | **PASS** (bucket-level) / **caveat** | 1,675/1,675 `.py` files independently re-enumerated and reconciled to a bounded context or cross-cutting bucket with an exact, reproducible sum match. The *literal* per-file `comm -23` this prompt specifies reports 1,652 "orphans" because the source documents map coverage at directory-bucket granularity, not per-file — see §2 for why that is not a real gap |
| Gate 3: Dynamic Surface & Secrets | **FAIL (partial)** | Dynamic-loading (`importlib`) and background/concurrency surfaces are thoroughly documented with real mitigation notes. Environment-variable parity is **not**: only 93/180 (52%) of environment variable names actually read by the Python codebase appear anywhere in `01_system_topology.md`, including 4 credential/key-bearing variables and 10 regulatory AS2-gateway endpoint/certificate variables that are undocumented outright |
| Gate 4: Topological DAG & Slice 01 | **FAIL** | Static Python-import graph is acyclic (0 cross-app imports in production code — confirmed). But at the **runtime dependency** level (the level that actually determines deployability), Slice 01 (`Cadence.Apps.Execution`) has a confirmed **bidirectional, same-tier** HTTP dependency cycle with Slice 02 (`Cadence.Apps.Designer`). Slice 01 is not a true leaf; the migration plan documents this (MIG-03) but does not resolve it |
| Gate 5: .NET 10 Build & Standards | **FAIL** | `dotnet build Cadence.sln --configuration Release` fails with **13 errors, 0 warnings** — every one of the 10 restorable projects fails to restore. Root cause: `Directory.Packages.props` pins exact preview package versions (`10.0.0-preview.1.25120.13`) that no longer exist on nuget.org, plus one package (`ArchUnitNET.xUnit 0.11.1`) that does not exist on nuget.org at all, plus a known high-severity vulnerability advisory on a transitive package, all promoted to build errors by this repo's own `TreatWarningsAsErrors=true` policy. `Directory.Build.props` and Central Package Management configuration are themselves correct |

**Overall verdict: PHASE 1 BLOCKED.** See §6 for the full blocker list and exact remediation.

---

## 1. Gate 1 — Artifact Completeness & Depth

```
$ ls -la docs/migration/phase0/
01_system_topology.md      48879 bytes
02_bounded_contexts.md     17643 bytes
03_migration_plan.md       15547 bytes
04_scaffold_solution.sh    10552 bytes  -rwxr-xr-x  (executable ✓)
05_orphan_audit_gate.md    13185 bytes

$ rg -n -i "TODO|TBD|<replace_me>|\betc\.\b|FIXME|XXX" docs/migration/phase0/*.md
(no matches)
```

- `01_system_topology.md` — confirmed to contain: entry-point inventory (§2, HTTP/async-worker/scheduler/CLI/scripts/CI), datastores (§3.1 persistence engines per service), an explicit environment-variable dictionary (§3.5, subsections 3.5.1–3.5.6), and a dedicated dark-debt/dynamic-behavior audit (§4). Substantive, not a stub.
- `02_bounded_contexts.md` — confirmed to contain a full context catalog (§1) with an **Inbound Callers (verified)** / **Outbound Deps (verified)** column pair for every row, plus a dedicated circular-dependency section (§4.2) — see Gate 4.
- `03_migration_plan.md` — confirmed to contain a sequenced migration table (Phase/Slice ID/Domain/Files/Complexity/Blast Radius/Blockers, SLICE-00a through SLICE-18) and a separate cross-cutting blocker table (MIG-01 through MIG-10).
- `04_scaffold_solution.sh` — present, executable, idempotent (checks for existing `.csproj` before creating). Notably, the script's own header **already discloses** that it was authored but never executed end-to-end because the .NET SDK could not be installed in the environment that produced it — this session closes that gap (see Gate 5).
- `05_orphan_audit_gate.md` — documents codebase file coverage at directory-bucket granularity with a reproducible `find | wc -l` count and per-bucket table.

No placeholder text found anywhere. **Gate 1: PASS.**

---

## 2. Gate 2 — Zero-Orphan Math Test

### 2a. Literal reproduction of this prompt's specified method

```
$ find . -type f -name "*.py" -not -path "*/.*/*" -not -path "*/venv/*" \
    -not -path "*/.venv/*" -not -path "*/__pycache__/*" -not -path "*/site-packages/*" \
    -not -path "*/node_modules/*" | sort | wc -l
1675

$ grep -oE '[A-Za-z0-9_./-]+\.py' docs/migration/phase0/02_bounded_contexts.md docs/migration/phase0/05_orphan_audit_gate.md | sort -u | wc -l
31   # individual file paths named literally in either document

$ comm -23 all_python_files.txt mapped_python_files.txt | wc -l
1652  # "orphans" under a literal per-file join
```

Taken at face value, this looks like a catastrophic 98.6% coverage failure. **It is not.** `05_orphan_audit_gate.md` states its method explicitly in §1: it does not enumerate all 1,675 files by name — it assigns every file to one of ~38 directory buckets (`apps/execution`, `packages/security`, `scripts`, etc.), and every bucket to exactly one bounded context or cross-cutting concern. A per-file grep against the document text was never going to find most of the 1,675 file paths, because the document was never written to contain them individually — it is a legitimate, standard way to certify coverage over a codebase this size, not an evasion.

### 2b. Independent re-verification of the bucket-level method actually used

```
$ awk -F/ '{ if ($1=="apps" || $1=="packages") print $1"/"$2; else print $1 }' \
    all_python_files.txt | sort | uniq -c
```
Reproduced independently in this session — **identical** to the table in `05_orphan_audit_gate.md` §2, bucket-for-bucket and count-for-count. Sum = 1,675, matching the raw `find` count exactly.

```
$ comm -23 <(reproduced bucket list) <(buckets named in §2/§3 of 05_orphan_audit_gate.md)
(empty)
```
Every one of the ~38 buckets that exists on disk is named and assigned to a context or cross-cutting concern in the document. No bucket is missing.

**Conclusion:** at the granularity the source documents actually use and disclose, coverage is exact and mechanically verified — 1,675/1,675, 0 orphaned buckets. **Gate 2: PASS**, with the above caveat recorded so a future reviewer does not misread the literal-script result as a real gap.

---

## 3. Gate 3 — Dynamic Runtime & Secret Surface Cross-Check

### 3a. Dynamic invocations

```
$ rg -c "(importlib|__import__|getattr\(|setattr\(|eval\(|exec\(|metaclass)" --type py
1120 matches across 170 files (getattr: 940, setattr: 140, importlib: 25, __import__: 3,
                                 eval(: 3, exec(: 11, metaclass: 0)
```

The raw count is dominated by ordinary `getattr`/`setattr` idiom (1,080 of 1,120 matches), which is not metaprogramming risk and is not what the topology doc claims to catalog. The literal `eval(`/`exec(` matches were manually inspected and are **all false positives** of the regex (`test_..._retrieval()`, `asyncio.create_subprocess_exec(...)`) — zero actual `eval()`/`exec()` builtin calls exist in the repository, which matches `01_system_topology.md` §4.2's explicit claim ("No `eval()`/`exec()` in production code, and no metaclasses anywhere in the repository").

The genuinely risky subset — `importlib`-based dynamic module loading — **is** documented with real file:line references and explicit mitigation/consequence notes in `01_system_topology.md` §4.1 (4 confirmed sites: `apps/execution/database/models/__init__.py`, `packages/security/audit_logger.py`, `apps/gateway/main.py`, plus two CI-tooling duplicates). This section correctly identifies that `packages/security`'s upward `importlib` reflection breaks its "dependency-free leaf" status — an important, correct finding that is carried forward as blocker MIG-02.

**Dynamic invocations sub-check: PASS.**

### 3b. Environment variable parity — **the real gap**

```
$ rg -o -N "(os\.environ(\.get)?\[?['\"][A-Za-z0-9_]+['\"]|os\.getenv\(['\"][A-Za-z0-9_]+['\"])" --type py \
    | grep -oE "['\"][A-Za-z0-9_]+['\"]" | tr -d "'\"" | sort -u | wc -l
180   # unique env var names actually read by the Python codebase

$ # cross-referenced against every whole-word occurrence anywhere in 01_system_topology.md
93 documented / 180 found  →  87 not found anywhere in the document (48%)
```

Filtering out variables whose only usages are in `scripts/` (explicitly out-of-migration-scope per the document's own Gate-2 categorization) or `tests/`-only fixtures still leaves **65 environment variables read by in-scope `apps/` or `packages/` production code that do not appear anywhere in `01_system_topology.md`**, including:

- **Credential/key material:** `KEYCLOAK_CLIENT_SECRET` (`apps/gateway`), `GXP_SIGNING_PRIVATE_KEY`, `GXP_SIGNING_CERT`, `GXP_SIGNER_ID` (`packages/compliance`), `CDISC_LIBRARY_API_KEY` (`apps/gateway`), `PSEUDONYMIZATION_SALT` (`apps/interop`)
- **Five regulatory-authority AS2 gateway endpoints and their client certs**, entirely undocumented: `EMA_AS2_URL`/`EMA_GATEWAY_CERT`, `FDA_ESG_AS2_URL`/`FDA_ESG_CERT`, `HC_AS2_URL`/`HC_CERT`, `MHRA_AS2_URL`/`MHRA_CERT`, `PMDA_AS2_URL`/`PMDA_CERT` (all `apps/safety`)
- **Operational tuning knobs with GxP process implications:** `OUTBOX_ATTEMPT_CAP`, `OUTBOX_BATCH_SIZE`, `OUTBOX_MAX_CONCURRENCY`, `OUTBOX_POLL_INTERVAL_SECONDS`, `SEALER_INTERVAL_SECONDS`, `ETMF_SEALER_INTERVAL_SECONDS`, `ETMF_STRICT_ARCHIVAL`, `ECONSENT_ARCHIVAL_ATTEMPT_CAP`, `ETMF_EXPIRATION_SCANNER_INTERVAL_SECONDS`, `ETMF_EXPIRATION_WARNING_WINDOWS`, `QUERY_ESCALATION_INTERVAL_SECONDS`, `QUERY_ESCALATION_DIGEST_SECONDS`, `TICKETS_ESCALATION_INTERVAL_SECONDS`, `NOTIFICATION_MAX_ATTEMPTS`/`NOTIFICATION_RETRY_BASE_DELAY`/`NOTIFICATION_RETRY_MAX_DELAY`
- **AI-tier model routing:** `AI_TIER_1_MODEL`, `AI_TIER_2_MODEL`, `AI_TIER_3_MODEL`, `AI_TIER_1_EMBED_MODEL` (`apps/ai_gateway`)
- **Storage/cloud config:** `AWS_REGION`, `STORAGE_REGION`, `STORAGE_SSE_ALGORITHM`, `GOOGLE_APPLICATION_CREDENTIALS`, `GOOGLE_DRIVE_FOLDER_ID`, `GOOGLE_DRIVE_SERVICE_ACCOUNT_PATH`
- **Other API-surface config:** `ALLOWED_ORIGINS`, `RATE_LIMIT_MAX_REQUESTS`, `RATE_LIMIT_WINDOW`, `WEBHOOK_SIGNATURE_HEADER`, `SMTP_PORT`/`SMTP_USERNAME`/`SMTP_SENDER`/`SMTP_USE_TLS`/`SMTP_USE_SSL` (this last group is *partially* addressed — the doc documents `SMTP_HOST`/`PASSWORD` individually but collapses the rest into a `SMTP_HOST/PORT/USERNAME/...` shorthand that a strict per-name check does not credit), `NCI_EVS_TIMEOUT_CONNECT`/`_POOL`/`_READ`/`_WRITE`, `CODING_CACHE_TTL`, `LAB_RANGE_CACHE_TTL`, `STUDY_REGISTRY_URL`, `SAFETY_DB_INGESTION_ENDPOINT`, `SAFETY_DB_TRANSMISSION_ENDPOINT`, `ETMF_FORWARDING_ENABLED`, `ETMF_NOTIFICATIONS_CLIENT_TIMEOUT`, `ANOMALY_WORKER_POLL_INTERVAL_SECONDS`, `ALLOW_MOCK_SIGNATURES`

For context: `01_system_topology.md` §3.5.6 does separately catalog "37 live variables absent from `.env.example`" — a real and useful piece of forensic work — but that list is a *different* set from the 65 above (checked: all 37 already appear as documented text in the doc itself). The 65 above are variables missing from the environment-variable dictionary altogether, not just from `.env.example`.

**Environment variable parity sub-check: FAIL.** ~52% documented, with security-sensitive and regulatory-integration variables among the gaps.

### 3c. Background & concurrency operations

```
$ rg -c "(subprocess\.Popen|subprocess\.run|threading\.Thread|concurrent\.futures|@celery|@app\.task|apscheduler)" --type py
144 matches across 60 files
```

Cross-referenced against `01_system_topology.md` §2.2 (asynchronous/background workers — a full table of every `asyncio`-task worker per service, file:line, and purpose) and §4.3 (native binaries/subprocess sites — a full table of every `subprocess`/daemon invocation with binary and owning service). Every production background-job and subprocess site this session found is accounted for in one of those two tables; the remaining files in the raw match list are CLI tooling (`packages/cli/commands/*.py`) and `scripts/` (explicitly cross-cutting/not-migrated), which the document correctly excludes from the bounded-context worker inventory.

**Background/concurrency sub-check: PASS.**

**Gate 3 overall: FAIL**, driven entirely by environment-variable parity (§3b). The dynamic-loading and concurrency sub-checks are solid work and should not be re-done — only the environment-variable dictionary needs backfilling.

---

## 4. Gate 4 — Dependency Graph & Topological Sort Audit

### 4a. Acyclic verification — static import level

```
$ for each apps/<context>/: rg "^(from|import) apps\.<other-context>" --type py -g '!*/tests/*'
(zero matches, all contexts)
```
Confirmed independently: **zero cross-app Python `import` statements exist in production code anywhere in the repository.** (32 cross-app imports were found in total, but every single one is confined to a `tests/` directory — test fixtures importing another service's DB models/app object for integration test setup, not production coupling.) This matches `01_system_topology.md` and `05_orphan_audit_gate.md`'s own claim and is the correct basis for treating the *static* dependency graph as acyclic.

### 4b. The runtime dependency graph is not acyclic — and this is where the real risk is

A DAG built only from `import` statements is the wrong instrument for this codebase: every cross-service coupling in this system is a runtime HTTP call (`os.getenv("X_URL")` + `httpx`), not a Python import, precisely because these are independently deployable FastAPI services today. `02_bounded_contexts.md` §4.2 already identifies this and finds **three bidirectional service pairs**, reproduced and confirmed in this session by direct inspection:

| Pair | Forward edge | Reverse edge | Tier relationship | Risk |
|---|---|---|---|---|
| Execution ↔ CTMS | `apps/execution/greenlight_gating.py:77` (`CTMS_SERVICE_URL`) | `apps/ctms/application/doa_service.py:15` etc. (`EXECUTION_URL`) | Different tiers (2 vs 3) | Lower — later tier can build against a frozen contract |
| Execution ↔ Etmf | `apps/execution/workers/outbox_worker.py:132` (`ETMF_URL`) | `apps/etmf/adapters/lock_client.py:60` (`EXECUTION_URL`) | Different tiers (2 vs 3) | Lower — same reasoning |
| **Execution ↔ Designer** | `apps/execution/designer_client.py:31` (`os.getenv("DESIGNER_URL")`), consumed by `apps/execution/eligibility_service.py` | `apps/designer/presentation/routers/designer_routes.py:2618` (`os.getenv("EXECUTION_SERVICE_URL")`) | **Both Tier 1/2 — same tier** | **Genuine cycle** |

Confirmed directly in this session:
```
$ rg -n 'os.getenv("DESIGNER_URL"' apps/execution/designer_client.py
31:    self.base_url = (base_url or os.getenv("DESIGNER_URL") or "http://localhost:8001").rstrip("/")

$ rg -n "EXECUTION_SERVICE_URL" apps/designer/presentation/routers/designer_routes.py
2618:            execution_url = os.getenv("EXECUTION_SERVICE_URL", "http://localhost:8001")
```

### 4c. Slice 01 True Leaf Test — result

`03_migration_plan.md` designates **SLICE-01 = `Cadence.Apps.Execution`** as the first Phase 1 slice, and `05_orphan_audit_gate.md` §6 explicitly recommends it. Per this gate's own rule ("Slice 01 may only depend on low-level utilities or third-party packages; if it imports another business context, the sequence is flawed"):

- A naive static-import check would report Slice 01 clean (0 `import apps.designer` statements) — **this would be a false pass.**
- The actual runtime dependency graph shows Slice 01 has a live, bidirectional HTTP dependency on Slice 02 (Designer), which is scheduled in the **same tier** and is **not yet migrated** when Slice 01 kicks off. Execution literally cannot serve eligibility-criteria requests without Designer being reachable, and Designer's protocol-amendment publish flow calls back into Execution.
- `03_migration_plan.md`'s own blocker table lists this exact fact as **MIG-03** ("Decide Execution↔Designer same-tier sequencing") against SLICE-01 and SLICE-02 — so the plan is not unaware of the problem. But MIG-03 is listed as an open decision ("parallel with contract-freeze, or explicit sub-order"), not a resolution, and Slice 01 is still named the lead slice without that decision having been made.

**Gate 4 verdict: FAIL under the strict leaf-test rule.** Slice 01 is not a true leaf. This does not necessarily mean Execution is the wrong slice to start with — its centrality is exactly why the plan wants it first — but Phase 1 cannot begin execution on Slice 01 with "no upstream business-context dependency" as an assumption. The MIG-03 decision must be made and recorded (either: (a) freeze the Execution↔Designer HTTP contract in `Cadence.Contracts` and migrate both in parallel, or (b) explicitly sub-sequence one ahead of the other and stub the other side) **before** Slice 01 work starts, not during it.

---

## 5. Gate 5 — .NET 10 Baseline Build & Tooling Gate

The .NET 10 SDK (10.0.100, pinned in `global.json`) was not present in this environment at audit start (`05_orphan_audit_gate.md` §4 documents the same absence in the environment that produced Phase 0, and explicitly flags the scaffold as "authored and reasoned about, not build-verified"). **This session installed the SDK** (`dotnet-install.sh --channel 10.0 --version 10.0.100`) specifically so this gate could be executed for real rather than deferred again.

### 5a. Build validation — FAIL

```
$ dotnet build Cadence.sln --configuration Release --verbosity normal
...
    0 Warning(s)
    13 Error(s)
Build FAILED.
```

All 10 restorable `.csproj` files in the solution fail to restore. Root causes, all confirmed by direct inspection of `Directory.Packages.props`:

1. **NU1603 (Warning-as-error) — stale exact preview pins, 4 packages:**
   ```
   <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0-preview.1.25120.13" />
   <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0-preview.1.25120.13" />
   <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0-preview.1.25120.13" />
   <PackageVersion Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.0-preview.1.25120.13" />
   <PackageVersion Include="Microsoft.Extensions.Http.Polly" Version="10.0.0-preview.1.25120.13" />
   ```
   None of these exact builds exist on nuget.org any longer (`preview.2.25163.x`/`preview.2.25164.x` builds are what's actually live). NuGet resolves the nearest available version and, under this repo's `TreatWarningsAsErrors=true`, that resolution-mismatch warning becomes a hard error — 6 of the 13 errors are this.

2. **NU1101 — nonexistent package:**
   ```
   <PackageVersion Include="ArchUnitNET.xUnit" Version="0.11.1" />
   ```
   `tests/Cadence.ArchitectureTests.csproj : error NU1101: Unable to find package ArchUnitNET.xUnit. No packages exist with this id in source(s): nuget.org` — this package/version does not exist on nuget.org under this exact ID at all (the correct package id at the time of this audit is `ArchUnitNET.xUnitV3` or `ArchUnitNET` + a separate xUnit adapter, depending on target xUnit version — needs a maintainer decision, not a version bump).

3. **NU1903 (Warning-as-error) — known vulnerability:**
   ```
   error NU1903: Warning As Error: Package 'Microsoft.OpenApi' 2.0.0-preview7 has a known high severity
   vulnerability, https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
   ```
   A transitive dependency of `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi`, flagged 3 times across affected projects.

**Net result: 0 of the 10 restorable projects build. The Cadence.sln baseline this Phase 0 effort was supposed to hand off is not currently buildable**, confirming — with hard evidence — the caveat `05_orphan_audit_gate.md` itself raised but could not check.

### 5b. Global rules inspection — PASS

```
$ cat Directory.Build.props
<TargetFramework>net10.0</TargetFramework>        ✓
<LangVersion>14</LangVersion>                       ✓ (matches "14 or preview")
<Nullable>enable</Nullable>                         ✓
<TreatWarningsAsErrors>true</TreatWarningsAsErrors> ✓
<AnalysisLevel>latest-recommended</AnalysisLevel>   ✓
```
All five required global rules are present and correctly set.

### 5c. Central Package Management — PASS

```
$ grep ManagePackageVersionsCentrally Directory.Packages.props
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>   ✓

$ grep -rn 'PackageReference.*Version=' --include="*.csproj" .
(no matches — zero hardcoded versions in any .csproj)                   ✓
```

**Gate 5 verdict: FAIL**, but narrowly and mechanically — the *standards enforcement machinery* (TFM, nullable, warnings-as-errors, analyzer level, CPM) is all correctly wired. The failure is entirely in the specific package version pins, which is a small, well-scoped fix (see §6).

---

## 6. Remediation

### Required before Phase 1 (blocking)

1. **Fix `Directory.Packages.props` pins** (Gate 5): re-pin `Microsoft.AspNetCore.OpenApi`, `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.Extensions.Http.Polly` to a `10.0.0-preview.2.*`/current-live build (confirm exact build via `dotnet package search Microsoft.EntityFrameworkCore --prerelease` against nuget.org at fix time, since preview builds roll frequently). Replace `ArchUnitNET.xUnit 0.11.1` with the correct current package id for this repo's xUnit version. Resolve or explicitly suppress (with a documented risk acceptance, not a blanket `NoWarn`) the `Microsoft.OpenApi 2.0.0-preview7` NU1903 advisory — check whether a newer `Microsoft.AspNetCore.OpenApi` pin also pulls a patched `Microsoft.OpenApi`. Re-run `dotnet build Cadence.sln --configuration Release --verbosity normal` and confirm 0 errors / 0 warnings before treating this baseline as build-verified.
2. **Resolve MIG-03 explicitly** (Gate 4) before Slice 01 work starts: decide and record either (a) parallel Execution+Designer migration with the `DESIGNER_URL`/`EXECUTION_SERVICE_URL` HTTP contracts frozen in `Cadence.Contracts` first, or (b) an explicit sub-order with a stub/mock on the not-yet-migrated side. Update `03_migration_plan.md` §3 (the SLICE-01 row) to reflect the decision, not just list it as an open blocker.
3. **Backfill the environment-variable dictionary** in `01_system_topology.md` §3.5 with the 65 undocumented in-scope variables listed in §3b above, prioritizing the 6 credential/key-bearing variables and the 10 AS2 regulatory-gateway endpoint/cert variables — these are exactly the kind of thing a secret-manager migration plan cannot afford to discover late.

### Not blocking, but worth doing alongside the above

- Record in `03_migration_plan.md` that the Execution↔CTMS and Execution↔Etmf bidirectional edges (§4b above), while lower-risk, should be drawn as bidirectional in whatever DAG diagram/tooling gets built for Phase 1, not left as one-directional arrows (this is already flagged in `02_bounded_contexts.md` §4.2 itself).
- The `SMTP_PORT`/`SMTP_USERNAME`/`SMTP_SENDER`/`SMTP_USE_TLS`/`SMTP_USE_SSL` shorthand notation in the topology doc's SMTP row should be expanded to name each variable individually, so an automated documentation-coverage check (like this one) doesn't have to special-case it.

---

## 7. Final Gate Verdict

**PHASE 1 BLOCKED.**

Remaining blockers, in the order they should be closed:
1. `Directory.Packages.props` package-version fixes + a clean `dotnet build` with 0 warnings/0 errors (Gate 5).
2. An explicit, recorded MIG-03 sequencing decision for the Execution↔Designer same-tier cycle before Slice 01 work is staffed (Gate 4).
3. Environment-variable dictionary backfill for the 65 undocumented in-scope variables, particularly the credential and regulatory-gateway ones (Gate 3).

Gates 1 and 2 are genuinely solid and need no rework. Once the three items above are closed, this baseline is in a materially strong position — the topology and coupling analysis documents are unusually rigorous (they correctly self-identified the Execution↔Designer cycle and the `packages/security` leaf-status problem before this audit ever ran) — but none of the three blockers above should be waived, since each maps to a concrete Phase 1 failure mode: an unbuildable solution, a slice sequenced against an unmet dependency, or a secret-manager migration missing real secrets.

---

## Addendum (2026-09-03) — Gate 4 remediation: MIG-03 resolved

§4c's Slice 01 True Leaf Test finding — that Slice 01 (Execution) is not a true leaf because of its live, bidirectional, same-tier HTTP cycle with Slice 02 (Designer), and that MIG-03 was an open decision rather than a resolution — has been addressed. This addendum records only that the remediation landed and where; the original audit findings above are left unmodified.

MIG-03 is now a recorded decision: parallel migration of SLICE-01 and SLICE-02 with contract-first coordination, both directions' HTTP contracts frozen in `Cadence.Contracts` (`IDesignerEligibilityApiClient` for the Execution→Designer edge, `IExecutionAmendmentApiClient` for the Designer→Execution edge) ahead of either team's service-code work. The decision, its justification against the sub-sequencing alternative, and the staffing implication are recorded in `docs/migration/phase0/03_migration_plan.md` §3.1; the SLICE-01/SLICE-02 rows in that document's §2 table and `02_bounded_contexts.md` §4.2 were updated to point at it.

This closes remediation item 2 of §6/§7 above. Items 1 (`Directory.Packages.props` pins / Gate 5) and 3 (environment-variable dictionary backfill / Gate 3) are unaffected by this addendum and remain open under their own tracking.
