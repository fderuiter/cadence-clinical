<template>
  <div class="documents-hub-layout">
    <!-- Left Navigation Panel: Folder Tree -->
    <aside
      class="hub-sidebar"
      aria-label="Clinical Folder Tree"
    >
      <DocumentsFolderTree
        :studies-tree="fileshareStore.filteredStudiesTree"
        :initial-study-id="initialStudyId"
        @select-node="handleTreeNodeSelect"
      />
    </aside>

    <!-- Main Workspace: Search Bar, Toolbar & Documents Grid -->
    <main
      class="hub-content-area"
      aria-label="Documents Grid Workspace"
    >
      <!-- Top Action Bar -->
      <div class="hub-header-bar">
        <div class="header-left-col">
          <h2 class="hub-page-title">
            Documents Hub
          </h2>
          <p class="hub-page-subtitle">
            Cross-study unified clinical fileshare, protected media viewer &amp; permission delegation vault
          </p>
        </div>

        <div class="header-right-col">
          <!-- Search Bar -->
          <div class="search-input-wrapper">
            <span class="search-icon-prefix">🔍</span>
            <input
              v-model="searchInput"
              type="text"
              placeholder="Search by filename, study, uploader, or site..."
              class="hub-search-input"
              aria-label="Search all documents"
              @input="onSearchInput"
            >
            <button
              v-if="searchInput"
              class="clear-search-btn"
              aria-label="Clear search"
              @click="clearSearch"
            >
              ×
            </button>
          </div>
        </div>
      </div>

      <!-- Active Filters & Bulk Actions Ribbon -->
      <div class="hub-filter-ribbon">
        <div class="active-filter-pills">
          <span
            v-if="fileshareStore.activeFilters.studyId"
            class="filter-pill study-pill"
          >
            Study: <strong>{{ fileshareStore.activeFilters.studyId }}</strong>
            <button
              class="remove-pill-btn"
              @click="clearFilter('studyId')"
            >×</button>
          </span>

          <span
            v-if="fileshareStore.activeFilters.siteId"
            class="filter-pill site-pill"
          >
            Site: <strong>{{ fileshareStore.activeFilters.siteId }}</strong>
            <button
              class="remove-pill-btn"
              @click="clearFilter('siteId')"
            >×</button>
          </span>

          <span
            v-if="fileshareStore.activeFilters.zoneCode"
            class="filter-pill zone-pill"
          >
            Zone: <strong>{{ fileshareStore.activeFilters.zoneCode }}</strong>
            <button
              class="remove-pill-btn"
              @click="clearFilter('zoneCode')"
            >×</button>
          </span>

          <span
            v-if="fileshareStore.activeFilters.sectionCode"
            class="filter-pill section-pill"
          >
            Section: <strong>{{ fileshareStore.activeFilters.sectionCode }}</strong>
            <button
              class="remove-pill-btn"
              @click="clearFilter('sectionCode')"
            >×</button>
          </span>

          <button
            v-if="hasActiveFilter"
            class="btn btn-sm btn-outline-secondary btn-reset-all"
            @click="resetAllFilters"
          >
            Reset All Filters
          </button>
        </div>

        <!-- Bulk Selection Action Ribbon -->
        <div
          v-if="selectedFiles.length > 0"
          class="bulk-actions-toolbar"
        >
          <span class="selection-count-badge">
            ✓ {{ selectedFiles.length }} Selected
          </span>
          <button
            class="btn btn-sm btn-primary btn-bulk-share"
            @click="openBulkShareDrawer"
          >
            🔗 Bulk Share ({{ selectedFiles.length }})
          </button>
          <button
            class="btn btn-sm btn-outline-secondary btn-deselect-all"
            @click="fileshareStore.clearSelection()"
          >
            Deselect All
          </button>
        </div>
      </div>

      <!-- Main Data Table -->
      <div class="table-responsive hub-table-container">
        <table class="documents-table">
          <thead>
            <tr>
              <th class="checkbox-col">
                <input
                  type="checkbox"
                  :checked="isAllSelected"
                  :indeterminate="isPartiallySelected"
                  aria-label="Select all visible documents"
                  @change="toggleSelectAll"
                >
              </th>
              <th
                class="sortable-th"
                @click="setSort('filename')"
              >
                Name
                <span class="sort-indicator">{{ getSortIndicator('filename') }}</span>
              </th>
              <th
                class="sortable-th"
                @click="setSort('study_id')"
              >
                Study
                <span class="sort-indicator">{{ getSortIndicator('study_id') }}</span>
              </th>
              <th
                class="sortable-th"
                @click="setSort('site_id')"
              >
                Site
                <span class="sort-indicator">{{ getSortIndicator('site_id') }}</span>
              </th>
              <th
                class="sortable-th"
                @click="setSort('uploaded_by')"
              >
                Uploaded By
                <span class="sort-indicator">{{ getSortIndicator('uploaded_by') }}</span>
              </th>
              <th
                class="sortable-th"
                @click="setSort('uploaded_at')"
              >
                Last Modified
                <span class="sort-indicator">{{ getSortIndicator('uploaded_at') }}</span>
              </th>
              <th
                class="sortable-th"
                @click="setSort('status')"
              >
                Status
                <span class="sort-indicator">{{ getSortIndicator('status') }}</span>
              </th>
              <th>Shared</th>
              <th class="actions-th">
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="paginatedDocuments.length === 0">
              <td
                colspan="9"
                class="empty-grid-cell"
              >
                <div class="empty-state-box">
                  <span class="empty-state-icon">📂</span>
                  <h4>No clinical documents match your search or filter criteria.</h4>
                  <p>Try resetting filters or expanding other study branches in the folder tree.</p>
                </div>
              </td>
            </tr>

            <tr
              v-for="file in paginatedDocuments"
              :key="file.id"
              class="document-row"
              :class="{ 'row-selected': isFileSelected(file.id) }"
            >
              <!-- Multi-select Checkbox -->
              <td
                class="checkbox-col"
                @click.stop
              >
                <input
                  type="checkbox"
                  :checked="isFileSelected(file.id)"
                  :aria-label="'Select document ' + file.filename"
                  @change="fileshareStore.toggleFileSelection(file.id)"
                >
              </td>

              <!-- File Name & MIME Icon -->
              <td
                class="filename-col"
                @click="openPreview(file)"
              >
                <span
                  class="mime-icon"
                  :title="file.mime_type"
                >{{ getMimeIcon(file) }}</span>
                <span
                  class="filename-text"
                  :title="file.filename"
                >{{ file.filename }}</span>
                <span
                  v-if="file.version_index > 1"
                  class="version-pill"
                >v{{ file.version_index }}.0</span>
              </td>

              <!-- Study ID -->
              <td>
                <span class="study-badge">{{ file.study_id }}</span>
              </td>

              <!-- Site ID -->
              <td>
                <span
                  v-if="file.site_id"
                  class="site-badge"
                >{{ file.site_id }}</span>
                <span
                  v-else
                  class="text-muted"
                >Global</span>
              </td>

              <!-- Uploaded By -->
              <td>
                <span class="uploader-name">{{ file.uploaded_by || file.created_by }}</span>
              </td>

              <!-- Last Modified -->
              <td class="date-cell">
                {{ formatDate(file.uploaded_at || file.created_at) }}
              </td>

              <!-- Status -->
              <td>
                <span
                  v-if="file.is_on_hold"
                  class="status-pill status-hold"
                  :title="file.hold_reason || 'File is under retention hold.'"
                >
                  🔒 {{ file.status === 'ON_HOLD' ? 'ON HOLD' : (file.status || 'ON HOLD') }}
                </span>
                <span
                  v-else
                  class="status-pill"
                  :class="getStatusClass(file.status)"
                >
                  {{ file.status || "ACTIVE" }}
                </span>
              </td>

              <!-- Shared Active Grant Count -->
              <td>
                <span
                  v-if="file.grant_count > 0"
                  class="shared-badge has-grants"
                  :title="`${file.grant_count} active share delegations`"
                >
                  👥 {{ file.grant_count }} Shared
                </span>
                <span
                  v-else
                  class="shared-badge private"
                  title="Private to study team"
                >
                  🔒 Private
                </span>
              </td>

              <!-- Action Buttons -->
              <td
                class="actions-cell"
                @click.stop
              >
                <div class="action-btn-group">
                  <button
                    class="btn btn-sm btn-outline-primary btn-preview-action"
                    title="View Protected Preview"
                    :aria-label="'Preview ' + file.filename"
                    @click="openPreview(file)"
                  >
                    👁️ Preview
                  </button>
                  <button
                    class="btn btn-sm btn-outline-secondary btn-share-action"
                    title="Manage Share Permissions"
                    :aria-label="'Share ' + file.filename"
                    @click="openShareDrawerForFile(file)"
                  >
                    🔗 Share
                  </button>
                  <button
                    class="btn btn-sm btn-outline-info btn-activity-action"
                    title="View Document Activity Feed"
                    :aria-label="'Activity for ' + file.filename"
                    @click="openActivityDrawerForFile(file)"
                  >
                    📋 Activity
                  </button>
                  <button
                    class="btn btn-sm btn-outline-danger btn-delete-action"
                    :disabled="file.is_on_hold"
                    :title="file.is_on_hold ? 'File is under retention hold.' : 'Delete document'"
                    :aria-label="'Delete ' + file.filename"
                    @click="handleDeleteFile(file)"
                  >
                    🗑️ Delete
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination Footer -->
      <div
        v-if="totalItems > 0"
        class="hub-pagination-bar"
      >
        <div class="pagination-info">
          Showing
          <span class="font-bold">{{ Math.min((currentPage - 1) * itemsPerPage + 1, totalItems) }}</span>
          to
          <span class="font-bold">{{ Math.min(currentPage * itemsPerPage, totalItems) }}</span>
          of
          <span class="font-bold">{{ totalItems }}</span> documents
        </div>

        <div class="pagination-buttons">
          <button
            class="btn btn-sm btn-outline-secondary prev-btn"
            :disabled="currentPage === 1"
            aria-label="Previous page"
            @click="currentPage--"
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
              @click="currentPage = page"
            >
              {{ page }}
            </button>
          </div>

          <button
            class="btn btn-sm btn-outline-secondary next-btn"
            :disabled="currentPage === totalPages"
            aria-label="Next page"
            @click="currentPage++"
          >
            Next ▶
          </button>
        </div>
      </div>
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
  </div>
</template>

<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useFileshareStore } from "../stores/fileshare";
import DocumentsFolderTree from "../components/fileshare/DocumentsFolderTree.vue";
import ShareDrawer from "../components/fileshare/ShareDrawer.vue";
import ActivityFeed from "../components/fileshare/ActivityFeed.vue";
import MediaPreviewModal from "../components/fileshare/MediaPreviewModal.vue";

const props = defineProps({
  initialStudyId: {
    type: String,
    default: null,
  },
});

const fileshareStore = useFileshareStore();

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
  return !!(f.studyId || f.siteId || f.zoneCode || f.sectionCode || f.folderCode || f.searchQuery);
});

// Sorting Logic
function setSort(field) {
  if (sortField.value === field) {
    sortDirection.value = sortDirection.value === "asc" ? "desc" : "asc";
  } else {
    sortField.value = field;
    sortDirection.value = "asc";
  }
}

function getSortIndicator(field) {
  if (sortField.value !== field) return "↕";
  return sortDirection.value === "asc" ? "▲" : "▼";
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
const totalPages = computed(() => Math.max(1, Math.ceil(totalItems.value / itemsPerPage)));

const paginatedDocuments = computed(() => {
  const start = (currentPage.value - 1) * itemsPerPage;
  return sortedDocuments.value.slice(start, start + itemsPerPage);
});

// Selection Management
const selectedFiles = computed(() => {
  return fileshareStore.files.filter((f) => fileshareStore.selectedFileIds.includes(f.id));
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
  const count = pageDocs.filter((f) => fileshareStore.selectedFileIds.includes(f.id)).length;
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

  if (mime.includes("pdf") || name.endsWith(".pdf")) return "📕";
  if (mime.includes("video") || name.endsWith(".mp4") || name.endsWith(".webm")) return "🎬";
  if (mime.includes("audio") || name.endsWith(".mp3") || name.endsWith(".wav")) return "🎙️";
  if (mime.includes("image") || name.endsWith(".png") || name.endsWith(".jpg")) return "🖼️";
  if (mime.includes("spreadsheet") || name.endsWith(".xlsx") || name.endsWith(".csv")) return "📊";
  if (mime.includes("word") || name.endsWith(".docx")) return "📝";
  return "📄";
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
  if (s.includes("approved") || s.includes("active") || s.includes("verified")) return "status-approved";
  if (s.includes("review") || s.includes("pending")) return "status-review";
  if (s.includes("hold") || s.includes("archive")) return "status-hold";
  return "status-draft";
}

async function handleDeleteFile(file) {
  if (file.is_on_hold) return;
  const confirmed = window.confirm(`Are you sure you want to delete ${file.filename}?`);
  if (confirmed) {
    await fileshareStore.deleteFile(file.id, "User deleted document from Documents Hub");
  }
}
</script>

<style scoped>
.documents-hub-layout {
  display: flex;
  height: calc(100vh - 120px);
  background-color: #f8fafc;
  margin: -20px;
  overflow: hidden;
  position: relative;
}

/* Sidebar Folder Tree */
.hub-sidebar {
  width: 340px;
  min-width: 300px;
  background-color: #ffffff;
  height: 100%;
  overflow: hidden;
  box-shadow: 2px 0 6px rgba(0, 0, 0, 0.03);
}

/* Main Workspace */
.hub-content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
  background-color: #ffffff;
}

.hub-header-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-bottom: 1px solid var(--border);
  background-color: #ffffff;
  gap: 16px;
}

.hub-page-title {
  margin: 0;
  font-size: 1.3rem;
  font-weight: 800;
  color: var(--primary);
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
  color: #94a3b8;
}

.hub-search-input {
  width: 100%;
  padding: 9px 32px 9px 34px;
  border: 1px solid var(--border);
  border-radius: 8px;
  font-size: 0.85rem;
  background-color: #f8fafc;
  outline: none;
  transition: all 0.2s;
  box-sizing: border-box;
}

.hub-search-input:focus {
  background-color: #ffffff;
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.12);
}

.clear-search-btn {
  position: absolute;
  right: 10px;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  font-size: 1rem;
  color: #94a3b8;
  cursor: pointer;
}

/* Filter & Ribbon Bar */
.hub-filter-ribbon {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 24px;
  background-color: #f8fafc;
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
  background-color: #e0f2fe;
  color: #0369a1;
  border-radius: 6px;
  font-size: 0.75rem;
}

.remove-pill-btn {
  background: none;
  border: none;
  color: #0369a1;
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
  color: #0284c7;
  background-color: #e0f2fe;
  padding: 4px 8px;
  border-radius: 4px;
}

/* Data Table Container */
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
  background-color: #ffffff;
  color: #475569;
  font-weight: 600;
  font-size: 0.78rem;
  padding: 10px 14px;
  border-bottom: 2px solid #e2e8f0;
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
  font-size: 0.7rem;
  margin-left: 4px;
  color: #94a3b8;
}

.documents-table td {
  padding: 10px 14px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
  vertical-align: middle;
}

.document-row:hover {
  background-color: #f8fafc;
}

.document-row.row-selected {
  background-color: #f0f9ff;
}

.checkbox-col {
  width: 36px;
  text-align: center;
}

.filename-col {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
}

.mime-icon {
  font-size: 1.1rem;
}

.filename-text {
  font-weight: 600;
  color: #0f172a;
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
  font-size: 0.7rem;
  background-color: #f1f5f9;
  color: #475569;
  padding: 1px 5px;
  border-radius: 4px;
  font-weight: 600;
}

.study-badge {
  font-family: monospace;
  font-size: 0.78rem;
  font-weight: 600;
  color: #0369a1;
}

.site-badge {
  font-family: monospace;
  font-size: 0.78rem;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
}

.uploader-name {
  font-family: monospace;
  font-size: 0.78rem;
  color: #475569;
}

.date-cell {
  font-size: 0.78rem;
  color: #64748b;
  white-space: nowrap;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.72rem;
  font-weight: 700;
}

.status-approved {
  background-color: #dcfce7;
  color: #15803d;
}

.status-review {
  background-color: #fef9c3;
  color: #854d0e;
}

.status-hold {
  background-color: #fee2e2;
  color: #b91c1c;
}

.status-draft {
  background-color: #f1f5f9;
  color: #475569;
}

.shared-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 6px;
  white-space: nowrap;
}

.shared-badge.has-grants {
  background-color: #e0f2fe;
  color: #0284c7;
}

.shared-badge.private {
  background-color: #f1f5f9;
  color: #64748b;
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
  border: 1px solid #0284c7;
  color: #0284c7;
  background-color: transparent;
}

.btn-outline-info:hover:not(:disabled) {
  background-color: #e0f2fe;
}

.btn-outline-danger {
  border: 1px solid #ef4444;
  color: #ef4444;
  background-color: transparent;
}

.btn-outline-danger:hover:not(:disabled) {
  background-color: #fee2e2;
}

.btn-outline-danger:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  border-color: #cbd5e1;
  color: #94a3b8;
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
  color: #334155;
}

.empty-state-box p {
  margin: 0;
  font-size: 0.82rem;
  color: #64748b;
}

/* Pagination Bar */
.hub-pagination-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 24px;
  border-top: 1px solid var(--border);
  background-color: #ffffff;
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
  color: #ffffff;
  border-color: var(--accent);
}
</style>
