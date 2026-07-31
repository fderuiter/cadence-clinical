<template>
  <div class="document-grid-container">
    <div class="grid-header">
      <div class="header-titles">
        <h2>Artifact Documents Registry</h2>
        <p
          v-if="selectedArtifactCode"
          class="active-artifact-subtitle"
        >
          Showing documents for Artifact: <strong>{{ selectedArtifactCode }}</strong>
        </p>
        <p
          v-else
          class="active-artifact-subtitle"
        >
          Please select an artifact from the binder tree to view documents.
        </p>
      </div>
      <button
        class="btn btn-primary upload-trigger-btn"
        :disabled="!selectedArtifactCode"
        @click="openUploadModal"
      >
        <span>📤</span> Upload Regulated Document
      </button>
    </div>

    <!-- Documents Data Table -->
    <div class="table-responsive">
      <table class="documents-table">
        <thead>
          <tr>
            <th>Document Name</th>
            <th>Taxonomy</th>
            <th>Version</th>
            <th>Status</th>
            <th>Uploaded By</th>
            <th>Uploaded At</th>
            <th class="actions-column">
              Actions
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="documents.length === 0">
            <td
              colspan="7"
              class="empty-table-cell"
            >
              No documents have been uploaded for this artifact yet.
            </td>
          </tr>
          <tr
            v-for="doc in documents"
            :key="doc.id"
            class="document-row"
          >
            <td class="doc-name-cell">
              <span class="file-icon">📄</span>
              <span
                class="filename"
                :title="doc.filename"
              >{{ doc.filename }}</span>
            </td>
            <td>
              <span class="taxonomy-pill">
                Z{{ doc.zone }} - S{{ doc.section }} [{{ doc.artifact_code }}]
              </span>
            </td>
            <td>
              <span class="version-tag">v{{ doc.version_index }}.0</span>
            </td>
            <td>
              <span
                class="status-badge"
                :class="getStatusClass(doc.status)"
              >
                {{ formatStatus(doc.status) }}
              </span>
            </td>
            <td>
              <div class="user-meta">
                <span class="username">{{ doc.created_by }}</span>
              </div>
            </td>
            <td class="date-cell">
              {{ formatDate(doc.created_at) }}
            </td>
            <td class="actions-cell">
              <button
                class="btn btn-sm btn-outline-primary"
                title="View Watermarked PDF Preview"
                @click="$emit('preview', doc)"
              >
                👁️ Preview
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- GxP Electronic Record Drag & Drop Upload Modal -->
    <div
      v-if="showUploadModal"
      class="modal-backdrop"
    >
      <div class="modal-card upload-modal">
        <div class="modal-header">
          <h3>FDA 21 CFR Part 11 Compliant Document Ingestion</h3>
          <button
            class="close-modal-btn"
            @click="closeUploadModal"
          >
            ×
          </button>
        </div>

        <div class="modal-body">
          <!-- GxP Compliance Warning Box -->
          <div class="gxp-warning-alert">
            <strong>⚠️ Regulated Record Ingestion Warning:</strong>
            All actions are logged in the append-only eTMF audit ledger. You must provide a valid electronic signature context and change reason justification to submit this record.
          </div>

          <!-- Drag and Drop Target -->
          <div
            class="drag-drop-zone"
            :class="{ 'is-dragging': isDragging }"
            @dragover.prevent="onDragOver"
            @dragleave.prevent="onDragLeave"
            @drop.prevent="onDrop"
            @click="triggerFileSelect"
          >
            <input
              ref="fileInputRef"
              type="file"
              class="hidden-file-input"
              accept=".pdf"
              @change="onFileSelected"
            >
            <div class="drop-prompt-content">
              <span class="upload-cloud-icon">☁️</span>
              <p
                v-if="!selectedFile"
                class="drop-text"
              >
                Drag and drop your regulated PDF here, or <span class="highlight">browse</span>
              </p>
              <div
                v-else
                class="selected-file-details"
              >
                <p class="file-name-success">
                  🎉 {{ selectedFile.name }}
                </p>
                <p class="file-size-meta">
                  Size: {{ (selectedFile.size / 1024).toFixed(1) }} KB | Type: {{ selectedFile.type || "application/pdf" }}
                </p>
              </div>
            </div>
          </div>

          <!-- Upload Metadata Form -->
          <form
            class="upload-meta-form"
            @submit.prevent="submitUpload"
          >
            <div class="form-group-row">
              <div class="form-group">
                <label>Target Study ID</label>
                <input
                  v-model="studyId"
                  type="text"
                  class="form-control"
                  readonly
                >
              </div>
              <div class="form-group">
                <label>Site ID (Optional)</label>
                <input
                  v-model="siteId"
                  type="text"
                  placeholder="e.g. SITE-01"
                  class="form-control"
                >
              </div>
            </div>

            <div class="form-group-row">
              <div class="form-group">
                <label>DIA TMF Artifact Code</label>
                <input
                  v-model="artifactCode"
                  type="text"
                  class="form-control"
                  readonly
                >
              </div>
              <div class="form-group">
                <label>Artifact Type (Label)</label>
                <input
                  v-model="artifactType"
                  type="text"
                  class="form-control"
                  readonly
                >
              </div>
            </div>

            <!-- Mandatory Reason for Change under 21 CFR Part 11 -->
            <div class="form-group">
              <label class="required-label">
                Reason for Change / Upload Justification
              </label>
              <textarea
                v-model="reasonForChange"
                rows="3"
                placeholder="Specify the regulatory or scientific justification for ingesting this record version (e.g. 'Initial Approved protocol for site initiation', 'Site license renewal')."
                class="form-control"
                required
              />
            </div>

            <div
              v-if="uploadError"
              class="upload-error-banner"
            >
              ❌ {{ uploadError }}
            </div>

            <div class="modal-footer-actions">
              <button
                type="button"
                class="btn btn-outline-secondary"
                :disabled="isSubmitting"
                @click="closeUploadModal"
              >
                Cancel
              </button>
              <button
                type="submit"
                class="btn btn-primary"
                :disabled="isSubmitting || !selectedFile || !reasonForChange.trim()"
              >
                <span v-if="isSubmitting">Uploading & Indexing...</span>
                <span v-else>🔒 Commit Electronic Record</span>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";
import { useEtmfStore } from "../../stores/etmf";

defineProps({
  documents: {
    type: Array,
    required: true,
  },
});

defineEmits(["preview"]);

const etmfStore = useEtmfStore();

const selectedArtifactCode = ref("");
const showUploadModal = ref(false);
const isDragging = ref(false);
const isSubmitting = ref(false);
const uploadError = ref("");

// Selected File reference
const selectedFile = ref(null);
const fileInputRef = ref(null);

// Form Fields
const studyId = ref("STUDY-USDM-001");
const siteId = ref("");
const artifactCode = ref("");
const artifactType = ref("");
const reasonForChange = ref("");

// Listen for artifact selections on the store to update current context
watch(
  () => etmfStore.selectedArtifactId,
  (newCode) => {
    selectedArtifactCode.value = newCode || "";
    if (newCode) {
      artifactCode.value = newCode;

      // Look up artifact label from local tree structure
      let foundName = "Clinical Trial Document";
      etmfStore.binderTree.forEach((zone) => {
        zone.children?.forEach((sec) => {
          sec.children?.forEach((art) => {
            if (art.code === newCode) {
              foundName = art.name;
            }
          });
        });
      });
      artifactType.value = foundName;
    }
  },
  { immediate: true }
);

function getStatusClass(status) {
  const s = (status || "").toLowerCase();
  if (s.includes("draft")) return "status-draft";
  if (s.includes("review") || s.includes("pending")) return "status-pending";
  if (s.includes("approved") || s.includes("active")) return "status-approved";
  if (s.includes("archive")) return "status-archived";
  return "status-default";
}

function formatStatus(status) {
  const s = status || "DRAFT";
  return s.toUpperCase().replace(/_/g, " ");
}

function formatDate(dateStr) {
  if (!dateStr) return "N/A";
  try {
    const d = new Date(dateStr);
    return d.toLocaleString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return dateStr;
  }
}

// Modal Handlers
function openUploadModal() {
  if (!selectedArtifactCode.value) return;
  studyId.value = etmfStore.currentStudyId;
  selectedFile.value = null;
  reasonForChange.value = "";
  uploadError.value = "";
  showUploadModal.value = true;
}

function closeUploadModal() {
  showUploadModal.value = false;
  selectedFile.value = null;
}

// Drag & Drop
function onDragOver() {
  isDragging.value = true;
}

function onDragLeave() {
  isDragging.value = false;
}

function onDrop(e) {
  isDragging.value = false;
  const files = e.dataTransfer?.files;
  if (files && files.length > 0) {
    selectedFile.value = files[0];
  }
}

function triggerFileSelect() {
  fileInputRef.value?.click();
}

function onFileSelected(e) {
  const target = e.target;
  if (target.files && target.files.length > 0) {
    selectedFile.value = target.files[0];
  }
}

// Form Submission
async function submitUpload() {
  if (!selectedFile.value || !reasonForChange.value.trim()) {
    uploadError.value = "Please select a file and provide a change reason.";
    return;
  }

  isSubmitting.value = true;
  uploadError.value = "";

  try {
    const file = selectedFile.value;

    // Helper to read file as text to supply to the indexer/content payload field
    const fileContent = await new Promise((resolve) => {
      const reader = new FileReader();
      reader.onload = (e) => {
        resolve((e.target?.result) || "Mock text content of eTMF PDF");
      };
      reader.onerror = () => {
        resolve("Mock fallback PDF plain text index content.");
      };
      reader.readAsText(file.slice(0, 5000)); // Read first few KB as text safely
    });

    // Extract zone and section integers/strings dynamically from code
    const codeParts = artifactCode.value.split(".");
    const zoneInt = parseInt(codeParts[0]) || 1;
    const sectionStr = codeParts.slice(0, 2).join(".");

    const formData = new FormData();
    formData.append("study_id", studyId.value);
    formData.append("site_id", siteId.value || "");
    formData.append("artifact_type", artifactType.value);
    formData.append("filename", file.name);
    formData.append("content", `Document content index for: ${file.name}. Raw plaintext: ${fileContent}`);
    formData.append("mime_type", file.type || "application/pdf");
    formData.append("artifact_code", artifactCode.value);
    formData.append("zone", String(zoneInt));
    formData.append("section", sectionStr);
    formData.append("reason_for_change", reasonForChange.value);

    await etmfStore.uploadDocument(formData);

    // Success
    closeUploadModal();
  } catch (err) {
    console.error("Upload failed in component:", err);
    uploadError.value = err.message || "Ingestion transaction rejected. Verify site/study lock status.";
  } finally {
    isSubmitting.value = false;
  }
}
</script>

<style scoped>
.document-grid-container {
  display: flex;
  flex-direction: column;
  background-color: #ffffff;
  padding: 24px;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  height: 100%;
}

.grid-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 24px;
  border-bottom: 1px solid #f1f5f9;
  padding-bottom: 16px;
}

.header-titles h2 {
  font-size: 1.4rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0 0 6px 0;
}

.active-artifact-subtitle {
  font-size: 0.9rem;
  color: #64748b;
  margin: 0;
}

.upload-trigger-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  font-weight: 600;
  font-size: 0.9rem;
  border-radius: 6px;
}

.table-responsive {
  flex: 1;
  overflow-x: auto;
}

.documents-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 0.9rem;
}

.documents-table th {
  background-color: #f8fafc;
  color: #475569;
  font-weight: 600;
  padding: 12px 16px;
  border-bottom: 2px solid #e2e8f0;
}

.documents-table td {
  padding: 14px 16px;
  border-bottom: 1px solid #e2e8f0;
  color: #334155;
  vertical-align: middle;
}

.document-row:hover {
  background-color: #f8fafc;
}

.empty-table-cell {
  text-align: center;
  padding: 32px;
  color: #64748b;
  font-style: italic;
}

.doc-name-cell {
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: 250px;
}

.doc-name-cell .filename {
  font-weight: 600;
  color: #1e293b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.taxonomy-pill {
  font-family: monospace;
  font-size: 0.8rem;
  background-color: #f1f5f9;
  color: #475569;
  padding: 3px 8px;
  border-radius: 4px;
  font-weight: 500;
}

.version-tag {
  font-family: monospace;
  font-weight: 700;
  color: #0f172a;
}

/* Status Badges */
.status-badge {
  display: inline-block;
  padding: 4px 10px;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
}

.status-draft {
  background-color: #f1f5f9;
  color: #64748b;
}

.status-pending {
  background-color: #fef3c7;
  color: #d97706;
}

.status-approved {
  background-color: #dcfce7;
  color: #15803d;
}

.status-archived {
  background-color: #fee2e2;
  color: #b91c1c;
}

.status-default {
  background-color: #e2e8f0;
  color: #475569;
}

.user-meta .username {
  font-weight: 500;
}

.date-cell {
  color: #64748b;
  font-size: 0.85rem;
}

/* Modals */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background-color: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background-color: #ffffff;
  border-radius: 12px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04);
  max-width: 600px;
  width: 90%;
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-bottom: 1px solid #e2e8f0;
}

.modal-header h3 {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: #0f172a;
}

.close-modal-btn {
  background: none;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
  color: #94a3b8;
  padding: 0;
}

.close-modal-btn:hover {
  color: #475569;
}

.modal-body {
  padding: 24px;
  max-height: 80vh;
  overflow-y: auto;
}

.gxp-warning-alert {
  background-color: #fffbeb;
  border-left: 4px solid #f59e0b;
  padding: 12px 16px;
  border-radius: 6px;
  font-size: 0.85rem;
  color: #78350f;
  margin-bottom: 20px;
  line-height: 1.4;
}

.drag-drop-zone {
  border: 2px dashed #cbd5e1;
  background-color: #f8fafc;
  border-radius: 8px;
  padding: 32px 16px;
  text-align: center;
  cursor: pointer;
  transition: all 0.2s;
  margin-bottom: 20px;
}

.drag-drop-zone:hover,
.drag-drop-zone.is-dragging {
  border-color: #2563eb;
  background-color: #eff6ff;
}

.hidden-file-input {
  display: none;
}

.upload-cloud-icon {
  font-size: 2.5rem;
  color: #94a3b8;
  display: block;
  margin-bottom: 8px;
}

.drop-text {
  font-size: 0.9rem;
  color: #475569;
  margin: 0;
}

.drop-text .highlight {
  color: #2563eb;
  font-weight: 600;
  text-decoration: underline;
}

.file-name-success {
  font-weight: 700;
  color: #166534;
  margin: 0 0 4px 0;
}

.file-size-meta {
  font-size: 0.8rem;
  color: #64748b;
  margin: 0;
}

.upload-meta-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-group-row {
  display: flex;
  gap: 16px;
}

.form-group-row .form-group {
  flex: 1;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #334155;
}

.required-label::after {
  content: " *";
  color: #ef4444;
}

.form-control {
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.9rem;
  outline: none;
  transition: border-color 0.2s;
  background-color: #ffffff;
}

.form-control:focus {
  border-color: #2563eb;
}

.form-control[readonly] {
  background-color: #f1f5f9;
  color: #64748b;
  cursor: not-allowed;
}

.upload-error-banner {
  background-color: #fef2f2;
  border: 1px solid #fca5a5;
  color: #991b1b;
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.modal-footer-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 24px;
  border-top: 1px solid #e2e8f0;
  padding-top: 16px;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 8px 16px;
  font-size: 0.9rem;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-primary {
  background-color: #2563eb;
  color: #ffffff;
  border: 1px solid #2563eb;
}

.btn-primary:hover:not(:disabled) {
  background-color: #1d4ed8;
}

.btn-primary:disabled {
  background-color: #93c5fd;
  border-color: #93c5fd;
  cursor: not-allowed;
}

.btn-outline-primary {
  background-color: transparent;
  color: #2563eb;
  border: 1px solid #2563eb;
}

.btn-outline-primary:hover {
  background-color: #eff6ff;
}

.btn-outline-secondary {
  background-color: transparent;
  color: #475569;
  border: 1px solid #cbd5e1;
}

.btn-outline-secondary:hover {
  background-color: #f8fafc;
}

.btn-sm {
  padding: 4px 10px;
  font-size: 0.8rem;
}
</style>
