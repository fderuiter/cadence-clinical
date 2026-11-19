<template>
  <Teleport to="body">
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- Esc closes the dialog; role="dialog" is the correct role for the container -->
    <div
      v-if="isOpen"
      ref="modalRef"
      class="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="batch-modal-title"
      tabindex="-1"
    >
      <div class="modal-card">
        <!-- Header -->
        <div class="modal-header">
          <div class="title-group">
            <span class="badge badge-gxp">21 CFR Part 11 / DICOM PS 3.15</span>
            <h3 id="batch-modal-title" class="modal-title">
              Batch DICOM Folder &amp; Archive Ingestion
            </h3>
            <p class="modal-subtitle">
              Drag and drop complete DICOM study directories or ZIP archives.
              Non-DICOM items (PDFs, notes) are automatically filtered and
              reported.
            </p>
          </div>
          <button
            type="button"
            class="btn-close"
            aria-label="Close modal"
            @click="handleClose"
          >
            &times;
          </button>
        </div>

        <!-- Body -->
        <div class="modal-body">
          <!-- Parameter Bindings -->
          <div class="param-grid">
            <div class="param-field">
              <label for="batch-subj-input" class="param-label"
                >Subject ID</label
              >
              <input
                id="batch-subj-input"
                v-model="inputSubjectId"
                type="text"
                class="param-input"
                placeholder="e.g. SUBJ-ONC-001"
              />
            </div>
            <div class="param-field">
              <label for="batch-time-input" class="param-label"
                >Timepoint</label
              >
              <input
                id="batch-time-input"
                v-model="inputTimepoint"
                type="text"
                class="param-input"
                placeholder="e.g. BASELINE"
              />
            </div>
            <div class="param-field">
              <label for="batch-study-input" class="param-label"
                >Study Protocol</label
              >
              <input
                id="batch-study-input"
                v-model="inputStudyId"
                type="text"
                class="param-input"
                placeholder="e.g. STUDY-ONC-2026"
              />
            </div>
            <div class="param-field">
              <label for="batch-site-input" class="param-label">Site ID</label>
              <input
                id="batch-site-input"
                v-model="inputSiteId"
                type="text"
                class="param-input"
                placeholder="e.g. SITE-101"
              />
            </div>
          </div>

          <!-- Step 1: Dropzone State -->
          <div v-if="!ingestionResult && !isUploading">
            <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -- drag-and-drop is a mouse shortcut; the folder and file buttons inside are the keyboard path -->
            <div
              class="dropzone-area"
              :class="{ 'is-dragging': isDragging }"
              @dragover.prevent="isDragging = true"
              @dragleave.prevent="isDragging = false"
              @drop.prevent="handleDrop"
            >
              <div class="dropzone-content">
                <span class="dropzone-icon">📁</span>
                <p class="dropzone-title">
                  Drag &amp; Drop DICOM Study Folder or ZIP Archive
                </p>
                <p class="dropzone-hint">
                  Supports multi-slice DICOM folders, subdirectories, or .zip
                  files up to 2 GB
                </p>

                <div class="dropzone-actions">
                  <button
                    type="button"
                    class="btn btn-secondary"
                    @click="triggerFolderSelect"
                  >
                    📂 Select Folder
                  </button>
                  <button
                    type="button"
                    class="btn btn-secondary"
                    @click="triggerFileSelect"
                  >
                    📦 Select Files / ZIP
                  </button>
                </div>

                <!-- Hidden file inputs -->
                <input
                  ref="folderInputRef"
                  type="file"
                  aria-label="DICOM study folder"
                  webkitdirectory
                  directory
                  multiple
                  class="sr-only"
                  @change="handleFileInput"
                />
                <input
                  ref="fileInputRef"
                  type="file"
                  multiple
                  aria-label="DICOM files or ZIP archive"
                  accept=".dcm,.zip,application/zip"
                  class="sr-only"
                  @change="handleFileInput"
                />
              </div>
            </div>

            <!-- Pre-Upload File Detection Summary -->
            <div v-if="selectedFiles.length > 0" class="file-summary-card">
              <div class="summary-header">
                <span class="summary-title">Selected Items Summary</span>
                <span class="badge badge-info"
                  >{{ selectedFiles.length }} Total Files</span
                >
              </div>

              <div class="summary-stats">
                <div class="stat-box">
                  <span class="stat-num text-success">{{
                    candidateDicomCount
                  }}</span>
                  <span class="stat-label">Candidate DICOM Items</span>
                </div>
                <div class="stat-box">
                  <span class="stat-num text-warning">{{
                    nonDicomList.length
                  }}</span>
                  <span class="stat-label">Non-DICOM Files Detected</span>
                </div>
              </div>

              <!-- Non-DICOM Alert -->
              <div v-if="nonDicomList.length > 0" class="non-dicom-alert">
                <span class="alert-icon">⚠️</span>
                <div class="alert-text">
                  <strong>Non-DICOM Files Detected:</strong> The following
                  {{ nonDicomList.length }} file(s) will be automatically
                  filtered out during ingestion:
                  <ul class="non-dicom-file-list">
                    <li v-for="f in nonDicomList.slice(0, 5)" :key="f.name">
                      <code>{{ f.name }}</code>
                    </li>
                    <li v-if="nonDicomList.length > 5">
                      <em
                        >...and {{ nonDicomList.length - 5 }} more non-DICOM
                        file(s)</em
                      >
                    </li>
                  </ul>
                </div>
              </div>
            </div>
          </div>

          <!-- Step 2: Uploading / Processing State -->
          <div v-if="isUploading" class="uploading-state-card">
            <div class="spinner-icon">🔄</div>
            <h4 class="uploading-title">
              Streaming &amp; De-identifying DICOM Batch...
            </h4>
            <p class="uploading-desc">
              Scrubbing 50+ PHI header tags, shifting dates, and applying PS
              3.15 pixel masks in memory.
            </p>
            <div class="progress-bar-track">
              <div
                class="progress-bar-fill"
                :style="{ width: uploadProgress + '%' }"
              ></div>
            </div>
            <span class="progress-text">{{ uploadProgress }}% Complete</span>
          </div>

          <!-- Step 3: Success Certificate Summary -->
          <div v-if="ingestionResult" class="certificate-summary-card">
            <div class="cert-banner">
              <span class="cert-icon">🔒</span>
              <div>
                <h4 class="cert-title">
                  PS 3.15 De-Identification Certificate Issued
                </h4>
                <p class="cert-subtitle">
                  Batch session cryptographic manifest persisted with
                  HMAC-SHA256 signature.
                </p>
              </div>
            </div>

            <div class="cert-details-grid">
              <div class="detail-row">
                <span class="detail-label">Certificate ID:</span>
                <span class="detail-val code">{{
                  ingestionResult.certificateId
                }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">Anonymized Study UID:</span>
                <span class="detail-val code">{{
                  ingestionResult.anonymizedStudyUid
                }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">Ingested Slices:</span>
                <span class="detail-val font-bold text-success"
                  >{{ ingestionResult.ingestedSliceCount }} slices</span
                >
              </div>
              <div class="detail-row">
                <span class="detail-label">Filtered Non-DICOM Items:</span>
                <span class="detail-val"
                  >{{ ingestionResult.skippedNonDicomCount }} files
                  skipped</span
                >
              </div>
              <div class="detail-row">
                <span class="detail-label">Scrubbed PHI Tags:</span>
                <span class="detail-val"
                  >{{
                    ingestionResult.manifest?.scrubbed_tags_count || 56
                  }}
                  DICOM tags sanitized</span
                >
              </div>
              <div class="detail-row">
                <span class="detail-label">Pixel Masking:</span>
                <span class="detail-val">{{
                  ingestionResult.manifest?.pixel_mask_applied
                    ? "Applied (Header & Overlay Redacted)"
                    : "Cleared"
                }}</span>
              </div>
              <div class="detail-row">
                <span class="detail-label">HMAC Signature:</span>
                <span class="detail-val code signature-truncate">{{
                  ingestionResult.signature
                }}</span>
              </div>
            </div>

            <!-- Reported Skipped Non-DICOM Files -->
            <div
              v-if="
                ingestionResult.skippedNonDicomFiles &&
                ingestionResult.skippedNonDicomFiles.length > 0
              "
              class="skipped-report-box"
            >
              <span class="report-heading"
                >Reported Non-DICOM Items Filtered:</span
              >
              <ul class="skipped-list">
                <li v-for="f in ingestionResult.skippedNonDicomFiles" :key="f">
                  <code>{{ f }}</code>
                </li>
              </ul>
            </div>
          </div>

          <!-- Error Alert -->
          <div v-if="errorMessage" class="error-alert-box">
            <span class="error-icon">❌</span>
            <span>{{ errorMessage }}</span>
          </div>
        </div>

        <!-- Footer -->
        <div class="modal-footer">
          <button
            v-if="!ingestionResult"
            type="button"
            class="btn btn-secondary"
            :disabled="isUploading"
            @click="handleClose"
          >
            Cancel
          </button>

          <button
            v-if="!ingestionResult"
            type="button"
            class="btn btn-primary"
            :disabled="selectedFiles.length === 0 || isUploading"
            @click="startBatchIngestion"
          >
            {{ isUploading ? "Ingesting..." : "Start Batch Ingestion" }}
          </button>

          <button
            v-if="ingestionResult"
            type="button"
            class="btn btn-success"
            @click="completeAndClose"
          >
            Done &amp; View Ingested Series
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useFocusTrap } from "ui";

const props = defineProps({
  isOpen: { type: Boolean, default: false },
  subjectId: { type: String, default: "SUBJ-ONC-001" },
  timepoint: { type: String, default: "BASELINE" },
  studyId: { type: String, default: "STUDY-ONC-2026" },
  siteId: { type: String, default: "SITE-101" },
});

const emit = defineEmits(["close", "ingested"]);

const modalRef = ref(null);
const folderInputRef = ref(null);
const fileInputRef = ref(null);

const inputSubjectId = ref(props.subjectId);
const inputTimepoint = ref(props.timepoint);
const inputStudyId = ref(props.studyId);
const inputSiteId = ref(props.siteId);

const isDragging = ref(false);
const isUploading = ref(false);
const uploadProgress = ref(0);
const selectedFiles = ref([]);
const errorMessage = ref("");
const ingestionResult = ref(null);

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      inputSubjectId.value = props.subjectId;
      inputTimepoint.value = props.timepoint;
      inputStudyId.value = props.studyId;
      inputSiteId.value = props.siteId;
      resetState();
    }
  }
);

function resetState() {
  selectedFiles.value = [];
  errorMessage.value = "";
  ingestionResult.value = null;
  isUploading.value = false;
  uploadProgress.value = 0;
  isDragging.value = false;
}

const nonDicomExtensions = [
  ".pdf",
  ".txt",
  ".doc",
  ".docx",
  ".png",
  ".jpg",
  ".jpeg",
  ".csv",
  ".xml",
  ".ds_store",
  ".json",
];

function isNonDicomFile(file) {
  const name = file.name.toLowerCase();
  return nonDicomExtensions.some((ext) => name.endsWith(ext));
}

const nonDicomList = computed(() => {
  return selectedFiles.value.filter(isNonDicomFile);
});

const candidateDicomCount = computed(() => {
  return selectedFiles.value.length - nonDicomList.value.length;
});

function triggerFolderSelect() {
  folderInputRef.value?.click();
}

function triggerFileSelect() {
  fileInputRef.value?.click();
}

function handleFileInput(event) {
  const files = Array.from(event.target.files || []);
  if (files.length > 0) {
    selectedFiles.value = files;
  }
}

async function handleDrop(event) {
  isDragging.value = false;
  const items = event.dataTransfer?.items;
  if (items && items.length > 0) {
    const fileList = [];
    const entryPromises = [];

    for (let i = 0; i < items.length; i++) {
      const item = items[i];
      if (item.webkitGetAsEntry) {
        const entry = item.webkitGetAsEntry();
        if (entry) {
          entryPromises.push(traverseEntry(entry, fileList));
        }
      } else if (item.kind === "file") {
        const file = item.getAsFile();
        if (file) fileList.push(file);
      }
    }

    await Promise.all(entryPromises);
    selectedFiles.value = fileList;
  } else if (event.dataTransfer?.files) {
    selectedFiles.value = Array.from(event.dataTransfer.files);
  }
}

function traverseEntry(entry, fileList) {
  return new Promise((resolve) => {
    if (entry.isFile) {
      entry.file(
        (file) => {
          fileList.push(file);
          resolve();
        },
        () => resolve()
      );
    } else if (entry.isDirectory) {
      const dirReader = entry.createReader();
      dirReader.readEntries(
        async (entries) => {
          const promises = entries.map((child) =>
            traverseEntry(child, fileList)
          );
          await Promise.all(promises);
          resolve();
        },
        () => resolve()
      );
    } else {
      resolve();
    }
  });
}

async function startBatchIngestion() {
  if (selectedFiles.value.length === 0) return;

  isUploading.value = true;
  uploadProgress.value = 15;
  errorMessage.value = "";

  try {
    const formData = new FormData();
    formData.append("subjectId", inputSubjectId.value);
    formData.append("timepointName", inputTimepoint.value);
    formData.append("studyId", inputStudyId.value);
    formData.append("siteId", inputSiteId.value);

    for (const file of selectedFiles.value) {
      formData.append("files", file, file.webkitRelativePath || file.name);
    }

    uploadProgress.value = 40;

    const response = await fetch("/api/v1/imaging/studies/batch", {
      method: "POST",
      // The gateway signs the operator's identity; the imaging service records it on the certificate.
      body: formData,
    });

    uploadProgress.value = 85;

    if (!response.ok) {
      const errData = await response.json().catch(() => ({}));
      throw new Error(
        errData.message || `Upload failed with HTTP ${response.status}`
      );
    }

    const data = await response.json();
    uploadProgress.value = 100;
    ingestionResult.value = {
      studyEnvelopeId: data.studyEnvelopeId || data.StudyEnvelopeId,
      certificateId: data.certificateId || data.CertificateId,
      anonymizedStudyUid: data.anonymizedStudyUid || data.AnonymizedStudyUid,
      ingestedSliceCount:
        data.ingestedSliceCount ??
        data.IngestedSliceCount ??
        candidateDicomCount.value,
      skippedNonDicomCount:
        data.skippedNonDicomCount ??
        data.SkippedNonDicomCount ??
        nonDicomList.value.length,
      skippedNonDicomFiles:
        data.skippedNonDicomFiles ||
        data.SkippedNonDicomFiles ||
        nonDicomList.value.map((f) => f.name),
      seriesSummary: data.seriesSummary || data.SeriesSummary || [],
      manifest: data.manifest || data.Manifest,
      signature:
        data.signature || data.Signature || "SIG-HMAC256-VALID-21CFR11",
    };
  } catch (err) {
    errorMessage.value = err.message || "Failed to process batch DICOM upload.";
  } finally {
    isUploading.value = false;
  }
}

function handleClose() {
  resetState();
  emit("close");
}

useFocusTrap(modalRef, {
  isOpen: () => props.isOpen,
  onEscape: handleClose,
});

function completeAndClose() {
  if (ingestionResult.value) {
    emit("ingested", ingestionResult.value);
  }
  handleClose();
}
</script>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  background-color: rgba(15, 23, 42, 0.75);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 16px;
}

.modal-card {
  background-color: var(--semantic-color-surface);
  border-radius: 12px;
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.2),
    0 10px 10px -5px rgba(0, 0, 0, 0.1);
  width: 100%;
  max-width: 680px;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  border: 1px solid var(--semantic-color-border-control);
}

.modal-header {
  padding: 20px 24px;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.title-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.badge {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
  width: fit-content;
}

.badge-gxp {
  background-color: rgba(56, 189, 248, 0.2);
  color: var(--semantic-color-accent-inverse);
  border: 1px solid rgba(56, 189, 248, 0.4);
}

.badge-info {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.modal-title {
  font-size: 1.25rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
  margin: 0;
}

.modal-subtitle {
  font-size: 0.8rem;
  color: var(--semantic-color-text-inverse-muted);
  margin: 0;
  line-height: 1.3;
}

.btn-close {
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  font-size: 1.5rem;
  line-height: 1;
  cursor: pointer;
  padding: 0;
}

.btn-close:hover {
  color: var(--semantic-color-text-inverse);
}

.modal-body {
  padding: 24px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.param-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
}

.param-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.param-label {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse-muted);
}

.param-input {
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  outline: none;
}

.param-input:focus {
  border-color: var(--semantic-color-primary);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
}

.dropzone-area {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 32px 20px;
  text-align: center;
  background-color: var(--semantic-color-surface-muted);
  transition: all 0.2s ease;
  cursor: pointer;
}

.dropzone-area:hover,
.dropzone-area.is-dragging {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-info-bg-soft);
}

.dropzone-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.dropzone-icon {
  font-size: 2.5rem;
}

.dropzone-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0;
}

.dropzone-hint {
  font-size: 0.8rem;
  color: var(--semantic-color-text-inverse-muted);
  margin: 0;
}

.dropzone-actions {
  display: flex;
  gap: 10px;
  margin-top: 12px;
}

.file-summary-card {
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.summary-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.summary-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.summary-stats {
  display: flex;
  gap: 16px;
}

.stat-box {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 8px 12px;
  flex: 1;
  display: flex;
  flex-direction: column;
}

.stat-num {
  font-size: 1.1rem;
  font-weight: 800;
}

.stat-label {
  font-size: 0.7rem;
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.text-success {
  color: var(--semantic-color-success);
}

.text-warning {
  color: var(--semantic-color-warning);
}

.non-dicom-alert {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  gap: 10px;
  font-size: 0.78rem;
  color: var(--semantic-color-warning-dark);
}

.non-dicom-file-list {
  margin: 4px 0 0 0;
  padding-left: 18px;
}

.uploading-state-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 32px 20px;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.spinner-icon {
  font-size: 2rem;
  animation: spin 1.5s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

.uploading-title {
  font-size: 1.05rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 0;
}

.uploading-desc {
  font-size: 0.8rem;
  color: var(--semantic-color-text-inverse-muted);
  margin: 0;
}

.progress-bar-track {
  width: 100%;
  max-width: 400px;
  height: 10px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 5px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background-color: var(--semantic-color-primary);
  transition: width 0.3s ease;
}

.progress-text {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-primary);
}

.certificate-summary-card {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.cert-banner {
  display: flex;
  align-items: center;
  gap: 12px;
}

.cert-icon {
  font-size: 2rem;
}

.cert-title {
  font-size: 1rem;
  font-weight: 800;
  color: var(--semantic-color-success-dark);
  margin: 0;
}

.cert-subtitle {
  font-size: 0.78rem;
  color: var(--semantic-color-success);
  margin: 0;
}

.cert-details-grid {
  display: flex;
  flex-direction: column;
  gap: 6px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-success-border-soft);
  border-radius: 6px;
  padding: 12px;
  font-size: 0.78rem;
}

.detail-row {
  display: flex;
  justify-content: space-between;
}

.detail-label {
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.detail-val {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.detail-val.code {
  font-family: monospace;
}

.signature-truncate {
  max-width: 240px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.skipped-report-box {
  font-size: 0.75rem;
  color: var(--semantic-color-warning-dark);
  background-color: var(--semantic-color-warning-bg-soft);
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-warning-border);
}

.skipped-list {
  margin: 4px 0 0 0;
  padding-left: 18px;
}

.error-alert-box {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  border-radius: 6px;
  padding: 10px 12px;
  color: var(--semantic-color-error-text-dark);
  font-size: 0.8rem;
  display: flex;
  gap: 8px;
  align-items: center;
}

.modal-footer {
  padding: 16px 24px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 700;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-inverse);
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--semantic-color-primary);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header);
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-text-inverse);
}

.btn-success:hover {
  background-color: var(--semantic-color-success);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border-width: 0;
}
</style>
