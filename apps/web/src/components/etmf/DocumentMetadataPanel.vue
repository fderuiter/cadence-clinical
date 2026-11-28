<template>
  <div v-if="inspectedDoc" class="document-metadata-panel">
    <!-- DIA Reference Model Taxonomy Section -->
    <div class="drawer-section">
      <h4 class="section-heading">DIA Reference Model Taxonomy</h4>
      <div class="meta-grid">
        <div class="meta-item">
          <span class="meta-label">Artifact Code</span>
          <span class="meta-val font-mono">{{
            inspectedDoc.artifact_code || inspectedDoc.taxonomy_code || "—"
          }}</span>
        </div>
        <div class="meta-item">
          <span class="meta-label">Zone &amp; Section</span>
          <span class="meta-val"
            >Zone {{ inspectedDoc.zone_id || inspectedDoc.zone || "—" }} ·
            {{ inspectedDoc.section || "—" }}</span
          >
        </div>
        <div class="meta-item">
          <span class="meta-label">Version</span>
          <span class="meta-val badge status-review"
            >v{{ inspectedDoc.version_index || 1 }}.0</span
          >
        </div>
        <div class="meta-item">
          <span class="meta-label">Lifecycle Status</span>
          <span class="meta-val badge status-approved">{{
            formatClinicalStatus(inspectedDoc.status || "DRAFT")
          }}</span>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Audit Trail & Change Justification -->
    <div class="drawer-section">
      <h4 class="section-heading">21 CFR Part 11 Audit Record</h4>
      <div class="reason-box">
        <strong>Mandatory Reason for Change:</strong>
        <p
          style="
            margin: 4px 0 0 0;
            font-size: 12px;
            color: var(--semantic-color-text);
          "
        >
          {{ inspectedDoc.reason_for_change || inspectedDoc.reason || "—" }}
        </p>
      </div>
      <div class="meta-grid" style="margin-top: 10px">
        <div class="meta-item">
          <span class="meta-label">Uploaded By</span>
          <span class="meta-val font-mono">{{
            inspectedDoc.created_by || inspectedDoc.uploaded_by || "—"
          }}</span>
        </div>
        <div class="meta-item">
          <span class="meta-label">Uploaded At</span>
          <span class="meta-val font-mono">{{
            inspectedDoc.formattedCreatedAt || inspectedDoc.created_at || "—"
          }}</span>
        </div>
      </div>
    </div>

    <!-- Cryptographic SHA-256 Checksum & Seal -->
    <div class="drawer-section">
      <h4 class="section-heading">Cryptographic Integrity</h4>
      <div class="crypto-seal-card">
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 6px;
          "
        >
          <span style="font-size: var(--font-size-2xs); font-weight: 600"
            >Cryptographic Integrity Verification</span
          >
          <span
            v-if="getDocSha256(inspectedDoc) !== '—'"
            class="badge status-approved"
            style="font-size: var(--font-size-2xs)"
            >SHA-256 Recorded</span
          >
          <span
            v-else
            class="badge status-draft"
            style="font-size: var(--font-size-2xs)"
            >Not Computed</span
          >
        </div>
        <SupportDetails
          v-if="getDocSha256(inspectedDoc) !== '—'"
          summary-label="SHA-256 verification digest"
          :reference="getDocSha256(inspectedDoc)"
        />
        <span
          v-else
          style="
            font-size: var(--font-size-2xs);
            color: var(--semantic-color-text-muted);
          "
          >Digest not yet recorded</span
        >
      </div>
    </div>

    <!-- De-Identification / Redaction Manifest -->
    <div class="drawer-section">
      <h4 class="section-heading">HIPAA / GDPR De-identification</h4>
      <div class="redaction-info">
        <span v-if="inspectedDoc.is_redacted" class="badge status-approved">
          De-Identified Copy (PHI/PII Scrubber Applied)
        </span>
        <span v-else class="badge status-draft">
          Regulated Source Original
        </span>
      </div>
    </div>

    <!-- Multimodal DIA Document Intelligence & Lifecycle Transitions -->
    <div class="drawer-section">
      <h4 class="section-heading">DIA Document Intelligence</h4>
      <div class="intelligence-card">
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 8px;
          "
        >
          <span style="font-size: 12px; font-weight: 600"
            >DIA Classifier Confidence</span
          >
          <span
            class="badge"
            :class="
              hasClassifierConfidence(inspectedDoc)
                ? 'status-approved'
                : 'status-draft'
            "
            style="font-size: var(--font-size-2xs); font-weight: bold"
          >
            {{ getDocConfidence(inspectedDoc) }}
          </span>
        </div>
        <div class="meta-grid">
          <div class="meta-item">
            <span class="meta-label">Detected Modality</span>
            <span class="meta-val">{{
              formatDocumentModality(getDocModality(inspectedDoc))
            }}</span>
          </div>
          <div class="meta-item">
            <span class="meta-label">Signature Verification</span>
            <span class="meta-val" :class="getSigStatusClass(inspectedDoc)">
              {{ getSigStatusText(inspectedDoc) }}
            </span>
          </div>
          <div v-if="getExtractedInvestigator(inspectedDoc)" class="meta-item">
            <span class="meta-label">Investigator</span>
            <span class="meta-val font-mono">{{
              getExtractedInvestigator(inspectedDoc)
            }}</span>
          </div>
          <div v-if="getExtractedProtocol(inspectedDoc)" class="meta-item">
            <span class="meta-label">Protocol #</span>
            <span class="meta-val font-mono">{{
              getExtractedProtocol(inspectedDoc)
            }}</span>
          </div>
        </div>

        <!-- DRAFT -> TECHNICAL_QC transition -->
        <div v-if="inspectedDoc.status === 'DRAFT'" class="qc-accept-panel">
          <ClinicalInput
            id="draft-submit-qc-reason"
            :model-value="draftSubmitReason"
            label="Submit Draft for Technical QC (min 10 characters)"
            density="compact"
            :show-query-flag="false"
            control-class="qc-reason-input"
            :disabled="!canStage || submittingDraftQc"
            placeholder="Ready for technical QC review and taxonomy verification"
            @update:model-value="emit('update:draftSubmitReason', $event)"
          />
          <p v-if="!canStage" id="etmf-stage-role-note" class="qc-role-note">
            {{ ETMF_STAGE_ROLE_REASON }}
          </p>
          <p
            v-if="qcReviewError"
            id="etmf-qc-review-error"
            class="qc-error-note"
            role="alert"
          >
            {{ qcReviewError }}
          </p>
          <button
            id="btn-submit-technical-qc"
            type="button"
            class="btn btn-secondary"
            style="
              width: 100%;
              padding: 6px 10px;
              font-size: var(--font-size-2xs);
              cursor: pointer;
            "
            :disabled="
              !canStage ||
              submittingDraftQc ||
              draftSubmitReason.trim().length < 10
            "
            :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
            @click="emit('submit-draft-qc', inspectedDoc)"
          >
            {{
              submittingDraftQc
                ? "Submitting for Technical QC..."
                : "Submit for Technical QC"
            }}
          </button>
        </div>

        <!-- TECHNICAL_QC -> APPROVED clinical QC review -->
        <div
          v-else-if="inspectedDoc.status === 'TECHNICAL_QC'"
          class="qc-accept-panel"
        >
          <ClinicalInput
            id="qc-accept-reason"
            :model-value="qcAcceptReason"
            label="Clinical QC Review Justification (min 10 characters)"
            density="compact"
            :show-query-flag="false"
            control-class="qc-reason-input"
            :disabled="!canReviewQc || reviewingQc"
            placeholder="Verified artifact taxonomy, dates, and signatures"
            @update:model-value="emit('update:qcAcceptReason', $event)"
          />
          <p v-if="!canReviewQc" id="etmf-qc-role-note" class="qc-role-note">
            {{ ETMF_QC_REVIEW_ROLE_REASON }}
          </p>
          <p
            v-if="qcReviewError"
            id="etmf-qc-review-error"
            class="qc-error-note"
            role="alert"
          >
            {{ qcReviewError }}
          </p>
          <button
            id="btn-accept-ai-classification"
            type="button"
            class="btn btn-secondary"
            style="
              width: 100%;
              padding: 6px 10px;
              font-size: var(--font-size-2xs);
              cursor: pointer;
            "
            :disabled="
              !canReviewQc || reviewingQc || qcAcceptReason.trim().length < 10
            "
            :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
            @click="emit('accept-classification', inspectedDoc)"
          >
            {{
              reviewingQc
                ? "Submitting Clinical QC..."
                : "✓ Accept AI Classification"
            }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import { defineLazyOverlay } from "../../utils/lazyOverlay.js";

const SupportDetails = defineLazyOverlay(
  () => import("../canvas/SupportDetails.vue")
);
</script>

<script setup>
import { ClinicalInput } from "ui";
import {
  ETMF_QC_REVIEW_ROLE_REASON,
  ETMF_STAGE_ROLE_REASON,
} from "../../canvas/authority";
import {
  formatClinicalStatus,
  formatDocumentModality,
} from "../../utils/clinicalLabels";

defineProps({
  inspectedDoc: {
    type: Object,
    default: null,
  },
  draftSubmitReason: {
    type: String,
    default: "",
  },
  qcAcceptReason: {
    type: String,
    default: "",
  },
  canStage: {
    type: Boolean,
    default: false,
  },
  canReviewQc: {
    type: Boolean,
    default: false,
  },
  submittingDraftQc: {
    type: Boolean,
    default: false,
  },
  reviewingQc: {
    type: Boolean,
    default: false,
  },
  qcReviewError: {
    type: String,
    default: "",
  },
});

const emit = defineEmits([
  "close",
  "update:draftSubmitReason",
  "update:qcAcceptReason",
  "submit-draft-qc",
  "accept-classification",
  "preview",
]);

function getDocSha256(doc) {
  if (!doc) return "—";
  if (doc.content_checksum) return doc.content_checksum;
  if (doc.contentChecksum) return doc.contentChecksum;
  if (doc.sha256_hash) return doc.sha256_hash;
  if (doc.sha256) return doc.sha256;
  if (doc.cryptographic_seal) return doc.cryptographic_seal;
  if (doc.merkle_hash) return doc.merkle_hash;
  const report = getIntelligenceReport(doc);
  if (report?.sha256_hash) return report.sha256_hash;
  return "—";
}

function getIntelligenceReport(doc) {
  if (!doc) return null;
  const meta = doc.metadata_json || doc.metadataJson || {};
  return meta.document_intelligence || null;
}

function hasClassifierConfidence(doc) {
  const report = getIntelligenceReport(doc);
  return typeof report?.primary_classification?.confidence === "number";
}

function getDocConfidence(doc) {
  const report = getIntelligenceReport(doc);
  if (typeof report?.primary_classification?.confidence === "number") {
    const pct = Math.round(report.primary_classification.confidence * 100);
    const tier = report.confidence_tier || "CLASSIFIED";
    return `${pct}% (${tier})`;
  }
  return "Not Classified";
}

function getDocModality(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.modality) return report.modality;
  return doc.mime_type && doc.mime_type.includes("pdf")
    ? "PDF_BINARY"
    : "STRUCTURED_FORM";
}

function getSigStatusText(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.signature_analysis) {
    const status = report.signature_analysis.status;
    if (status === "FULLY_SIGNED") return "✓ Fully Signed (Verified)";
    if (status === "PARTIALLY_SIGNED") return "Partially Signed";
    if (status === "UNSIGNED") return "Unsigned";
    if (status === "SIGNATURE_NOT_REQUIRED") return "Not Required";
  }
  if (doc.signer || doc.signing_timestamp) {
    return "✓ Signed";
  }
  return "Unsigned";
}

function getSigStatusClass(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.signature_analysis) {
    const status = report.signature_analysis.status;
    if (status === "FULLY_SIGNED") return "text-success";
    if (status === "PARTIALLY_SIGNED" || status === "UNSIGNED")
      return "text-warning";
  }
  if (doc.signer || doc.signing_timestamp) {
    return "text-success";
  }
  return "";
}

function getExtractedInvestigator(doc) {
  const report = getIntelligenceReport(doc);
  if (
    report &&
    report.extracted_metadata &&
    report.extracted_metadata.investigator_name
  ) {
    return report.extracted_metadata.investigator_name;
  }
  return doc.document_owner_id || null;
}

function getExtractedProtocol(doc) {
  const report = getIntelligenceReport(doc);
  if (
    report &&
    report.extracted_metadata &&
    report.extracted_metadata.protocol_number
  ) {
    return report.extracted_metadata.protocol_number;
  }
  return doc.study_id || null;
}
</script>

<style scoped>
.metadata-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  max-width: 100vw;
  background: var(--semantic-color-backdrop-soft);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

:deep(.metadata-drawer) {
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
  max-width: 100vw;
  background: var(--semantic-color-surface);
  height: 100%;
  display: flex;
  flex-direction: column;
  box-shadow: var(--shadow-drawer-left);
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

:deep(.drawer-body) {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.btn-close-drawer {
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.drawer-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-heading {
  margin: 0;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--text-muted);
}

.meta-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 12px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.meta-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.meta-val {
  font-size: 13px;
  font-weight: 600;
  color: var(--semantic-color-text);
  overflow-wrap: anywhere;
}

.font-mono {
  font-family: var(--semantic-font-mono);
}

.reason-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-left: 3px solid var(--semantic-color-primary);
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 12px;
}

.crypto-seal-card {
  padding: 10px 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px dashed var(--border);
  border-radius: 6px;
}

.intelligence-card {
  padding: 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
}

.qc-accept-panel {
  margin-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

:deep(.qc-reason-input) {
  width: 100%;
  box-sizing: border-box;
  padding: 6px 8px;
  font-size: 12px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-radius: 4px;
}

.qc-role-note {
  margin: 0;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-warning-text);
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 4px;
  padding: 6px 8px;
}

.qc-error-note {
  margin: 0;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-error-text-dark);
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 4px;
  padding: 6px 8px;
}

.text-success {
  color: var(--semantic-color-success);
}

.text-warning {
  color: var(--semantic-color-warning);
}
</style>
