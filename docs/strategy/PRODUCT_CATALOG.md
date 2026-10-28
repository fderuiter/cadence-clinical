# Cadence: proposed product packaging and licensing

**Status: commercial design proposal; not products available for purchase.** Marketing names, suite name and trademarks are provisional and require clearance. The existing development system is pre-release and not validated for live clinical studies.

## Brand architecture
- **Master brand:** Cadence Clinical (provisional).
- **Full portfolio:** Cadence Complete.
- **Unified UI:** Cadence Workspace, assembled in `cadence-clinical`.
- **Shared foundation:** Cadence Core (kernel infrastructure, not a paid customer prerequisite).
- **Product families:** eight; one family may contain several separately sold applications.

| Product family | Candidate standalone applications | Engineering repository |
| --- | --- | --- |
| Blueprint | Protocol Designer; Study Builder; Standards Workbench; Feasibility | [`cadence-study-design`](https://github.com/fderuiter/cadence-study-design) |
| Capture | EDC; Clinical Data Management | [`cadence-clinical-data`](https://github.com/fderuiter/cadence-clinical-data) |
| Orbit | CTMS; eTMF; Site/eISF; Payments | [`cadence-trial-operations`](https://github.com/fderuiter/cadence-trial-operations) |
| Engage | Consent; Outcomes/eCOA; Recruitment | [`cadence-participants`](https://github.com/fderuiter/cadence-participants) |
| Allocate | RTSM; Clinical Supplies; Device Accountability | [`cadence-interventions`](https://github.com/fderuiter/cadence-interventions) |
| Vigil | Safety; Signal Management | [`cadence-safety`](https://github.com/fderuiter/cadence-safety) |
| Lens | Imaging; Laboratory; Statistical Analysis; Real-World Evidence | [`cadence-evidence`](https://github.com/fderuiter/cadence-evidence) |
| Assure | Quality Management; Regulatory Management | [`cadence-quality-regulatory`](https://github.com/fderuiter/cadence-quality-regulatory) |

24 **candidate** applications are listed across these eight families. These are possible SKUs, not 24 production-ready systems or 24 deployment processes.

## Packaging
- **Individual:** customer licenses one complete application.
- **Select:** customer licenses a custom selection of applications.
- **Complete:** customer licenses the full eligible portfolio (subject to actual available products).
- A customer must be able to integrate a standalone Cadence app with supported external EDC/CTMS/safety platforms. Do not require purchase of an unrelated Cadence family for basic functionality.
- Additional integrations and advanced automation may be commercial add-ons, but audit/authorization/critical security are **not** upsells.

## Entitlement model
Separate `customer organization`, `subscription`, `product entitlement`, `activation`, `usage meter`, `clinical user authorization`, and `tenant/study access`. Commercial license != permission to see a patient's data. Entitlements cannot bypass study scope, blinding or consent.
- Define product-local minimum self-service setup and import/export.
- Define upgrade/downgrade and trial-to-paid data continuity.
- For expired contracts, preserve required regulated records, audit trails, retention and authorized retrieval/export consistent with applicable agreements and requirements.
- Prefer a shared runtime for initial hosted SaaS with per-tenant entitlements; distinct physical deployments need an evidence-based architecture decision.
- Pricing metrics and numbers are **not set**.

## Initial release hypothesis
Prioritize **Blueprint + Capture** for a synthetic medical-device study: structured protocol → approved study-build specification → validated eCRF and edit checks → data correction, review and lock → traceable standard dataset/analysis export. Demonstrate separately usable products first, then integrated advantages.
- No commercial launch without fit-for-intended-use verification, validation/support obligations and security assessment.
- Validate demand with interviews/design partners, not feature counts alone.
- See [market hypotheses](MARKET_HYPOTHESES.md) and [migration plan](../architecture/repository-migration/MIGRATION_PLAN.md).
