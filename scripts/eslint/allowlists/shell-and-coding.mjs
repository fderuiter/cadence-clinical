// Domain allowlist shard: App shell, authentication/entry views, and medical coding / MDR surfaces.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const SHELL_AND_CODING_RAW_MODAL_OVERLAY_ALLOWLIST = [
  "components/CodingQueueTable.vue",
  "components/DictionaryBrowserModal.vue",
  "components/shared/ReAuthModal.vue",
  "views/MdrView.vue",
];

export const SHELL_AND_CODING_EXEMPT_2D_MATRICES = [];

export const SHELL_AND_CODING_RAW_TABLE_ALLOWLIST = [
  "components/CodingQueueTable.vue",
  "components/SynonymImportDrawer.vue",
  "views/KnowledgeView.vue",
];

export const SHELL_AND_CODING_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/AppShell.vue",
  "components/CodingQueueTable.vue",
  "components/shared/ReAuthModal.vue",
  "views/ForbiddenView.vue",
  "views/KnowledgeView.vue",
  "views/LoginView.vue",
  "views/MdrView.vue",
  "views/MedicalCodingView.vue",
  "views/NotificationsView.vue",
  "views/OverviewView.vue",
];
