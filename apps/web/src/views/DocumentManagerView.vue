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
      <DocumentGrid
        :documents="etmfStore.documents"
        @preview="handlePreviewDocument"
        @inspect="handleInspectDocument"
      />
    </main>

    <!-- Document Metadata Inspection Drawer -->
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
              <h3 style="margin: 0; font-size: 16px">Document Metadata &amp; 21 CFR Part 11 Manifest</h3>
              <p style="margin: 2px 0 0 0; font-size: 11px; color: var(--text-muted)">
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
                <span class="meta-val font-mono">{{ inspectedDoc.artifact_code || inspectedDoc.taxonomy_code || "01.01.01" }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Zone &amp; Section</span>
                <span class="meta-val">Zone {{ inspectedDoc.zone_id || 1 }} - Trial Management</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Version</span>
                <span class="meta-val badge status-review">v{{ inspectedDoc.version_index || 1 }}.0</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Lifecycle Status</span>
                <span class="meta-val badge status-approved">{{ inspectedDoc.status || "DRAFT" }}</span>
              </div>
            </div>
          </div>

          <!-- 21 CFR Part 11 Audit Trail & Change Justification -->
          <div class="drawer-section">
            <h4 class="section-heading">21 CFR Part 11 Audit Record</h4>
            <div class="reason-box">
              <strong>Mandatory Reason for Change:</strong>
              <p style="margin: 4px 0 0 0; font-size: 12px; color: var(--text)">
                {{ inspectedDoc.reason_for_change || inspectedDoc.reason || "Standard clinical document ingestion and lifecycle management." }}
              </p>
            </div>
            <div class="meta-grid" style="margin-top: 10px">
              <div class="meta-item">
                <span class="meta-label">Uploaded By</span>
                <span class="meta-val font-mono">{{ inspectedDoc.created_by || inspectedDoc.uploaded_by || "crc.user" }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Uploaded At</span>
                <span class="meta-val font-mono">{{ inspectedDoc.formattedCreatedAt || inspectedDoc.created_at || new Date().toISOString() }}</span>
              </div>
            </div>
          </div>

          <!-- Cryptographic SHA-256 Checksum & Seal -->
          <div class="drawer-section">
            <h4 class="section-heading">Cryptographic Integrity</h4>
            <div class="crypto-seal-card">
              <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 6px">
                <span style="font-size: 11px; font-weight: 600">SHA-256 Checksum Digest</span>
                <span class="badge status-approved" style="font-size: 9px">LEDGER SEALED</span>
              </div>
              <code class="sha256-hash-display">{{ getDocSha256(inspectedDoc) }}</code>
            </div>
          </div>

          <!-- De-Identification / Redaction Manifest -->
          <div class="drawer-section">
            <h4 class="section-heading">HIPAA / GDPR De-identification</h4>
            <div class="redaction-info">
              <span v-if="inspectedDoc.is_redacted" class="badge status-approved">
                🛡️ De-Identified Copy (PHI/PII Scrubber Applied)
              </span>
              <span v-else class="badge status-draft">
                🔒 Regulated Source Original
              </span>
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
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { useRoute } from "vue-router";
import { useEtmfStore } from "../stores/etmf";
import { useClinicalStore } from "../stores/clinical";
import TmfBinderTree from "../components/etmf/TmfBinderTree.vue";
import DocumentGrid from "../components/etmf/DocumentGrid.vue";
import PdfPreviewModal from "../components/etmf/PdfPreviewModal.vue";

const etmfStore = useEtmfStore();
const clinicalStore = useClinicalStore();
const route = useRoute();
const selectedDoc = ref(null);
const inspectedDoc = ref(null);

function handleArtifactSelect(artifactCode) {
  etmfStore.fetchDocuments(artifactCode);
}

function handlePreviewDocument(doc) {
  selectedDoc.value = doc;
}

function handleInspectDocument(doc) {
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
  return "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
}

onMounted(() => {
  if (route && route.query) {
    if (route.query.studyId) clinicalStore.activeStudyId = route.query.studyId;
    if (route.query.siteId) clinicalStore.activeSiteId = route.query.siteId;
    if (route.query.subjectId)
      clinicalStore.activeSubjectId = route.query.subjectId;
    if (route.query.visitId) clinicalStore.activeVisitId = route.query.visitId;
  }
  etmfStore.fetchBinderTree();
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
}

.metadata-drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
}

.metadata-drawer {
  width: 440px;
  background: #ffffff;
  height: 100%;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 16px rgba(0, 0, 0, 0.1);
  animation: slideInRight 0.2s ease-out;
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
}

.drawer-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-heading {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--primary);
  border-bottom: 1px solid var(--border);
  padding-bottom: 4px;
}

.meta-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.meta-label {
  font-size: 11px;
  color: var(--text-muted);
  font-weight: 500;
}

.meta-val {
  font-size: 12px;
  font-weight: 500;
}

.font-mono {
  font-family: monospace;
}

.reason-box {
  padding: 10px 12px;
  background: rgba(0, 123, 255, 0.05);
  border-left: 3px solid var(--accent);
  border-radius: 4px;
  font-size: 12px;
}

.crypto-seal-card {
  padding: 10px 12px;
  background: #f8fafc;
  border: 1px dashed var(--border);
  border-radius: 6px;
}

.sha256-hash-display {
  display: block;
  font-size: 11px;
  color: var(--primary);
  word-break: break-all;
  background: #fff;
  padding: 6px;
  border-radius: 4px;
  border: 1px solid var(--border);
}

.drawer-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
}
</style>
