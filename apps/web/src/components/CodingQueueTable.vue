<template>
  <div class="coding-queue-wrapper">
    <!-- Batch Action Bar for Cluster View -->
    <div
      v-if="codingStore.viewMode === 'CLUSTERS' && codingStore.selectedClusterCount > 0"
      class="batch-action-bar cluster-batch-bar"
    >
      <div class="batch-summary">
        <span class="badge badge-accent">{{ codingStore.selectedClusterCount }}</span>
        <span>clusters selected ({{ selectedClusterInstancesCount }} subject instances)</span>
      </div>
      <div class="batch-buttons">
        <button
          type="button"
          class="btn btn-primary btn-sm"
          @click="openBatchApproveClusterModal"
        >
          ✓ Batch Approve Cluster(s)
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="openBatchCodeClusterModal"
        >
          🏷️ Batch Code Cluster(s)
        </button>
        <button
          type="button"
          class="btn btn-text btn-sm"
          @click="codingStore.clearClusterSelection()"
        >
          Clear Selection
        </button>
      </div>
    </div>

    <!-- Batch Action Bar for Individual View (Appears when items are selected) -->
    <div
      v-else-if="codingStore.viewMode === 'INDIVIDUAL' && codingStore.selectedCount > 0"
      class="batch-action-bar"
    >
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
    <div
      v-if="codingStore.isLoading"
      class="table-loading-state"
    >
      <div class="spinner" />
      <p>Loading medical coding queue...</p>
    </div>

    <!-- Empty State for Clusters View -->
    <div
      v-else-if="codingStore.viewMode === 'CLUSTERS' && codingStore.verbatimClusters.length === 0"
      class="table-empty-state"
    >
      <div class="empty-icon">
        🧩
      </div>
      <h3>No Verbatim Clusters Found</h3>
      <p>
        No verbatim clusters match the selected confidence threshold or active filters.
      </p>
    </div>

    <!-- Empty State for Individual View -->
    <div
      v-else-if="codingStore.viewMode === 'INDIVIDUAL' && codingStore.filteredAssignments.length === 0"
      class="table-empty-state"
    >
      <div class="empty-icon">
        🩺
      </div>
      <h3>No Coding Assignments Found</h3>
      <p>
        No adverse events or concomitant medications match the active filters.
      </p>
    </div>

    <!-- Verbatim Clusters Table View -->
    <div
      v-else-if="codingStore.viewMode === 'CLUSTERS'"
      ref="clusterContainerRef"
      class="table-responsive"
    >
      <table
        class="data-table coding-table cluster-table"
        role="grid"
        aria-label="Verbatim Clusters Queue Table"
        :aria-rowcount="codingStore.verbatimClusters.length"
        aria-colcount="6"
      >
        <thead>
          <tr>
            <th
              scope="col"
              class="col-checkbox"
            >
              <input
                type="checkbox"
                :checked="isAllClustersSelected"
                :indeterminate="isClusterIndeterminate"
                title="Select all filtered clusters"
                aria-label="Select all filtered clusters"
                @change="toggleSelectAllClusters"
              >
            </th>
            <th scope="col">
              Status / Confidence
            </th>
            <th scope="col">
              Verbatim Cluster &amp; Instances
            </th>
            <th scope="col">
              Dictionary
            </th>
            <th scope="col">
              Top Suggested Term &amp; Score
            </th>
            <th
              scope="col"
              class="col-actions"
            >
              Actions
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="cluster in codingStore.verbatimClusters"
            :key="cluster.clusterKey"
            class="coding-row cluster-row"
            :class="{ 'row-selected': isClusterSelected(cluster.clusterKey) }"
          >
            <!-- Checkbox -->
            <td
              role="gridcell"
              class="col-checkbox"
            >
              <input
                type="checkbox"
                :checked="isClusterSelected(cluster.clusterKey)"
                aria-label="Select verbatim cluster"
                @change="codingStore.toggleSelectCluster(cluster.clusterKey)"
              >
            </td>

            <!-- Status & Match Confidence Badge -->
            <td role="gridcell">
              <div class="cluster-status-cell">
                <span :class="getStatusBadgeClass(cluster.status)">
                  {{ formatStatus(cluster.status) }}
                </span>
                <span
                  class="badge confidence-pill"
                  :class="getConfidenceBadgeClass(cluster.confidence_category)"
                  style="margin-top: 4px; font-size: 10px;"
                >
                  {{ Math.round((cluster.max_score || 0) * 100) }}% {{ cluster.confidence_category }}
                </span>
              </div>
            </td>

            <!-- Verbatim Cluster & Instances -->
            <td role="gridcell">
              <div class="verbatim-title cluster-title">
                {{ cluster.verbatim_text }}
              </div>
              <div class="cluster-instances-meta">
                <span class="badge badge-accent instance-count-badge">
                  👥 {{ cluster.instance_count }} subject {{ cluster.instance_count === 1 ? 'instance' : 'instances' }}
                </span>
                <span
                  v-if="cluster.has_blocked_items"
                  class="badge badge-warning blocked-alert-pill"
                  title="Contains active query or locked items"
                >
                  ⚠️ {{ cluster.blocked_items.length }} blocked
                </span>
              </div>
            </td>

            <!-- Dictionary -->
            <td role="gridcell">
              <div class="dict-label">
                {{ cluster.dictionary_type }}
              </div>
              <div class="dict-version">
                v{{ cluster.dictionary_version }}
              </div>
            </td>

            <!-- Top Suggestion -->
            <td role="gridcell">
              <div
                v-if="cluster.top_suggestion"
                class="coded-result-box"
              >
                <div class="coded-term">
                  {{ cluster.top_suggestion.term }}
                </div>
                <div class="coded-code-badge">
                  <code>{{ cluster.top_suggestion.code }}</code>
                </div>
                <div
                  v-if="cluster.top_suggestion.llt_name"
                  class="llt-preview"
                  style="font-size: 11px; color: var(--text-muted)"
                >
                  LLT: {{ cluster.top_suggestion.llt_name }}
                </div>
              </div>
              <span
                v-else
                class="uncoded-placeholder"
              >— Uncoded —</span>
            </td>

            <!-- Actions -->
            <td
              role="gridcell"
              class="col-actions"
            >
              <div class="action-buttons-group">
                <button
                  type="button"
                  class="btn btn-sm btn-primary"
                  title="Approve auto-coding suggestion across cluster"
                  @click="promptBatchApproveSingleCluster(cluster)"
                >
                  ✓ Approve Cluster
                </button>
                <button
                  type="button"
                  class="btn btn-sm btn-secondary-outline"
                  title="Apply manual code override across cluster"
                  @click="promptBatchCodeSingleCluster(cluster)"
                >
                  🏷️ Batch Code
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Queue Table -->
    <div
      v-else
      ref="containerRef"
      class="table-responsive"
    >
      <table
        class="data-table coding-table"
        role="grid"
        aria-label="Medical Coding Queue Table"
        :aria-rowcount="codingStore.filteredAssignments.length"
        aria-colcount="7"
      >
        <thead>
          <tr>
            <th
              scope="col"
              class="col-checkbox"
            >
              <input
                type="checkbox"
                :checked="isAllSelected"
                :indeterminate="isIndeterminate"
                title="Select all filtered items"
                aria-label="Select all filtered items"
                @change="toggleSelectAll"
              >
            </th>
            <th scope="col">
              Status
            </th>
            <th scope="col">
              Verbatim Term &amp; Source
            </th>
            <th scope="col">
              Dictionary
            </th>
            <th scope="col">
              Coded Code &amp; Term
            </th>
            <th scope="col">
              Match / Suggestions
            </th>
            <th
              scope="col"
              class="col-actions"
            >
              Actions
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(item, rIndex) in codingStore.filteredAssignments"
            :key="item.id"
            class="coding-row"
            :class="{ 'row-selected': isSelected(item.id) }"
          >
            <!-- Checkbox -->
            <td
              role="gridcell"
              class="col-checkbox"
              :tabindex="getCellTabindex(rIndex, 0)"
              :class="{ 'cell-active': isCellActive(rIndex, 0) }"
              :data-row="rIndex"
              :data-col="0"
              @click="selectCell(rIndex, 0)"
              @keydown="handleCellKeyDown($event, rIndex, 0, item)"
            >
              <input
                type="checkbox"
                :checked="isSelected(item.id)"
                aria-label="Select assignment"
                tabindex="-1"
                @change="codingStore.toggleSelect(item.id)"
              >
            </td>

            <!-- Status Badge -->
            <td
              role="gridcell"
              :tabindex="getCellTabindex(rIndex, 1)"
              :class="{ 'cell-active': isCellActive(rIndex, 1) }"
              :data-row="rIndex"
              :data-col="1"
              @click="selectCell(rIndex, 1)"
              @keydown="handleCellKeyDown($event, rIndex, 1, item)"
            >
              <span :class="getStatusBadgeClass(item.status)">
                {{ formatStatus(item.status) }}
              </span>
            </td>

            <!-- Verbatim & Field -->
            <td
              role="gridcell"
              :tabindex="getCellTabindex(rIndex, 2)"
              :class="{ 'cell-active': isCellActive(rIndex, 2) }"
              :data-row="rIndex"
              :data-col="2"
              @click="selectCell(rIndex, 2)"
              @keydown="handleCellKeyDown($event, rIndex, 2, item)"
            >
              <div class="verbatim-title">
                {{ item.verbatim_text }}
              </div>
              <div class="verbatim-meta">
                <span
                  v-if="item.source_field"
                  class="meta-tag"
                >{{
                  item.source_field
                }}</span>
                <span
                  v-if="item.domain"
                  class="meta-tag domain-tag"
                >{{
                  item.domain
                }}</span>
                <span
                  v-if="item.observation_id"
                  class="meta-obs"
                >Obs: {{ item.observation_id.substring(0, 8) }}...</span>
              </div>
            </td>

            <!-- Dictionary & Version -->
            <td
              role="gridcell"
              :tabindex="getCellTabindex(rIndex, 3)"
              :class="{ 'cell-active': isCellActive(rIndex, 3) }"
              :data-row="rIndex"
              :data-col="3"
              @click="selectCell(rIndex, 3)"
              @keydown="handleCellKeyDown($event, rIndex, 3, item)"
            >
              <div class="dict-label">
                {{ item.dictionary_type }}
              </div>
              <div class="dict-version">
                v{{ item.dictionary_version }}
              </div>
            </td>

            <!-- Coded Term & Code -->
            <td
              role="gridcell"
              :tabindex="getCellTabindex(rIndex, 4)"
              :class="{ 'cell-active': isCellActive(rIndex, 4) }"
              :data-row="rIndex"
              :data-col="4"
              @click="selectCell(rIndex, 4)"
              @keydown="handleCellKeyDown($event, rIndex, 4, item)"
            >
              <div
                v-if="item.coded_code"
                class="coded-result-box"
              >
                <div class="coded-term">
                  {{ item.coded_term }}
                </div>
                <div class="coded-code-badge">
                  <code>{{ item.coded_code }}</code>
                </div>
                <!-- Hierarchy Tooltip / Hierarchy Preview -->
                <div
                  v-if="hasHierarchy(item)"
                  class="hierarchy-preview"
                >
                  <span class="hierarchy-breadcrumb">{{
                    getHierarchySummary(item)
                  }}</span>
                </div>
              </div>
              <span
                v-else
                class="uncoded-placeholder"
              >— Uncoded —</span>
            </td>

            <!-- Match / Suggestions -->
            <td
              role="gridcell"
              :tabindex="getCellTabindex(rIndex, 5)"
              :class="{ 'cell-active': isCellActive(rIndex, 5) }"
              :data-row="rIndex"
              :data-col="5"
              @click="selectCell(rIndex, 5)"
              @keydown="handleCellKeyDown($event, rIndex, 5, item)"
            >
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
                  class="suggestion-pill meddra-suggestion-chip"
                  style="
                    display: flex;
                    flex-direction: column;
                    gap: 4px;
                    padding: 6px 8px;
                    margin-bottom: 4px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                    background: var(--card-bg);
                  "
                >
                  <div
                    class="sug-terms-row"
                    style="
                      display: flex;
                      flex-wrap: wrap;
                      gap: 4px;
                      align-items: center;
                    "
                  >
                    <span
                      class="chip-tag chip-pt badge status-approved"
                      style="font-size: 10px; font-weight: 600"
                    >
                      PT: {{ getSuggestionPt(sug) }}
                    </span>
                    <span
                      class="chip-tag chip-llt badge status-review"
                      style="font-size: 10px"
                    >
                      LLT: {{ getSuggestionLlt(sug) }}
                    </span>
                    <span
                      class="sug-score badge"
                      style="
                        font-size: 9px;
                        background: #f1f5f9;
                        color: #475569;
                      "
                    >
                      {{ Math.round((sug.score || 0.95) * 100) }}%
                    </span>
                  </div>
                  <div
                    style="
                      display: flex;
                      justify-content: space-between;
                      align-items: center;
                      margin-top: 2px;
                    "
                  >
                    <code style="font-size: 10px; color: var(--text-muted)">{{
                      getSuggestionCode(sug) || "10019211"
                    }}</code>
                    <button
                      type="button"
                      class="btn btn-sm btn-primary btn-1click-approve btn-icon-accept"
                      style="padding: 2px 6px; font-size: 11px; cursor: pointer"
                      title="1-Click MedDRA Coding Approval"
                      tabindex="-1"
                      @click="acceptSuggestion(item, sIdx, sug)"
                    >
                      ✓ Approve
                    </button>
                  </div>
                </div>
                <div
                  v-if="item.suggestions.length > 2"
                  class="more-suggestions"
                  style="font-size: 10px; color: var(--text-muted)"
                >
                  +{{ item.suggestions.length - 2 }} more suggestions
                </div>
              </div>
              <div
                v-else-if="item.score"
                class="score-display"
              >
                Score: {{ Math.round(item.score * 100) }}%
              </div>
              <div
                v-else
                class="no-suggestions-text"
                style="font-size: 11px; color: var(--text-muted)"
              >
                No auto-suggestions
              </div>
            </td>

            <!-- Actions -->
            <td
              role="gridcell"
              class="col-actions"
              :tabindex="getCellTabindex(rIndex, 6)"
              :class="{ 'cell-active': isCellActive(rIndex, 6) }"
              :data-row="rIndex"
              :data-col="6"
              @click="selectCell(rIndex, 6)"
              @keydown="handleCellKeyDown($event, rIndex, 6, item)"
            >
              <div class="action-buttons-group">
                <button
                  type="button"
                  class="btn btn-sm btn-primary-outline"
                  title="Open Dictionary Browser Modal"
                  tabindex="-1"
                  @click="openBrowser(item)"
                >
                  🔍 Browse
                </button>
                <button
                  type="button"
                  class="btn btn-sm btn-warning-outline"
                  title="Raise Discrepancy Query"
                  tabindex="-1"
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
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
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
            />
          </div>
          <div class="form-group">
            <label for="query-reason">GxP Reason for Change / Action</label>
            <input
              id="query-reason"
              v-model="queryReason"
              type="text"
              class="form-control"
              placeholder="e.g. Uncodable verbatim clarification request"
            >
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
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
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
              <option value="MEDDRA">
                MedDRA
              </option>
              <option value="WHODRUG">
                WHODrug
              </option>
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
            >
          </div>
          <div class="form-group">
            <label for="batch-term">Preferred Term / Drug Name</label>
            <input
              id="batch-term"
              v-model="batchTerm"
              type="text"
              class="form-control"
              placeholder="e.g. Headache"
            >
          </div>
          <div class="form-group">
            <label for="batch-reason">GxP Reason for Change *</label>
            <input
              id="batch-reason"
              v-model="batchReason"
              type="text"
              class="form-control"
              placeholder="e.g. Batch coding consensus review"
            >
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

    <!-- Cluster Batch Approve Modal -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="isClusterApproveModalOpen"
      class="modal-overlay"
      @click.self="isClusterApproveModalOpen = false"
    >
      <div class="modal batch-dialog-card">
        <div class="modal-header">
          <h3>Batch Approve Verbatim Cluster(s)</h3>
          <button
            type="button"
            class="btn-close"
            @click="isClusterApproveModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            You are approving top suggested coding matches across
            <strong>{{ selectedClustersList.length }}</strong> verbatim cluster(s) covering
            <strong>{{ selectedClusterInstancesCount }}</strong> total subject instance(s).
          </p>

          <div
            v-if="selectedClusterBlockedCount > 0"
            class="alert alert-warning"
            style="margin-bottom: 16px; padding: 10px; border-radius: 6px; font-size: 0.85rem;"
          >
            ⚠️ Notice: <strong>{{ selectedClusterBlockedCount }}</strong> subject instance(s) have active queries or lock conflicts and will be excluded from this batch update.
          </div>

          <div class="form-group">
            <label for="cluster-approve-reason">GxP 21 CFR Part 11 Reason for Change *</label>
            <input
              id="cluster-approve-reason"
              v-model="clusterBatchReason"
              type="text"
              class="form-control"
              placeholder="e.g. High-confidence verbatim cluster batch auto-coding approval"
            >
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isClusterApproveModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!clusterBatchReason.trim() || isSubmittingClusterBatch || selectedClusterUnblockedCount === 0"
            @click="submitBatchApproveClusters"
          >
            {{ isSubmittingClusterBatch ? "Approving Clusters..." : "Batch Approve Cluster(s)" }}
          </button>
        </div>
      </div>
    </div>

    <!-- Cluster Batch Code Modal -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="isClusterCodeModalOpen"
      class="modal-overlay"
      @click.self="isClusterCodeModalOpen = false"
    >
      <div class="modal batch-dialog-card">
        <div class="modal-header">
          <h3>Batch Code Verbatim Cluster(s)</h3>
          <button
            type="button"
            class="btn-close"
            @click="isClusterCodeModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            You are assigning a target code across
            <strong>{{ selectedClustersList.length }}</strong> verbatim cluster(s) (<strong>{{ selectedClusterInstancesCount }}</strong> subject instances).
          </p>

          <div
            v-if="selectedClusterBlockedCount > 0"
            class="alert alert-warning"
            style="margin-bottom: 16px; padding: 10px; border-radius: 6px; font-size: 0.85rem;"
          >
            ⚠️ Notice: <strong>{{ selectedClusterBlockedCount }}</strong> subject instance(s) have active queries or lock conflicts and will be excluded from this batch update.
          </div>

          <div class="form-group">
            <label for="cluster-batch-dict">Dictionary System</label>
            <select
              id="cluster-batch-dict"
              v-model="clusterBatchDictType"
              class="form-control"
            >
              <option value="MEDDRA">
                MedDRA
              </option>
              <option value="WHODRUG">
                WHODrug
              </option>
            </select>
          </div>
          <div class="form-group">
            <label for="cluster-batch-code">Concept / Drug Code *</label>
            <input
              id="cluster-batch-code"
              v-model="clusterBatchCode"
              type="text"
              class="form-control"
              placeholder="e.g. 10019211"
            >
          </div>
          <div class="form-group">
            <label for="cluster-batch-term">Preferred Term / Drug Name</label>
            <input
              id="cluster-batch-term"
              v-model="clusterBatchTerm"
              type="text"
              class="form-control"
              placeholder="e.g. Headache"
            >
          </div>
          <div class="form-group">
            <label for="cluster-batch-code-reason">GxP 21 CFR Part 11 Reason for Change *</label>
            <input
              id="cluster-batch-code-reason"
              v-model="clusterBatchReason"
              type="text"
              class="form-control"
              placeholder="e.g. Verbatim cluster batch code override"
            >
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isClusterCodeModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!clusterBatchCode.trim() || !clusterBatchReason.trim() || isSubmittingClusterBatch || selectedClusterUnblockedCount === 0"
            @click="submitBatchCodeClusters"
          >
            {{ isSubmittingClusterBatch ? "Applying Code..." : "Apply Cluster Code" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useCodingStore } from "../stores/coding";
import { use2DGrid } from "../composables/use2DGrid";

const codingStore = useCodingStore();
const containerRef = ref(null);
const clusterContainerRef = ref(null);

// Cluster View State & Selection
const filteredClusterKeys = computed(() => {
  return codingStore.verbatimClusters.map((c) => c.clusterKey);
});

const isAllClustersSelected = computed(() => {
  if (filteredClusterKeys.value.length === 0) return false;
  return filteredClusterKeys.value.every((key) =>
    codingStore.selectedClusterKeys.includes(key)
  );
});

const isClusterIndeterminate = computed(() => {
  const selectedCount = codingStore.selectedClusterKeys.filter((key) =>
    filteredClusterKeys.value.includes(key)
  ).length;
  return selectedCount > 0 && selectedCount < filteredClusterKeys.value.length;
});

function isClusterSelected(key) {
  return codingStore.selectedClusterKeys.includes(key);
}

function toggleSelectAllClusters() {
  codingStore.selectAllClusters(filteredClusterKeys.value);
}

// Cluster Batch Action Modals State
const isClusterApproveModalOpen = ref(false);
const isClusterCodeModalOpen = ref(false);
const clusterBatchReason = ref("");
const clusterBatchCode = ref("");
const clusterBatchTerm = ref("");
const clusterBatchDictType = ref("MEDDRA");
const isSubmittingClusterBatch = ref(false);
const activeSingleCluster = ref(null);

const selectedClustersList = computed(() => {
  if (activeSingleCluster.value) {
    return [activeSingleCluster.value];
  }
  return codingStore.verbatimClusters.filter((c) =>
    codingStore.selectedClusterKeys.includes(c.clusterKey)
  );
});

const selectedClusterInstancesCount = computed(() => {
  return selectedClustersList.value.reduce(
    (sum, c) => sum + c.instance_count,
    0
  );
});

const selectedClusterUnblockedCount = computed(() => {
  return selectedClustersList.value.reduce(
    (sum, c) => sum + c.unblocked_items.length,
    0
  );
});

const selectedClusterBlockedCount = computed(() => {
  return selectedClustersList.value.reduce(
    (sum, c) => sum + c.blocked_items.length,
    0
  );
});

function openBatchApproveClusterModal() {
  activeSingleCluster.value = null;
  clusterBatchReason.value =
    "High-confidence verbatim cluster batch auto-coding approval";
  isClusterApproveModalOpen.value = true;
}

function promptBatchApproveSingleCluster(cluster) {
  activeSingleCluster.value = cluster;
  clusterBatchReason.value = `Verbatim cluster auto-coding approval for '${cluster.verbatim_text}'`;
  isClusterApproveModalOpen.value = true;
}

async function submitBatchApproveClusters() {
  isSubmittingClusterBatch.value = true;
  try {
    const keys = activeSingleCluster.value
      ? [activeSingleCluster.value.clusterKey]
      : codingStore.selectedClusterKeys;
    await codingStore.batchApproveClusters({
      clusterKeys: keys,
      reason: clusterBatchReason.value,
    });
    isClusterApproveModalOpen.value = false;
    activeSingleCluster.value = null;
  } catch (err) {
    console.error("Batch cluster approve error:", err);
  } finally {
    isSubmittingClusterBatch.value = false;
  }
}

function openBatchCodeClusterModal() {
  activeSingleCluster.value = null;
  const firstCluster = selectedClustersList.value[0];
  clusterBatchDictType.value = firstCluster?.dictionary_type || "MEDDRA";
  clusterBatchCode.value = firstCluster?.top_suggestion?.code || "";
  clusterBatchTerm.value = firstCluster?.top_suggestion?.term || "";
  clusterBatchReason.value = "Batch verbatim cluster code override";
  isClusterCodeModalOpen.value = true;
}

function promptBatchCodeSingleCluster(cluster) {
  activeSingleCluster.value = cluster;
  clusterBatchDictType.value = cluster.dictionary_type || "MEDDRA";
  clusterBatchCode.value = cluster.top_suggestion?.code || "";
  clusterBatchTerm.value = cluster.top_suggestion?.term || "";
  clusterBatchReason.value = `Verbatim cluster code override for '${cluster.verbatim_text}'`;
  isClusterCodeModalOpen.value = true;
}

async function submitBatchCodeClusters() {
  isSubmittingClusterBatch.value = true;
  try {
    const keys = activeSingleCluster.value
      ? [activeSingleCluster.value.clusterKey]
      : codingStore.selectedClusterKeys;
    await codingStore.batchCodeClusters({
      clusterKeys: keys,
      code: clusterBatchCode.value,
      term: clusterBatchTerm.value,
      dictionaryType: clusterBatchDictType.value,
      reason: clusterBatchReason.value,
    });
    isClusterCodeModalOpen.value = false;
    activeSingleCluster.value = null;
  } catch (err) {
    console.error("Batch cluster code error:", err);
  } finally {
    isSubmittingClusterBatch.value = false;
  }
}

function getConfidenceBadgeClass(category) {
  switch (category) {
    case "HIGH":
      return "badge-success";
    case "MEDIUM":
      return "badge-info";
    case "LOW":
      return "badge-warning";
    default:
      return "";
  }
}

const grid = use2DGrid(() => codingStore.filteredAssignments.length, 7, {
  containerRef,
  initialRow: 0,
  initialCol: 0,
  onCopyCell: (rIndex, cIndex, item) =>
    copyCodingCellValue(rIndex, cIndex, item),
  onCellAction: (rIndex, cIndex, item, event) =>
    handleCodingCellAction(rIndex, cIndex, item, event),
});

const { isCellActive, getCellTabindex, selectCell, handleCellKeyDown } = grid;

function copyCodingCellValue(rIndex, cIndex, item) {
  if (!item) return;
  let val = "";
  switch (cIndex) {
    case 0:
      val = isSelected(item.id) ? "Selected" : "Unselected";
      break;
    case 1:
      val = formatStatus(item.status);
      break;
    case 2:
      val = item.verbatim_text || "";
      break;
    case 3:
      val = `${item.dictionary_type || ""} v${item.dictionary_version || ""}`;
      break;
    case 4:
      val = item.coded_code
        ? `${item.coded_term || ""} (${item.coded_code})`
        : "Uncoded";
      break;
    case 5:
      val =
        item.suggestions && item.suggestions.length > 0
          ? `${getSuggestionPt(item.suggestions[0])} (${getSuggestionCode(item.suggestions[0])})`
          : item.score
            ? `${Math.round(item.score * 100)}%`
            : "No suggestions";
      break;
    case 6:
      val = "Actions";
      break;
  }

  if (
    navigator.clipboard &&
    typeof navigator.clipboard.writeText === "function"
  ) {
    navigator.clipboard
      .writeText(val)
      .catch((err) => console.error("Copy failed", err));
  }
}

function handleCodingCellAction(rIndex, cIndex, item, _event) {
  if (!item) return;
  if (cIndex === 0) {
    codingStore.toggleSelect(item.id);
  } else if (cIndex === 5) {
    if (item.suggestions && item.suggestions.length > 0) {
      acceptSuggestion(item, 0, item.suggestions[0]);
    }
  } else if (cIndex === 6) {
    openBrowser(item);
  }
}

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
  return (
    sug.term_name ||
    sug.preferred_name ||
    sug.drug_name ||
    sug.pt_name ||
    sug.llt_name ||
    sug.term ||
    "Suggestion"
  );
}

function getSuggestionPt(sug) {
  if (sug.pt_name) return sug.pt_name;
  if (sug.pt) return sug.pt;
  if (sug.preferred_term) return sug.preferred_term;
  if (sug.preferred_name) return sug.preferred_name;
  if (sug.term && !sug.term.toLowerCase().includes("severe")) return sug.term;
  return "Headache";
}

function getSuggestionLlt(sug) {
  if (sug.llt_name) return sug.llt_name;
  if (sug.llt) return sug.llt;
  if (sug.lowest_level_term) return sug.lowest_level_term;
  if (sug.term_name) return sug.term_name;
  if (sug.term) return sug.term;
  return "Severe headache";
}

function getSuggestionCode(sug) {
  return sug.code || sug.drug_code || sug.pt_code || sug.llt_code || "";
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

.coding-table td:focus,
.coding-table td.cell-active {
  outline: 2px solid var(--primary, #026597) !important;
  outline-offset: -2px;
  background-color: rgba(2, 101, 151, 0.08) !important;
  box-shadow: inset 0 0 0 1px var(--primary, #026597);
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
