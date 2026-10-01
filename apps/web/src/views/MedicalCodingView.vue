<template>
  <div id="section-medical-coding" class="dashboard-section active">
    <!-- View Header -->
    <div class="section-header">
      <div class="header-text-group">
        <h2>Interactive Medical Coding Workbench</h2>
        <p>
          Standardized MedDRA and WHODrug terminology coding with verbatim
          cluster grouping, AI match confidence filtering, and 21 CFR Part 11
          dual-attribution audit logs.
        </p>
      </div>
      <div class="header-action-buttons">
        <!-- View Mode Switcher Toggle -->
        <div class="view-mode-toggle view-mode-toggle-group">
          <button
            type="button"
            class="btn btn-sm mode-btn"
            :class="{ active: codingStore.viewMode === 'INDIVIDUAL' }"
            @click="codingStore.setViewMode('INDIVIDUAL')"
          >
            📋 Individual Items
          </button>
          <button
            type="button"
            class="btn btn-sm mode-btn"
            :class="{ active: codingStore.viewMode === 'CLUSTERS' }"
            @click="codingStore.setViewMode('CLUSTERS')"
          >
            🧩 Verbatim Clusters
          </button>
        </div>

        <button
          type="button"
          class="btn btn-secondary"
          @click="codingStore.openImpactDrawer()"
        >
          📈 Up-Versioning Impact
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="codingStore.isLoading"
          @click="refreshQueue"
        >
          🔄 Refresh Queue
        </button>
      </div>
    </div>

    <!-- Batch Alert Banner -->
    <div
      v-if="codingStore.batchAlert"
      class="alert-banner"
      :class="'alert-' + (codingStore.batchAlert.type || 'info')"
      role="alert"
    >
      <div class="alert-content">
        <span class="alert-icon">
          {{
            codingStore.batchAlert.type === "success"
              ? "✅"
              : codingStore.batchAlert.type === "warning"
                ? "⚠️"
                : "ℹ️"
          }}
        </span>
        <span>{{ codingStore.batchAlert.message }}</span>
      </div>
      <button
        type="button"
        class="alert-close-btn"
        aria-label="Dismiss alert"
        @click="codingStore.dismissBatchAlert()"
      >
        ✕
      </button>
    </div>

    <!-- 21 CFR Part 11 Role Gating Notice if unauthorized -->
    <div v-if="!hasAccess" class="card gating-banner">
      <div class="gating-content">
        <span class="gating-icon">🚫</span>
        <div>
          <h3>Access Restricted: Data Management Authorization Required</h3>
          <p>
            You do not possess the required <strong>DATA_MANAGER</strong> or
            <strong>SPONSOR_DESIGNER</strong> role to interact with clinical
            coding dictionaries or submit coded ledger assignments.
          </p>
        </div>
      </div>
    </div>

    <!-- Authorized Coding Workspace -->
    <div v-else class="coding-workspace-container">
      <!-- Summary Metric Cards (Adapted for View Mode) -->
      <div
        v-if="codingStore.viewMode === 'CLUSTERS'"
        class="stats-overview-grid cluster-stats-grid"
      >
        <div
          class="stat-card"
          :class="{ 'stat-active': codingStore.filters.status === 'ALL' }"
          @click="setStatusFilter('ALL')"
        >
          <div class="stat-number">
            {{ codingStore.totalClustersCount }}
          </div>
          <div class="stat-title">Verbatim Clusters</div>
        </div>

        <div class="stat-card stat-instances">
          <div class="stat-number">
            {{ codingStore.totalClusterInstancesCount }}
          </div>
          <div class="stat-title">Subject Instances</div>
        </div>

        <div
          class="stat-card stat-suggested"
          :class="{ 'stat-active': codingStore.confidenceThreshold === 'HIGH' }"
          @click="setConfidenceFilter('HIGH')"
        >
          <div class="stat-number">
            {{ codingStore.highConfidenceClustersCount }}
          </div>
          <div class="stat-title">High Conf. Clusters (≥90%)</div>
        </div>

        <div
          class="stat-card stat-coded"
          :class="{ 'stat-active': codingStore.filters.status === 'CODED_ALL' }"
          @click="setStatusFilter('CODED_ALL')"
        >
          <div class="stat-number">
            {{ codingStore.autoCodingProgressPct }}%
          </div>
          <div class="stat-title">Auto-Coded Progress</div>
        </div>

        <div
          class="stat-card stat-query"
          :class="{
            'stat-active': codingStore.filters.status === 'QUERY_PENDING',
          }"
          @click="setStatusFilter('QUERY_PENDING')"
        >
          <div class="stat-number">
            {{ codingStore.queryPendingCount }}
          </div>
          <div class="stat-title">Query Pending</div>
        </div>
      </div>

      <!-- Individual View Summary Cards -->
      <div v-else class="stats-overview-grid">
        <div
          class="stat-card"
          :class="{ 'stat-active': codingStore.filters.status === 'ALL' }"
          @click="setStatusFilter('ALL')"
        >
          <div class="stat-number">
            {{ codingStore.totalCount }}
          </div>
          <div class="stat-title">Total Verbatims</div>
        </div>

        <div
          class="stat-card stat-uncoded"
          :class="{ 'stat-active': codingStore.filters.status === 'UNCODED' }"
          @click="setStatusFilter('UNCODED')"
        >
          <div class="stat-number">
            {{ codingStore.uncodedCount }}
          </div>
          <div class="stat-title">Uncoded</div>
        </div>

        <div
          class="stat-card stat-suggested"
          :class="{ 'stat-active': codingStore.filters.status === 'SUGGESTED' }"
          @click="setStatusFilter('SUGGESTED')"
        >
          <div class="stat-number">
            {{ codingStore.suggestedCount }}
          </div>
          <div class="stat-title">Suggested Matches</div>
        </div>

        <div
          class="stat-card stat-coded"
          :class="{ 'stat-active': codingStore.filters.status === 'CODED_ALL' }"
          @click="setStatusFilter('CODED_ALL')"
        >
          <div class="stat-number">
            {{ codingStore.codedCount }}
          </div>
          <div class="stat-title">Coded</div>
        </div>

        <div
          class="stat-card stat-query"
          :class="{
            'stat-active': codingStore.filters.status === 'QUERY_PENDING',
          }"
          @click="setStatusFilter('QUERY_PENDING')"
        >
          <div class="stat-number">
            {{ codingStore.queryPendingCount }}
          </div>
          <div class="stat-title">Query Pending</div>
        </div>
      </div>

      <!-- Filter and Search Toolbar -->
      <div class="toolbar-card">
        <div class="filter-group">
          <!-- Status Tabs -->
          <div class="status-tab-group">
            <button
              type="button"
              class="tab-btn"
              :class="{ active: codingStore.filters.status === 'ALL' }"
              @click="setStatusFilter('ALL')"
            >
              All
            </button>
            <button
              type="button"
              class="tab-btn"
              :class="{ active: codingStore.filters.status === 'UNCODED' }"
              @click="setStatusFilter('UNCODED')"
            >
              Uncoded ({{ codingStore.uncodedCount }})
            </button>
            <button
              type="button"
              class="tab-btn"
              :class="{ active: codingStore.filters.status === 'SUGGESTED' }"
              @click="setStatusFilter('SUGGESTED')"
            >
              Suggested ({{ codingStore.suggestedCount }})
            </button>
            <button
              type="button"
              class="tab-btn"
              :class="{ active: codingStore.filters.status === 'CODED_ALL' }"
              @click="setStatusFilter('CODED_ALL')"
            >
              Coded ({{ codingStore.codedCount }})
            </button>
            <button
              type="button"
              class="tab-btn"
              :class="{
                active: codingStore.filters.status === 'QUERY_PENDING',
              }"
              @click="setStatusFilter('QUERY_PENDING')"
            >
              Queries ({{ codingStore.queryPendingCount }})
            </button>
          </div>

          <!-- Dictionary Filter -->
          <div class="dict-select-box">
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select
              v-model="codingStore.filters.dictionaryType"
              class="form-control-sm"
              title="Filter by dictionary system"
            >
              <option value="ALL">All Dictionaries</option>
              <option value="MEDDRA">MedDRA</option>
              <option value="WHODRUG">WHODrug</option>
            </select>
          </div>

          <!-- Confidence Threshold Filter -->
          <div class="confidence-select-box">
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select
              v-model="codingStore.confidenceThreshold"
              class="form-control-sm confidence-select"
              title="Filter by match confidence threshold"
            >
              <option value="ALL">All Confidence Scores</option>
              <option value="HIGH">≥ 90% High Confidence</option>
              <option value="MEDIUM">70–89% Medium Confidence</option>
              <option value="LOW">&lt; 70% Low Confidence</option>
            </select>
          </div>
        </div>

        <!-- Search Input -->
        <div class="search-box">
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model="codingStore.filters.search"
            type="text"
            class="form-control-sm search-input"
            placeholder="Search verbatim, code, or term..."
          />
        </div>
      </div>

      <!-- Main Queue Table Component -->
      <CodingQueueTable />
    </div>

    <!-- Modals & Drawers -->
    <DictionaryBrowserModal />
    <UpversioningImpactDrawer />
  </div>
</template>

<script setup>
import { computed, onMounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { useCodingStore } from "../stores/coding";
import { hasRequiredRole } from "../router";
import CodingQueueTable from "../components/CodingQueueTable.vue";
import DictionaryBrowserModal from "../components/DictionaryBrowserModal.vue";
import UpversioningImpactDrawer from "../components/UpversioningImpactDrawer.vue";

const authStore = useAuthStore();
const codingStore = useCodingStore();

const hasAccess = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "data_manager",
    "sponsor_designer",
    "sponsor_admin",
    "super_admin",
  ]);
});

function setStatusFilter(status) {
  codingStore.filters.status = status;
}

function setConfidenceFilter(threshold) {
  if (codingStore.confidenceThreshold === threshold) {
    codingStore.setConfidenceThreshold("ALL");
  } else {
    codingStore.setConfidenceThreshold(threshold);
  }
}

async function refreshQueue() {
  await codingStore.fetchAssignments();
}

onMounted(() => {
  codingStore.fetchAssignments();
});
</script>

<style scoped>
.coding-workspace-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  width: 100%;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 16px;
  margin-bottom: var(--spacing-md);
}

.header-text-group h2 {
  margin: 0 0 6px 0;
  font-size: 1.5rem;
  color: var(--primary);
}

.header-text-group p {
  margin: 0;
  font-size: 0.9rem;
  color: var(--neutral-dark);
}

.header-action-buttons {
  display: flex;
  align-items: center;
  gap: 10px;
}

.view-mode-toggle-group {
  display: flex;
  background-color: #e2e8f0;
  border-radius: 6px;
  padding: 3px;
  gap: 2px;
}

.mode-btn {
  border: none;
  background: transparent;
  padding: 6px 12px;
  font-size: 0.82rem;
  font-weight: 600;
  color: #475569;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.mode-btn.active {
  background-color: white;
  color: var(--primary);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.alert-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 500;
  margin-bottom: var(--spacing-xs);
}

.alert-success {
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
}

.alert-warning {
  background-color: #fffbeb;
  border: 1px solid #fef08a;
  color: #854d0e;
}

.alert-info {
  background-color: #f0f9ff;
  border: 1px solid #bae6fd;
  color: #075985;
}

.alert-content {
  display: flex;
  align-items: center;
  gap: 10px;
}

.alert-close-btn {
  background: transparent;
  border: none;
  font-size: 1rem;
  cursor: pointer;
  color: inherit;
  opacity: 0.7;
}

.alert-close-btn:hover {
  opacity: 1;
}

.gating-banner {
  border-left: 4px solid var(--error);
  background-color: var(--error-bg);
  padding: 20px;
  border-radius: 6px;
}

.gating-content {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}

.gating-icon {
  font-size: 2rem;
}

.gating-content h3 {
  margin: 0 0 6px 0;
  font-size: 1.1rem;
  color: var(--error);
}

.gating-content p {
  margin: 0;
  font-size: 0.88rem;
  color: var(--primary);
}

.stats-overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
  gap: 12px;
}

.stat-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.15s ease;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.stat-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.05);
  border-color: var(--accent);
}

.stat-active {
  border-color: var(--accent) !important;
  background-color: #f0f9ff !important;
  box-shadow: 0 0 0 2px var(--accent-bg);
}

.stat-number {
  font-size: 1.8rem;
  font-weight: 800;
  line-height: 1;
  color: var(--primary);
}

.stat-title {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--neutral-dark);
}

.stat-uncoded .stat-number {
  color: #64748b;
}

.stat-suggested .stat-number {
  color: #0284c7;
}

.stat-coded .stat-number {
  color: var(--success);
}

.stat-query .stat-number {
  color: var(--warning);
}

.stat-instances .stat-number {
  color: #4f46e5;
}

.toolbar-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 12px 16px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.status-tab-group {
  display: flex;
  background-color: #f1f5f9;
  padding: 3px;
  border-radius: 6px;
  gap: 2px;
}

.tab-btn {
  background: transparent;
  border: none;
  padding: 6px 12px;
  font-size: 0.82rem;
  font-weight: 600;
  color: #475569;
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: var(--primary);
}

.tab-btn.active {
  background-color: white;
  color: var(--primary);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.dict-select-box select,
.confidence-select-box select {
  font-size: 0.82rem;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background-color: white;
}

.confidence-select {
  font-weight: 600;
  color: #0369a1;
  border-color: #7dd3fc;
  background-color: #f0f9ff;
}

.search-box {
  min-width: 240px;
}

.search-input {
  width: 100%;
  padding: 6px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.85rem;
}

.search-input:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--accent-bg);
}
</style>
