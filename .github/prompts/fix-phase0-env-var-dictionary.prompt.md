# Fix Prompt C — Backfill the Environment Variable Dictionary (Gate 3 Remediation)

## Role & Execution Protocol

You are a Principal Systems Forensic Auditor completing a documentation gap, not
writing code. Your job is to bring `docs/migration/phase0/01_system_topology.md`
§3.5 ("Complete environment-variable dictionary") to genuine 100% coverage of
every environment variable actually read by the in-scope Python codebase,
verified the same mechanical way the rest of Phase 0 was audited — by shell
command, not by memory or by re-skimming the doc's prose. Do not accept "looks
complete" as a stopping condition; prove it with a reproducible diff that
returns empty.

**Source of truth for this task:**
`docs/migration/phase0/00_validation_gate_report.md` §3b ("Environment variable
parity — the real gap"). It already lists the 65 undocumented in-scope variables
found in this session's audit, grouped by category, with the owning app/package
for each. Start from that list — do not re-derive it from nothing — but do
re-verify it, since the codebase may have changed since the audit ran.

---

## Step 1 — Re-establish ground truth

```bash
rg -o -N "(os\.environ(\.get)?\[?['\"][A-Za-z0-9_]+['\"]|os\.getenv\(['\"][A-Za-z0-9_]+['\"])" --type py \
  | grep -oE "['\"][A-Za-z0-9_]+['\"]" | tr -d "'\"" | sort -u > /tmp/all_env_vars.txt
wc -l /tmp/all_env_vars.txt
```

For each variable, determine which top-level directory (`apps/<context>`,
`packages/<package>`, `scripts`, `tests`) references it:

```bash
while read -r v; do
  dirs=$(rg -F -l "$v" --type py </dev/null | awk -F/ '{ if ($1=="apps"||$1=="packages") print $1"/"$2; else print $1 }' | sort -u | paste -sd, -)
  echo "$v => $dirs"
done < /tmp/all_env_vars.txt > /tmp/env_vars_with_owners.txt
```

Variables whose _only_ owning directories are `scripts/` or `tests/` are out of
scope for this dictionary (Phase 0 already established `scripts/` is
cross-cutting CI tooling, explicitly not migrated, per `01_system_topology.md`
§2.5, and `tests/` root fixtures are cross-service test harness, not
bounded-context config). Filter those out — you are backfilling the
_business-config_ dictionary, not cataloging every CI script's env usage.

## Step 2 — Compute the real gap

```bash
grep -oE '`[A-Z][A-Z0-9_]{2,}`' docs/migration/phase0/01_system_topology.md | tr -d '`' | sort -u > /tmp/documented.txt
comm -23 <(cut -d' ' -f1 /tmp/env_vars_with_owners.txt | sort -u) /tmp/documented.txt
```

Note: a plain backtick-token diff will produce some false positives where the
doc uses shorthand notation (e.g. `SMTP_HOST/PORT/USERNAME/...` covering several
names in one token) — for each apparent gap, manually check whether the doc
already covers it under a compound/shorthand notation before treating it as a
real miss. If you find such a shorthand, **expand it** to name each variable
individually in this pass, so future automated checks don't have to special-case
it (this was flagged as a known rough edge in the validation report).

## Step 3 — Document each real gap

For every variable confirmed missing, add an entry to `01_system_topology.md`
§3.5, in the existing subsection structure (§3.5.1–3.5.6) that best fits it, or
a new §3.5.7 if none fit. Follow the existing entries' format exactly — each one
needs:

- The variable name (backtick-quoted, matching the existing style).
- The file:line of at least one real read site (use
  `rg -n "VAR_NAME" <owning-dir> --type py` to find it — don't guess).
- Which service(s)/context(s) own or read it.
- A one-line note on sensitivity/criticality where relevant, matching the
  existing doc's practice of flagging things like "critical" secrets vs.
  low-risk config (see how `SMTP_PASSWORD` is flagged as critical in the
  existing table, for the pattern to follow).

Pay particular attention to getting these categories right, since the validation
report flagged them as the highest-consequence gaps:

- **Credential/key material** (`KEYCLOAK_CLIENT_SECRET`,
  `GXP_SIGNING_PRIVATE_KEY`, `GXP_SIGNING_CERT`, `GXP_SIGNER_ID`,
  `CDISC_LIBRARY_API_KEY`, `PSEUDONYMIZATION_SALT`) — these need to land in
  whatever section of the doc is read by whoever designs the .NET
  secret-manager/Key Vault baseline. If §3.5.2 ("Cryptographic secrets") is the
  right home, put them there; if it needs a new subsection for API keys/salts
  distinct from signing keys, add one.
- **Regulatory AS2 gateway endpoints and certs** (EMA, FDA, HC, MHRA, PMDA — URL
  and cert pairs) — these are integration endpoints with per-jurisdiction
  identity; document them as a coherent group (a small table, one row per
  authority) rather than scattered individual bullets, since a Phase 1
  implementer will need to configure all five together.
- Everything else the report lists (operational tuning knobs, AI-tier model
  routing, storage/cloud config, rate-limit/CORS config) — document at whatever
  level of individual detail the existing doc uses for comparable variables
  nearby.

## Step 4 — Verify zero remaining gap

Re-run the Step 2 diff after your edits. It must return empty (modulo variables
you've deliberately and explicitly scoped out as `scripts`/`tests`-only, which
should be a short, named list — not a silent exclusion).

```bash
comm -23 <(cut -d' ' -f1 /tmp/env_vars_with_owners.txt | sort -u) \
         <(grep -oE '`[A-Z][A-Z0-9_]{2,}`' docs/migration/phase0/01_system_topology.md | tr -d '`' | sort -u)
```

## Step 5 — Update the validation report

Append a dated addendum to `docs/migration/phase0/00_validation_gate_report.md`
noting Gate 3's environment-variable-parity finding has been addressed, with the
final documented/found counts (should now read 100%, or name the specific
out-of-scope exclusions if not literally 100%). Do not rewrite the original
audit findings — append, don't overwrite history, consistent with how this
repo's Phase 0 docs already treat prior findings (see how
`05_orphan_audit_gate.md` references and does not erase
`Migration_Phase0_Readiness_Audit.md`'s AUD-NET10-002 finding).

## Deliverable

- Updated `docs/migration/phase0/01_system_topology.md` §3.5 with all ~65
  previously-undocumented in-scope variables added, each with a real file:line
  citation.
- The `SMTP_*` shorthand notation expanded to individually named variables.
- A dated addendum to `docs/migration/phase0/00_validation_gate_report.md`.
- No code changes — this is a documentation-only task. Do not touch
  `.env.example`, `appsettings.json`, or any application source file.
