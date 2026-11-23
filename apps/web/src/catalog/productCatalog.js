/**
 * Commercial packaging, product-family metadata, and transparent benchmark scoping
 * aligned with docs/strategy/PRODUCT_CATALOG.md and docs/strategy/MARKET_HYPOTHESES.md.
 *
 * Commercial entitlements govern workspace packaging and trial mutation boundaries;
 * they never confer clinical data access or bypass kernel Grants, blinding, or consent.
 */

export {
  BASELINE_KERNEL_GUARANTEES,
  MODULAR_SCOPING_BENCHMARK,
  PRODUCT_FAMILIES,
  findProductFamily,
} from "./productFamilies.js";

export const PRE_RELEASE_ENVIRONMENT_NOTICE =
  "Pre-release unvalidated environment — not qualified for live clinical trial conduct.";

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

export function findCommercialTier(code) {
  if (!code) return COMMERCIAL_TIERS[0];
  const normalized = String(code).trim().toLowerCase();
  return (
    COMMERCIAL_TIERS.find((tier) => tier.code.toLowerCase() === normalized) ||
    COMMERCIAL_TIERS[0]
  );
}
