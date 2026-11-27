// Domain allowlist shard: Clinical operational consoles, SoA matrices, digitization, and CTMS demo.
// Shrink-only legacy allowlists (ADR-2656 / ADR-2673 / ADR-2674). Do not add new entries.

export const CLINICAL_CONSOLES_RAW_MODAL_OVERLAY_ALLOWLIST = [
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "views/AmendmentDiffView.vue",
];

export const CLINICAL_CONSOLES_EXEMPT_2D_MATRICES = [
  "components/clinical/ClinicalSoAMatrix.vue",
  "components/clinical/SoAMatrixEditor.vue",
];

export const CLINICAL_CONSOLES_RAW_TABLE_ALLOWLIST = [
  "components/clinical/AdaptiveDoseEscalationConsole.vue",
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/CecAdjudicationConsole.vue",
  "components/clinical/CellGeneTherapyCocConsole.vue",
  "components/clinical/ClinicalLakehouseConsole.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/IECriteriaTable.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/ProtocolTrainingConsole.vue",
  "components/clinical/SensorTelemetryConsole.vue",
  "components/clinical/SsuGreenlightConsole.vue",
  "components/clinical/StatisticalComputingConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "components/clinical/TherapeuticAreaWizard.vue",
  "views/AmendmentDiffView.vue",
  "views/demo/CtmsDemoWorkspace.vue",
];

export const CLINICAL_CONSOLES_UNGUARDED_BUTTON_ALLOWLIST = [
  "components/clinical/AdaptiveDoseEscalationConsole.vue",
  "components/clinical/AggregateSafetyConsole.vue",
  "components/clinical/CdiscPipelineConsole.vue",
  "components/clinical/CecAdjudicationConsole.vue",
  "components/clinical/ClinicalLakehouseConsole.vue",
  "components/clinical/DeviceInvestigationConsole.vue",
  "components/clinical/LabGovernanceConsole.vue",
  "components/clinical/ProtocolTrainingConsole.vue",
  "components/clinical/SensorTelemetryConsole.vue",
  "components/clinical/SsuGreenlightConsole.vue",
  "components/clinical/StatisticalComputingConsole.vue",
  "components/clinical/TelehealthDtpConsole.vue",
  "components/clinical/TherapeuticAreaWizard.vue",
  "views/AmendmentDiffView.vue",
  "views/ProtocolDigitizationView.vue",
  "views/demo/CtmsDemoWorkspace.vue",
];
