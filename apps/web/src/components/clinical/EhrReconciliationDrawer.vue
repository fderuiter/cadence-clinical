<template>
  <ClinicalDrawer
    :model-value="isOpen"
    :teleport="false"
    size="xl"
    :dismissible="!isSubmitting"
    overlay-class="drawer-backdrop"
    panel-class="ehr-reconciliation-drawer"
    aria-labelledby="drawer-heading"
    @close="closeDrawer"
  >
    <template #header>
      <div class="drawer-header">
        <div class="header-title-wrap">
          <Stethoscope
            :size="20"
            class="header-icon"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <h2 id="drawer-heading" class="drawer-title">
            eSource EHR Reconciliation Drawer
          </h2>
        </div>

        <button
          id="btn-close-ehr-drawer"
          type="button"
          class="btn-close"
          aria-label="Close EHR Reconciliation Drawer"
          :disabled="isSubmitting"
          @click="closeDrawer"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <div class="intro-meta-block">
        <div class="badge-row">
          <span class="badge badge-smart">SMART on FHIR</span>
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-provenance">Immutable Provenance</span>
        </div>
        <p class="drawer-subtitle">
          Side-by-side reconciliation of candidate EHR records with current eCRF
          fields. Non-destructive review gating eliminates silent background
          data overwrites.
        </p>
      </div>

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
            <RefreshCw
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Refresh</span>
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
          <span class="metric-label">Accepted &amp; Committed</span>
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
            type="button"
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
            <Zap
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Select High Confidence (&ge;90%)</span>
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

      <!-- Main Split-Screen Reconciliation View -->
      <div v-if="isLoading" class="state-container">
        <SkeletonLoader type="card" :count="3" />
      </div>

      <div
        v-else-if="filteredCandidates.length === 0"
        class="state-container empty-state"
      >
        <EmptyState
          title="No Candidate Records Found"
          message="No EHR source data items match the current filters or need reconciliation."
        >
          <template #action>
            <button
              type="button"
              class="btn btn-primary btn-sm btn-refresh-candidates"
              @click="fetchCandidates"
            >
              <RefreshCw
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span>Refresh EHR Ingestion</span>
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
            `status-${(item.status || 'pending_review').toLowerCase()}`,
            `disc-${(item.discrepancy_type || 'not_compared').toLowerCase()}`,
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
              <span class="variable-tag mono">{{ item.target_variable }}</span>
              <span class="test-name">{{
                item.cdash_test || item.cdash_testcd
              }}</span>
            </div>
            <div class="card-meta-right">
              <span
                :class="[
                  'discrepancy-pill',
                  `pill-${(item.discrepancy_type || 'not_compared').toLowerCase()}`,
                ]"
              >
                <component
                  :is="discrepancyIcon(item.discrepancy_type)"
                  :size="16"
                  :stroke-width="1.75"
                  aria-hidden="true"
                  focusable="false"
                />
                <span>{{ formatDiscrepancy(item.discrepancy_type) }}</span>
              </span>
              <span
                :class="[
                  'status-pill',
                  `status-${(item.status || 'pending_review').toLowerCase()}`,
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
                <span class="pane-title">
                  <Hospital
                    :size="16"
                    aria-hidden="true"
                    :stroke-width="1.75"
                    focusable="false"
                  />
                  <span>Candidate EHR Source Data</span>
                </span>
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
                    >{{ Math.round((item.confidence_score ?? 0) * 100) }}%</span
                  >
                </div>
              </div>
            </div>

            <!-- RIGHT PANE: Current eCRF Field in EDC -->
            <div class="split-pane ecrf-pane">
              <div class="pane-header">
                <span class="pane-title">
                  <FileEdit
                    :size="16"
                    aria-hidden="true"
                    :stroke-width="1.75"
                    focusable="false"
                  />
                  <span>Current eCRF Target Field</span>
                </span>
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
                  <span class="val truncate" :title="item.mapping_provenance">{{
                    item.mapping_provenance
                  }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- Resolution / Audit Trail Banner (If Resolved) -->
          <div
            v-if="item.status !== 'PENDING_REVIEW'"
            class="resolution-banner"
            :class="`banner-${(item.status || 'resolved').toLowerCase()}`"
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
              <FileSignature
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span>21 CFR Part 11 eSigned</span>
            </div>
            <div v-if="item.clinical_query_id" class="query-badge">
              <HelpCircle
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span
                >Query #{{ item.clinical_query_id.substring(0, 8) }} Open</span
              >
            </div>
          </div>

          <!-- Card Footer: Non-Destructive 1-Click Action Buttons & Provenance Inspector -->
          <footer class="card-footer">
            <div class="action-btn-group">
              <button
                type="button"
                class="btn btn-action btn-accept"
                :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                @click="promptAccept(item)"
              >
                <Check
                  :size="16"
                  aria-hidden="true"
                  :stroke-width="1.75"
                  focusable="false"
                />
                <span>Accept &amp; Commit</span>
              </button>

              <button
                type="button"
                class="btn btn-action btn-reject"
                :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                @click="openRejectModal(item)"
              >
                <X
                  :size="16"
                  aria-hidden="true"
                  :stroke-width="1.75"
                  focusable="false"
                />
                <span>Reject with Reason</span>
              </button>

              <button
                type="button"
                class="btn btn-action btn-query"
                :disabled="item.status !== 'PENDING_REVIEW' || isSubmitting"
                @click="openQueryModal(item)"
              >
                <HelpCircle
                  :size="16"
                  aria-hidden="true"
                  :stroke-width="1.75"
                  focusable="false"
                />
                <span>Raise Query</span>
              </button>
            </div>

            <button
              type="button"
              class="btn btn-link-provenance"
              @click="openProvenanceModal(item)"
            >
              <Search
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span>Inspect Provenance</span>
            </button>
          </footer>
        </article>
      </div>
    </div>

    <!-- Optional Footer Action Bar for Selected Items -->
    <template v-if="selectedCandidateIds.length > 0" #footer>
      <div class="floating-batch-bar">
        <div class="batch-count-summary">
          <strong class="batch-count-badge tabular-nums">{{
            selectedCandidateIds.length
          }}</strong>
          item(s) selected
        </div>
        <div class="batch-actions">
          <button
            type="button"
            class="btn btn-success btn-batch-accept"
            :disabled="isSubmitting"
            @click="handleBatchAccept"
          >
            <Check
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Batch Accept ({{ selectedCandidateIds.length }})</span>
          </button>
          <button
            type="button"
            class="btn btn-danger btn-batch-reject"
            :disabled="isSubmitting"
            @click="openBatchRejectModal"
          >
            <X
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Batch Reject ({{ selectedCandidateIds.length }})</span>
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
    </template>
  </ClinicalDrawer>

  <!-- MODAL 1: Reject with Reason Dialog -->
  <ClinicalModal
    :model-value="showRejectModal"
    :teleport="false"
    size="md"
    :dismissible="!isSubmitting"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card"
    aria-labelledby="reject-modal-title"
    @close="closeRejectModal"
  >
    <template #header>
      <div class="modal-header">
        <h3 id="reject-modal-title">Reject EHR Candidate Data</h3>
        <button
          type="button"
          class="btn-close"
          aria-label="Close reject dialog"
          :disabled="isSubmitting"
          @click="closeRejectModal"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

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

    <template #footer>
      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
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
    </template>
  </ClinicalModal>

  <!-- MODAL 2: Raise Clinical Query Dialog -->
  <ClinicalModal
    :model-value="showQueryModal"
    :teleport="false"
    size="md"
    :dismissible="!isSubmitting"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card"
    aria-labelledby="query-modal-title"
    @close="closeQueryModal"
  >
    <template #header>
      <div class="modal-header">
        <h3 id="query-modal-title">Raise GxP Discrepancy Query</h3>
        <button
          type="button"
          class="btn-close"
          aria-label="Close query dialog"
          :disabled="isSubmitting"
          @click="closeQueryModal"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

    <div class="modal-body">
      <p class="modal-instruction">
        Opens a formal clinical query in the study query ledger to flag a
        discrepancy between the EHR source and the eCRF.
      </p>
      <div class="field-summary">
        <strong>Target Field:</strong> {{ activeItem?.target_variable }}<br />
        <strong>EHR Source Value:</strong>
        {{ activeItem?.candidate_value }} {{ activeItem?.candidate_unit }}<br />
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

    <template #footer>
      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
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
    </template>
  </ClinicalModal>

  <!-- MODAL 3: Source Provenance & Part 11 Audit Trail Viewer -->
  <ClinicalModal
    :model-value="showProvenanceModal"
    :teleport="false"
    size="lg"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card modal-lg"
    aria-labelledby="prov-modal-title"
    @close="closeProvenanceModal"
  >
    <template #header>
      <div class="modal-header">
        <h3 id="prov-modal-title">
          FHIR Resource Provenance &amp; Audit Trail
        </h3>
        <button
          type="button"
          class="btn-close"
          aria-label="Close provenance dialog"
          @click="closeProvenanceModal"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

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

    <template #footer>
      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          @click="closeProvenanceModal"
        >
          Close
        </button>
      </div>
    </template>
  </ClinicalModal>

  <!-- MODAL 4: Batch Reject Dialog -->
  <ClinicalModal
    :model-value="showBatchRejectModal"
    :teleport="false"
    size="md"
    :dismissible="!isSubmitting"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card"
    aria-labelledby="batch-reject-modal-title"
    @close="closeBatchRejectModal"
  >
    <template #header>
      <div class="modal-header">
        <h3 id="batch-reject-modal-title">Batch Reject EHR Candidate Data</h3>
        <button
          type="button"
          class="btn-close"
          aria-label="Close batch reject dialog"
          :disabled="isSubmitting"
          @click="closeBatchRejectModal"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </button>
      </div>
    </template>

    <div class="modal-body">
      <p class="modal-instruction">
        Under GxP and FDA Electronic Source Data guidelines, a clinical
        justification is mandatory when rejecting candidate EHR records. The
        rationale below will be recorded in Part 11 audit trails across all
        {{ selectedCandidateIds.length }} selected items.
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

    <template #footer>
      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
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
    </template>
  </ClinicalModal>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { ClinicalDrawer, ClinicalModal, EmptyState } from "ui";
import {
  AlertTriangle,
  Calendar,
  Check,
  Sparkles,
  FileEdit,
  FileSignature,
  HelpCircle,
  Hospital,
  RefreshCw,
  Search,
  Stethoscope,
  X,
  Zap,
} from "lucide-vue-next";
import { interopService } from "../../api/interop";
import SkeletonLoader from "../common/SkeletonLoader.vue";

export type EhrCandidate = NonNullable<
  Awaited<ReturnType<typeof interopService.listEhrCandidates>>["candidates"]
>[number];

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
const showRejectModal = ref(false);
const rejectReasonInput = ref("");
const showBatchRejectModal = ref(false);
const batchRejectReasonInput = ref("");
const showQueryModal = ref(false);
const queryExplanationInput = ref("");
const showProvenanceModal = ref(false);

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
    .filter(
      (c) => c.status === "PENDING_REVIEW" && (c.confidence_score ?? 0) >= 0.9
    )
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
    if (
      selectedDomain.value !== "ALL" &&
      item.target_domain !== selectedDomain.value
    ) {
      return false;
    }
    if (
      selectedStatus.value !== "ALL" &&
      item.status !== selectedStatus.value
    ) {
      return false;
    }
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

function formatDate(dt?: string | null): string {
  if (!dt) return "";
  try {
    return new Date(dt).toLocaleString();
  } catch {
    return dt;
  }
}

function discrepancyIcon(disc?: string | null) {
  switch (disc) {
    case "NEW_FIELD":
      return Sparkles;
    case "EXACT_MATCH":
      return Check;
    case "VALUE_MISMATCH":
      return AlertTriangle;
    case "UNIT_MISMATCH":
      return RefreshCw;
    case "DATE_MISMATCH":
      return Calendar;
    default:
      return HelpCircle;
  }
}

function formatDiscrepancy(disc?: string | null): string {
  switch (disc) {
    case "NEW_FIELD":
      return "New Field";
    case "EXACT_MATCH":
      return "Matches eCRF";
    case "VALUE_MISMATCH":
      return "Value Conflict";
    case "UNIT_MISMATCH":
      return "Unit Mismatch";
    case "DATE_MISMATCH":
      return "Date Mismatch";
    case "NOT_COMPARED":
      return "Not compared with eCRF";
    default:
      return disc || "";
  }
}

function formatStatus(st?: string | null): string {
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
      return st || "";
  }
}

function formatResolutionAction(act?: string | null): string {
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

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
/* 100% Vanilla CSS Tokens Architecture — Zero Tailwind Classes */
:deep(.drawer-backdrop) {
  overscroll-behavior: contain;
  touch-action: pan-y;
}

:global(
  .clinical-drawer-backdrop .clinical-drawer-panel.ehr-reconciliation-drawer
) {
  width: var(
    --semantic-drawer-width-xl,
    var(--drawer-width-xl, min(100vw, 920px))
  );
  max-width: 100vw;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

:global(
  .clinical-drawer-backdrop .ehr-reconciliation-drawer > .clinical-drawer-footer
) {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-top-color: var(--semantic-color-border-inverse);
}

.drawer-header {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-bottom-color: var(--semantic-color-border-inverse);
}

.header-icon {
  color: var(--semantic-color-accent-inverse);
}

.drawer-title {
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.btn-close {
  color: var(--semantic-color-text-inverse-muted);
}

.btn-close:hover:not(:disabled) {
  color: var(--semantic-color-text-inverse);
  background-color: var(--semantic-color-surface-elevated);
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
  overscroll-behavior: contain;
  touch-action: pan-y;
  font-variant-numeric: tabular-nums;
}

.intro-meta-block {
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
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
  letter-spacing: 0.03em;
  text-transform: uppercase;
}

.badge-smart {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.badge-gxp {
  background-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}

.badge-provenance {
  background-color: var(--semantic-color-accent);
  color: var(--semantic-color-text-on-accent);
}

.drawer-subtitle {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  line-height: 1.4;
}

/* Context Bar */
.context-bar {
  padding: 10px 14px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  display: flex;
  align-items: center;
  gap: 12px 20px;
  font-size: 0.85rem;
  flex-wrap: wrap;
}

.context-item {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
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
  font-family: var(--semantic-font-mono);
}

.context-actions {
  margin-left: auto;
}

/* Responsive Metrics Grid inside scroll body (Req 6) */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(135px, 1fr));
  gap: 10px;
}

.metric-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  min-width: 0;
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
  font-size: 1.2rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.metric-label {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

/* Toolbar & Filter Tabs */
.toolbar {
  padding: 12px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.domain-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.domain-tab {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
  min-height: 44px;
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.domain-tab.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.quick-select-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.toolbar-controls {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: center;
  flex-wrap: wrap;
}

.filter-control {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.filter-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.select-input,
.search-input {
  min-height: 44px;
  padding: 6px 12px;
  font-size: 1rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  max-width: 100%;
  box-sizing: border-box;
}

.search-control {
  flex: 1 1 220px;
  min-width: 0;
}

.search-input {
  width: 100%;
  max-width: 100%;
}

.state-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 36px 16px;
  color: var(--semantic-color-text-secondary);
  text-align: center;
}

.candidate-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* Card Architecture */
.reconciliation-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: var(--shadow-sm);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-width: 0;
}

.reconciliation-card:hover {
  box-shadow: var(--shadow-md);
}

.reconciliation-card.status-accepted {
  border-left: 4px solid var(--semantic-color-success);
}

.reconciliation-card.status-rejected {
  border-left: 4px solid var(--semantic-color-error);
}

.reconciliation-card.status-queried,
.reconciliation-card.status-pending_review {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
}

/* Card Header */
.card-header {
  padding: 10px 14px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.card-meta-left,
.card-meta-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
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
  color: var(--text-main);
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

.discrepancy-pill,
.status-pill {
  font-size: var(--font-size-2xs);
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
  background-color: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border: 1px solid var(--semantic-badge-queried-border);
}

/* Split Screen Grid */
.split-screen-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.split-pane {
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
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
  gap: 8px;
  flex-wrap: wrap;
}

.pane-title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.source-badge,
.form-badge {
  font-size: var(--font-size-2xs);
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
  flex-wrap: wrap;
  min-height: 32px;
}

.val-num {
  font-size: 1.2rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  word-break: break-word;
}

.val-unit,
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
  flex-wrap: wrap;
}

.meta-row .lbl {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.meta-row .val {
  color: var(--semantic-color-text);
  word-break: break-word;
}

.truncate {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 100%;
}

/* Resolution Banner */
.resolution-banner {
  padding: 10px 14px;
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
  font-size: var(--font-size-2xs);
  opacity: 0.85;
}

.esign-badge,
.query-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid currentColor;
}

/* Card Footer & Action Buttons */
.card-footer {
  padding: 10px 14px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.action-btn-group {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn {
  min-height: 44px;
  padding: 6px 12px;
  font-size: 0.8rem;
  font-weight: 600;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  transition: all 0.15s ease;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.btn-secondary {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}

.btn-danger {
  background-color: var(--semantic-color-error-interactive);
  color: var(--semantic-color-text-on-solid);
}

.btn-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error-interactive-hover);
}

.btn-success {
  background-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}

.btn-success:hover:not(:disabled) {
  background-color: var(--semantic-color-success-accent);
}

.btn-accept {
  background-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}

.btn-accept:hover:not(:disabled) {
  background-color: var(--semantic-color-success-accent);
}

.btn-reject {
  background-color: var(--semantic-color-warning-action);
  color: var(--semantic-color-text-on-solid);
}

.btn-reject:hover:not(:disabled) {
  background-color: var(--semantic-color-warning-dark);
}

.btn-query {
  background-color: var(--semantic-color-primary-dark);
  color: var(--semantic-color-text-on-primary);
}

.btn-query:hover:not(:disabled) {
  background-color: var(--semantic-color-primary);
}

.btn-outline-primary {
  background-color: transparent;
  border: 1px solid var(--semantic-color-primary);
  color: var(--semantic-color-primary);
}

.btn-outline-secondary {
  background-color: transparent;
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
}

.btn-ghost {
  background: transparent;
  border: 1px solid var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse-muted);
}

.btn-ghost:hover:not(:disabled) {
  background-color: var(--semantic-color-inverse-tint);
  color: var(--semantic-color-text-inverse);
}

.btn-link-provenance {
  background: none;
  border: none;
  color: var(--semantic-color-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  text-decoration: underline;
  min-height: 44px;
}

/* Sibling ClinicalModal header/body/footer */
.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.modal-header h3 {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
}

.modal-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  font-size: 0.85rem;
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
  min-height: 88px;
  padding: 10px 12px;
  font-size: 1rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-family: inherit;
  resize: vertical;
  box-sizing: border-box;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
}

.provenance-summary-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
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
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.prov-stat .val {
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  word-break: break-word;
}

.section-subheading {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 6px 0;
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

/* Floating Batch Bar inside #footer */
.floating-batch-bar {
  box-shadow: var(--shadow-md);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
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
  color: var(--semantic-color-text-on-primary);
  font-weight: 700;
  border-radius: 12px;
  padding: 2px 8px;
  font-size: 0.8rem;
}

.batch-actions {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.card-selection-check {
  display: flex;
  align-items: center;
}

.candidate-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  cursor: pointer;
  accent-color: var(--semantic-color-primary);
}

.candidate-checkbox:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}

/* Mobile Breakpoint Overrides (max-width: 640px) */
@media (max-width: 640px) {
  :global(
    .clinical-drawer-backdrop .clinical-drawer-panel.ehr-reconciliation-drawer
  ) {
    width: 100vw;
    max-width: 100vw;
  }

  .metrics-grid {
    grid-template-columns: repeat(2, 1fr);
  }

  .toolbar-controls {
    flex-direction: column;
    align-items: stretch;
  }

  .filter-control {
    width: 100%;
    justify-content: space-between;
  }

  .select-input,
  .search-input {
    width: 100%;
    max-width: 100%;
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
    gap: 10px;
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

@media (max-width: 340px) {
  .metrics-grid {
    grid-template-columns: 1fr;
  }
}
</style>
