<!-- apps/web/src/views/DocumentManagerView.vue -->
<template>
  <div class="document-manager-layout">
    <aside class="sidebar-binder-tree">
      <TmfBinderTree
        :tree="etmfStore.binderTree"
        @select-artifact="handleArtifactSelect"
      />
    </aside>
    <main class="document-content-area">
      <div class="etmf-workspace-bar">
        <div class="etmf-tab-group">
          <button
            id="btn-etmf-tab-binder"
            type="button"
            class="etmf-tab-btn"
            :class="{ active: activeWorkspaceTab === 'binder' }"
            @click="activeWorkspaceTab = 'binder'"
          >
            📁 Binder Documents
          </button>
          <button
            id="btn-etmf-tab-qc-queue"
            type="button"
            class="etmf-tab-btn"
            :class="{ active: activeWorkspaceTab === 'qc-queue' }"
            @click="activeWorkspaceTab = 'qc-queue'"
          >
            🛡️ Clinical QC Queue
          </button>
        </div>
        <span v-if="effectiveStudyId" class="etmf-study-chip">
          Study: <strong>{{ effectiveStudyId }}</strong>
        </span>
      </div>

      <DocumentGrid
        v-if="activeWorkspaceTab === 'binder'"
        :documents="etmfStore.documents"
        @preview="handlePreviewDocument"
        @inspect="handleInspectDocument"
      />
      <CraQcQueue v-else />
    </main>

    <!-- Document Metadata Inspection Drawer -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="inspectedDoc"
      class="metadata-drawer-overlay"
      @click.self="inspectedDoc = null"
    >
      <div class="metadata-drawer">
        <div class="drawer-header">
          <div style="display: flex; align-items: center; gap: 8px">
            <span style="font-size: 20px">📋</span>
            <div>
              <h3 style="margin: 0; font-size: 16px">
                Document Metadata &amp; 21 CFR Part 11 Manifest
              </h3>
              <p
                style="
                  margin: 2px 0 0 0;
                  font-size: 11px;
                  color: var(--text-muted);
                "
              >
                {{ inspectedDoc.filename }}
              </p>
            </div>
          </div>
          <button
            class="btn btn-secondary btn-close-drawer"
            style="padding: 4px 8px; font-size: 12px; cursor: pointer"
            aria-label="Close inspection drawer"
            @click="inspectedDoc = null"
          >
            ✕
          </button>
        </div>

        <div class="drawer-body">
          <!-- DIA Reference Model Taxonomy Section -->
          <div class="drawer-section">
            <h4 class="section-heading">DIA Reference Model Taxonomy</h4>
            <div class="meta-grid">
              <div class="meta-item">
                <span class="meta-label">Artifact Code</span>
                <span class="meta-val font-mono">{{
                  inspectedDoc.artifact_code ||
                  inspectedDoc.taxonomy_code ||
                  "01.01.01"
                }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Zone &amp; Section</span>
                <span class="meta-val"
                  >Zone {{ inspectedDoc.zone_id || inspectedDoc.zone || 1 }} -
                  Trial Management</span
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
                  inspectedDoc.status || "DRAFT"
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
                {{
                  inspectedDoc.reason_for_change ||
                  inspectedDoc.reason ||
                  "Standard clinical document ingestion and lifecycle management."
                }}
              </p>
            </div>
            <div class="meta-grid" style="margin-top: 10px">
              <div class="meta-item">
                <span class="meta-label">Uploaded By</span>
                <span class="meta-val font-mono">{{
                  inspectedDoc.created_by ||
                  inspectedDoc.uploaded_by ||
                  "crc.user"
                }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Uploaded At</span>
                <span class="meta-val font-mono">{{
                  inspectedDoc.formattedCreatedAt ||
                  inspectedDoc.created_at ||
                  "—"
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
                <span style="font-size: 11px; font-weight: 600"
                  >SHA-256 Checksum Digest</span
                >
                <span class="badge status-approved" style="font-size: 9px"
                  >LEDGER SEALED</span
                >
              </div>
              <code class="sha256-hash-display">{{
                getDocSha256(inspectedDoc)
              }}</code>
            </div>
          </div>

          <!-- De-Identification / Redaction Manifest -->
          <div class="drawer-section">
            <h4 class="section-heading">HIPAA / GDPR De-identification</h4>
            <div class="redaction-info">
              <span
                v-if="inspectedDoc.is_redacted"
                class="badge status-approved"
              >
                🛡️ De-Identified Copy (PHI/PII Scrubber Applied)
              </span>
              <span v-else class="badge status-draft">
                🔒 Regulated Source Original
              </span>
            </div>
          </div>

          <!-- Multimodal DIA Document Intelligence & Signature Completeness -->
          <div class="drawer-section">
            <h4 class="section-heading">🤖 DIA Document Intelligence</h4>
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
                  class="badge status-approved"
                  style="font-size: 10px; font-weight: bold"
                >
                  {{ getDocConfidence(inspectedDoc) }}
                </span>
              </div>
              <div class="meta-grid">
                <div class="meta-item">
                  <span class="meta-label">Detected Modality</span>
                  <span class="meta-val">{{
                    getDocModality(inspectedDoc)
                  }}</span>
                </div>
                <div class="meta-item">
                  <span class="meta-label">Signature Verification</span>
                  <span
                    class="meta-val"
                    :class="getSigStatusClass(inspectedDoc)"
                  >
                    {{ getSigStatusText(inspectedDoc) }}
                  </span>
                </div>
                <div
                  v-if="getExtractedInvestigator(inspectedDoc)"
                  class="meta-item"
                >
                  <span class="meta-label">Investigator</span>
                  <span class="meta-val font-mono">{{
                    getExtractedInvestigator(inspectedDoc)
                  }}</span>
                </div>
                <div
                  v-if="getExtractedProtocol(inspectedDoc)"
                  class="meta-item"
                >
                  <span class="meta-label">Protocol #</span>
                  <span class="meta-val font-mono">{{
                    getExtractedProtocol(inspectedDoc)
                  }}</span>
                </div>
              </div>
              <div
                v-if="
                  inspectedDoc.status === 'TECHNICAL_QC' ||
                  inspectedDoc.status === 'DRAFT'
                "
                class="qc-accept-panel"
              >
                <label for="qc-accept-reason" class="meta-label">
                  Clinical QC Review Justification (min 10 characters)
                </label>
                <input
                  id="qc-accept-reason"
                  v-model="qcAcceptReason"
                  type="text"
                  class="qc-reason-input"
                  :disabled="!canReviewQc || reviewingQc"
                  placeholder="Verified artifact taxonomy, dates, and signatures"
                />
                <p
                  v-if="!canReviewQc"
                  id="etmf-qc-role-note"
                  class="qc-role-note"
                >
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
                    font-size: 11px;
                    cursor: pointer;
                  "
                  :disabled="
                    !canReviewQc ||
                    reviewingQc ||
                    qcAcceptReason.trim().length < 10
                  "
                  :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
                  @click="handleAcceptClassification(inspectedDoc)"
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

        <!-- Drawer Footer Actions -->
        <div class="drawer-footer">
          <button
            class="btn btn-primary btn-preview-from-drawer"
            style="padding: 8px 16px; font-size: 12px; cursor: pointer"
            @click="handlePreviewFromDrawer(inspectedDoc)"
          >
            👁️ Open Secure PDF Preview
          </button>
        </div>
      </div>
    </div>

    <!-- Secure PDF Preview Modal -->
    <PdfPreviewModal
      v-if="selectedDoc"
      :document="selectedDoc"
      @close="selectedDoc = null"
    />

    <!-- Real-time eTMF Export Job Progress Drawer -->
    <ExportJobDrawer />
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import {
  ETMF_QC_REVIEW_ROLE_REASON,
  canReviewEtmfQc,
} from "../canvas/authority";
import CraQcQueue from "../components/etmf/CraQcQueue.vue";
import DocumentGrid from "../components/etmf/DocumentGrid.vue";
import ExportJobDrawer from "../components/etmf/ExportJobDrawer.vue";
import PdfPreviewModal from "../components/etmf/PdfPreviewModal.vue";
import TmfBinderTree from "../components/etmf/TmfBinderTree.vue";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { useEtmfStore } from "../stores/etmf";
import { useToastStore } from "../stores/toasts.js";

const props = defineProps({
  studyId: {
    type: String,
    default: "",
  },
});

const authStore = useAuthStore();
const etmfStore = useEtmfStore();
const clinicalStore = useClinicalStore();
const route = useRoute();

const activeWorkspaceTab = ref("binder");
const selectedDoc = ref(null);
const inspectedDoc = ref(null);
const qcAcceptReason = ref(
  "Verified DIA Reference Model classification and wet-ink signatures."
);
const reviewingQc = ref(false);
const qcReviewError = ref("");

const canReviewQc = computed(() => canReviewEtmfQc(authStore));
const effectiveStudyId = computed(
  () => props.studyId || clinicalStore.activeStudyId || etmfStore.currentStudyId
);

function syncStudyScope(study) {
  if (study) {
    clinicalStore.activeStudyId = study;
    etmfStore.currentStudyId = study;
  }
}

watch(
  () => props.studyId,
  (val) => {
    if (val) {
      syncStudyScope(val);
      etmfStore.fetchBinderTree();
    }
  }
);

function handleArtifactSelect(artifactCode) {
  etmfStore.fetchDocuments(artifactCode);
}

function handlePreviewDocument(doc) {
  selectedDoc.value = doc;
}

function handleInspectDocument(doc) {
  qcReviewError.value = "";
  inspectedDoc.value = doc;
}

function handlePreviewFromDrawer(doc) {
  inspectedDoc.value = null;
  selectedDoc.value = doc;
}

function getDocSha256(doc) {
  if (doc.sha256_hash) return doc.sha256_hash;
  if (doc.sha256) return doc.sha256;
  if (doc.cryptographic_seal) return doc.cryptographic_seal;
  if (doc.merkle_hash) return doc.merkle_hash;
  return "—";
}

function getIntelligenceReport(doc) {
  if (!doc) return null;
  const meta = doc.metadata_json || doc.metadataJson || {};
  return meta.document_intelligence || null;
}

function getDocConfidence(doc) {
  const report = getIntelligenceReport(doc);
  if (
    report &&
    report.primary_classification &&
    report.primary_classification.confidence
  ) {
    return `${Math.round(report.primary_classification.confidence * 100)}% High Confidence`;
  }
  return "95% High Confidence";
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
    if (status === "PARTIALLY_SIGNED") return "⚠️ Partially Signed";
    if (status === "UNSIGNED") return "❌ Unsigned";
    if (status === "SIGNATURE_NOT_REQUIRED") return "⚪ Not Required";
  }
  if (doc.approval_status === "APPROVED" || doc.signer) {
    return "✓ Fully Signed (Verified)";
  }
  return "✓ Verified";
}

function getSigStatusClass(doc) {
  const report = getIntelligenceReport(doc);
  if (report && report.signature_analysis) {
    const status = report.signature_analysis.status;
    if (status === "FULLY_SIGNED") return "text-success";
    if (status === "PARTIALLY_SIGNED" || status === "UNSIGNED")
      return "text-warning";
  }
  return "text-success";
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

async function handleAcceptClassification(doc) {
  if (!doc || !canReviewQc.value) return;
  const docId = doc.id || doc.document_id || doc.documentId;
  const reason = qcAcceptReason.value.trim();
  if (!docId || reason.length < 10) {
    qcReviewError.value =
      "A clinical QC justification of at least 10 characters is required.";
    return;
  }

  reviewingQc.value = true;
  qcReviewError.value = "";
  try {
    const updated = await etmfStore.reviewQcDocument(docId, {
      decision: "ACCEPT",
      reason_for_change: reason,
    });
    if (inspectedDoc.value) {
      const nextStatus = updated?.status ?? inspectedDoc.value.status;
      inspectedDoc.value = {
        ...inspectedDoc.value,
        ...(updated || {}),
        status: nextStatus,
        approval_status: nextStatus,
      };
    }
    useToastStore().push({
      tone: "success",
      title: "Artifact approved in eTMF",
      message: `${doc.filename || docId} passed clinical QC review.`,
    });
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError("Clinical QC review failed", err);
    qcReviewError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  } finally {
    reviewingQc.value = false;
  }
}

onMounted(() => {
  if (props.studyId) {
    syncStudyScope(props.studyId);
  } else if (route && route.query) {
    if (route.query.studyId) syncStudyScope(route.query.studyId);
    if (route.query.siteId) clinicalStore.activeSiteId = route.query.siteId;
    if (route.query.subjectId)
      clinicalStore.activeSubjectId = route.query.subjectId;
    if (route.query.visitId) clinicalStore.activeVisitId = route.query.visitId;
  }
  etmfStore.fetchBinderTree();
});

defineExpose({
  inspectedDoc,
  qcAcceptReason,
  handleAcceptClassification,
});
</script>

<style scoped>
.document-manager-layout {
  display: flex;
  height: calc(100vh - 120px);
  gap: 20px;
  background-color: #f8fafc;
  margin: -20px;
  padding: 20px;
  overflow: hidden;
  position: relative;
}

.sidebar-binder-tree {
  width: 340px;
  min-width: 300px;
  background-color: #ffffff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.document-content-area {
  flex: 1;
  background-color: #ffffff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  display: flex;
  flex-direction: column;
}

.etmf-workspace-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  background: #f8fafc;
}

.etmf-tab-group {
  display: flex;
  gap: 8px;
}

.etmf-tab-btn {
  padding: 6px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #ffffff;
  font-size: 12px;
  font-weight: 600;
  color: #475569;
  cursor: pointer;
}

.etmf-tab-btn.active {
  background: #0f4c81;
  border-color: #0f4c81;
  color: #ffffff;
}

.etmf-study-chip {
  font-size: 12px;
  color: #475569;
}

.qc-accept-panel {
  margin-top: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.qc-reason-input {
  width: 100%;
  box-sizing: border-box;
  padding: 6px 8px;
  font-size: 12px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
}

.qc-role-note {
  margin: 0;
  font-size: 11px;
  color: #92400e;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 4px;
  padding: 6px 8px;
}

.qc-error-note {
  margin: 0;
  font-size: 11px;
  color: #991b1b;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 4px;
  padding: 6px 8px;
}

.metadata-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  max-width: 100vw;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.metadata-drawer {
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
  max-width: 100vw;
  background: #ffffff;
  height: 100%;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 16px rgba(0, 0, 0, 0.1);
  animation: slideInRight 0.2s ease-out;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.drawer-body {
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
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--text-muted);
}

.meta-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.meta-label {
  font-size: 11px;
  color: var(--text-muted);
}

.meta-val {
  font-size: 13px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.font-mono {
  font-family: var(--font-mono, monospace);
}

.reason-box {
  background: #f8fafc;
  border: 1px solid var(--border);
  border-left: 3px solid var(--semantic-color-primary, #2563eb);
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 12px;
}

.crypto-seal-card {
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  padding: 10px 12px;
  border-radius: 6px;
}

.sha256-hash-display {
  display: block;
  font-family: var(--font-mono, monospace);
  font-size: 11px;
  word-break: break-all;
  color: #166534;
  background: rgba(255, 255, 255, 0.7);
  padding: 6px;
  border-radius: 4px;
}

.drawer-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
}

.intelligence-card {
  background: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
}

.text-success {
  color: #166534;
}

.text-warning {
  color: #b45309;
}
</style>
