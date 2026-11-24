/**
 * Commercial packaging, product-family metadata, and transparent benchmark scoping
 * aligned with docs/strategy/PRODUCT_CATALOG.md and docs/strategy/MARKET_HYPOTHESES.md.
 *
 * Commercial entitlements govern workspace packaging and trial mutation boundaries;
 * they never confer clinical data access or bypass kernel Grants, blinding, or consent.
 */

export { PRE_RELEASE_ENVIRONMENT_NOTICE } from "./preReleaseNotice.js";

export const PRODUCT_FAMILIES = /* @__PURE__ */ Object.freeze([
  {
    code: "Blueprint",
    name: "Blueprint",
    responsibility: "Protocol and study design",
    candidateApps: [
      "Protocol Designer",
      "Study Builder",
      "Standards Workbench",
      "Feasibility",
    ],
    deliveryStatus: "Preview active in Study Workspace",
    minimumTier: "Individual",
    summary:
      "Structured USDM protocol versions, Schedule of Activities matrices, lineage, and amendment impact analysis.",
  },
  {
    code: "Capture",
    name: "Capture",
    responsibility: "Clinical collection and discrepancy workflows",
    candidateApps: ["EDC", "Clinical Data Management"],
    deliveryStatus: "Preview active in Data Collection",
    minimumTier: "Individual",
    summary:
      "Visit eCRF capture, source data verification, governed value corrections, discrepancy queries, and scope locks.",
  },
  {
    code: "Orbit",
    name: "Orbit",
    responsibility: "Trial operations and site readiness",
    candidateApps: ["CTMS", "eTMF", "Site/eISF", "Payments"],
    deliveryStatus: "Site readiness active; vault workflows in pre-release",
    minimumTier: "Select",
    summary:
      "Site activation readiness, operational milestones, investigator site files, and trial master file governance.",
  },
  {
    code: "Engage",
    name: "Engage",
    responsibility: "Participant workflows",
    candidateApps: ["Consent", "Outcomes/eCOA", "Recruitment"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Select",
    summary:
      "Electronic informed consent versioning, participant outcomes capture, telehealth visits, and lay summaries.",
  },
  {
    code: "Allocate",
    name: "Allocate",
    responsibility: "Intervention assignment and accountability",
    candidateApps: ["RTSM", "Clinical Supplies", "Device Accountability"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Select",
    summary:
      "Randomization, investigational supply accountability, and medical-device / IVD exposure tracking.",
  },
  {
    code: "Vigil",
    name: "Vigil",
    responsibility: "Safety and pharmacovigilance",
    candidateApps: ["Safety", "Signal Management"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "Adverse event intake, clinical endpoint adjudication, DSUR compilation, and safety signal tracking.",
  },
  {
    code: "Lens",
    name: "Lens",
    responsibility: "Scientific evidence",
    candidateApps: [
      "Imaging",
      "Laboratory",
      "Statistical Analysis",
      "Real-World Evidence",
    ],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "DICOM / RECIST imaging workflows, laboratory reference governance, connected sensors, and statistical lineage.",
  },
  {
    code: "Assure",
    name: "Assure",
    responsibility: "Quality and regulatory workflows",
    candidateApps: ["Quality Management", "Regulatory Management"],
    deliveryStatus: "Pre-release module roadmap",
    minimumTier: "Complete",
    summary:
      "Risk-based quality management (RBQM), CAPA oversight, SOP training gating, and submission readiness.",
  },
]);

export const COMMERCIAL_TIERS = /* @__PURE__ */ Object.freeze([
  {
    code: "Individual",
    name: "Individual",
    subtitle: "Focused evaluation of standalone clinical workflows",
    families: ["Blueprint", "Capture"],
    trialBadge: "Starter Trial Default",
    highlights: [
      "Provisions an isolated kernel tenant with Blueprint and Capture enabled immediately",
      "Integrates with external CTMS, eTMF, or safety systems via published OpenAPI endpoints without requiring unrelated families",
      "Includes baseline tamper-evident audit chains, electronic signatures, and fine-grained clinical Grants",
    ],
    scopingNote:
      "Scoped per study complexity and active environments during commercial partner discovery; no fixed fees or payment cards in pre-release.",
  },
  {
    code: "Select",
    name: "Select",
    subtitle: "Custom multi-family combination tailored to study design",
    families: ["Blueprint", "Capture", "Orbit", "Engage", "Allocate"],
    trialBadge: "Multi-Family Trial",
    highlights: [
      "Combines Blueprint + Capture with operational, participant, and supply families (Orbit, Engage, Allocate)",
      "Upgrades in place over the same Tenant, Organization, Study, and audit history with zero data migration",
      "Preserves standalone API boundaries so external systems remain supported alongside selected families",
    ],
    scopingNote:
      "Custom application selection scoped to active sites, participant channels, and integration requirements.",
  },
  {
    code: "Complete",
    name: "Complete",
    subtitle: "Full eligible Cadence Clinical portfolio across eight families",
    families: [
      "Blueprint",
      "Capture",
      "Orbit",
      "Engage",
      "Allocate",
      "Vigil",
      "Lens",
      "Assure",
    ],
    trialBadge: "Full Portfolio Trial",
    highlights: [
      "Unlocks every eligible product family as kernel modules reach pre-release availability",
      "Maintains strict separation between commercial entitlements and clinical data access Grants",
      "Retains regulated record retrieval, audit verification, and export continuity if a trial expires",
    ],
    scopingNote:
      "Portfolio agreement scoped with sponsor governance, data residency, and qualification requirements.",
  },
]);

export const BASELINE_KERNEL_GUARANTEES = /* @__PURE__ */ Object.freeze([
  {
    id: "audit",
    title: "Tamper-evident audit trails",
    detail:
      "Every command commits state, hash-chained audit entries, and outbox events atomically in Cadence Core — never sold as an add-on.",
  },
  {
    id: "signatures",
    title: "Electronic signatures (21 CFR Part 11)",
    detail:
      "Meaningful human attestations bound to fresh OIDC authentication remain available across all tiers and trial states.",
  },
  {
    id: "authorization",
    title: "Clinical Grants separate from commercial plans",
    detail:
      "Commercial Subscriptions and ProductEntitlements never grant patient data access or bypass study scope, blinding, or consent.",
  },
  {
    id: "continuity",
    title: "Post-expiry regulated read & export",
    detail:
      "When a trial window expires, new study mutations pause while authorized retrieval, audit review, and regulated exports remain active.",
  },
]);

export const MODULAR_SCOPING_BENCHMARK = /* @__PURE__ */ Object.freeze([
  {
    step: "1. Self-service account & workspace",
    industryPattern:
      "Modern EDC platforms such as Castor EDC and Veeva SiteVault let research teams register online and launch an evaluation workspace without waiting on manual operator provisioning.",
    cadenceApproach:
      "OIDC self-registration in Keycloak followed by automated Clinical Kernel tenant, sponsor organization, role, grant, and starter entitlement provisioning.",
  },
  {
    step: "2. Modular product selection",
    industryPattern:
      "Modular clinical suites (e.g., Castor modular pricing across core EDC plus eConsent, ePRO, and RTSM modules) scope deployments around protocol needs rather than forcing monolithic bundles.",
    cadenceApproach:
      "Individual, Select, and Complete tiers across eight product families with published APIs so standalone apps coexist with external systems.",
  },
  {
    step: "3. Study & residency scoping",
    industryPattern:
      "Commercial quotes factor study duration, site count, participant volume, and regional data residency before live clinical conduct.",
    cadenceApproach:
      "Per-tenant regional residency tags on kernel.tenants and transparent scoping conversations — zero opaque upcharges for baseline audit or security controls.",
  },
]);

export function findProductFamily(code) {
  return PRODUCT_FAMILIES.find((family) => family.code === code) || null;
}

export function findCommercialTier(code) {
  if (!code) return COMMERCIAL_TIERS[0];
  const normalized = String(code).trim().toLowerCase();
  return (
    COMMERCIAL_TIERS.find((tier) => tier.code.toLowerCase() === normalized) ||
    COMMERCIAL_TIERS[0]
  );
}
