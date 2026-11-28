// Domain allowlist shard: CRF authoring, eCRF capture, Clinical Canvas casebook, persona, and observations.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const CRF_ECRF_CANVAS_RAW_MODAL_OVERLAY_ALLOWLIST = [];

export const CRF_ECRF_CANVAS_EXEMPT_2D_MATRICES = [
  "components/canvas/ParticipantFormsPanel.vue",
  "components/canvas/ScheduleOfActivitiesSection.vue",
  "components/crf/widgets/RepeatingLogGridWidget.vue",
];

export const CRF_ECRF_CANVAS_RAW_TABLE_ALLOWLIST = [
  "components/crf/CanvasFieldWidget.vue",
  "components/ecrf/EcrfItemTable.vue",
  "components/ecrf/EcrfSignaturesSection.vue",
  "components/persona/CraVerificationConsole.vue",
  "views/ObservationView.vue",
];

export const CRF_ECRF_CANVAS_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/crf/CanvasFieldWidget.vue",
  "components/crf/CrfAuthoringCanvas.vue",
  "components/crf/FormSectionContainer.vue",
  "components/crf/RuleTracerPanel.vue",
  "components/persona/CrcFormRenderer.vue",
  "components/persona/DesignerSchemaPanel.vue",
];
