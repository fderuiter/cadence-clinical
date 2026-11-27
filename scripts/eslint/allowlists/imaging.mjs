// Domain allowlist shard: Medical imaging, DICOM viewers, oncology calipers, and dual-reader adjudication.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const IMAGING_RAW_MODAL_OVERLAY_ALLOWLIST = [
  "components/imaging/BatchDicomDropzoneModal.vue",
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "components/imaging/OncologyCalipers.vue",
];

export const IMAGING_EXEMPT_2D_MATRICES = [
  "components/imaging/OncologyCalipers.vue",
];

export const IMAGING_RAW_TABLE_ALLOWLIST = [
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "views/ImagingView.vue",
];

export const IMAGING_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/imaging/BatchDicomDropzoneModal.vue",
  "components/imaging/DicomMprViewer.vue",
  "components/imaging/DualReaderAdjudicationPanel.vue",
  "components/imaging/OncologyCalipers.vue",
];
