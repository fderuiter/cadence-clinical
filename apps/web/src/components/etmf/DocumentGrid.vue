<template>
  <div class="document-grid-container">
    <div class="grid-header">
      <div class="header-titles">
        <h2>Artifact Documents Registry</h2>
        <p v-if="selectedArtifactCode" class="active-artifact-subtitle">
          Showing documents for Artifact:
          <strong>{{ selectedArtifactCode }}</strong>
        </p>
        <p v-else class="active-artifact-subtitle">
          Please select an artifact from the binder tree to view documents.
        </p>
      </div>
      <div style="display: flex; gap: 8px">
        <button
          v-if="selectedDocumentIds.length > 0"
          class="btn btn-outline-primary export-selected-btn"
          :aria-label="
            'Download ' +
            selectedDocumentIds.length +
            ' selected documents as ZIP package'
          "
          @click="handleExportSelected"
        >
          <span>📦</span> Download Selected ({{ selectedDocumentIds.length }})
        </button>
        <button
          class="btn btn-secondary batch-upload-trigger-btn"
          aria-label="Batch Ingestion multi-file upload"
          @click="isBatchDrawerOpen = true"
        >
          <span>📤</span> Batch Ingestion
        </button>
        <button
          class="btn btn-primary upload-trigger-btn"
          :disabled="!selectedArtifactCode"
          :aria-label="
            selectedArtifactCode
              ? 'Upload Regulated Document for artifact ' + selectedArtifactCode
              : 'Upload Regulated Document'
          "
          @click="openUploadModal"
        >
          <span>📄</span> Upload Single Document
        </button>
      </div>
    </div>

    <!-- Documents Data Table -->
    <div ref="containerRef" class="table-responsive">
      <table
        class="documents-table"
        role="grid"
        aria-label="Artifact Documents Registry"
        :aria-rowcount="paginatedDocuments.length"
        aria-colcount="7"
      >
        <thead>
          <tr>
            <th scope="col" style="width: 40px; text-align: center">
              <input
                type="checkbox"
                aria-label="Select all documents on current page"
                :checked="isAllSelected"
                @change="toggleSelectAll"
              />
            </th>
            <th scope="col">Document Name</th>
            <th scope="col">Taxonomy</th>
            <th scope="col">Version</th>
            <th scope="col">Status</th>
            <th scope="col">Uploaded By</th>
            <th scope="col">Uploaded At</th>
            <th scope="col" class="actions-column">Actions</th>
          </tr>
        </thead>
        <tbody>
          <template v-if="isLoading">
            <tr
              v-for="sIdx in 5"
              :key="`doc-skel-${sIdx}`"
              class="skeleton-table-row"
            >
              <td colspan="8">
                <SkeletonLoader type="row" :count="1" :columns="8" />
              </td>
            </tr>
          </template>
          <tr v-else-if="paginatedDocuments.length === 0">
            <td colspan="8" role="gridcell" class="empty-table-cell">
              <EmptyState
                icon="📄"
                title="No Regulated Documents Found"
                message="No documents have been uploaded for this artifact yet. Upload a single document or run batch ingestion to populate the eTMF."
              >
                <template #action>
                  <button
                    type="button"
                    class="btn btn-primary btn-sm upload-empty-btn"
                    :disabled="!selectedArtifactCode"
                    @click="openUploadModal"
                  >
                    <span>📄</span> Upload Single Document
                  </button>
                  <button
                    type="button"
                    class="btn btn-secondary btn-sm batch-empty-btn"
                    @click="isBatchDrawerOpen = true"
                  >
                    <span>📤</span> Batch Ingestion
                  </button>
                </template>
              </EmptyState>
            </td>
          </tr>
          <template v-else>
            <tr
              v-for="(doc, rIndex) in paginatedDocuments"
              :key="doc.id"
              class="document-row"
            >
              <td role="gridcell" style="text-align: center" @click.stop>
                <input
                  type="checkbox"
                  :aria-label="'Select document ' + doc.filename"
                  :checked="isDocumentSelected(doc.id)"
                  @change="etmfStore.toggleDocumentSelection(doc.id)"
                />
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 0)"
                :class="{
                  'cell-active': isCellActive(rIndex, 0),
                }"
                :data-row="rIndex"
                :data-col="0"
                class="doc-name-cell"
                @click="selectCell(rIndex, 0)"
                @keydown="handleCellKeyDown($event, rIndex, 0, doc)"
              >
                <span class="file-icon">📄</span>
                <span class="filename" :title="doc.filename">{{
                  doc.filename
                }}</span>
                <span
                  v-if="doc.is_redacted"
                  class="compliance-badge-pill"
                  title="HIPAA/GDPR Compliant Redacted Copy"
                  >🛡️ Compliant</span
                >
              </td>
              <td
                role="gridcell"
                :tabindex="
                  isEditing && isCellActive(rIndex, 1)
                    ? -1
                    : getCellTabindex(rIndex, 1)
                "
                :class="{
                  'cell-active': isCellActive(rIndex, 1),
                }"
                :data-row="rIndex"
                :data-col="1"
                @click="selectCell(rIndex, 1)"
                @dblclick="startEditing(rIndex, doc)"
                @keydown="handleCellKeyDown($event, rIndex, 1, doc)"
              >
                <div
                  v-if="isEditing && isCellActive(rIndex, 1)"
                  class="inline-edit-container"
                >
                  <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                  <select
                    ref="inlineSelectRef"
                    v-model="tempTaxonomyCode"
                    class="form-control inline-select"
                    @change="commitTaxonomy(doc)"
                    @keydown="handleSelectKeyDown($event, doc)"
                    @blur="cancelEditing"
                  >
                    <option
                      v-for="opt in taxonomyOptions"
                      :key="opt.code"
                      :value="opt.code"
                    >
                      Z{{ getZoneAndSectionFromCode(opt.code).zone }} - S{{
                        getZoneAndSectionFromCode(opt.code).section
                      }}
                      [{{ opt.code }}] ({{ opt.name }})
                    </option>
                  </select>
                </div>
                <span v-else class="taxonomy-pill">
                  Z{{ doc.zone }} - S{{ doc.section }} [{{ doc.artifact_code }}]
                </span>
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 2)"
                :class="{
                  'cell-active': isCellActive(rIndex, 2),
                }"
                :data-row="rIndex"
                :data-col="2"
                @click="selectCell(rIndex, 2)"
                @keydown="handleCellKeyDown($event, rIndex, 2, doc)"
              >
                <span class="version-tag">v{{ doc.version_index }}.0</span>
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 3)"
                :class="{
                  'cell-active': isCellActive(rIndex, 3),
                }"
                :data-row="rIndex"
                :data-col="3"
                @click="selectCell(rIndex, 3)"
                @keydown="handleCellKeyDown($event, rIndex, 3, doc)"
              >
                <span class="status-badge" :class="getStatusClass(doc.status)">
                  {{ formatStatus(doc.status) }}
                </span>
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 4)"
                :class="{
                  'cell-active': isCellActive(rIndex, 4),
                }"
                :data-row="rIndex"
                :data-col="4"
                @click="selectCell(rIndex, 4)"
                @keydown="handleCellKeyDown($event, rIndex, 4, doc)"
              >
                <div class="user-meta">
                  <span class="username">{{ doc.created_by }}</span>
                </div>
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 5)"
                :class="{
                  'cell-active': isCellActive(rIndex, 5),
                }"
                :data-row="rIndex"
                :data-col="5"
                class="date-cell"
                @click="selectCell(rIndex, 5)"
                @keydown="handleCellKeyDown($event, rIndex, 5, doc)"
              >
                {{ doc.formattedCreatedAt }}
              </td>
              <td
                role="gridcell"
                :tabindex="getCellTabindex(rIndex, 6)"
                :class="{
                  'cell-active': isCellActive(rIndex, 6),
                }"
                :data-row="rIndex"
                :data-col="6"
                class="actions-cell"
                @click="selectCell(rIndex, 6)"
                @keydown="handleCellKeyDown($event, rIndex, 6, doc)"
              >
                <div style="display: flex; gap: 4px; justify-content: flex-end">
                  <button
                    class="btn btn-sm btn-outline-secondary btn-inspect-doc"
                    title="Inspect 21 CFR Part 11 Metadata"
                    :aria-label="'Inspect metadata for ' + doc.filename"
                    @click.stop="$emit('inspect', doc)"
                  >
                    📋 Inspect
                  </button>
                  <button
                    class="btn btn-sm btn-outline-primary btn-preview-doc"
                    title="View Watermarked PDF Preview"
                    :aria-label="
                      'Preview secure watermarked document ' + doc.filename
                    "
                    @click.stop="$emit('preview', doc)"
                  >
                    👁️ Preview
                  </button>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <!-- Pagination Controls -->
    <div v-if="totalItems > 0" class="pagination-controls">
      <div class="pagination-info">
        Showing
        <span class="font-semibold">{{
          Math.min((currentPage - 1) * itemsPerPage + 1, totalItems)
        }}</span>
        to
        <span class="font-semibold">{{
          Math.min(currentPage * itemsPerPage, totalItems)
        }}</span>
        of
        <span class="font-semibold">{{ totalItems }}</span> documents
      </div>

      <div class="pagination-buttons">
        <button
          class="btn btn-sm btn-outline-secondary prev-btn"
          :disabled="currentPage === 1"
          aria-label="Previous page"
          @click="prevPage"
        >
          ◀ Previous
        </button>

        <div class="page-numbers">
          <button
            v-for="page in totalPages"
            :key="page"
            class="btn btn-sm page-num-btn"
            :class="{ active: currentPage === page }"
            :aria-label="'Go to page ' + page"
            :aria-current="currentPage === page ? 'page' : undefined"
            @click="goToPage(page)"
          >
            {{ page }}
          </button>
        </div>

        <button
          class="btn btn-sm btn-outline-secondary next-btn"
          :disabled="currentPage === totalPages"
          aria-label="Next page"
          @click="nextPage"
        >
          Next ▶
        </button>
      </div>
    </div>

    <!-- GxP Electronic Record Drag & Drop Upload Modal -->
    <div
      v-if="showUploadModal"
      ref="uploadModalRef"
      class="modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label="Document Ingestion Modal"
      tabindex="-1"
    >
      <div class="modal-card upload-modal">
        <div class="modal-header">
          <h3>FDA 21 CFR Part 11 Compliant Document Ingestion</h3>
          <button
            class="close-modal-btn"
            aria-label="Close modal"
            @click="closeUploadModal"
          >
            ×
          </button>
        </div>

        <div class="modal-body">
          <!-- GxP Compliance Warning Box -->
          <div class="gxp-warning-alert">
            <strong>⚠️ Regulated Record Ingestion Warning:</strong>
            All actions are logged in the append-only eTMF audit ledger. You
            must provide a valid electronic signature context and change reason
            justification to submit this record.
          </div>

          <!-- Drag and Drop Target -->
          <div
            class="drag-drop-zone"
            :class="{ 'is-dragging': isDragging }"
            tabindex="0"
            role="button"
            aria-label="Drag and drop your regulated PDF here, or press Enter or Space to browse."
            @dragover.prevent="onDragOver"
            @dragleave.prevent="onDragLeave"
            @drop.prevent="onDrop"
            @click="triggerFileSelect"
            @keydown.enter="triggerFileSelect"
            @keydown.space.prevent="triggerFileSelect"
          >
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              ref="fileInputRef"
              type="file"
              class="hidden-file-input"
              accept=".pdf"
              @change="onFileSelected"
            />
            <div class="drop-prompt-content" aria-hidden="true">
              <span class="upload-cloud-icon">☁️</span>
              <p v-if="!selectedFile" class="drop-text">
                Drag and drop your regulated PDF here, or
                <span class="highlight">browse</span>
              </p>
              <div v-else class="selected-file-details">
                <p class="file-name-success">🎉 {{ selectedFile.name }}</p>
                <p class="file-size-meta">
                  Size: {{ (selectedFile.size / 1024).toFixed(1) }} KB | Type:
                  {{ selectedFile.type || "application/pdf" }}
                </p>
              </div>
            </div>
          </div>

          <!-- Upload Metadata Form -->
          <form class="upload-meta-form" @submit.prevent="submitUpload">
            <div class="form-group-row">
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Target Study ID</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model="studyId"
                  type="text"
                  class="form-control"
                  readonly
                />
              </div>
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Site ID (Optional)</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model="siteId"
                  type="text"
                  placeholder="e.g. SITE-01"
                  class="form-control"
                />
              </div>
            </div>

            <div class="form-group-row">
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>DIA TMF Artifact Code</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model="artifactCode"
                  type="text"
                  class="form-control"
                  readonly
                />
              </div>
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Artifact Type (Label)</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model="artifactType"
                  type="text"
                  class="form-control"
                  readonly
                />
              </div>
            </div>

            <!-- Mandatory Reason for Change under 21 CFR Part 11 -->
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label class="required-label">
                Reason for Change / Upload Justification
              </label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="reasonForChange"
                rows="3"
                placeholder="Specify the regulatory or scientific justification for ingesting this record version (e.g. 'Initial Approved protocol for site initiation', 'Site license renewal')."
                class="form-control"
                required
              />
            </div>

            <div v-if="uploadError" class="upload-error-banner">
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
                :disabled="
                  isSubmitting || !selectedFile || !reasonForChange.trim()
                "
              >
                <span v-if="isSubmitting">Uploading & Indexing...</span>
                <span v-else>🔒 Commit Electronic Record</span>
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>

    <!-- Batch Ingestion Drawer -->
    <BatchUploadDrawer
      :is-open="isBatchDrawerOpen"
      :study-id="etmfStore.currentStudyId"
      @close="isBatchDrawerOpen = false"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick } from "vue";
import { useFocusTrap } from "ui";
import { useEtmfStore } from "../../stores/etmf";
import { etmfService } from "../../api/etmf";
import { use2DGrid } from "../../composables/use2DGrid";
import { useToastStore } from "../../stores/toasts.js";
import BatchUploadDrawer from "./BatchUploadDrawer.vue";
import SkeletonLoader from "../common/SkeletonLoader.vue";
import EmptyState from "../common/EmptyState.vue";

const isBatchDrawerOpen = ref(false);

const props = defineProps({
  documents: {
    type: Array,
    required: true,
  },
  isLoading: {
    type: Boolean,
    default: false,
  },
});

defineEmits(["preview", "inspect"]);

const etmfStore = useEtmfStore();

// 2D Interactive Grid selection & editing state
const containerRef = ref(null);
const tempTaxonomyCode = ref("");
const inlineSelectRef = ref(null);

const grid = use2DGrid(() => paginatedDocuments.value.length, 7, {
  containerRef,
  initialRow: 0,
  initialCol: 0,
  onCopyCell: (rIndex, cIndex, doc) => copyActiveCellValue(doc),
  onCellAction: (rIndex, cIndex, doc, _event) => {
    if (cIndex === 1) {
      startEditing(rIndex, doc);
    }
  },
});

const {
  activeRowIndex,
  activeColIndex,
  isEditing,
  isCellActive,
  getCellTabindex,
  selectCell,
  handleCellKeyDown,
  exitEditing,
} = grid;

const taxonomyOptions = computed(() => {
  const allNodes = Object.values(etmfStore.folderLookup);
  const seenCodes = new Set();
  const options = [];
  for (const node of allNodes) {
    if (node.type === "artifact" && node.code && !seenCodes.has(node.code)) {
      seenCodes.add(node.code);
      options.push(node);
    }
  }
  return options;
});

function getZoneAndSectionFromCode(code) {
  if (!code) return { zone: 1, section: "01.01" };
  const parts = code.split(".");
  const zone = parseInt(parts[0]) || 1;
  const section = parts.slice(0, 2).join(".");
  return { zone, section };
}

async function startEditing(rIndex, doc) {
  grid.startEditing(rIndex, 1);
  tempTaxonomyCode.value = doc.artifact_code;
  await nextTick();
  if (inlineSelectRef.value) {
    const el = Array.isArray(inlineSelectRef.value)
      ? inlineSelectRef.value[0]
      : inlineSelectRef.value;
    if (el && typeof el.focus === "function") {
      el.focus();
    }
  }
}

function cancelEditing() {
  setTimeout(() => {
    if (isEditing.value) {
      exitEditing();
    }
  }, 150);
}

async function commitTaxonomy(doc) {
  if (!isEditing.value) return;

  const targetCode = tempTaxonomyCode.value;
  const selectedNode = etmfStore.folderLookup[targetCode];
  if (!selectedNode) {
    cancelEditing();
    return;
  }

  const { zone, section } = getZoneAndSectionFromCode(targetCode);

  try {
    await etmfService.tagDocument(
      doc.id,
      {
        zone,
        section,
        artifact_code: targetCode,
      },
      {
        changeReason:
          "Corrected taxonomy classification via interactive grid navigation",
      }
    );

    await etmfStore.fetchDocuments(etmfStore.selectedArtifactId);
  } catch (err) {
    console.error("Failed to tag/classify document:", err);
  } finally {
    exitEditing();
  }
}

function copyActiveCellValue(doc) {
  let val;
  switch (activeColIndex.value) {
    case 0:
      val = doc.filename || "";
      break;
    case 1:
      val = `Z${doc.zone} - S${doc.section} [${doc.artifact_code}]`;
      break;
    case 2:
      val = `v${doc.version_index}.0`;
      break;
    case 3:
      val = formatStatus(doc.status);
      break;
    case 4:
      val = doc.created_by || "";
      break;
    case 5:
      val = doc.formattedCreatedAt || "";
      break;
    case 6:
      val = "Preview";
      break;
    default:
      return;
  }

  if (
    navigator.clipboard &&
    typeof navigator.clipboard.writeText === "function"
  ) {
    navigator.clipboard.writeText(val).catch((err) => {
      console.error("Clipboard copy failed:", err);
    });
  } else {
    const textarea = document.createElement("textarea");
    textarea.value = val;
    textarea.style.position = "fixed";
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    try {
      document.execCommand("copy");
    } catch (err) {
      console.error("Fallback copy failed:", err);
    }
    document.body.removeChild(textarea);
  }
}

function handleSelectKeyDown(event, doc) {
  if (event.key === "Enter") {
    event.preventDefault();
    event.stopPropagation();
    commitTaxonomy(doc);
  } else if (event.key === "Escape") {
    event.preventDefault();
    event.stopPropagation();
    cancelEditing();
  }
}

const selectedArtifactCode = ref("");
const showUploadModal = ref(false);
const isDragging = ref(false);
const isSubmitting = ref(false);
const uploadError = ref("");

const selectedDocumentIds = computed(() => etmfStore.selectedDocumentIds);

function isDocumentSelected(docId) {
  return etmfStore.selectedDocumentIds.includes(docId);
}

const isAllSelected = computed(() => {
  return (
    paginatedDocuments.value.length > 0 &&
    paginatedDocuments.value.every((d) =>
      etmfStore.selectedDocumentIds.includes(d.id)
    )
  );
});

function toggleSelectAll() {
  if (isAllSelected.value) {
    const paginatedIds = new Set(paginatedDocuments.value.map((d) => d.id));
    const newSelected = etmfStore.selectedDocumentIds.filter(
      (id) => !paginatedIds.has(id)
    );
    etmfStore.setSelectedDocumentIds(newSelected);
  } else {
    const selectedSet = new Set(etmfStore.selectedDocumentIds);
    paginatedDocuments.value.forEach((d) => selectedSet.add(d.id));
    etmfStore.setSelectedDocumentIds(Array.from(selectedSet));
  }
}

async function handleExportSelected() {
  if (etmfStore.selectedDocumentIds.length === 0) return;
  try {
    await etmfStore.triggerExport({
      document_ids: [...etmfStore.selectedDocumentIds],
      is_full_binder: false,
    });
  } catch (err) {
    useToastStore().apiError("Export failed", err);
  }
}

// Selected File reference
const selectedFile = ref(null);
const fileInputRef = ref(null);
const uploadModalRef = ref(null);

// Form Fields
const studyId = ref("STUDY-USDM-001");
const siteId = ref("");
const artifactCode = ref("");
const artifactType = ref("");
const reasonForChange = ref("");

useFocusTrap(uploadModalRef, {
  isOpen: showUploadModal,
  onEscape: closeUploadModal,
});

// Local pagination state
const currentPage = ref(1);
const itemsPerPage = 20;

// Pre-computed formatted documents (date formatted beforehand)
const formattedDocuments = computed(() => {
  return props.documents.map((doc) => ({
    ...doc,
    formattedCreatedAt: formatDate(doc.created_at),
  }));
});

// Reset currentPage.value = 1 when the underlying documents list changes
watch(
  () => props.documents,
  () => {
    currentPage.value = 1;
    activeRowIndex.value = -1;
    activeColIndex.value = -1;
    isEditing.value = false;
  },
  { deep: true }
);

watch(currentPage, () => {
  activeRowIndex.value = -1;
  activeColIndex.value = -1;
  isEditing.value = false;
});

const totalItems = computed(() => formattedDocuments.value.length);
const totalPages = computed(
  () => Math.ceil(totalItems.value / itemsPerPage) || 1
);

const paginatedDocuments = computed(() => {
  const start = (currentPage.value - 1) * itemsPerPage;
  const end = start + itemsPerPage;
  return formattedDocuments.value.slice(start, end);
});

function prevPage() {
  if (currentPage.value > 1) {
    currentPage.value--;
  }
}

function nextPage() {
  if (currentPage.value < totalPages.value) {
    currentPage.value++;
  }
}

function goToPage(page) {
  if (page >= 1 && page <= totalPages.value) {
    currentPage.value = page;
  }
}

// Listen for artifact selections on the store to update current context
watch(
  () => etmfStore.selectedArtifactId,
  (newCode) => {
    selectedArtifactCode.value = newCode || "";
    if (newCode) {
      artifactCode.value = newCode;

      // Use instant O(1) folderLookup map instead of slow recursive search
      const node = etmfStore.folderLookup[newCode];
      artifactType.value = node ? node.name : "Clinical Trial Document";
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

    // Helper to read file as text to supply to the indexer/content payload field. #4523: an unreadable or
    // empty file aborts the upload; placeholder text is never filed as document content.
    const fileContent = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (e) => {
        const text = e.target?.result;
        if (text) {
          resolve(text);
        } else {
          reject(
            new Error(`"${file.name}" is empty; no document content to file.`)
          );
        }
      };
      reader.onerror = () => {
        reject(
          new Error(
            `"${file.name}" could not be read: ${reader.error?.message || "unknown read error"}`
          )
        );
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
    formData.append(
      "content",
      `Document content index for: ${file.name}. Raw plaintext: ${fileContent}`
    );
    formData.append("mime_type", file.type || "application/pdf");
    formData.append("artifact_code", artifactCode.value);
    formData.append("zone", String(zoneInt));
    formData.append("section", sectionStr);
    formData.append("reason_for_change", reasonForChange.value);

    await etmfStore.uploadDocument(formData);

    // Success
    closeUploadModal();
  } catch (err) {
    useToastStore().apiError("eTMF document upload failed", err);
    uploadError.value =
      err.message ||
      "Ingestion transaction rejected. Verify site/study lock status.";
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
  border-bottom: 1px solid #f1f5f9; /* deid-ignore */
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
  border-bottom: 2px solid #e2e8f0; /* deid-ignore */
}

.documents-table td {
  padding: 14px 16px;
  border-bottom: 1px solid #e2e8f0; /* deid-ignore */
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
  background-color: #f1f5f9; /* deid-ignore */
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
  background-color: #f1f5f9; /* deid-ignore */
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
  background-color: #e2e8f0; /* deid-ignore */
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
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.1),
    0 10px 10px -5px rgba(0, 0, 0, 0.04);
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
  border-bottom: 1px solid #e2e8f0; /* deid-ignore */
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
  outline: 2px solid transparent;
  transition: border-color 0.2s;
  background-color: #ffffff;
}

.form-control:focus {
  border-color: #2563eb;
}

.form-control[readonly] {
  background-color: #f1f5f9; /* deid-ignore */
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
  border-top: 1px solid #e2e8f0; /* deid-ignore */
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

/* Pagination Styling */
.pagination-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid #e2e8f0; /* deid-ignore */
}

.pagination-info {
  font-size: 0.875rem;
  color: #64748b;
}

.font-semibold {
  font-weight: 600;
  color: #1e293b;
}

.pagination-buttons {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-numbers {
  display: flex;
  gap: 4px;
}

.page-num-btn {
  min-width: 32px;
  padding: 4px 8px;
  background-color: transparent;
  border: 1px solid #cbd5e1;
  color: #475569;
}

.page-num-btn:hover {
  background-color: #f1f5f9; /* deid-ignore */
  border-color: #cbd5e1;
}

.page-num-btn.active {
  background-color: #2563eb;
  color: #ffffff;
  border-color: #2563eb;
}

.prev-btn:disabled,
.next-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* 2D Interactive Grid styles */
.documents-table td {
  cursor: cell;
}

.documents-table td * {
  cursor: initial;
}

.documents-table td:focus,
.documents-table td.cell-active {
  outline: 2px solid #0284c7; /* High contrast sky/blue */
  outline-offset: -2px;
  background-color: #f0f9ff !important; /* deid-ignore */
}

.inline-edit-container {
  display: flex;
  align-items: center;
  width: 100%;
}

.inline-select {
  width: 100%;
  padding: 4px 8px;
  font-size: 0.85rem;
  height: auto;
  border-radius: 4px;
}

.compliance-badge-pill {
  font-size: 0.7rem;
  background-color: #dbeafe;
  color: #1e40af;
  padding: 1px 6px;
  border-radius: 10px;
  font-weight: 700;
  margin-left: 6px;
  white-space: nowrap;
}
</style>
