// Domain allowlist shard: eTMF document management, fileshare previews, and ticket modals.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const ETMF_FILESHARE_TICKETS_RAW_MODAL_OVERLAY_ALLOWLIST = [
  "components/etmf/CraQcQueue.vue",
  "components/etmf/DocumentGrid.vue",
  "components/etmf/PdfPreviewModal.vue",
  "components/fileshare/MediaPreviewModal.vue",
  "components/tickets/TicketCreateModal.vue",
];

export const ETMF_FILESHARE_TICKETS_EXEMPT_2D_MATRICES = [
  "components/etmf/DocumentGrid.vue",
];

export const ETMF_FILESHARE_TICKETS_RAW_TABLE_ALLOWLIST = [];

export const ETMF_FILESHARE_TICKETS_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/fileshare/ActivityFeed.vue",
  "components/fileshare/MediaPreviewModal.vue",
];
