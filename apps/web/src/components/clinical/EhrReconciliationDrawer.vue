<template>
  <div
    v-if="isOpen"
    class="drawer-backdrop"
    role="presentation"
    @click.self="closeDrawer"
  >
    <aside
      ref="drawerRef"
      class="ehr-reconciliation-drawer"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-heading"
    >
      <!-- Drawer Header -->
      <header class="drawer-header">
        <div class="header-titles">
          <div class="badge-row">
            <span class="badge badge-smart">SMART on FHIR</span>
            <span class="badge badge-gxp">21 CFR Part 11</span>
            <span class="badge badge-provenance">Immutable Provenance</span>
          </div>
          <h2 id="drawer-heading" class="drawer-title">
            eSource EHR Reconciliation Drawer
          </h2>
          <p class="drawer-subtitle">
            Side-by-side reconciliation of candidate EHR records with current
            eCRF fields. Non-destructive review gating eliminates silent
            background data overwrites.
          </p>
        </div>

        <button
          type="button"
          class="btn-close"
          aria-label="Close Reconciliation Drawer"
          @click="closeDrawer"
        >
          ✕
        </button>
      </header>

      <!-- Subject & Study Context Bar -->
      <div class="context-bar">
        <div class="context-item">
          <span class="context-label">Study:</span>
          <span class="context-value">{{ studyId || "STUDY-101" }}</span>
        </div>
        <div class="context-item">
          <span class="context-label">Subject Pseudonym:</span>
          <span class="context-value mono">{{
            subjectPseudonym || subjectId || "PAT-001"
          }}</span>
        </div>
        <div class="context-item">
          <span class="context-label">Site:</span>
          <span class="context-value">{{ siteId || "SITE-01" }}</span>
        </div>
        <div class="context-actions">
          <button
            type="button"
            class="btn btn-sm btn-secondary"
            :disabled="isLoading"
            @click="fetchCandidates"
          >
            🔄 Refresh
          </button>
        </div>
      </div>

      <!-- Metric Cards / Summary Counters -->
      <div class="metrics-grid">
        <div class="metric-card">
          <span class="metric-num">{{ candidates.length }}</span>
          <span class="metric-label">Total Ingested</span>
        </div>
        <div class="metric-card metric-pending">
          <span class="metric-num">{{ pendingCount }}</span>
          <span class="metric-label">Pending Review</span>
        </div>
        <div class="metric-card metric-accepted">
          <span class="metric-num">{{ acceptedCount }}</span>
          <span class="metric-label">Accepted & Committed</span>
        </div>
        <div class="metric-card metric-rejected">
          <span class="metric-num">{{ rejectedCount }}</span>
          <span class="metric-label">Rejected</span>
        </div>
        <div class="metric-card metric-queried">
          <span class="metric-num">{{ queriedCount }}</span>
          <span class="metric-label">Queries Raised</span>
        </div>
      </div>

      <!-- Filters & Search Toolbar -->
      <div class="toolbar">
        <!-- CDISC Domain Filter Tabs -->
        <div
          class="domain-tabs"
          role="tablist"
          aria-label="CDISC Domain Filter"
        >
          <button
            v-for="d in domainOptions"
            :key="d.value"
            role="tab"
            :aria-selected="selectedDomain === d.value"
            :class="['domain-tab', { active: selectedDomain === d.value }]"
            @click="selectedDomain = d.value"
          >
            {{ d.label }}
          </button>
        </div>

        <!-- Quick Select Toolbar Actions -->
        <div class="quick-select-bar">
          <button
            type="button"
            class="btn btn-sm btn-outline-primary btn-select-high-confidence"
            title="Select all pending items with confidence score >= 90%"
            @click="selectHighConfidence"
          >
            ⚡ Select High Confidence (≥90%)
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-secondary btn-toggle-select-all"
            @click="toggleSelectAllPending"
          >
            {{
              isAllPendingSelected
                ? "Deselect All Pending"
                : "Select All Pending"
            }}
          </button>
        </div>

        <!-- Status Filter & Search Input -->
        <div class="toolbar-controls">
          <div class="filter-control">
            <label for="status-filter" class="filter-label">Status:</label>
            <select
              id="status-filter"
              v-model="selectedStatus"
              class="select-input"
            >
              <option value="ALL">All Statuses</option>
              <option value="PENDING_REVIEW">Pending Review</option>
              <option value="ACCEPTED">Accepted</option>
              <option value="REJECTED">Rejected</option>
              <option value="QUERIED">Queried</option>
            </select>
          </div>

          <div class="search-control">
            <input
              v-model="searchQuery"
              type="text"
              class="search-input"
              placeholder="Search variable, test code, or value..."
              aria-label="Search candidate records"
            />
          </div>
        </div>
      </div>

      <!-- Floating Action Bar for Selected Items -->
      <div v-if="selectedCandidateIds.length > 0" class="floating-batch-bar">
        <div class="batch-count-summary">
          <span class="batch-count-badge">{{
            selectedCandidateIds.length
          }}</span>
          item(s) selected
        </div>
        <div class="batch-actions">
          <button
            type="button"
            class="btn btn-success btn-batch-accept"
            :disabled="isSubmitting"
            @click="handleBatchAccept"
          >
            ✓ Batch Accept ({{ selectedCandidateIds.length }})
          </button>
          <button
            type="button"
            class="btn btn-danger btn-batch-reject"
            :disabled="isSubmitting"
            @click="openBatchRejectModal"
          >
            ✕ Batch Reject ({{ selectedCandidateIds.length }})
          </button>
          <button
            type="button"
            class="btn btn-ghost btn-clear-selection"
            @click="clearSelection"
          >
            Clear Selection
          </button>
        </div>
      </div>

      <!-- Main Split-Screen Reconciliation View -->
      <div class="drawer-body">
        <div v-if="isLoading" class="state-container">
          <SkeletonLoader type="card" :count="3" />
        </div>

        <div
          v-else-if="filteredCandidates.length === 0"
          class="state-container empty-state"
        >
          <EmptyState
            icon="📋"
            title="No Candidate Records Found"
            message="No EHR source data items match the current filters or need reconciliation."
          >
            <template #action>
              <button
                type="button"
                class="btn btn-primary btn-sm btn-refresh-candidates"
                @click="fetchCandidates"
              >
                🔄 Refresh EHR Ingestion
              </button>
              <button
                v-if="
                  selectedDomain !== 'ALL' ||
                  selectedStatus !== 'ALL' ||
                  searchQuery.trim()
                "
                type="button"
                class="btn btn-secondary btn-sm btn-clear-filters"
                @click="resetFilters"
              >
                Clear Filters
              </button>
            </template>
          </EmptyState>
        </div>

        <div v-else class="candidate-list">
          <article
            v-for="item in filteredCandidates"
            :key="item.id"
            class="reconciliation-card"
            :class="[
              `status-${item.status.toLowerCase()}`,
              `disc-${item.discrepancy_type.toLowerCase()}`,
            ]"
          >
            <!-- Card Header: Variable & Provenance Tag -->
            <div class="card-header">
              <div class="card-selection-check">
                <input
                  :id="`select-cand-${item.id}`"
                  v-model="selectedCandidateIds"
                  type="checkbox"
                  :value="item.id"
                  :disabled="item.status !== 'PENDING_REVIEW'"
                  class="candidate-checkbox"
                  :aria-label="`Select candidate ${item.target_variable}`"
                />
              </div>
              <div class="card-meta-left">
                <span class="domain-tag">{{ item.target_domain }}</span>
                <span class="variable-tag mono">{{
                  item.target_variable
                }}</span>
                <span class="test-name">{{
                  item.cdash_test || item.cdash_testcd
                }}</span>
              </div>
              <div class="card-meta-right">
                <span
                  :class="[
                    'discrepancy-pill',
                    `pill-${item.discrepancy_type.toLowerCase()}`,
                  ]"
                >
                  {{ formatDiscrepancy(item.discrepancy_type) }}
                </span>
                <span
                  :class="[
                    'status-pill',
                    `status-${item.status.toLowerCase()}`,
                  ]"
                >
                  {{ formatStatus(item.status) }}
                </span>
              </div>
            </div>

            <!-- Split-Screen Grid: Left (Candidate EHR Data) vs Right (Current eCRF Field) -->
            <div class="split-screen-grid">
              <!-- LEFT PANE: Ingested EHR Source Data -->
              <div class="split-pane ehr-pane">
                <div class="pane-header">
                  <span class="pane-title">🏥 Candidate EHR Source Data</span>
                  <span class="source-badge">{{ item.ehr_system_id }}</span>
                </div>
                <div class="data-row-primary">
                  <span class="val-num">{{
                    formatValue(item.candidate_value)
                  }}</span>
                  <span v-if="item.candidate_unit" class="val-unit">{{
                    item.candidate_unit
                  }}</span>
                </div>
                <div class="provenance-metadata">
                  <div class="meta-row">
                    <span class="lbl">Resource:</span>
                    <span class="val mono"
                      >{{ item.fhir_resource_type }}/{{
                        item.fhir_resource_id
                      }}</span
                    >
                  </div>
                  <div class="meta-row">
                    <span class="lbl">Obs Timestamp:</span>
                    <span class="val">{{
                      formatDate(item.source_timestamp)
                    }}</span>
                  </div>
                  <div class="meta-row">
                    <span class="lbl">Confidence:</span>
                    <span class="val"
                      >{{ Math.round(item.confidence_score * 100) }}%</span
                    >
                  </div>
                </div>
              </div>

              <!-- RIGHT PANE: Current eCRF Field in EDC -->
              <div class="split-pane ecrf-pane">
                <div class="pane-header">
                  <span class="pane-title">📝 Current eCRF Target Field</span>
                  <span class="form-badge">{{ item.target_domain }} Form</span>
                </div>
                <div class="data-row-primary">
                  <span
                    v-if="
                      item.current_ecrf_value !== null &&
                      item.current_ecrf_value !== undefined
                    "
                    class="val-num"
                  >
                    {{ formatValue(item.current_ecrf_value) }}
                  </span>
                  <span v-else class="val-empty italic">
                    [Empty / Unentered Field]
                  </span>
                </div>
                <div class="provenance-metadata">
                  <div class="meta-row">
                    <span class="lbl">Field Target:</span>
                    <span class="val mono">{{ item.target_variable }}</span>
                  </div>
                  <div class="meta-row">
                    <span class="lbl">Last Modified:</span>
                    <span class="val">{{
                      item.current_ecrf_updated_at
                        ? formatDate(item.current_ecrf_updated_at)
                        : "Not populated"
                    }}</span>
                  </div>
                  <div class="meta-row">
                    <span class="lbl">Mapping Rule:</span>
                    <span
                      class="val truncate"
                      :title="item.mapping_provenance"
                      >{{ item.mapping_provenance }}</span
                    >
                  </div>
                </div>
              </div>
            </div>

            <!-- Resolution / Audit Trail Banner (If Resolved) -->
            <div
              v-if="item.status !== 'PENDING_REVIEW'"
              class="resolution-banner"
              :class="`banner-${item.status.toLowerCase()}`"
            >
              <div class="resolution-info">
                <strong
                  >{{ formatResolutionAction(item.resolution_action) }}:</strong
                >
                <span class="reason-text">{{
                  item.resolution_reason || "No comment provided"
                }}</span>
                <span class="resolved-by-tag"
                  >by {{ item.resolved_by || "CRC Coordinator" }} at
                  {{ formatDate(item.resolved_at) }}</span
                >
              </div>
              <div v-if="item.electronic_signature" class="esign-badge">
                ✍️ 21 CFR Part 11 eSigned
              </div>
              <div v-if="item.clinical_query_id" class="query-badge">
                ❓ Query #{{ item.clinical_query_id.substring(0, 8) }} Open
              </div>
            </div>

            <!-- Card Footer: Non-Destructive 1-Click Action Buttons & Provenance Inspector -->
            <footer class="card-footer">
              <div class="action-btn-group">
                <!-- 1-Click Accept & Commit -->
                <button
                  type="button"
                  class="btn btn-action btn-accept"
                  :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                  @click="promptAccept(item)"
                >
                  <span class="icon">✓</span> Accept & Commit
                </button>

                <!-- 1-Click Reject with Reason -->
                <button
                  type="button"
                  class="btn btn-action btn-reject"
                  :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                  @click="openRejectModal(item)"
                >
                  <span class="icon">✕</span> Reject with Reason
                </button>

                <!-- 1-Click Raise Clinical Query -->
                <button
                  type="button"
                  class="btn btn-action btn-query"
                  :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                  @click="openQueryModal(item)"
                >
                  <span class="icon">❓</span> Raise Query
                </button>
              </div>

              <!-- Provenance Inspector Toggle -->
              <button
                type="button"
                class="btn btn-link-provenance"
                @click="openProvenanceModal(item)"
              >
                🔍 Inspect Provenance
              </button>
            </footer>
          </article>
        </div>
      </div>

      <!-- MODAL 1: Reject with Reason Dialog -->
      <div
        v-if="showRejectModal"
        class="modal-backdrop"
        role="presentation"
        @click.self="closeRejectModal"
      >
        <div
          ref="rejectModalRef"
          class="modal-card"
          role="dialog"
          aria-modal="true"
          aria-labelledby="reject-modal-title"
          tabindex="-1"
        >
          <div class="modal-header">
            <h3 id="reject-modal-title">Reject EHR Candidate Data</h3>
            <button
              type="button"
              class="btn-close"
              aria-label="Close reject dialog"
              @click="closeRejectModal"
            >
              ✕
            </button>
          </div>
          <div class="modal-body">
            <p class="modal-instruction">
              Under GxP and FDA Electronic Source Data guidelines, a clinical
              justification is mandatory when rejecting candidate EHR records.
            </p>
            <div class="field-summary">
              <strong>Field:</strong> {{ activeItem?.target_variable }} ({{
                activeItem?.cdash_test
              }})<br />
              <strong>Candidate Value:</strong>
              {{ activeItem?.candidate_value }} {{ activeItem?.candidate_unit }}
            </div>
            <div class="form-group">
              <label for="reject-reason" class="form-label"
                >Rejection Reason: *</label
              >
              <textarea
                id="reject-reason"
                v-model="rejectReasonInput"
                class="textarea-input"
                rows="4"
                placeholder="e.g., Outside protocol visit window, duplicate entry, clinical value verified inconsistent with chart notes..."
              />
            </div>
          </div>
          <div class="modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="closeRejectModal"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn btn-danger"
              :disabled="!rejectReasonInput.trim() || isSubmitting"
              @click="submitReject"
            >
              Confirm Rejection
            </button>
          </div>
        </div>
      </div>

      <!-- MODAL 2: Raise Clinical Query Dialog -->
      <div
        v-if="showQueryModal"
        class="modal-backdrop"
        role="presentation"
        @click.self="closeQueryModal"
      >
        <div
          ref="queryModalRef"
          class="modal-card"
          role="dialog"
          aria-modal="true"
          aria-labelledby="query-modal-title"
          tabindex="-1"
        >
          <div class="modal-header">
            <h3 id="query-modal-title">Raise GxP Discrepancy Query</h3>
            <button
              type="button"
              class="btn-close"
              aria-label="Close query dialog"
              @click="closeQueryModal"
            >
              ✕
            </button>
          </div>
          <div class="modal-body">
            <p class="modal-instruction">
              Opens a formal clinical query in the study query ledger to flag a
              discrepancy between the EHR source and the eCRF.
            </p>
            <div class="field-summary">
              <strong>Target Field:</strong> {{ activeItem?.target_variable
              }}<br />
              <strong>EHR Source Value:</strong>
              {{ activeItem?.candidate_value }} {{ activeItem?.candidate_unit
              }}<br />
              <strong>Current eCRF Value:</strong>
              {{ activeItem?.current_ecrf_value || "[Empty]" }}
            </div>
            <div class="form-group">
              <label for="query-explanation" class="form-label"
                >Discrepancy Explanation: *</label
              >
              <textarea
                id="query-explanation"
                v-model="queryExplanationInput"
                class="textarea-input"
                rows="4"
                placeholder="Describe the discrepancy and required resolution from the study investigator..."
              />
            </div>
          </div>
          <div class="modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="closeQueryModal"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn btn-primary"
              :disabled="!queryExplanationInput.trim() || isSubmitting"
              @click="submitQuery"
            >
              Raise Query
            </button>
          </div>
        </div>
      </div>

      <!-- MODAL 3: Source Provenance & Part 11 Audit Trail Viewer -->
      <div
        v-if="showProvenanceModal"
        class="modal-backdrop"
        role="presentation"
        @click.self="closeProvenanceModal"
      >
        <div
          ref="provenanceModalRef"
          class="modal-card modal-lg"
          role="dialog"
          aria-modal="true"
          aria-labelledby="prov-modal-title"
          tabindex="-1"
        >
          <div class="modal-header">
            <h3 id="prov-modal-title">
              FHIR Resource Provenance & Audit Trail
            </h3>
            <button
              type="button"
              class="btn-close"
              aria-label="Close provenance dialog"
              @click="closeProvenanceModal"
            >
              ✕
            </button>
          </div>
          <div class="modal-body">
            <div class="provenance-summary-grid">
              <div class="prov-stat">
                <span class="lbl">EHR System:</span>
                <span class="val">{{ activeItem?.ehr_system_id }}</span>
              </div>
              <div class="prov-stat">
                <span class="lbl">FHIR Resource:</span>
                <span class="val mono"
                  >{{ activeItem?.fhir_resource_type }}/{{
                    activeItem?.fhir_resource_id
                  }}</span
                >
              </div>
              <div class="prov-stat">
                <span class="lbl">Observation Date:</span>
                <span class="val">{{
                  activeItem?.source_timestamp
                    ? formatDate(activeItem.source_timestamp)
                    : ""
                }}</span>
              </div>
              <div class="prov-stat">
                <span class="lbl">CDASH Target:</span>
                <span class="val mono">{{ activeItem?.target_variable }}</span>
              </div>
            </div>

            <div class="raw-fhir-container">
              <h4 class="section-subheading">Raw FHIR JSON Payload:</h4>
              <pre
                class="json-code"
              ><code>{{ JSON.stringify(activeItem?.raw_fhir_resource, null, 2) }}</code></pre>
            </div>
          </div>
          <div class="modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="closeProvenanceModal"
            >
              Close
            </button>
          </div>
        </div>
      </div>

      <!-- MODAL 4: Batch Reject Dialog -->
      <div
        v-if="showBatchRejectModal"
        class="modal-backdrop"
        role="presentation"
        @click.self="closeBatchRejectModal"
      >
        <div
          ref="batchRejectModalRef"
          class="modal-card"
          role="dialog"
          aria-modal="true"
          aria-labelledby="batch-reject-modal-title"
          tabindex="-1"
        >
          <div class="modal-header">
            <h3 id="batch-reject-modal-title">
              Batch Reject EHR Candidate Data
            </h3>
            <button
              type="button"
              class="btn-close"
              aria-label="Close batch reject dialog"
              @click="closeBatchRejectModal"
            >
              ✕
            </button>
          </div>
          <div class="modal-body">
            <p class="modal-instruction">
              Under GxP and FDA Electronic Source Data guidelines, a clinical
              justification is mandatory when rejecting candidate EHR records.
              The rationale below will be recorded in Part 11 audit trails
              across all {{ selectedCandidateIds.length }} selected items.
            </p>
            <div class="field-summary">
              <strong>Selected Records:</strong>
              {{ selectedCandidateIds.length }} pending candidate record(s)
            </div>
            <div class="form-group">
              <label for="batch-reject-reason" class="form-label"
                >Clinical Rationale: *</label
              >
              <textarea
                id="batch-reject-reason"
                v-model="batchRejectReasonInput"
                class="textarea-input"
                rows="4"
                placeholder="e.g., Outside visit window, duplicate observations, inconsistent with chart notes..."
              />
            </div>
          </div>
          <div class="modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="closeBatchRejectModal"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn btn-danger btn-submit-batch-reject"
              :disabled="!batchRejectReasonInput.trim() || isSubmitting"
              @click="submitBatchReject"
            >
              Confirm Batch Rejection ({{ selectedCandidateIds.length }})
            </button>
          </div>
        </div>
      </div>
    </aside>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { EmptyState, useFocusTrap } from "ui";
import { interopService } from "../../api/interop";
import SkeletonLoader from "../common/SkeletonLoader.vue";

export interface EhrCandidate {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string;
  cdash_test?: string;
  candidate_value: any;
  candidate_unit?: string;
  current_ecrf_value?: any;
  current_ecrf_updated_at?: string;
  discrepancy_type:
    | "NEW_FIELD"
    | "EXACT_MATCH"
    | "VALUE_MISMATCH"
    | "UNIT_MISMATCH"
    | "DATE_MISMATCH"
    | "NOT_COMPARED";
  confidence_score: number;
  mapping_provenance: string;
  raw_fhir_resource: Record<string, any>;
  status: "PENDING_REVIEW" | "ACCEPTED" | "REJECTED" | "QUERIED";
  resolution_action?: string;
  resolution_reason?: string;
  resolved_by?: string;
  resolved_at?: string;
  electronic_signature?: Record<string, any>;
  clinical_query_id?: string;
  created_at?: string;
  version_index: number;
}

const props = defineProps<{
  isOpen: boolean;
  studyId?: string;
  subjectId?: string;
  subjectPseudonym?: string;
  siteId?: string;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "commit", candidate: EhrCandidate): void;
  (e: "batch-commit", candidates: EhrCandidate[]): void;
  (e: "reject", candidate: EhrCandidate): void;
  (e: "batch-reject", candidates: EhrCandidate[]): void;
  (e: "query", candidate: EhrCandidate, queryId: string): void;
}>();

const candidates = ref<EhrCandidate[]>([]);
const selectedCandidateIds = ref<string[]>([]);
const isLoading = ref(false);
const isSubmitting = ref(false);

const selectedDomain = ref<string>("ALL");
const selectedStatus = ref<string>("ALL");
const searchQuery = ref<string>("");

function resetFilters() {
  selectedDomain.value = "ALL";
  selectedStatus.value = "ALL";
  searchQuery.value = "";
}

const activeItem = ref<EhrCandidate | null>(null);
const drawerRef = ref<HTMLElement | null>(null);
const rejectModalRef = ref<HTMLElement | null>(null);
const batchRejectModalRef = ref<HTMLElement | null>(null);
const queryModalRef = ref<HTMLElement | null>(null);
const provenanceModalRef = ref<HTMLElement | null>(null);
const showRejectModal = ref(false);
const rejectReasonInput = ref("");
const showBatchRejectModal = ref(false);
const batchRejectReasonInput = ref("");
const showQueryModal = ref(false);
const queryExplanationInput = ref("");
const showProvenanceModal = ref(false);

useFocusTrap(
  drawerRef,
  computed(() => props.isOpen),
  {
    onEscape: () => closeDrawer(),
  }
);

useFocusTrap(rejectModalRef, showRejectModal, {
  onEscape: () => closeRejectModal(),
});

useFocusTrap(queryModalRef, showQueryModal, {
  onEscape: () => closeQueryModal(),
});

useFocusTrap(provenanceModalRef, showProvenanceModal, {
  onEscape: () => closeProvenanceModal(),
});

useFocusTrap(batchRejectModalRef, showBatchRejectModal, {
  onEscape: () => closeBatchRejectModal(),
});

const isAllPendingSelected = computed(() => {
  const pendingFiltered = filteredCandidates.value.filter(
    (c) => c.status === "PENDING_REVIEW"
  );
  if (pendingFiltered.length === 0) return false;
  return pendingFiltered.every((c) =>
    selectedCandidateIds.value.includes(c.id)
  );
});

function selectHighConfidence() {
  const highConfPending = candidates.value
    .filter((c) => c.status === "PENDING_REVIEW" && c.confidence_score >= 0.9)
    .map((c) => c.id);
  const set = new Set([...selectedCandidateIds.value, ...highConfPending]);
  selectedCandidateIds.value = Array.from(set);
}

function toggleSelectAllPending() {
  const pendingFiltered = filteredCandidates.value.filter(
    (c) => c.status === "PENDING_REVIEW"
  );
  if (isAllPendingSelected.value) {
    const pendingIds = new Set(pendingFiltered.map((c) => c.id));
    selectedCandidateIds.value = selectedCandidateIds.value.filter(
      (id) => !pendingIds.has(id)
    );
  } else {
    const set = new Set([
      ...selectedCandidateIds.value,
      ...pendingFiltered.map((c) => c.id),
    ]);
    selectedCandidateIds.value = Array.from(set);
  }
}

function clearSelection() {
  selectedCandidateIds.value = [];
}

async function handleBatchAccept() {
  if (selectedCandidateIds.value.length === 0) return;
  isSubmitting.value = true;
  try {
    const payload = {
      candidate_ids: selectedCandidateIds.value,
      commit_reason: "CRC batch accepted high-confidence candidate data",
      change_reason: "CRC eSource Batch Reconciliation Accept & Commit",
    };
    const resp = await interopService.batchAcceptEhrCandidates(payload);
    if (resp.success && resp.candidates) {
      resp.candidates.forEach((updated: EhrCandidate) => {
        const idx = candidates.value.findIndex((c) => c.id === updated.id);
        if (idx !== -1) {
          candidates.value[idx] = updated;
        }
      });
      emit("batch-commit", resp.candidates);
      clearSelection();
    }
  } catch (err) {
    console.error("Failed to batch accept candidates:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function openBatchRejectModal() {
  if (selectedCandidateIds.value.length === 0) return;
  batchRejectReasonInput.value = "";
  showBatchRejectModal.value = true;
}

function closeBatchRejectModal() {
  showBatchRejectModal.value = false;
  batchRejectReasonInput.value = "";
}

async function submitBatchReject() {
  if (
    selectedCandidateIds.value.length === 0 ||
    !batchRejectReasonInput.value.trim()
  )
    return;
  isSubmitting.value = true;
  try {
    const payload = {
      candidate_ids: selectedCandidateIds.value,
      rejection_reason: batchRejectReasonInput.value.trim(),
      change_reason: "CRC eSource Batch Reconciliation Reject",
    };
    const resp = await interopService.batchRejectEhrCandidates(payload);
    if (resp.success && resp.candidates) {
      resp.candidates.forEach((updated: EhrCandidate) => {
        const idx = candidates.value.findIndex((c) => c.id === updated.id);
        if (idx !== -1) {
          candidates.value[idx] = updated;
        }
      });
      emit("batch-reject", resp.candidates);
      clearSelection();
    }
    closeBatchRejectModal();
  } catch (err) {
    console.error("Failed to batch reject candidates:", err);
  } finally {
    isSubmitting.value = false;
  }
}

const domainOptions = [
  { label: "All Domains", value: "ALL" },
  { label: "Demographics (DM)", value: "DM" },
  { label: "Vital Signs (VS)", value: "VS" },
  { label: "Laboratory (LB)", value: "LB" },
  { label: "Medical History (MH)", value: "MH" },
  { label: "Medications (CM)", value: "CM" },
];

const pendingCount = computed(
  () => candidates.value.filter((c) => c.status === "PENDING_REVIEW").length
);
const acceptedCount = computed(
  () => candidates.value.filter((c) => c.status === "ACCEPTED").length
);
const rejectedCount = computed(
  () => candidates.value.filter((c) => c.status === "REJECTED").length
);
const queriedCount = computed(
  () => candidates.value.filter((c) => c.status === "QUERIED").length
);

const filteredCandidates = computed(() => {
  return candidates.value.filter((item) => {
    // Domain filter
    if (
      selectedDomain.value !== "ALL" &&
      item.target_domain !== selectedDomain.value
    ) {
      return false;
    }
    // Status filter
    if (
      selectedStatus.value !== "ALL" &&
      item.status !== selectedStatus.value
    ) {
      return false;
    }
    // Search query
    if (searchQuery.value.trim()) {
      const q = searchQuery.value.toLowerCase().trim();
      const matchVar = item.target_variable.toLowerCase().includes(q);
      const matchTest = (item.cdash_test || "").toLowerCase().includes(q);
      const matchVal = String(item.candidate_value).toLowerCase().includes(q);
      const matchSys = item.ehr_system_id.toLowerCase().includes(q);
      if (!matchVar && !matchTest && !matchVal && !matchSys) {
        return false;
      }
    }
    return true;
  });
});

async function fetchCandidates() {
  isLoading.value = true;
  try {
    const params: Record<string, any> = {};
    if (props.studyId) params.studyId = props.studyId;
    if (props.subjectId || props.subjectPseudonym) {
      params.subjectId = props.subjectPseudonym || props.subjectId;
    }
    const resp = await interopService.listEhrCandidates(params);
    candidates.value = resp.candidates || [];
  } catch (err) {
    console.error("Failed to load eSource candidates:", err);
  } finally {
    isLoading.value = false;
  }
}

watch(
  () => props.isOpen,
  (val) => {
    if (val) {
      fetchCandidates();
    }
  }
);

onMounted(() => {
  if (props.isOpen) {
    fetchCandidates();
  }
});

function closeDrawer() {
  emit("close");
}

async function promptAccept(item: EhrCandidate) {
  isSubmitting.value = true;
  try {
    const payload = {
      candidate_id: item.id,
      commit_reason: "CRC accepted candidate data from hospital EHR",
      change_reason: "CRC eSource Reconciliation Accept & Commit",
    };
    const resp = await interopService.acceptEhrCandidate(payload);
    if (resp.success && resp.candidate) {
      // Update local state
      const idx = candidates.value.findIndex((c) => c.id === item.id);
      if (idx !== -1) {
        candidates.value[idx] = resp.candidate;
      }
      emit("commit", resp.candidate);
    }
  } catch (err) {
    console.error("Failed to accept candidate:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function openRejectModal(item: EhrCandidate) {
  activeItem.value = item;
  rejectReasonInput.value = "";
  showRejectModal.value = true;
}

function closeRejectModal() {
  showRejectModal.value = false;
  activeItem.value = null;
  rejectReasonInput.value = "";
}

async function submitReject() {
  if (!activeItem.value || !rejectReasonInput.value.trim()) return;
  isSubmitting.value = true;
  try {
    const payload = {
      candidate_id: activeItem.value.id,
      rejection_reason: rejectReasonInput.value.trim(),
      change_reason: "CRC eSource Reconciliation Reject",
    };
    const resp = await interopService.rejectEhrCandidate(payload);
    if (resp.success && resp.candidate) {
      const idx = candidates.value.findIndex(
        (c) => c.id === activeItem.value?.id
      );
      if (idx !== -1) {
        candidates.value[idx] = resp.candidate;
      }
      emit("reject", resp.candidate);
    }
    closeRejectModal();
  } catch (err) {
    console.error("Failed to reject candidate:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function openQueryModal(item: EhrCandidate) {
  activeItem.value = item;
  queryExplanationInput.value = "";
  showQueryModal.value = true;
}

function closeQueryModal() {
  showQueryModal.value = false;
  activeItem.value = null;
  queryExplanationInput.value = "";
}

async function submitQuery() {
  if (!activeItem.value || !queryExplanationInput.value.trim()) return;
  isSubmitting.value = true;
  try {
    const payload = {
      candidate_id: activeItem.value.id,
      query_explanation: queryExplanationInput.value.trim(),
      discrepancy_type: activeItem.value.discrepancy_type,
      change_reason: "CRC Raised eSource Discrepancy Query",
    };
    const resp = await interopService.raiseEhrClinicalQuery(payload);
    if (resp.success && resp.candidate) {
      const idx = candidates.value.findIndex(
        (c) => c.id === activeItem.value?.id
      );
      if (idx !== -1) {
        candidates.value[idx] = resp.candidate;
      }
      emit("query", resp.candidate, resp.clinical_query_id || "");
    }
    closeQueryModal();
  } catch (err) {
    console.error("Failed to raise query:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function openProvenanceModal(item: EhrCandidate) {
  activeItem.value = item;
  showProvenanceModal.value = true;
}

function closeProvenanceModal() {
  showProvenanceModal.value = false;
  activeItem.value = null;
}

function formatValue(val: any): string {
  if (val === null || val === undefined) return "";
  if (typeof val === "object") return JSON.stringify(val);
  return String(val);
}

function formatDate(dt?: string): string {
  if (!dt) return "";
  try {
    return new Date(dt).toLocaleString();
  } catch {
    return dt;
  }
}

function formatDiscrepancy(disc: string): string {
  switch (disc) {
    case "NEW_FIELD":
      return "✨ New Field";
    case "EXACT_MATCH":
      return "✓ Matches eCRF";
    case "VALUE_MISMATCH":
      return "⚠️ Value Conflict";
    case "UNIT_MISMATCH":
      return "🔄 Unit Mismatch";
    case "DATE_MISMATCH":
      return "📅 Date Mismatch";
    case "NOT_COMPARED":
      return "Not compared with eCRF";
    default:
      return disc;
  }
}

function formatStatus(st: string): string {
  switch (st) {
    case "PENDING_REVIEW":
      return "Pending Review";
    case "ACCEPTED":
      return "Accepted & Committed";
    case "REJECTED":
      return "Rejected";
    case "QUERIED":
      return "Query Open";
    default:
      return st;
  }
}

function formatResolutionAction(act?: string): string {
  switch (act) {
    case "ACCEPT_COMMIT":
      return "Committed to eCRF";
    case "REJECT":
      return "Rejected by CRC";
    case "QUERY":
      return "Clinical Discrepancy Query Raised";
    default:
      return act || "Resolved";
  }
}
</script>

<style scoped>
/* 100% Vanilla CSS Tokens Architecture — Zero Tailwind Classes */
.drawer-backdrop {
  position: fixed;
  inset: 0;
  max-width: 100vw;
  background-color: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(3px);
  display: flex;
  justify-content: flex-end;
  z-index: 1050;
  animation: fadeIn 0.2s ease-out;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.ehr-reconciliation-drawer {
  width: var(
    --semantic-drawer-width-xl,
    var(--drawer-width-xl, min(100vw, 920px))
  );
  max-width: 100vw;
  height: 100vh;
  background-color: var(--semantic-color-surface);
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.25s cubic-bezier(0.16, 1, 0.3, 1);
  overflow: hidden;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

/* Header */
.drawer-header {
  padding: 20px 24px;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  border-bottom: 2px solid var(--semantic-color-text-dark);
}

.header-titles {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.badge-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
  letter-spacing: 0.03em;
  text-transform: uppercase;
}

.badge-smart {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.badge-gxp {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.badge-provenance {
  background-color: var(--semantic-color-accent);
  color: var(--semantic-color-surface);
}

.drawer-title {
  font-size: 1.35rem;
  font-weight: 800;
  color: var(--semantic-color-surface);
  margin: 0;
  line-height: 1.2;
}

.drawer-subtitle {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  max-width: 750px;
  line-height: 1.4;
}

.btn-close {
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  font-size: 1.25rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  transition:
    color 0.15s,
    background-color 0.15s;
}

.btn-close:hover {
  color: var(--semantic-color-text-inverse);
  background-color: var(--semantic-color-surface-elevated);
}

/* Context Bar */
.context-bar {
  padding: 10px 24px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  gap: 20px;
  font-size: 0.85rem;
  flex-wrap: wrap;
}

.context-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.context-label {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.context-value {
  color: var(--semantic-color-text);
  font-weight: 700;
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.context-actions {
  margin-left: auto;
}

/* Metrics Grid */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
  padding: 16px 24px;
  background-color: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
}

.metric-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.metric-card.metric-pending {
  border-color: var(--semantic-color-warning-border);
  background-color: var(--semantic-color-warning-bg-soft);
}

.metric-card.metric-accepted {
  border-color: var(--semantic-color-success-border-soft);
  background-color: var(--semantic-color-success-bg-soft);
}

.metric-card.metric-rejected {
  border-color: var(--semantic-color-error-border-soft);
  background-color: var(--semantic-color-error-bg-soft);
}

.metric-card.metric-queried {
  border-color: var(--semantic-color-warning-border-strong);
  background-color: var(--semantic-color-warning-bg-soft);
}

.metric-num {
  font-size: 1.25rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.metric-label {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

/* Toolbar & Filter Tabs */
.toolbar {
  padding: 12px 24px;
  background-color: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.domain-tabs {
  display: flex;
  gap: 6px;
  overflow-x: auto;
  padding-bottom: 2px;
}

.domain-tab {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.15s ease;
}

.domain-tab:hover {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.domain-tab.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.toolbar-controls {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  align-items: center;
}

.filter-control {
  display: flex;
  align-items: center;
  gap: 8px;
}

.filter-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.select-input,
.search-input {
  padding: 6px 12px;
  font-size: 0.82rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  max-width: 100%;
}

.search-input {
  width: 280px;
  max-width: 100%;
}

/* Body & Candidate List */
.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px 24px;
  background-color: var(--semantic-color-surface-muted);
  overscroll-behavior: contain;
  touch-action: pan-y;
}

.state-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: var(--semantic-color-text-secondary);
  text-align: center;
}

.empty-icon {
  font-size: 3rem;
  margin-bottom: 12px;
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid var(--semantic-color-border-control);
  border-top-color: var(--semantic-color-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  margin-bottom: 12px;
}

.candidate-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* Card Architecture */
.reconciliation-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: 0 1px 4px rgba(0, 0, 0, 0.05);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}

.reconciliation-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
}

.reconciliation-card.status-accepted {
  border-left: 4px solid var(--semantic-color-success);
}

.reconciliation-card.status-rejected {
  border-left: 4px solid var(--semantic-color-error);
}

.reconciliation-card.status-queried {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
}

.reconciliation-card.status-pending_review {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
}

/* Card Header */
.card-header {
  padding: 12px 16px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.card-meta-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.domain-tag {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-weight: 700;
  font-size: 0.75rem;
  padding: 2px 6px;
  border-radius: 4px;
}

.variable-tag {
  font-weight: 700;
  font-size: 0.85rem;
  color: var(--semantic-color-primary);
}

.test-name {
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.card-meta-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.discrepancy-pill {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
}

.pill-new_field {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.pill-exact_match {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.pill-value_mismatch {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.pill-unit_mismatch {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.pill-date_mismatch {
  background-color: var(--semantic-color-orange-bg);
  color: var(--semantic-color-orange-text);
}

.status-pill {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
}

.status-pending_review {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.status-accepted {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.status-rejected {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.status-queried {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text-dark);
}

/* Split Screen Grid */
.split-screen-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  divide-x: 1px solid var(--semantic-color-border);
}

.split-pane {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ehr-pane {
  background-color: var(--semantic-color-surface);
  border-right: 1px solid var(--semantic-color-border);
}

.ecrf-pane {
  background-color: var(--semantic-color-surface-muted);
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.pane-title {
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.source-badge,
.form-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.data-row-primary {
  display: flex;
  align-items: baseline;
  gap: 6px;
  min-height: 32px;
}

.val-num {
  font-size: 1.25rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.val-unit {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.val-empty {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.provenance-metadata {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.75rem;
  border-top: 1px dashed var(--semantic-color-border);
  padding-top: 6px;
}

.meta-row {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.meta-row .lbl {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.meta-row .val {
  color: var(--semantic-color-text);
}

.truncate {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 260px;
}

/* Resolution Banner */
.resolution-banner {
  padding: 10px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 0.8rem;
  border-top: 1px solid var(--semantic-color-border);
  flex-wrap: wrap;
  gap: 8px;
}

.banner-accepted {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.banner-rejected {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}

.banner-queried {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text-dark);
}

.resolved-by-tag {
  margin-left: 6px;
  font-size: 0.72rem;
  opacity: 0.85;
}

.esign-badge,
.query-badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: rgba(255, 255, 255, 0.8);
  border: 1px solid currentColor;
}

/* Card Footer & Action Buttons */
.card-footer {
  padding: 10px 16px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.action-btn-group {
  display: flex;
  gap: 8px;
}

.btn {
  padding: 6px 12px;
  font-size: 0.8rem;
  font-weight: 600;
  border-radius: 4px;
  border: 1px solid transparent;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  transition: all 0.15s ease;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-sm {
  padding: 4px 8px;
  font-size: 0.75rem;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-secondary {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.btn-accept {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.btn-accept:hover:not(:disabled) {
  background-color: var(--semantic-color-success);
}

.btn-reject {
  background-color: var(--semantic-color-warning-action);
  color: var(--semantic-color-surface);
}

.btn-reject:hover:not(:disabled) {
  background-color: var(--semantic-color-warning-action);
}

.btn-query {
  background-color: var(--semantic-color-accent);
  color: var(--semantic-color-surface);
}

.btn-query:hover:not(:disabled) {
  background-color: var(--semantic-color-accent);
}

.btn-link-provenance {
  background: none;
  border: none;
  color: var(--semantic-color-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  text-decoration: underline;
}

.btn-link-provenance:hover {
  color: var(--semantic-color-primary);
}

/* Modals */
.modal-backdrop {
  position: fixed;
  inset: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1100;
  animation: fadeIn 0.15s ease-out;
}

.modal-card {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  width: 520px;
  max-width: 90vw;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.modal-lg {
  width: 720px;
}

.modal-header {
  padding: 16px 20px;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  font-size: 0.85rem;
  max-height: 70vh;
  overflow-y: auto;
}

.modal-instruction {
  color: var(--semantic-color-text-secondary);
  margin: 0;
  line-height: 1.4;
}

.field-summary {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 10px 14px;
  font-size: 0.8rem;
  line-height: 1.5;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.textarea-input {
  width: 100%;
  padding: 10px 12px;
  font-size: 0.85rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-family: inherit;
  resize: vertical;
}

.modal-footer {
  padding: 14px 20px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

/* Provenance Inspector specifics */
.provenance-summary-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  background-color: var(--semantic-color-surface-muted);
  padding: 12px 16px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
}

.prov-stat {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.prov-stat .lbl {
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.prov-stat .val {
  font-size: 0.85rem;
  color: var(--semantic-color-text);
}

.section-subheading {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 10px 0 6px 0;
}

.raw-fhir-container {
  display: flex;
  flex-direction: column;
}

.json-code {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  padding: 12px;
  border-radius: 6px;
  font-size: 0.75rem;
  max-height: 250px;
  overflow: auto;
  margin: 0;
}

/* Keyframes */
@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

/* Mobile Breakpoint Overrides (max-width: 640px) */
@media (max-width: 640px) {
  .ehr-reconciliation-drawer {
    width: 100vw;
    max-width: 100vw;
  }

  .drawer-header,
  .context-bar,
  .toolbar,
  .drawer-body {
    padding-left: 16px;
    padding-right: 16px;
  }

  .metrics-grid {
    grid-template-columns: repeat(2, 1fr);
    padding: 12px 16px;
  }

  .toolbar-controls {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .filter-control {
    width: 100%;
    justify-content: space-between;
  }

  .select-input,
  .search-input {
    width: 100%;
    max-width: 100%;
    box-sizing: border-box;
  }

  .search-control {
    width: 100%;
  }

  .split-screen-grid {
    grid-template-columns: 1fr;
  }

  .ehr-pane {
    border-right: none;
    border-bottom: 1px solid var(--semantic-color-border);
  }

  .card-footer {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .action-btn-group {
    flex-direction: column;
    width: 100%;
  }

  .action-btn-group .btn {
    width: 100%;
    justify-content: center;
    min-height: var(--semantic-touch-target-min, 48px);
  }

  .btn-link-provenance {
    width: 100%;
    text-align: center;
    padding: 8px 0;
    min-height: var(--semantic-touch-target-min, 48px);
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }

  .modal-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .modal-footer .btn {
    width: 100%;
    justify-content: center;
    min-height: var(--semantic-touch-target-min, 48px);
  }

  .provenance-summary-grid {
    grid-template-columns: 1fr;
  }
}

/* Quick Select Bar & Floating Batch Action Bar */
.quick-select-bar {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-top: 8px;
}

.btn-outline-primary {
  background-color: transparent;
  border: 1px solid var(--semantic-color-primary);
  color: var(--semantic-color-primary);
  border-radius: 4px;
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.btn-outline-primary:hover {
  background-color: var(--semantic-color-primary-light);
}

.btn-outline-secondary {
  background-color: transparent;
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  border-radius: 4px;
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.btn-outline-secondary:hover {
  background-color: var(--semantic-color-surface-header);
}

.floating-batch-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 20px;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-bottom: 2px solid var(--semantic-color-text-dark);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  animation: slideDown 0.2s ease-out;
}

.batch-count-summary {
  font-size: 0.875rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 6px;
}

.batch-count-badge {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text);
  font-weight: 700;
  border-radius: 12px;
  padding: 2px 8px;
  font-size: 0.8rem;
}

.batch-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  border: none;
  padding: 6px 14px;
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.85rem;
  cursor: pointer;
}

.btn-success:hover:not(:disabled) {
  background-color: var(--semantic-color-success);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  border: none;
  padding: 6px 14px;
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.85rem;
  cursor: pointer;
}

.btn-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error);
}

.btn-ghost {
  background: transparent;
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  cursor: pointer;
}

.btn-ghost:hover {
  background-color: rgba(255, 255, 255, 0.1);
  color: var(--semantic-color-surface);
}

.card-selection-check {
  display: flex;
  align-items: center;
  padding-right: 8px;
}

.candidate-checkbox {
  width: 18px;
  height: 18px;
  cursor: pointer;
  accent-color: var(--semantic-color-primary);
}

.candidate-checkbox:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
</style>
