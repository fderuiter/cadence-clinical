# Python Reference Inventory (#4789)

The Python files still in `packages/` are behavioural references for C# ports
that do not exist yet or are not yet covered by tests ([AGENTS.md](../AGENTS.md)
pattern 11, [MIGRATION_WORKFLOW.md](MIGRATION_WORKFLOW.md)). They are never
built, run or imported by anything. Everything else was deleted once a tested C#
port existed, the kernel specification superseded it, or it had no behaviour
worth keeping
([ADR-2492](adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md)).
Deleted files stay recoverable from git history (for example
`git show 354f7ea:packages/database/graph.py`).

A file leaves this list when one of these is true:

- **Port missing:** a C# implementation lands with tests, or the kernel
  specification or an ADR records that the behaviour is not carried forward.
- **Port untested:** tests cover the C# port's main behaviour (the note says
  which paths are missing).
- **Ported, deletion pending:** the tested C# port exists; the file goes once a
  maintainer approves the deletion.

Delete the Python file and its entry here in the same PR. Update the package
README's "Python reference sources" line too.

| Package                                      | Port missing | Port untested | Ported, deletion pending |
| -------------------------------------------- | -----------: | ------------: | -----------------------: |
| [packages/engine_core](#packagesengine_core) |            2 |             0 |                        0 |
| [packages/security](#packagessecurity)       |            5 |             0 |                        0 |

## packages/engine_core

- **`timeline/engine.py`** (`Port missing`) — Visit-window schedule and
  deviation assessment. _C# today:_ `ExpectedSchedule` (kernel,
  [ADR-2487](adr/2026-09-30-expected-schedule-as-a-pure-function.md)) places
  timed activities with their windows and window status from the reference start
  (K-TIME-7). _Notes:_ Early/late classification of an actual visit is
  `ExpectedSchedule.Assess`
  ([ADR-2507](adr/2026-09-30-visit-timing-anchored-visits-and-cycles-on-the-expected-schedule.md));
  major/minor is a Quality rule, not a fixed 7 days. Not in the kernel yet
  (#5045, ADR-2507): repeating cycle templates and visits anchored on another
  visit.
- **`timeline/models.py`** (`Port missing`) — Timeline models. _C# today:_ none.

## packages/security

- **`cert_store.py`** (`Port missing`) — X.509 register, revoke, CRL status,
  self-signed and trust checks. _C# today:_ none.
- **`crypto_verifier.py`** (`Port missing`) — `verify_electronic_signature`:
  JSON/PEM/XML signature parsing, XML-DSig stripping, mock-key detection,
  asymmetric verification or HMAC fallback. _C# today:_ only the primitive
  `packages/security/src/AsymmetricSigner.AsymmetricVerify`. _Notes:_ The
  orchestration is not ported.
- **`fail_fast.py`** (`Port missing`) — Refuses to start in non-dev environments
  on missing or insecure-fallback secrets; brand validation. _C# today:_
  `GATEWAY_SECRET` (required, at least 32 chars), the execution demographics key
  and the gateway service URLs
  ([ADR-2490](adr/2026-09-30-fail-fast-startup-validation-of-required-settings.md)).
  _Notes:_ See note S1.
- **`rbac.py`** (`Port missing`) — Role aliases, role → resource → action
  matrix, `Principal`, site/study scope, payload masking, auditor checks. _C#
  today:_ `packages/security/src/Rbac/RoleNormalization.cs`,
  `PermissionMatrix.cs`, `SiteAccessEvaluator.cs`, `StudyAccessEvaluator.cs`,
  `Blinding/FieldMasker.cs`, `Authentication/AuditorWriteGuardMiddleware.cs`.
  _Notes:_ See note S2.
- **`signing.py`** (`Port missing`) — Gateway HMAC, canonical signature,
  asymmetric sign, Merkle root and block hash, inbound-email webhook HMAC with
  replay cache, site greenlight token. _C# today:_
  `Authentication/HmacSigner.cs`, `CanonicalSigner.cs`, `AsymmetricSigner.cs`,
  `Audit/AuditDigestEngine.cs`. _Notes:_ See note S3.

**Note S1.** `fail_fast.py` refuses to start outside development when any
required secret is missing or still set to an insecure placeholder, and
validates the brand settings. In C#, every service requires `GATEWAY_SECRET` (at
least 32 characters), execution requires a valid demographics encryption key and
the gateway requires every service URL outside Development (#4872, ADR-2490). To
delete: cover the remaining services' required settings the same way, or record
that the Python's placeholder and brand checks are dropped.

**Note S2.** The C# `PermissionMatrix` has no entries for the roles
`grants_manager`, `sponsor_clinical`, `quality_manager`, `qa_lead` or
`quality_oversight` (`rbac.py:645-763`). The `system` and `anonymous` entries
are rightly dropped (K-TX-5). The rest is ported and tested. To delete: port
those five role bundles or record that they are dropped.

**Note S3.** Not ported:

- `verify_inbound_email_signature` (eTMF inbound webhook;
  `INBOUND_EMAIL_HMAC_SECRET`).
- `generate_greenlight_token` / `verify_greenlight_token`.
- `compute_merkle_root` / `compute_block_hash`.
