<template>
  <div class="documents-hub-layout">
    <!-- Left Navigation Panel: Folder Tree -->
    <aside class="hub-sidebar" aria-label="Clinical Folder Tree">
      <DocumentsFolderTree
        :studies-tree="fileshareStore.filteredStudiesTree"
        :initial-study-id="initialStudyId"
        @select-node="handleTreeNodeSelect"
      />
    </aside>

    <!-- Main Workspace: Search Bar, Toolbar & Documents Grid -->
    <main class="hub-content-area" aria-label="Documents Grid Workspace">
      <!-- Top Action Bar -->
      <div class="hub-header-bar">
        <div class="header-left-col">
          <h2 class="hub-page-title">Documents Hub</h2>
          <p class="hub-page-subtitle">
            Cross-study unified clinical fileshare, protected media viewer &amp;
            permission delegation vault
          </p>
        </div>

        <div
          class="header-right-col"
          style="display: flex; align-items: center; gap: 8px"
        >
          <button
            type="button"
            class="btn btn-primary btn-batch-upload"
            style="font-size: 13px; font-weight: 600; padding: 6px 14px"
            @click="isBatchDrawerOpen = true"
          >
            Batch Ingestion
          </button>
          <button
            type="button"
            class="btn btn-outline-secondary btn-qc-queue"
            style="font-size: 13px; font-weight: 600; padding: 6px 14px"
            @click="showQcQueue = !showQcQueue"
          >
            CRA QC Queue
          </button>

          <!-- Search Bar -->
          <div class="search-input-wrapper">
            <ClinicalInput
              id="documents-hub-search"
              v-model="searchInput"
              label="Search all documents"
              hide-label
              density="compact"
              :show-query-flag="false"
              control-class="hub-search-input"
              placeholder="Search by filename, study, uploader, or site..."
              :attributes="{ 'aria-label': 'Search all documents' }"
              @input="onSearchInput"
            />
            <button
              v-if="searchInput"
              type="button"
              class="clear-search-btn"
              aria-label="Clear search"
              @click="clearSearch"
            >
              ×
            </button>
          </div>
        </div>
      </div>

      <!-- CRA Quality Control Review Queue Panel -->
      <CraQcQueue v-if="showQcQueue" style="margin-bottom: 20px" />

      <!-- Active Filters & Bulk Actions Ribbon -->
      <div class="hub-filter-ribbon">
        <div class="active-filter-pills">
          <span
            v-if="fileshareStore.activeFilters.studyId"
            class="filter-pill study-pill"
          >
            Study: <strong>{{ fileshareStore.activeFilters.studyId }}</strong>
            <ClinicalIconButton
              class="remove-pill-btn"
              :label="`Remove study filter ${fileshareStore.activeFilters.studyId}`"
              @click="clearFilter('studyId')"
            >
              ×
            </ClinicalIconButton>
          </span>

          <span
            v-if="fileshareStore.activeFilters.siteId"
            class="filter-pill site-pill"
          >
            Site: <strong>{{ fileshareStore.activeFilters.siteId }}</strong>
            <ClinicalIconButton
              class="remove-pill-btn"
              :label="`Remove site filter ${fileshareStore.activeFilters.siteId}`"
              @click="clearFilter('siteId')"
            >
              ×
            </ClinicalIconButton>
          </span>

          <span
            v-if="fileshareStore.activeFilters.zoneCode"
            class="filter-pill zone-pill"
          >
            Zone: <strong>{{ fileshareStore.activeFilters.zoneCode }}</strong>
            <ClinicalIconButton
              class="remove-pill-btn"
              :label="`Remove zone filter ${fileshareStore.activeFilters.zoneCode}`"
              @click="clearFilter('zoneCode')"
            >
              ×
            </ClinicalIconButton>
          </span>

          <span
            v-if="fileshareStore.activeFilters.sectionCode"
            class="filter-pill section-pill"
          >
            Section:
            <strong>{{ fileshareStore.activeFilters.sectionCode }}</strong>
            <ClinicalIconButton
              class="remove-pill-btn"
              :label="`Remove section filter ${fileshareStore.activeFilters.sectionCode}`"
              @click="clearFilter('sectionCode')"
            >
              ×
            </ClinicalIconButton>
          </span>

          <button
            v-if="hasActiveFilter"
            type="button"
            class="btn btn-sm btn-outline-secondary btn-reset-all"
            @click="resetAllFilters"
          >
            Reset All Filters
          </button>
        </div>

        <!-- Bulk Selection Action Ribbon -->
        <div v-if="selectedFiles.length > 0" class="bulk-actions-toolbar">
          <span class="selection-count-badge tabular-nums">
            {{ selectedFiles.length }} Selected
          </span>
          <button
            type="button"
            class="btn btn-sm btn-primary btn-bulk-share"
            @click="openBulkShareDrawer"
          >
            Bulk Share ({{ selectedFiles.length }})
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary btn-deselect-all"
            @click="fileshareStore.clearSelection()"
          >
            Deselect All
          </button>
        </div>
      </div>

      <!-- Main Data Table -->
      <ClinicalDataTable
        title="Documents Hub"
        hide-header
        plain
        external-pagination
        external-sort
        :initial-sort-key="sortField"
        :initial-sort-order="sortDirection"
        :columns="documentColumns"
        :data="paginatedDocuments"
        row-key="id"
        :page="currentPage"
        :page-size="itemsPerPage"
        :total-records="totalItems"
        container-class="hub-data-table-shell"
        wrapper-class="table-responsive hub-table-container"
        table-class="documents-table"
        :row-class="resolveDocumentRowClass"
        @sort-change="handleSortChange"
      >
        <template #head-select>
          <input
            type="checkbox"
            :checked="isAllSelected"
            :indeterminate="isPartiallySelected"
            aria-label="Select all visible documents"
            @change="toggleSelectAll"
          />
        </template>

        <template #cell-select="{ row }">
          <input
            type="checkbox"
            :checked="isFileSelected(row.id)"
            :aria-label="'Select document ' + row.filename"
            @click.stop
            @change="fileshareStore.toggleFileSelection(row.id)"
          />
        </template>

        <template #cell-filename="{ row }">
          <button
            type="button"
            class="filename-col-inner"
            @click="openPreview(row)"
          >
            <span class="mime-icon" :title="row.mime_type"
              ><ClinicalIcon :name="getMimeIcon(row)" :size="16"
            /></span>
            <span class="filename-text" :title="row.filename">{{
              row.filename
            }}</span>
            <span v-if="row.version_index > 1" class="version-pill"
              >v{{ row.version_index }}.0</span
            >
          </button>
        </template>

        <template #cell-study_id="{ row }">
          <span class="study-badge">{{ row.study_id }}</span>
        </template>

        <template #cell-site_id="{ row }">
          <span v-if="row.site_id" class="site-badge">{{ row.site_id }}</span>
          <span v-else class="text-muted">Global</span>
        </template>

        <template #cell-uploaded_by="{ row }">
          <span class="uploader-name">{{
            row.uploaded_by || row.created_by
          }}</span>
        </template>

        <template #cell-uploaded_at="{ row }">
          {{ formatDate(row.uploaded_at || row.created_at) }}
        </template>

        <template #cell-status="{ row }">
          <span
            v-if="row.is_on_hold"
            class="status-pill status-hold"
            :title="row.hold_reason || 'File is under retention hold.'"
          >
            <ClinicalIcon name="lock" :size="16" />
            {{ row.status === "ON_HOLD" ? "ON HOLD" : row.status || "ON HOLD" }}
          </span>
          <span v-else class="status-pill" :class="getStatusClass(row.status)">
            {{ row.status || "ACTIVE" }}
          </span>
        </template>

        <template #cell-shared="{ row }">
          <span
            v-if="row.grant_count > 0"
            class="shared-badge has-grants"
            :title="`${row.grant_count} active share delegations`"
          >
            {{ row.grant_count }} Shared
          </span>
          <span
            v-else
            class="shared-badge private"
            title="Private to study team"
          >
            Private
          </span>
        </template>

        <template #cell-actions="{ row }">
          <div class="action-btn-group" @click.stop>
            <button
              type="button"
              class="btn btn-sm btn-outline-primary btn-preview-action"
              title="View Protected Preview"
              :aria-label="'Preview ' + row.filename"
              @click="openPreview(row)"
            >
              Preview
            </button>
            <button
              type="button"
              class="btn btn-sm btn-outline-secondary btn-share-action"
              title="Manage Share Permissions"
              :aria-label="'Share ' + row.filename"
              @click="openShareDrawerForFile(row)"
            >
              Share
            </button>
            <button
              type="button"
              class="btn btn-sm btn-outline-info btn-activity-action"
              title="View Document Activity Feed"
              :aria-label="'Activity for ' + row.filename"
              @click="openActivityDrawerForFile(row)"
            >
              Activity
            </button>
            <button
              type="button"
              class="btn btn-sm btn-outline-danger btn-delete-action"
              :disabled="row.is_on_hold"
              :title="
                row.is_on_hold
                  ? 'File is under retention hold.'
                  : 'Delete document'
              "
              :aria-label="'Delete ' + row.filename"
              @click="handleDeleteFile(row)"
            >
              Delete
            </button>
          </div>
        </template>

        <template #empty>
          <div class="empty-state-box">
            <h4>No clinical documents match your search or filter criteria.</h4>
            <p>
              Try resetting filters or expanding other study branches in the
              folder tree.
            </p>
          </div>
        </template>

        <template #pagination>
          <div v-if="totalItems > 0" class="hub-pagination-bar">
            <div class="pagination-info">
              Showing
              <span class="font-bold">{{
                Math.min((currentPage - 1) * itemsPerPage + 1, totalItems)
              }}</span>
              to
              <span class="font-bold">{{
                Math.min(currentPage * itemsPerPage, totalItems)
              }}</span>
              of
              <span class="font-bold">{{ totalItems }}</span> documents
            </div>

            <div class="pagination-buttons">
              <button
                type="button"
                class="btn btn-sm btn-outline-secondary prev-btn"
                :disabled="currentPage === 1"
                aria-label="Previous page"
                @click="currentPage--"
              >
                Previous
              </button>

              <div class="page-numbers">
                <button
                  v-for="page in totalPages"
                  :key="page"
                  type="button"
                  class="btn btn-sm page-num-btn"
                  :class="{ active: currentPage === page }"
                  :aria-label="'Go to page ' + page"
                  @click="currentPage = page"
                >
                  {{ page }}
                </button>
              </div>

              <button
                type="button"
                class="btn btn-sm btn-outline-secondary next-btn"
                :disabled="currentPage === totalPages"
                aria-label="Next page"
                @click="currentPage++"
              >
                Next
              </button>
            </div>
          </div>
        </template>
      </ClinicalDataTable>
    </main>

    <!-- Slide-in Share Management Drawer -->
    <ShareDrawer
      :is-open="isShareDrawerOpen"
      :files="filesToShare"
      @close="isShareDrawerOpen = false"
      @grant-created="onGrantCreated"
      @grant-revoked="onGrantRevoked"
    />

    <!-- Slide-in Activity Feed Drawer -->
    <ActivityFeed
      :is-open="isActivityDrawerOpen"
      :file="activityFile"
      @close="isActivityDrawerOpen = false"
    />

    <!-- Protected Media Preview Modal -->
    <MediaPreviewModal
      v-if="previewingFile"
      :file-record="previewingFile"
      :permissions="{ canDownload: true, canComment: true }"
      @close="previewingFile = null"
      @download="handleFileDownload"
    />

    <!-- Batch Ingestion Drawer -->
    <BatchUploadDrawer
      :is-open="isBatchDrawerOpen"
      :study-id="fileshareStore.activeFilters.studyId || 'STUDY-USDM-001'"
      :site-id="fileshareStore.activeFilters.siteId || null"
      @close="isBatchDrawerOpen = false"
    />
  </div>
</template>

<script setup>
import { ref, computed, defineAsyncComponent, onMounted, watch } from "vue";
import {
  ClinicalDataTable,
  ClinicalIcon,
  ClinicalIconButton,
  ClinicalInput,
} from "ui";
import { useFileshareStore } from "../stores/fileshare";
import DocumentsFolderTree from "../components/fileshare/DocumentsFolderTree.vue";
import ShareDrawer from "../components/fileshare/ShareDrawer.vue";
import ActivityFeed from "../components/fileshare/ActivityFeed.vue";
import BatchUploadDrawer from "../components/etmf/BatchUploadDrawer.vue";
import CraQcQueue from "../components/etmf/CraQcQueue.vue";
import { useConfirmDialog } from "../composables/useConfirmDialog.js";

const MediaPreviewModal = defineAsyncComponent(
  () => import("../components/fileshare/MediaPreviewModal.vue")
);

const props = defineProps({
  initialStudyId: {
    type: String,
    default: null,
  },
});

const fileshareStore = useFileshareStore();
const { confirmDialog } = useConfirmDialog();

// Batch Ingestion and CRA QC state
const isBatchDrawerOpen = ref(false);
const showQcQueue = ref(false);

// Search & Sorting state
const searchInput = ref("");
const sortField = ref("uploaded_at");
const sortDirection = ref("desc"); // "asc" | "desc"

// Pagination state
const currentPage = ref(1);
const itemsPerPage = 15;

// Modal / Drawer state
const previewingFile = ref(null);
const isShareDrawerOpen = ref(false);
const filesToShare = ref([]);
const isActivityDrawerOpen = ref(false);
const activityFile = ref(null);

const openActivityDrawerForFile = (file) => {
  activityFile.value = file;
  isActivityDrawerOpen.value = true;
};

onMounted(() => {
  if (props.initialStudyId) {
    fileshareStore.setFilter("studyId", props.initialStudyId);
  }
  fileshareStore.fetchFiles();
});

watch(
  () => props.initialStudyId,
  (newStudyId) => {
    if (newStudyId) {
      fileshareStore.setFilter("studyId", newStudyId);
    }
  }
);

function onSearchInput() {
  fileshareStore.setSearchQuery(searchInput.value);
  currentPage.value = 1;
}

function clearSearch() {
  searchInput.value = "";
  fileshareStore.setSearchQuery("");
  currentPage.value = 1;
}

function handleTreeNodeSelect(node) {
  fileshareStore.setFilter("studyId", node.studyId || null);
  fileshareStore.setFilter("siteId", node.siteId || null);
  fileshareStore.setFilter("zoneCode", node.zoneCode || null);
  fileshareStore.setFilter("sectionCode", node.sectionCode || null);
  fileshareStore.setFilter("folderCode", node.folderCode || null);
  currentPage.value = 1;
}

function clearFilter(key) {
  fileshareStore.setFilter(key, null);
  currentPage.value = 1;
}

function resetAllFilters() {
  fileshareStore.resetFilters();
  searchInput.value = "";
  currentPage.value = 1;
}

const hasActiveFilter = computed(() => {
  const f = fileshareStore.activeFilters;
  return !!(
    f.studyId ||
    f.siteId ||
    f.zoneCode ||
    f.sectionCode ||
    f.folderCode ||
    f.searchQuery
  );
});

// Sorting Logic
const documentColumns = [
  {
    key: "select",
    label: "",
    headerClass: "checkbox-col",
    cellClass: "checkbox-col",
  },
  {
    key: "filename",
    label: "Name",
    sortable: true,
    headerClass: "sortable-th",
    cellClass: "filename-col",
  },
  {
    key: "study_id",
    label: "Study",
    sortable: true,
    headerClass: "sortable-th",
  },
  { key: "site_id", label: "Site", sortable: true, headerClass: "sortable-th" },
  {
    key: "uploaded_by",
    label: "Uploaded By",
    sortable: true,
    headerClass: "sortable-th",
  },
  {
    key: "uploaded_at",
    label: "Last Modified",
    sortable: true,
    headerClass: "sortable-th",
    cellClass: "date-cell",
  },
  {
    key: "status",
    label: "Status",
    sortable: true,
    headerClass: "sortable-th",
  },
  { key: "shared", label: "Shared" },
  {
    key: "actions",
    label: "Actions",
    headerClass: "actions-th",
    cellClass: "actions-cell",
    align: "right",
  },
];

function handleSortChange({ key, order }) {
  sortField.value = key;
  sortDirection.value = order;
}

function resolveDocumentRowClass(file) {
  return ["document-row", { "row-selected": isFileSelected(file.id) }];
}

const sortedDocuments = computed(() => {
  const list = [...fileshareStore.filteredDocuments];
  const field = sortField.value;
  const dir = sortDirection.value === "asc" ? 1 : -1;

  return list.sort((a, b) => {
    let valA = a[field];
    let valB = b[field];

    if (valA === undefined || valA === null) valA = "";
    if (valB === undefined || valB === null) valB = "";

    if (typeof valA === "string") {
      return dir * valA.localeCompare(valB);
    }
    if (valA < valB) return -1 * dir;
    if (valA > valB) return 1 * dir;
    return 0;
  });
});

const totalItems = computed(() => sortedDocuments.value.length);
const totalPages = computed(() =>
  Math.max(1, Math.ceil(totalItems.value / itemsPerPage))
);

const paginatedDocuments = computed(() => {
  const start = (currentPage.value - 1) * itemsPerPage;
  return sortedDocuments.value.slice(start, start + itemsPerPage);
});

// Selection Management
const selectedFiles = computed(() => {
  return fileshareStore.files.filter((f) =>
    fileshareStore.selectedFileIds.includes(f.id)
  );
});

function isFileSelected(fileId) {
  return fileshareStore.selectedFileIds.includes(fileId);
}

const isAllSelected = computed(() => {
  const pageDocs = paginatedDocuments.value;
  if (pageDocs.length === 0) return false;
  return pageDocs.every((f) => fileshareStore.selectedFileIds.includes(f.id));
});

const isPartiallySelected = computed(() => {
  const pageDocs = paginatedDocuments.value;
  const count = pageDocs.filter((f) =>
    fileshareStore.selectedFileIds.includes(f.id)
  ).length;
  return count > 0 && count < pageDocs.length;
});

function toggleSelectAll(e) {
  const shouldSelect = e.target.checked;
  paginatedDocuments.value.forEach((f) => {
    const isSelected = fileshareStore.selectedFileIds.includes(f.id);
    if (shouldSelect && !isSelected) {
      fileshareStore.toggleFileSelection(f.id);
    } else if (!shouldSelect && isSelected) {
      fileshareStore.toggleFileSelection(f.id);
    }
  });
}

// Drawer & Modal Triggers
function openPreview(file) {
  previewingFile.value = file;
}

function openShareDrawerForFile(file) {
  filesToShare.value = [file];
  isShareDrawerOpen.value = true;
}

function openBulkShareDrawer() {
  if (selectedFiles.value.length > 0) {
    filesToShare.value = [...selectedFiles.value];
    isShareDrawerOpen.value = true;
  }
}

function onGrantCreated() {
  // refresh documents
  fileshareStore.fetchFiles();
}

function onGrantRevoked() {
  fileshareStore.fetchFiles();
}

function handleFileDownload(file) {
  console.log("Downloading regulated file:", file.filename);
}

function getMimeIcon(file) {
  const mime = (file.mime_type || "").toLowerCase();
  const name = (file.filename || "").toLowerCase();
  if (mime.includes("pdf") || name.endsWith(".pdf")) return "file-text";
  if (mime.includes("video") || name.endsWith(".mp4") || name.endsWith(".webm"))
    return "video";
  if (mime.includes("audio") || name.endsWith(".mp3") || name.endsWith(".wav"))
    return "mic";
  if (mime.includes("image") || name.endsWith(".png") || name.endsWith(".jpg"))
    return "camera";
  if (
    mime.includes("spreadsheet") ||
    name.endsWith(".xlsx") ||
    name.endsWith(".csv")
  )
    return "trending-up";
  if (mime.includes("word") || name.endsWith(".docx")) return "edit";
  return "file-text";
}

function formatDate(dateStr) {
  if (!dateStr) return "N/A";
  try {
    const d = new Date(dateStr);
    return d.toLocaleDateString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  } catch {
    return dateStr;
  }
}

function getStatusClass(status) {
  const s = (status || "").toLowerCase();
  if (s.includes("approved") || s.includes("active") || s.includes("verified"))
    return "status-approved";
  if (s.includes("review") || s.includes("pending")) return "status-review";
  if (s.includes("hold") || s.includes("archive")) return "status-hold";
  return "status-draft";
}

async function handleDeleteFile(file) {
  if (file.is_on_hold) return;
  const confirmed = await confirmDialog({
    title: "Delete Document",
    message: `Are you sure you want to delete ${file.filename}?`,
    confirmLabel: "Delete",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (confirmed) {
    await fileshareStore.deleteFile(
      file.id,
      "User deleted document from Documents Hub"
    );
  }
}
</script>

<style scoped>
.documents-hub-layout {
  display: flex;
  height: calc(100vh - 120px);
  background-color: var(--semantic-color-surface-muted);
  margin: -20px;
  overflow: hidden;
  position: relative;
}

/* Sidebar Folder Tree */
.hub-sidebar {
  width: 340px;
  min-width: 300px;
  background-color: var(--semantic-color-surface);
  height: 100%;
  overflow: hidden;
  box-shadow: var(--shadow-sm);
}

/* Main Workspace */
.hub-content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background-color: var(--semantic-color-surface);
}

.hub-header-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-bottom: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  gap: 16px;
}

.hub-page-title {
  margin: 0;
  font-size: 1.3rem;
  font-weight: 800;
  color: var(--text-main);
}

.hub-page-subtitle {
  margin: 2px 0 0 0;
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.search-input-wrapper {
  position: relative;
  width: 380px;
}

.search-icon-prefix {
  position: absolute;
  left: 12px;
  top: 50%;
  transform: translateY(-50%);
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.hub-search-input {
  width: 100%;
  padding: 9px 34px 9px 34px;
  border: 1px solid var(--border);
  border-radius: 8px;
  font-size: 0.85rem;
  background-color: var(--semantic-color-surface-muted);
  transition: all 0.2s;
  box-sizing: border-box;
}

.hub-search-input:focus-visible {
  background-color: var(--semantic-color-surface);
  border-color: var(--accent);
}

.clear-search-btn {
  position: absolute;
  right: 6px;
  top: 50%;
  transform: translateY(-50%);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background: none;
  border: none;
  border-radius: 4px;
  font-size: 1rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
}

/* Filter & Ribbon Bar */
.hub-filter-ribbon {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 24px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--border);
  min-height: 44px;
  gap: 12px;
}

.active-filter-pills {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.filter-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 8px;
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border-radius: 6px;
  font-size: 0.75rem;
}

.remove-pill-btn {
  background: none;
  border: none;
  color: var(--semantic-color-primary);
  font-weight: bold;
  cursor: pointer;
  padding: 0 2px;
  font-size: 0.85rem;
}

.bulk-actions-toolbar {
  display: flex;
  align-items: center;
  gap: 10px;
}

.selection-count-badge {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--semantic-color-primary);
  background-color: var(--semantic-color-primary-light);
  padding: 4px 8px;
  border-radius: 4px;
}

/* Data Table Container */
.hub-data-table-shell {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.hub-table-container {
  flex: 1;
  overflow-y: auto;
}

.documents-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 0.84rem;
}

.documents-table th {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
  font-size: 0.78rem;
  padding: 10px 14px;
  border-bottom: 2px solid var(--semantic-color-border);
  position: sticky;
  top: 0;
  z-index: 10;
  user-select: none;
}

.sortable-th {
  cursor: pointer;
}
.sortable-th:hover {
  color: var(--primary);
}

.sort-indicator {
  font-size: var(--font-size-2xs);
  margin-left: 4px;
  color: var(--semantic-color-text-secondary);
}

.documents-table td {
  padding: 10px 14px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
  vertical-align: middle;
}

.document-row:hover {
  background-color: var(--semantic-color-surface-muted);
}

.document-row.row-selected {
  background-color: var(--semantic-color-info-bg-soft);
}

.checkbox-col {
  width: 36px;
  text-align: center;
}

.filename-col {
  cursor: pointer;
}

.filename-col-inner {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  background: transparent;
  border: none;
  padding: 0;
  font: inherit;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.mime-icon {
  font-size: 1.1rem;
}

.filename-text {
  font-weight: 600;
  color: var(--semantic-color-text);
  max-width: 320px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.filename-col:hover .filename-text {
  color: var(--accent);
  text-decoration: underline;
}

.version-pill {
  font-size: var(--font-size-2xs);
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  padding: 1px 5px;
  border-radius: 4px;
  font-weight: 600;
}

.study-badge {
  font-family: var(--semantic-font-mono);
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-primary);
}

.site-badge {
  font-family: var(--semantic-font-mono);
  font-size: 0.78rem;
  background-color: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
}

.uploader-name {
  font-family: var(--semantic-font-mono);
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

.date-cell {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  white-space: nowrap;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
}

.status-approved {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.status-review {
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}

.status-hold {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.status-draft {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.shared-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 6px;
  white-space: nowrap;
}

.shared-badge.has-grants {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.shared-badge.private {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.actions-th {
  text-align: right;
  width: 340px;
}

.actions-cell {
  text-align: right;
}

.action-btn-group {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}

.btn-outline-info {
  border: 1px solid var(--semantic-color-primary);
  color: var(--semantic-color-primary);
  background-color: transparent;
}

.btn-outline-info:hover:not(:disabled) {
  background-color: var(--semantic-color-primary-light);
}

.btn-outline-danger {
  border: 1px solid var(--semantic-color-error);
  color: var(--semantic-color-error);
  background-color: transparent;
}

.btn-outline-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg);
}

.btn-outline-danger:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
}

/* Empty State */
.empty-grid-cell {
  padding: 60px 24px;
  text-align: center;
}

.empty-state-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.empty-state-icon {
  font-size: 3rem;
  opacity: 0.6;
}

.empty-state-box h4 {
  margin: 0;
  font-size: 1rem;
  color: var(--semantic-color-text-body);
}

.empty-state-box p {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}

/* Pagination Bar */
.hub-pagination-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 24px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  font-size: 0.82rem;
}

.pagination-buttons {
  display: flex;
  align-items: center;
  gap: 6px;
}

.page-numbers {
  display: flex;
  gap: 4px;
}

.page-num-btn.active {
  background-color: var(--accent);
  color: var(--semantic-color-text-on-accent);
  border-color: var(--accent);
}
</style>
