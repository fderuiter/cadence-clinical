# Cadence: proposed product packaging and licensing

**Status: commercial design proposal.** Marketing names, suite name and trademarks are provisional and require clearance. The existing development system is pre-release and not validated for live clinical studies. Implementation and verification are organized in [one repository](../adr/2026-11-19-keep-cadence-in-one-repository.md).

## Brand architecture
- **Master brand:** Cadence Clinical (provisional).
- **Full portfolio:** Cadence Complete.
- **Unified UI:** Cadence Workspace, assembled in `cadence-clinical`.
- **Shared foundation:** Cadence Core (kernel infrastructure, not a paid customer prerequisite).
- **Product families:** eight; one family may contain several separately sold applications.

| Product family | Candidate standalone applications | Product responsibility |
| --- | --- | --- |
| Blueprint | Protocol Designer, Study Builder, Standards Workbench, Feasibility | Protocol and study design |
| Capture | EDC, Clinical Data Management | Clinical collection and discrepancy workflows |
| Orbit | CTMS, eTMF, Site/eISF, Payments | Trial operations and site readiness |
| Engage | Consent, Outcomes/eCOA, Recruitment | Participant workflows |
| Allocate | RTSM, Clinical Supplies, Device Accountability | Intervention assignment and accountability |
| Vigil | Safety, Signal Management | Safety and pharmacovigilance |
| Lens | Imaging, Laboratory, Statistical Analysis, Real-World Evidence | Scientific evidence |
| Assure | Quality Management, Regulatory Management | Quality and regulatory workflows |

The 24 candidate applications are possible SKUs. The [project plan](../PROJECT_PLAN.md) identifies which clinical workflows are in the current delivery scope and where they live in the codebase.

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
Prioritize **Blueprint + Capture** for a synthetic medical-device study: structured protocol → approved study-build specification → eCRF and edit checks → data correction, review and lock → traceable clinical export. Demonstrate the integrated study journey and use the product boundaries to guide packaging hypotheses.
- No commercial launch without fit-for-intended-use verification, validation/support obligations and security assessment.
- Validate demand with interviews/design partners, not feature counts alone.
- See [market hypotheses](MARKET_HYPOTHESES.md) and the [project plan](../PROJECT_PLAN.md).
