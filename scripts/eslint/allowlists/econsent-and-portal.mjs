// Domain allowlist shard: eConsent authoring, ICF builder, lay summary portal, and participant wallet.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const ECONSENT_AND_PORTAL_RAW_MODAL_OVERLAY_ALLOWLIST = [];

export const ECONSENT_AND_PORTAL_EXEMPT_2D_MATRICES = [];

export const ECONSENT_AND_PORTAL_RAW_TABLE_ALLOWLIST = [
  "components/econsent/IcfSectionEditor.vue",
  "views/LaySummaryPortal.vue",
  "views/ParticipantWalletView.vue",
];

export const ECONSENT_AND_PORTAL_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/econsent/ComprehensionQuizBuilder.vue",
  "components/econsent/IcfSectionEditor.vue",
  "views/ConsentAuthoringView.vue",
  "views/ICFBuilderView.vue",
  "views/LaySummaryPortal.vue",
  "views/ParticipantWalletView.vue",
];
