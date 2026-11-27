<template>
  <div class="document-grid-container">
    <DocumentGridHeader
      :selected-artifact-code="selectedArtifactCode"
      :selected-count="selectedDocumentIds.length"
      :can-stage="canStage"
      :can-tag="canTag"
      :can-export="canExport"
      @export-selected="handleExportSelected"
      @open-batch="openBatchDrawer"
      @open-upload="openUploadModal"
    />

    <!-- Documents Data Table -->
    <div ref="containerRef" class="table-responsive">
      <table
        class="documents-table"
        role="grid"
        aria-label="Artifact Documents Registry"
        :aria-rowcount="paginatedDocuments.length"
        aria-colcount="8"
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
              <DocumentGridEmptyCell
                :scope-forbidden-guidance="
                  etmfStore.scopeForbiddenGuidance || ''
                "
                :documents-error="etmfStore.documentsError || ''"
                :can-stage="canStage"
                :selected-artifact-code="selectedArtifactCode"
                @open-upload="openUploadModal"
                @open-batch="openBatchDrawer"
              />
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
                v-bind="gridCellBind(rIndex, 0, 'doc-name-cell')"
                @click="selectCell(rIndex, 0)"
                @keydown="handleCellKeyDown($event, rIndex, 0, doc)"
              >
                <span class="file-icon"
                  ><ClinicalIcon name="file-text" :size="16"
                /></span>
                <span class="filename" :title="doc.filename">{{
                  doc.filename
                }}</span>
                <span
                  v-if="doc.is_redacted"
                  class="compliance-badge-pill"
                  title="HIPAA/GDPR Compliant Redacted Copy"
                >
                  Compliant</span
                >
              </td>
              <td
                role="gridcell"
                v-bind="gridCellBind(rIndex, 1)"
                @click="selectCell(rIndex, 1)"
                @dblclick="startEditing(rIndex, doc)"
                @keydown="handleCellKeyDown($event, rIndex, 1, doc)"
              >
                <div
                  v-if="isEditing && isCellActive(rIndex, 1)"
                  class="inline-edit-container"
                >
                  <select
                    ref="inlineSelectRef"
                    v-model="tempTaxonomyCode"
                    aria-label="Reclassify DIA TMF taxonomy"
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
                v-bind="gridCellBind(rIndex, 2)"
                @click="selectCell(rIndex, 2)"
                @keydown="handleCellKeyDown($event, rIndex, 2, doc)"
              >
                <span class="version-tag">v{{ doc.version_index }}.0</span>
              </td>
              <td
                role="gridcell"
                v-bind="gridCellBind(rIndex, 3)"
                @click="selectCell(rIndex, 3)"
                @keydown="handleCellKeyDown($event, rIndex, 3, doc)"
              >
                <span class="status-badge" :class="getStatusClass(doc.status)">
                  {{ formatStatus(doc.status) }}
                </span>
              </td>
              <td
                role="gridcell"
                v-bind="gridCellBind(rIndex, 4)"
                @click="selectCell(rIndex, 4)"
                @keydown="handleCellKeyDown($event, rIndex, 4, doc)"
              >
                <div class="user-meta">
                  <span class="username">{{ doc.created_by }}</span>
                </div>
              </td>
              <td
                role="gridcell"
                v-bind="gridCellBind(rIndex, 5, 'date-cell')"
                @click="selectCell(rIndex, 5)"
                @keydown="handleCellKeyDown($event, rIndex, 5, doc)"
              >
                {{ doc.formattedCreatedAt }}
              </td>
              <td
                role="gridcell"
                v-bind="gridCellBind(rIndex, 6, 'actions-cell')"
                @click="selectCell(rIndex, 6)"
                @keydown="handleCellKeyDown($event, rIndex, 6, doc)"
              >
                <div class="actions-row">
                  <button
                    type="button"
                    class="btn btn-sm btn-outline-secondary btn-inspect-doc"
                    title="Inspect 21 CFR Part 11 Metadata"
                    :aria-label="'Inspect metadata for ' + doc.filename"
                    @click.stop="$emit('inspect', doc)"
                  >
                    <span aria-hidden="true"
                      ><ClinicalIcon name="clipboard-list" :size="16"
                    /></span>
                    Inspect
                  </button>
                  <button
                    type="button"
                    class="btn btn-sm btn-outline-primary btn-preview-doc"
                    title="View Watermarked PDF Preview"
                    :aria-label="
                      'Preview secure watermarked document ' + doc.filename
                    "
                    @click.stop="$emit('preview', doc)"
                  >
                    <span aria-hidden="true"
                      ><ClinicalIcon name="camera" :size="16"
                    /></span>
                    Preview
                  </button>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <DocumentGridPagination
      :total-items="totalItems"
      :current-page="currentPage"
      :items-per-page="itemsPerPage"
      :total-pages="totalPages"
      @prev="prevPage"
      @next="nextPage"
      @go-to="goToPage"
    />

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
            type="button"
            class="close-modal-btn"
            aria-label="Close modal"
            @click="closeUploadModal"
          >
            ×
          </button>
        </div>

        <div class="modal-body">
          <div class="gxp-warning-alert">
            <strong> Regulated Record Ingestion Warning:</strong>
            All actions are logged in the append-only eTMF audit ledger. You
            must provide a valid electronic signature context and change reason
            justification to submit this record.
          </div>

          <ClinicalDropzone
            class="drag-drop-zone"
            accept=".pdf"
            :disabled="isSubmitting"
            :has-selection="!!selectedFile"
            icon="file-text"
            :prompt-text="
              selectedFile ? '' : 'Drag and drop your regulated PDF here'
            "
            :button-label="
              selectedFile ? 'Replace Regulated PDF' : 'Browse Regulated PDF'
            "
            input-aria-label="Regulated PDF file"
            removable
            remove-label="Remove File"
            :remove-context="selectedFile ? selectedFile.name : ''"
            @file-selected="onRegulatedFileSelected"
            @remove="selectedFile = null"
          >
            <template #selected>
              <div v-if="selectedFile" class="selected-file-details">
                <p class="file-name-success">
                  <span aria-hidden="true"
                    ><ClinicalIcon name="file-text" :size="16" /></span
                  >{{ selectedFile.name }}
                </p>
                <p class="file-size-meta">
                  Size: {{ (selectedFile.size / 1024).toFixed(1) }} KB | Type:
                  {{ selectedFile.type || "application/pdf" }}
                </p>
              </div>
            </template>
          </ClinicalDropzone>

          <DocumentUploadFields
            v-model:site-id="siteId"
            v-model:reason-for-change="reasonForChange"
            :study-id="studyId"
            :artifact-code="artifactCode"
            :artifact-type="artifactType"
            :upload-error="uploadError"
            :is-submitting="isSubmitting"
            :has-file="Boolean(selectedFile)"
            @cancel="closeUploadModal"
            @submit="submitUpload"
          />
        </div>
      </div>
    </div>

    <!-- Batch Ingestion Drawer -->
    <BatchUploadDrawer
      v-if="isBatchDrawerOpen"
      :is-open="isBatchDrawerOpen"
      :study-id="etmfStore.currentStudyId"
      :site-id="etmfStore.currentSiteId || null"
      @close="isBatchDrawerOpen = false"
    />
  </div>
</template>

<script>
import { defineLazyOverlay } from "../../utils/lazyOverlay.js";

const BatchUploadDrawer = defineLazyOverlay(
  () => import("./BatchUploadDrawer.vue")
);
const ClinicalDropzone = defineLazyOverlay(
  () => import("./LazyClinicalDropzone.js")
);
const DocumentUploadFields = defineLazyOverlay(
  () => import("./document-grid/DocumentUploadFields.vue")
);
</script>

<script setup>
import { nextTick, ref } from "vue";
import { ClinicalIcon, useFocusTrap } from "ui";
import {
  ETMF_EXPORT_ROLE_REASON,
  ETMF_STAGE_ROLE_REASON,
  ETMF_TAG_ROLE_REASON,
} from "../../canvas/authority.ts";
import { useToastStore } from "../../stores/toasts.js";
import SkeletonLoader from "../common/SkeletonLoader.vue";
import DocumentGridEmptyCell from "./document-grid/DocumentGridEmptyCell.vue";
import DocumentGridHeader from "./document-grid/DocumentGridHeader.vue";
import DocumentGridPagination from "./document-grid/DocumentGridPagination.vue";
import {
  formatStatus,
  getStatusClass,
  getZoneAndSectionFromCode,
  useDocumentGridState,
} from "./document-grid/useDocumentGridState";

const isBatchDrawerOpen = ref(false);

const props = defineProps({
  documents: { type: Array, required: true },
  isLoading: { type: Boolean, default: false },
});

defineEmits(["preview", "inspect"]);

const {
  artifactCode,
  artifactType,
  canExport,
  canStage,
  canTag,
  cancelEditing,
  closeUploadModal,
  commitTaxonomy,
  containerRef,
  currentPage,
  etmfStore,
  goToPage,
  grid,
  gridCellBind,
  handleCellKeyDown,
  handleSelectKeyDown,
  inlineSelectRef,
  isAllSelected,
  isCellActive,
  isDocumentSelected,
  isEditing,
  isSubmitting,
  itemsPerPage,
  nextPage,
  onRegulatedFileSelected,
  paginatedDocuments,
  prepareUploadModal,
  prevPage,
  reasonForChange,
  selectCell,
  selectedArtifactCode,
  selectedDocumentIds,
  selectedFile,
  showUploadModal,
  siteId,
  studyId,
  submitUpload,
  taxonomyOptions,
  tempTaxonomyCode,
  toggleSelectAll,
  totalItems,
  totalPages,
  uploadError,
  uploadModalRef,
} = useDocumentGridState(props, (rIndex, doc) => startEditing(rIndex, doc));

useFocusTrap(uploadModalRef, {
  isOpen: showUploadModal,
  onEscape: closeUploadModal,
});

function openBatchDrawer() {
  if (!canStage.value) {
    useToastStore().push({
      title: "Document Staging Restricted",
      message: ETMF_STAGE_ROLE_REASON,
      tone: "warning",
    });
    return;
  }
  isBatchDrawerOpen.value = true;
}

async function startEditing(rIndex, doc) {
  if (!canTag.value) {
    useToastStore().push({
      title: "Taxonomy Editing Restricted",
      message: ETMF_TAG_ROLE_REASON,
      tone: "warning",
    });
    return;
  }
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

async function handleExportSelected() {
  if (!canExport.value) {
    useToastStore().push({
      title: "eTMF Export Restricted",
      message: ETMF_EXPORT_ROLE_REASON,
      tone: "warning",
    });
    return;
  }
  const { executeExportSelectedDocuments } =
    await import("./document-grid/documentGridActions.js");
  await executeExportSelectedDocuments(etmfStore);
}

function openUploadModal() {
  if (!canStage.value) {
    useToastStore().push({
      title: "Document Upload Restricted",
      message: ETMF_STAGE_ROLE_REASON,
      tone: "warning",
    });
    return;
  }
  prepareUploadModal();
}
</script>

<style scoped src="./documentGrid.css"></style>
