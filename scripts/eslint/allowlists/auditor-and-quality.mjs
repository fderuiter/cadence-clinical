// Domain allowlist shard: Auditor, audit trail, RBQM quality cockpit, RACT, and rules views.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const AUDITOR_AND_QUALITY_RAW_MODAL_OVERLAY_ALLOWLIST = [
  "components/auditor/AuditorExportModal.vue",
  "components/quality/StatisticalForensicsViewer.vue",
];

export const AUDITOR_AND_QUALITY_EXEMPT_2D_MATRICES = [
  "components/quality/RactMatrixViewer.vue",
];

export const AUDITOR_AND_QUALITY_RAW_TABLE_ALLOWLIST = [
  "components/auditor/AuditTrailViewer.vue",
  "components/quality/StatisticalForensicsViewer.vue",
  "views/QualityCockpitView.vue",
  "views/RulesView.vue",
];

export const AUDITOR_AND_QUALITY_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/auditor/AuditorExportModal.vue",
  "components/quality/RactMatrixViewer.vue",
  "components/quality/StatisticalForensicsViewer.vue",
  "views/AuditView.vue",
  "views/AuditorView.vue",
  "views/QualityCockpitView.vue",
  "views/RulesView.vue",
];
