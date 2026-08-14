<template>
  <div class="coding-queue-wrapper">
    <!-- Batch Action Bar (Appears when items are selected) -->
    <div v-if="codingStore.selectedCount > 0" class="batch-action-bar">
      <div class="batch-summary">
        <span class="badge badge-accent">{{ codingStore.selectedCount }}</span>
        <span>items selected for batch action</span>
      </div>
      <div class="batch-buttons">
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="openBatchOverrideModal"
        >
          🏷️ Batch Code
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="acceptAllSuggestions"
        >
          ✓ Accept Top Suggestions
        </button>
        <button
          type="button"
          class="btn btn-text btn-sm"
          @click="codingStore.clearSelection()"
        >
          Clear Selection
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="codingStore.isLoading" class="table-loading-state">
      <div class="spinner"></div>
      <p>Loading medical coding queue...</p>
    </div>

    <!-- Empty State -->
    <div
      v-else-if="codingStore.filteredAssignments.length === 0"
      class="table-empty-state"
    >
      <div class="empty-icon">🩺</div>
      <h3>No Coding Assignments Found</h3>
      <p>
        No adverse events or concomitant medications match the active filters.
      </p>
    </div>

    <!-- Queue Table -->
    <div v-else class="table-responsive">
      <table class="data-table coding-table">
        <thead>
          <tr>
            <th class="col-checkbox">
              <input
                type="checkbox"
                :checked="isAllSelected"
                :indeterminate="isIndeterminate"
                title="Select all filtered items"
                @change="toggleSelectAll"
              />
            </th>
            <th>Status</th>
            <th>Verbatim Term &amp; Source</th>
            <th>Dictionary</th>
            <th>Coded Code &amp; Term</th>
            <th>Match / Suggestions</th>
            <th class="col-actions">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in codingStore.filteredAssignments"
            :key="item.id"
            :class="{ 'row-selected': isSelected(item.id) }"
          >
            <!-- Checkbox -->
            <td class="col-checkbox">
              <input
                type="checkbox"
                :checked="isSelected(item.id)"
                @change="codingStore.toggleSelect(item.id)"
              />
            </td>

            <!-- Status Badge -->
            <td>
              <span :class="getStatusBadgeClass(item.status)">
                {{ formatStatus(item.status) }}
              </span>
            </td>

            <!-- Verbatim & Field -->
            <td>
              <div class="verbatim-title">{{ item.verbatim_text }}</div>
              <div class="verbatim-meta">
                <span v-if="item.source_field" class="meta-tag">{{
                  item.source_field
                }}</span>
                <span v-if="item.domain" class="meta-tag domain-tag">{{
                  item.domain
                }}</span>
                <span v-if="item.observation_id" class="meta-obs"
                  >Obs: {{ item.observation_id.substring(0, 8) }}...</span
                >
              </div>
            </td>

            <!-- Dictionary & Version -->
            <td>
              <div class="dict-label">{{ item.dictionary_type }}</div>
              <div class="dict-version">v{{ item.dictionary_version }}</div>
            </td>

            <!-- Coded Term & Code -->
            <td>
              <div v-if="item.coded_code" class="coded-result-box">
                <div class="coded-term">{{ item.coded_term }}</div>
                <div class="coded-code-badge">
                  <code>{{ item.coded_code }}</code>
                </div>
                <!-- Hierarchy Tooltip / Hierarchy Preview -->
                <div v-if="hasHierarchy(item)" class="hierarchy-preview">
                  <span class="hierarchy-breadcrumb">{{
                    getHierarchySummary(item)
                  }}</span>
                </div>
              </div>
              <span v-else class="uncoded-placeholder">— Uncoded —</span>
            </td>

            <!-- Match / Suggestions -->
            <td>
              <!-- If Suggestions Exist -->
              <div
                v-if="
                  item.suggestions &&
                  Array.isArray(item.suggestions) &&
                  item.suggestions.length > 0
                "
                class="suggestions-container"
              >
                <div
                  v-for="(sug, sIdx) in item.suggestions.slice(0, 2)"
                  :key="sIdx"
                  class="suggestion-pill"
                >
                  <span class="sug-score"
                    >{{ Math.round((sug.score || 0) * 100) }}%</span
                  >
                  <span class="sug-name" :title="getSuggestionName(sug)">
                    {{ getSuggestionName(sug) }} ({{ getSuggestionCode(sug) }})
                  </span>
                  <button
                    type="button"
                    class="btn-icon-accept"
                    title="Accept this suggestion"
                    @click="acceptSuggestion(item, sIdx, sug)"
                  >
                    ✓
                  </button>
                </div>
                <div
                  v-if="item.suggestions.length > 2"
                  class="more-suggestions"
                >
                  +{{ item.suggestions.length - 2 }} more
                </div>
              </div>
              <div v-else-if="item.score" class="score-display">
                Score: {{ Math.round(item.score * 100) }}%
              </div>
              <div v-else class="no-suggestions-text">No suggestions</div>
            </td>

            <!-- Actions -->
            <td class="col-actions">
              <div class="action-buttons-group">
                <button
                  type="button"
                  class="btn btn-sm btn-primary-outline"
                  title="Open Dictionary Browser Modal"
                  @click="openBrowser(item)"
                >
                  🔍 Browse
                </button>
                <button
                  type="button"
                  class="btn btn-sm btn-warning-outline"
                  title="Raise Discrepancy Query"
                  @click="promptRaiseQuery(item)"
                >
                  ❓ Query
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Inline Discrepancy Query Modal -->
    <div
      v-if="isQueryModalOpen"
      class="modal-overlay"
      @click.self="isQueryModalOpen = false"
    >
      <div class="modal query-dialog-card">
        <div class="modal-header">
          <h3>Raise Clinical Coding Query</h3>
          <button
            type="button"
            class="btn-close"
            @click="isQueryModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            Raise a discrepancy query on observation
            <strong>{{ queryTargetItem?.verbatim_text }}</strong> ({{
              queryTargetItem?.source_field || "AE.AETERM"
            }}).
          </p>
          <div class="form-group">
            <label for="query-explanation">Discrepancy Query Message *</label>
            <textarea
              id="query-explanation"
              v-model="queryMessage"
              rows="3"
              class="form-control"
              placeholder="e.g. Verbatim term is ambiguous or compound. Please clarify spelling or enter specific diagnosis."
            ></textarea>
          </div>
          <div class="form-group">
            <label for="query-reason">GxP Reason for Change / Action</label>
            <input
              id="query-reason"
              v-model="queryReason"
              type="text"
              class="form-control"
              placeholder="e.g. Uncodable verbatim clarification request"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isQueryModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!queryMessage.trim() || isSubmittingQuery"
            @click="submitQuery"
          >
            {{
              isSubmittingQuery ? "Raising Query..." : "Raise Discrepancy Query"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- Batch Code Modal -->
    <div
      v-if="isBatchModalOpen"
      class="modal-overlay"
      @click.self="isBatchModalOpen = false"
    >
      <div class="modal batch-dialog-card">
        <div class="modal-header">
          <h3>Batch Code Selected Items</h3>
          <button
            type="button"
            class="btn-close"
            @click="isBatchModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            You are applying a uniform code to
            <strong>{{ codingStore.selectedCount }}</strong> selected items.
          </p>
          <div class="form-group">
            <label for="batch-dict">Dictionary Type</label>
            <select
              id="batch-dict"
              v-model="batchDictType"
              class="form-control"
            >
              <option value="MEDDRA">MedDRA</option>
              <option value="WHODRUG">WHODrug</option>
            </select>
          </div>
          <div class="form-group">
            <label for="batch-code">Concept / Drug Code *</label>
            <input
              id="batch-code"
              v-model="batchCode"
              type="text"
              class="form-control"
              placeholder="e.g. 10019211"
            />
          </div>
          <div class="form-group">
            <label for="batch-term">Preferred Term / Drug Name</label>
            <input
              id="batch-term"
              v-model="batchTerm"
              type="text"
              class="form-control"
              placeholder="e.g. Headache"
            />
          </div>
          <div class="form-group">
            <label for="batch-reason">GxP Reason for Change *</label>
            <input
              id="batch-reason"
              v-model="batchReason"
              type="text"
              class="form-control"
              placeholder="e.g. Batch coding consensus review"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isBatchModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="
              !batchCode.trim() || !batchReason.trim() || isSubmittingBatch
            "
            @click="submitBatchCode"
          >
            {{ isSubmittingBatch ? "Applying..." : "Apply Batch Code" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useCodingStore } from "../stores/coding";

const codingStore = useCodingStore();

// Discrepancy Query State
const isQueryModalOpen = ref(false);
const queryTargetItem = ref(null);
const queryMessage = ref("");
const queryReason = ref("");
const isSubmittingQuery = ref(false);

// Batch Modal State
const isBatchModalOpen = ref(false);
const batchDictType = ref("MEDDRA");
const batchCode = ref("");
const batchTerm = ref("");
const batchReason = ref("");
const isSubmittingBatch = ref(false);

const filteredIds = computed(() => {
  return codingStore.filteredAssignments.map((a) => a.id);
});

const isAllSelected = computed(() => {
  if (filteredIds.value.length === 0) return false;
  return filteredIds.value.every((id) =>
    codingStore.selectedAssignmentIds.includes(id)
  );
});

const isIndeterminate = computed(() => {
  const selectedCount = codingStore.selectedAssignmentIds.filter((id) =>
    filteredIds.value.includes(id)
  ).length;
  return selectedCount > 0 && selectedCount < filteredIds.value.length;
});

function isSelected(id) {
  return codingStore.selectedAssignmentIds.includes(id);
}

function toggleSelectAll() {
  codingStore.selectAll(filteredIds.value);
}

function formatStatus(status) {
  switch (status) {
    case "UNCODED":
      return "Uncoded";
    case "SUGGESTED":
      return "Suggested";
    case "CODED":
      return "Coded";
    case "AUTO_CODED":
      return "Auto-Coded";
    case "QUERY_PENDING":
      return "Query Pending";
    case "RECODING_REQUIRED":
      return "Recoding Required";
    default:
      return status || "Unknown";
  }
}

function getStatusBadgeClass(status) {
  switch (status) {
    case "UNCODED":
      return "badge badge-neutral";
    case "SUGGESTED":
      return "badge badge-info";
    case "CODED":
    case "AUTO_CODED":
      return "badge badge-success";
    case "QUERY_PENDING":
      return "badge badge-warning";
    case "RECODING_REQUIRED":
      return "badge badge-error";
    default:
      return "badge";
  }
}

function getSuggestionName(sug) {
  return sug.term_name || sug.preferred_name || sug.drug_name || "Suggestion";
}

function getSuggestionCode(sug) {
  return sug.code || sug.drug_code || "";
}

function hasHierarchy(item) {
  if (!item.hierarchy) return false;
  if (item.hierarchy.hierarchies && item.hierarchy.hierarchies.length > 0)
    return true;
  if (item.hierarchy.atc_context && item.hierarchy.atc_context.length > 0)
    return true;
  return false;
}

function getHierarchySummary(item) {
  if (!item.hierarchy) return "";
  if (item.hierarchy.hierarchies && item.hierarchy.hierarchies.length > 0) {
    const h = item.hierarchy.hierarchies[0];
    return `${h.soc_name || "SOC"} > ${h.hlgt_name || "HLGT"} > ${h.hlt_name || "HLT"} > ${h.pt_name || "PT"}`;
  }
  if (item.hierarchy.atc_context && item.hierarchy.atc_context.length > 0) {
    const atc = item.hierarchy.atc_context[0];
    return `ATC: ${atc.atc_code} (${atc.description})`;
  }
  return "";
}

async function acceptSuggestion(item, index, sug) {
  try {
    await codingStore.applyAction({
      assignmentId: item.id,
      action: "ACCEPT",
      code: getSuggestionCode(sug),
      term: getSuggestionName(sug),
      suggestionIndex: index,
      reasonForChange: `Accepted suggestion index ${index}: ${getSuggestionName(sug)}`,
    });
  } catch (err) {
    console.error("Accept suggestion error:", err);
  }
}

function openBrowser(item) {
  codingStore.openBrowser(item);
}

function promptRaiseQuery(item) {
  queryTargetItem.value = item;
  queryMessage.value = `The verbatim term '${item.verbatim_text}' in field ${item.source_field || "AETERM"} requires clinical clarification. Please specify diagnosis.`;
  queryReason.value = "Uncodable verbatim clarification query";
  isQueryModalOpen.value = true;
}

async function submitQuery() {
  if (!queryTargetItem.value) return;
  isSubmittingQuery.value = true;
  try {
    await codingStore.raiseQuery({
      assignmentId: queryTargetItem.value.id,
      queryText: queryMessage.value,
      reason: queryReason.value,
    });
    isQueryModalOpen.value = false;
  } catch (err) {
    console.error("Failed to raise query:", err);
  } finally {
    isSubmittingQuery.value = false;
  }
}

function openBatchOverrideModal() {
  batchDictType.value = "MEDDRA";
  batchCode.value = "";
  batchTerm.value = "";
  batchReason.value = "Batch coding assignment via workbench";
  isBatchModalOpen.value = true;
}

async function submitBatchCode() {
  isSubmittingBatch.value = true;
  try {
    await codingStore.batchAssign({
      assignmentIds: [...codingStore.selectedAssignmentIds],
      code: batchCode.value,
      term: batchTerm.value,
      dictionaryType: batchDictType.value,
      reason: batchReason.value,
      action: "OVERRIDE",
    });
    isBatchModalOpen.value = false;
  } catch (err) {
    console.error("Failed batch coding:", err);
  } finally {
    isSubmittingBatch.value = false;
  }
}

async function acceptAllSuggestions() {
  const targetIds = [...codingStore.selectedAssignmentIds];
  try {
    await codingStore.batchAssign({
      assignmentIds: targetIds,
      action: "ACCEPT",
      reason: "Batch accept top suggestion across selected items",
    });
  } catch (err) {
    console.error("Failed batch accept suggestions:", err);
  }
}
</script>

<style scoped>
.coding-queue-wrapper {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
  width: 100%;
}

.batch-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--accent);
  border-left: 4px solid var(--accent);
  border-radius: 6px;
  padding: 10px 16px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.05);
}

.batch-summary {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--primary);
}

.batch-buttons {
  display: flex;
  gap: 8px;
}

.table-responsive {
  width: 100%;
  overflow-x: auto;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 6px;
}

.coding-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.88rem;
  text-align: left;
}

.coding-table th {
  background-color: var(--neutral-light);
  color: var(--primary);
  font-weight: 700;
  padding: 12px 14px;
  border-bottom: 2px solid var(--border);
  white-space: nowrap;
}

.coding-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--border);
  vertical-align: middle;
}

.coding-table tr:hover {
  background-color: rgba(2, 101, 151, 0.03);
}

.row-selected {
  background-color: rgba(2, 101, 151, 0.07) !important;
}

.col-checkbox {
  width: 40px;
  text-align: center;
}

.col-actions {
  width: 160px;
  text-align: right;
}

.verbatim-title {
  font-weight: 600;
  color: var(--primary);
  margin-bottom: 4px;
}

.verbatim-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  font-size: 0.75rem;
}

.meta-tag {
  background-color: #e2e8f0;
  color: #334155;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: var(--font-mono, monospace);
}

.domain-tag {
  background-color: #dbeafe;
  color: #1e40af;
  font-weight: 600;
}

.meta-obs {
  color: var(--neutral-dark);
}

.dict-label {
  font-weight: 600;
  color: var(--primary);
}

.dict-version {
  font-size: 0.75rem;
  color: var(--neutral-dark);
}

.coded-result-box {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.coded-term {
  font-weight: 600;
  color: var(--success);
}

.coded-code-badge code {
  font-size: 0.78rem;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--primary);
}

.hierarchy-preview {
  margin-top: 4px;
  font-size: 0.72rem;
  color: var(--neutral-dark);
  max-width: 280px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.uncoded-placeholder {
  color: var(--neutral-dark);
  font-style: italic;
}

.suggestions-container {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.suggestion-pill {
  display: flex;
  align-items: center;
  gap: 6px;
  background-color: #eff6ff;
  border: 1px solid #bfdbfe;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.78rem;
}

.sug-score {
  font-weight: 700;
  color: var(--accent);
  background-color: white;
  padding: 1px 4px;
  border-radius: 3px;
}

.sug-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 140px;
  color: #1e3a8a;
}

.btn-icon-accept {
  background: var(--success);
  color: white;
  border: none;
  border-radius: 3px;
  padding: 2px 6px;
  cursor: pointer;
  font-weight: bold;
  font-size: 0.75rem;
  line-height: 1;
}

.btn-icon-accept:hover {
  opacity: 0.9;
}

.more-suggestions {
  font-size: 0.72rem;
  color: var(--neutral-dark);
}

.score-display {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--primary);
}

.no-suggestions-text {
  font-size: 0.78rem;
  color: var(--neutral-dark);
  font-style: italic;
}

.action-buttons-group {
  display: flex;
  gap: 6px;
  justify-content: flex-end;
}

.table-loading-state,
.table-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 6px;
  text-align: center;
  color: var(--neutral-dark);
}

.empty-icon {
  font-size: 2.5rem;
  margin-bottom: 12px;
}

.badge {
  display: inline-block;
  padding: 3px 8px;
  font-size: 0.75rem;
  font-weight: 700;
  border-radius: 12px;
}

.badge-neutral {
  background-color: #f1f5f9;
  color: #475569;
}

.badge-info {
  background-color: #e0f2fe;
  color: #0369a1;
}

.badge-success {
  background-color: var(--success-bg);
  color: var(--success);
}

.badge-warning {
  background-color: var(--warning-bg);
  color: var(--warning);
}

.badge-error {
  background-color: var(--error-bg);
  color: var(--error);
}

.badge-accent {
  background-color: #e0e7ff;
  color: #3730a3;
}

.btn-primary-outline {
  background-color: transparent;
  border: 1px solid var(--accent);
  color: var(--accent);
}

.btn-primary-outline:hover {
  background-color: var(--accent-bg);
}

.btn-warning-outline {
  background-color: transparent;
  border: 1px solid var(--warning);
  color: var(--warning);
}

.btn-warning-outline:hover {
  background-color: var(--warning-bg);
}

.btn-text {
  background: transparent;
  border: none;
  color: var(--neutral-dark);
  cursor: pointer;
}

.btn-text:hover {
  color: var(--primary);
  text-decoration: underline;
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.query-dialog-card,
.batch-dialog-card {
  background-color: white;
  width: 100%;
  max-width: 520px;
  border-radius: 8px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
}

.modal-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  color: var(--primary);
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: var(--neutral-dark);
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.modal-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  background-color: #f8fafc;
  border-bottom-left-radius: 8px;
  border-bottom-right-radius: 8px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary);
}

.form-control {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
  font-family: inherit;
}

.form-control:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 2px var(--accent-bg);
}
</style>
