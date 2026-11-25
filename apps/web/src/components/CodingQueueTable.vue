<template>
  <div class="coding-queue-wrapper">
    <!-- ==================================================================== -->
    <!-- CLUSTERS VIEW BATCH TOOLBAR -->
    <!-- ==================================================================== -->
    <div
      v-if="
        codingStore.viewMode === 'CLUSTERS' && codingStore.selectedCount > 0
      "
      class="batch-action-bar cluster-batch-bar"
    >
      <div class="batch-summary">
        <span class="badge badge-accent tabular-nums">{{
          codingStore.selectedCount
        }}</span>
        <span>verbatim clusters selected for batch action</span>
      </div>
      <div class="batch-buttons">
        <button
          type="button"
          class="btn btn-primary btn-sm btn-batch-approve"
          @click="openBatchApproveClustersModal(null)"
        >
          ⚡ Batch Approve Selected Clusters
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="openBatchCodeClustersModal(null)"
        >
          🏷️ Batch Code Selected Clusters
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

    <!-- ==================================================================== -->
    <!-- INDIVIDUAL VIEW BATCH TOOLBAR -->
    <!-- ==================================================================== -->
    <div
      v-else-if="
        codingStore.viewMode === 'INDIVIDUAL' && codingStore.selectedCount > 0
      "
      class="batch-action-bar"
    >
      <div class="batch-summary">
        <span class="badge badge-accent tabular-nums">{{
          codingStore.selectedCount
        }}</span>
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
      <SkeletonLoader
        type="row"
        :count="5"
        :columns="codingStore.viewMode === 'CLUSTERS' ? 7 : 8"
      />
    </div>

    <!-- Empty State -->
    <div
      v-else-if="
        codingStore.viewMode === 'CLUSTERS'
          ? codingStore.filteredClusters.length === 0
          : codingStore.filteredAssignments.length === 0
      "
      class="table-empty-state"
    >
      <EmptyState
        icon="🩺"
        title="No Coding Assignments Found"
        message="No adverse events or concomitant medications match the active filters or confidence score threshold."
      >
        <template #action>
          <button
            type="button"
            class="btn btn-secondary btn-sm btn-reset-filters"
            @click="resetFilters"
          >
            🔄 Reset Active Filters
          </button>
        </template>
      </EmptyState>
    </div>

    <!-- ==================================================================== -->
    <!-- VERBATIM CLUSTERS TABLE -->
    <!-- ==================================================================== -->
    <div
      v-else-if="codingStore.viewMode === 'CLUSTERS'"
      ref="containerRef"
      class="table-responsive"
    >
      <table
        class="data-table coding-table cluster-table"
        role="grid"
        aria-label="Verbatim Clusters Queue Table"
        :aria-rowcount="codingStore.filteredClusters.length"
        aria-colcount="7"
      >
        <thead>
          <tr>
            <th scope="col" class="col-checkbox">
              <input
                type="checkbox"
                :checked="isAllClustersSelected"
                :indeterminate="isClusterIndeterminate"
                title="Select all filtered clusters"
                aria-label="Select all filtered clusters"
                @change="toggleSelectAllClusters"
              />
            </th>
            <th scope="col">Cluster Status</th>
            <th scope="col">Normalized Verbatim Term</th>
            <th scope="col">Dictionary</th>
            <th scope="col">Suggested Code &amp; Preferred Term</th>
            <th scope="col">AI Confidence Score</th>
            <th scope="col" class="col-actions">Batch Actions</th>
          </tr>
        </thead>
        <tbody>
          <template
            v-for="cluster in codingStore.filteredClusters"
            :key="cluster.id"
          >
            <tr
              class="coding-row cluster-row"
              :class="{ 'row-selected': isClusterSelected(cluster.id) }"
            >
              <!-- Checkbox -->
              <td role="gridcell" class="col-checkbox">
                <input
                  type="checkbox"
                  :checked="isClusterSelected(cluster.id)"
                  aria-label="Select verbatim cluster"
                  @change="codingStore.toggleClusterSelect(cluster.id)"
                />
              </td>

              <!-- Status Badge -->
              <td role="gridcell">
                <span :class="getStatusBadgeClass(cluster.cluster_status)">
                  {{ formatStatus(cluster.cluster_status) }}
                </span>
              </td>

              <!-- Normalized Verbatim & Context -->
              <td role="gridcell">
                <div class="verbatim-title cluster-verbatim-text">
                  <strong>{{ cluster.verbatim_text }}</strong>
                </div>
                <div class="verbatim-meta">
                  <span class="badge badge-neutral instance-count-badge">
                    👥 {{ cluster.instance_count }} subject instance{{
                      cluster.instance_count === 1 ? "" : "s"
                    }}
                  </span>
                  <span
                    v-if="cluster.blocked_count > 0"
                    class="badge badge-warning blocked-count-badge"
                  >
                    ⚠️ {{ cluster.blocked_count }} query / lock blocked
                  </span>
                </div>
              </td>

              <!-- Dictionary System -->
              <td role="gridcell">
                <div class="dict-label">
                  {{ cluster.dictionary_type }}
                </div>
                <div class="dict-version">
                  v{{ cluster.dictionary_version }}
                </div>
              </td>

              <!-- Coded / Suggested Term & Code -->
              <td role="gridcell">
                <div class="coded-result-box">
                  <div class="coded-term">
                    {{ cluster.top_suggested_term }}
                  </div>
                  <div class="coded-code-badge">
                    <code>{{ cluster.top_suggested_code }}</code>
                  </div>
                </div>
              </td>

              <!-- AI Confidence Score -->
              <td role="gridcell">
                <div class="confidence-badge-wrapper">
                  <span :class="getConfidenceBadgeClass(cluster.score)">
                    {{ Math.round((cluster.score || 0.95) * 100) }}%
                    {{ getConfidenceLabel(cluster.score) }}
                  </span>
                </div>
              </td>

              <!-- Actions -->
              <td role="gridcell" class="col-actions">
                <div class="action-buttons-group">
                  <button
                    type="button"
                    class="btn btn-sm btn-primary btn-batch-approve-single"
                    title="1-Step Batch Approve Cluster"
                    @click="openBatchApproveClustersModal([cluster.id])"
                  >
                    ⚡ Batch Approve
                  </button>
                  <button
                    type="button"
                    class="btn btn-sm btn-secondary-outline"
                    title="Batch Code Override"
                    @click="openBatchCodeClustersModal([cluster.id])"
                  >
                    🏷️ Batch Code
                  </button>
                  <button
                    type="button"
                    class="btn btn-sm btn-text-outline"
                    :title="
                      expandedClusterId === cluster.id
                        ? 'Hide Subject Instances'
                        : 'View Subject Instances'
                    "
                    @click="toggleClusterExpansion(cluster.id)"
                  >
                    {{
                      expandedClusterId === cluster.id
                        ? "▲ Hide"
                        : "👁️ Instances (" + cluster.instance_count + ")"
                    }}
                  </button>
                </div>
              </td>
            </tr>

            <!-- Expanded Instances Drawer Row -->
            <tr
              v-if="expandedClusterId === cluster.id"
              :key="cluster.id + '-expanded'"
              class="cluster-instances-drawer-row"
            >
              <td colspan="7" class="drawer-cell">
                <div class="instances-drawer-container">
                  <div class="drawer-header">
                    <h4>
                      Underlying Subject Instances for Cluster:
                      <code>{{ cluster.verbatim_text }}</code>
                    </h4>
                    <span class="text-muted">
                      Showing {{ cluster.instances.length }} instance(s) across
                      study sites
                    </span>
                  </div>
                  <table class="data-table instances-subtable">
                    <thead>
                      <tr>
                        <th>Subject ID</th>
                        <th>Source Field</th>
                        <th>Status</th>
                        <th>Observation ID</th>
                        <th>Current Code &amp; Term</th>
                        <th>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr
                        v-for="inst in cluster.instances"
                        :key="inst.id"
                        class="subtable-row"
                      >
                        <td>
                          <code>{{
                            inst.subject_id || inst.id.substring(0, 10)
                          }}</code>
                        </td>
                        <td>{{ inst.source_field || "AETERM" }}</td>
                        <td>
                          <span :class="getStatusBadgeClass(inst.status)">
                            {{ formatStatus(inst.status) }}
                          </span>
                        </td>
                        <td>
                          <code
                            >{{
                              (inst.observation_id || inst.id).substring(0, 12)
                            }}...</code
                          >
                        </td>
                        <td>
                          <span v-if="inst.coded_code">
                            {{ inst.coded_term }} (<code>{{
                              inst.coded_code
                            }}</code
                            >)
                          </span>
                          <span v-else class="text-muted">— Uncoded —</span>
                        </td>
                        <td>
                          <div class="subtable-actions">
                            <button
                              type="button"
                              class="btn btn-xs btn-secondary"
                              @click="openBrowser(inst)"
                            >
                              🔍 Browse
                            </button>
                            <button
                              type="button"
                              class="btn btn-xs btn-warning"
                              @click="promptRaiseQuery(inst)"
                            >
                              ❓ Query
                            </button>
                          </div>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <!-- ==================================================================== -->
    <!-- INDIVIDUAL ASSIGNMENTS TABLE -->
    <!-- ==================================================================== -->
    <div v-else ref="containerRef" class="table-responsive">
      <table
        class="data-table coding-table"
        role="grid"
        aria-label="Medical Coding Queue Table"
        :aria-rowcount="codingStore.filteredAssignments.length"
        aria-colcount="7"
      >
        <thead>
          <tr>
            <th scope="col" class="col-checkbox">
              <input
                type="checkbox"
                :checked="isAllSelected"
                :indeterminate="isIndeterminate"
                title="Select all filtered items"
                aria-label="Select all filtered items"
                @change="toggleSelectAll"
              />
            </th>
            <th scope="col">Status</th>
            <th scope="col">Verbatim Term &amp; Source</th>
            <th scope="col">Dictionary</th>
            <th scope="col">Coded Code &amp; Term</th>
            <th scope="col">Match / Suggestions</th>
            <th scope="col" class="col-actions">Actions</th>
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
              />
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
              <div class="dict-version">v{{ item.dictionary_version }}</div>
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
              <div v-if="item.coded_code" class="coded-result-box">
                <div class="coded-term">
                  {{ item.coded_term }}
                </div>
                <div class="coded-code-badge">
                  <code>{{ item.coded_code }}</code>
                </div>
                <div v-if="hasHierarchy(item)" class="hierarchy-preview">
                  <span class="hierarchy-breadcrumb">{{
                    getHierarchySummary(item)
                  }}</span>
                </div>
              </div>
              <span v-else class="uncoded-placeholder">— Uncoded —</span>
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
                >
                  <div class="sug-terms-row">
                    <span class="chip-tag chip-pt badge status-approved">
                      PT: {{ getSuggestionPt(sug) }}
                    </span>
                    <span class="chip-tag chip-llt badge status-review">
                      LLT: {{ getSuggestionLlt(sug) }}
                    </span>
                    <span :class="getConfidenceBadgeClass(sug.score || 0.95)">
                      {{ Math.round((sug.score || 0.95) * 100) }}%
                    </span>
                  </div>
                  <div class="sug-action-row">
                    <code>{{ getSuggestionCode(sug) || "10019211" }}</code>
                    <button
                      type="button"
                      class="btn btn-sm btn-primary btn-1click-approve btn-icon-accept"
                      title="1-Click MedDRA Coding Approval"
                      tabindex="-1"
                      @click="acceptSuggestion(item, sIdx, sug)"
                    >
                      ✓ Approve
                    </button>
                  </div>
                </div>
              </div>
              <div v-else-if="item.score" class="score-display">
                Score: {{ Math.round(item.score * 100) }}%
              </div>
              <div v-else class="no-suggestions-text">No auto-suggestions</div>
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

    <!-- ==================================================================== -->
    <!-- BATCH APPROVE CLUSTERS MODAL -->
    <!-- ==================================================================== -->
    <div
      v-if="isApproveClusterModalOpen"
      class="modal-overlay"
      role="presentation"
      @click.self="isApproveClusterModalOpen = false"
    >
      <div
        ref="approveClusterModalRef"
        class="modal batch-dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="approve-cluster-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="approve-cluster-modal-title">
            ⚡ Batch Approve Verbatim Cluster(s)
          </h3>
          <button
            type="button"
            class="btn-close"
            aria-label="Close"
            @click="isApproveClusterModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            Executing 1-step batch approval across
            <strong>{{ targetClusterIdsForApprove.length }}</strong> verbatim
            cluster(s) containing
            <strong>{{ targetClustersSummary.totalInstances }}</strong> total
            subject instance(s).
          </p>

          <div
            v-if="targetClustersSummary.blockedInstances > 0"
            class="alert-banner alert-warning"
          >
            <span>
              ⚠️
              <strong>{{ targetClustersSummary.blockedInstances }}</strong>
              instance(s) have active discrepancy queries or lock conflicts and
              will be excluded.
              <strong>{{ targetClustersSummary.unblockedInstances }}</strong>
              unblocked instance(s) will be auto-coded.
            </span>
          </div>

          <div class="form-group">
            <label for="cluster-approve-reason"
              >GxP Reason for Change * (21 CFR Part 11 Audit Trail)</label
            >
            <input
              id="cluster-approve-reason"
              v-model="clusterApproveReason"
              type="text"
              class="form-control"
              placeholder="e.g. Batch approve verbatim cluster high-confidence match"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isApproveClusterModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="
              !clusterApproveReason.trim() || isSubmittingClusterApprove
            "
            @click="submitBatchApproveClusters"
          >
            {{
              isSubmittingClusterApprove
                ? "Approving Cluster..."
                : "Batch Approve Cluster(s)"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- ==================================================================== -->
    <!-- BATCH CODE CLUSTERS MODAL -->
    <!-- ==================================================================== -->
    <div
      v-if="isBatchClusterModalOpen"
      class="modal-overlay"
      role="presentation"
      @click.self="isBatchClusterModalOpen = false"
    >
      <div
        ref="batchClusterModalRef"
        class="modal batch-dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="batch-cluster-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="batch-cluster-modal-title">
            🏷️ Batch Code Verbatim Cluster(s)
          </h3>
          <button
            type="button"
            class="btn-close"
            aria-label="Close"
            @click="isBatchClusterModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p>
            Applying target dictionary code and preferred term across
            <strong>{{ targetClusterIdsForCode.length }}</strong> selected
            cluster(s).
          </p>
          <div class="form-group">
            <label for="batch-cluster-dict">Dictionary Type</label>
            <select
              id="batch-cluster-dict"
              v-model="batchClusterDictType"
              class="form-control"
            >
              <option value="MEDDRA">MedDRA</option>
              <option value="WHODRUG">WHODrug</option>
            </select>
          </div>
          <div class="form-group">
            <label for="batch-cluster-code">Target Concept / Drug Code *</label>
            <input
              id="batch-cluster-code"
              v-model="batchClusterCode"
              type="text"
              class="form-control"
              placeholder="e.g. 10019211"
            />
          </div>
          <div class="form-group">
            <label for="batch-cluster-term"
              >Target Preferred Term / Drug Name *</label
            >
            <input
              id="batch-cluster-term"
              v-model="batchClusterTerm"
              type="text"
              class="form-control"
              placeholder="e.g. Headache"
            />
          </div>
          <div class="form-group">
            <label for="batch-cluster-reason">GxP Reason for Change *</label>
            <input
              id="batch-cluster-reason"
              v-model="batchClusterReason"
              type="text"
              class="form-control"
              placeholder="e.g. Multi-cluster medical coding review and synonym propagation"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isBatchClusterModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="
              !batchClusterCode.trim() ||
              !batchClusterTerm.trim() ||
              !batchClusterReason.trim() ||
              isSubmittingClusterCode
            "
            @click="submitBatchCodeClusters"
          >
            {{
              isSubmittingClusterCode
                ? "Applying Batch Code..."
                : "Batch Code Clusters"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- ==================================================================== -->
    <!-- DISCREPANCY QUERY MODAL -->
    <!-- ==================================================================== -->
    <div
      v-if="isQueryModalOpen"
      class="modal-overlay"
      role="presentation"
      @click.self="isQueryModalOpen = false"
    >
      <div
        ref="queryModalRef"
        class="modal query-dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="coding-query-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="coding-query-modal-title">Raise Clinical Coding Query</h3>
          <button
            type="button"
            class="btn-close"
            aria-label="Close"
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

    <!-- ==================================================================== -->
    <!-- INDIVIDUAL BATCH CODE MODAL -->
    <!-- ==================================================================== -->
    <div
      v-if="isBatchModalOpen"
      class="modal-overlay"
      role="presentation"
      @click.self="isBatchModalOpen = false"
    >
      <div
        ref="batchModalRef"
        class="modal batch-dialog-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="batch-items-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="batch-items-modal-title">Batch Code Selected Items</h3>
          <button
            type="button"
            class="btn-close"
            aria-label="Close"
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
import { EmptyState, useFocusTrap } from "ui";
import { useCodingStore } from "../stores/coding";
import { use2DGrid } from "../composables/use2DGrid";
import SkeletonLoader from "./common/SkeletonLoader.vue";

const codingStore = useCodingStore();
const containerRef = ref(null);

function resetFilters() {
  codingStore.confidenceThreshold = "ALL";
  if (codingStore.filters) {
    codingStore.filters.status = "ALL";
    codingStore.filters.dictionaryType = "ALL";
    codingStore.filters.confidenceThreshold = "ALL";
    codingStore.filters.search = "";
  }
}

// 2D Grid navigation composable
const grid = use2DGrid(
  () =>
    codingStore.viewMode === "CLUSTERS"
      ? codingStore.filteredClusters.length
      : codingStore.filteredAssignments.length,
  7,
  {
    containerRef,
    initialRow: 0,
    initialCol: 0,
    onCopyCell: (rIndex, cIndex, item) =>
      copyCodingCellValue(rIndex, cIndex, item),
    onCellAction: (rIndex, cIndex, item, event) =>
      handleCodingCellAction(rIndex, cIndex, item, event),
  }
);

const { isCellActive, getCellTabindex, selectCell, handleCellKeyDown } = grid;

// Cluster Drawer Expansion State
const expandedClusterId = ref(null);

function toggleClusterExpansion(clusterId) {
  expandedClusterId.value =
    expandedClusterId.value === clusterId ? null : clusterId;
}

// Batch Approve Clusters Modal State
const isApproveClusterModalOpen = ref(false);
const targetClusterIdsForApprove = ref([]);
const clusterApproveReason = ref(
  "Batch approve verbatim cluster high-confidence match"
);
const isSubmittingClusterApprove = ref(false);

const targetClustersSummary = computed(() => {
  const clusters = codingStore.verbatimClusters.filter(
    (c) =>
      targetClusterIdsForApprove.value.includes(c.id) ||
      targetClusterIdsForApprove.value.includes(c.cluster_id)
  );
  let totalInstances = 0;
  let blockedInstances = 0;
  for (const c of clusters) {
    totalInstances += c.instance_count;
    blockedInstances += c.blocked_count;
  }
  return {
    totalInstances,
    blockedInstances,
    unblockedInstances: totalInstances - blockedInstances,
  };
});

function openBatchApproveClustersModal(clusterIds) {
  targetClusterIdsForApprove.value = clusterIds || [
    ...codingStore.selectedClusterIds,
  ];
  clusterApproveReason.value =
    "Batch approve verbatim cluster high-confidence match";
  isApproveClusterModalOpen.value = true;
}

async function submitBatchApproveClusters() {
  isSubmittingClusterApprove.value = true;
  try {
    await codingStore.batchApproveCluster({
      clusterIds: targetClusterIdsForApprove.value,
      reason: clusterApproveReason.value,
    });
    isApproveClusterModalOpen.value = false;
  } catch (err) {
    console.error("Failed batch approve clusters:", err);
  } finally {
    isSubmittingClusterApprove.value = false;
  }
}

// Batch Code Clusters Modal State
const isBatchClusterModalOpen = ref(false);
const targetClusterIdsForCode = ref([]);
const batchClusterDictType = ref("MEDDRA");
const batchClusterCode = ref("");
const batchClusterTerm = ref("");
const batchClusterReason = ref(
  "Multi-cluster medical coding review and synonym propagation"
);
const isSubmittingClusterCode = ref(false);

function openBatchCodeClustersModal(clusterIds) {
  targetClusterIdsForCode.value = clusterIds || [
    ...codingStore.selectedClusterIds,
  ];
  batchClusterDictType.value = "MEDDRA";
  batchClusterCode.value = "";
  batchClusterTerm.value = "";
  batchClusterReason.value =
    "Multi-cluster medical coding review and synonym propagation";
  isBatchClusterModalOpen.value = true;
}

async function submitBatchCodeClusters() {
  isSubmittingClusterCode.value = true;
  try {
    await codingStore.batchCodeClusters({
      clusterIds: targetClusterIdsForCode.value,
      code: batchClusterCode.value,
      term: batchClusterTerm.value,
      dictionaryType: batchClusterDictType.value,
      reason: batchClusterReason.value,
    });
    isBatchClusterModalOpen.value = false;
  } catch (err) {
    console.error("Failed batch code clusters:", err);
  } finally {
    isSubmittingClusterCode.value = false;
  }
}

// Cluster Selection Helper Methods
const filteredClusterIds = computed(() =>
  codingStore.filteredClusters.map((c) => c.id)
);

const isAllClustersSelected = computed(() => {
  if (filteredClusterIds.value.length === 0) return false;
  return filteredClusterIds.value.every((id) =>
    codingStore.selectedClusterIds.includes(id)
  );
});

const isClusterIndeterminate = computed(() => {
  const count = codingStore.selectedClusterIds.filter((id) =>
    filteredClusterIds.value.includes(id)
  ).length;
  return count > 0 && count < filteredClusterIds.value.length;
});

function isClusterSelected(id) {
  return codingStore.selectedClusterIds.includes(id);
}

function toggleSelectAllClusters() {
  codingStore.selectAllClusters(filteredClusterIds.value);
}

// Individual Items Selection Helper Methods
const filteredIds = computed(() =>
  codingStore.filteredAssignments.map((a) => a.id)
);

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

// Discrepancy Query State
const isQueryModalOpen = ref(false);
const queryTargetItem = ref(null);
const queryMessage = ref("");
const queryReason = ref("");
const isSubmittingQuery = ref(false);

// Individual Batch Modal State
const isBatchModalOpen = ref(false);
const batchDictType = ref("MEDDRA");
const batchCode = ref("");
const batchTerm = ref("");
const batchReason = ref("");
const isSubmittingBatch = ref(false);

const approveClusterModalRef = ref(null);
const batchClusterModalRef = ref(null);
const queryModalRef = ref(null);
const batchModalRef = ref(null);

useFocusTrap(approveClusterModalRef, {
  isOpen: () => isApproveClusterModalOpen.value,
  onEscape: () => {
    isApproveClusterModalOpen.value = false;
  },
});

useFocusTrap(batchClusterModalRef, {
  isOpen: () => isBatchClusterModalOpen.value,
  onEscape: () => {
    isBatchClusterModalOpen.value = false;
  },
});

useFocusTrap(queryModalRef, {
  isOpen: () => isQueryModalOpen.value,
  onEscape: () => {
    isQueryModalOpen.value = false;
  },
});

useFocusTrap(batchModalRef, {
  isOpen: () => isBatchModalOpen.value,
  onEscape: () => {
    isBatchModalOpen.value = false;
  },
});

function copyCodingCellValue(rIndex, cIndex, item) {
  if (!item) return;
  let val = "";
  if (codingStore.viewMode === "CLUSTERS") {
    switch (cIndex) {
      case 0:
        val = isClusterSelected(item.id) ? "Selected" : "Unselected";
        break;
      case 1:
        val = formatStatus(item.cluster_status);
        break;
      case 2:
        val = `${item.verbatim_text} (${item.instance_count} instances)`;
        break;
      case 3:
        val = `${item.dictionary_type} v${item.dictionary_version}`;
        break;
      case 4:
        val = `${item.top_suggested_term} (${item.top_suggested_code})`;
        break;
      case 5:
        val = `${Math.round((item.score || 0.95) * 100)}%`;
        break;
      case 6:
        val = "Batch Actions";
        break;
    }
  } else {
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
  if (codingStore.viewMode === "CLUSTERS") {
    if (cIndex === 0) {
      codingStore.toggleClusterSelect(item.id);
    } else if (cIndex === 6) {
      openBatchApproveClustersModal([item.id]);
    }
  } else {
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
    case "MIXED":
      return "Mixed Status";
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
    case "MIXED":
      return "badge badge-secondary";
    default:
      return "badge";
  }
}

function getConfidenceBadgeClass(score) {
  const s = score || 0.95;
  if (s >= 0.9) return "badge confidence-high";
  if (s >= 0.7) return "badge confidence-medium";
  return "badge confidence-low";
}

function getConfidenceLabel(score) {
  const s = score || 0.95;
  if (s >= 0.9) return "High Confidence";
  if (s >= 0.7) return "Medium Confidence";
  return "Low Confidence";
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
  background-color: var(--semantic-color-info-bg-soft);
  border: 1px solid var(--semantic-color-primary);
  border-radius: 8px;
  padding: 10px 16px;
  animation: fadeIn 0.2s ease;
}

.cluster-batch-bar {
  background-color: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success);
}

.batch-summary {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: var(--font-size-sm);
  color: var(--text-main);
}

.batch-buttons {
  display: flex;
  align-items: center;
  gap: 8px;
}

.btn-batch-approve {
  background-color: var(--semantic-color-success-interactive);
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-text-on-solid);
}

.btn-batch-approve:hover {
  background-color: var(--semantic-color-success);
}

.btn-batch-approve-single {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  font-weight: 600;
}

.confidence-high {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  border: 1px solid var(--semantic-color-success-border);
}

.confidence-medium {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-divider-accent);
}

.confidence-low {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
  border: 1px solid var(--semantic-color-warning-border);
}

.instance-count-badge {
  font-size: var(--font-size-2xs);
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  font-weight: 600;
}

.blocked-count-badge {
  font-size: var(--font-size-2xs);
  font-weight: 600;
}

.cluster-verbatim-text {
  font-size: var(--font-size-base);
  color: var(--text-main);
}

.cluster-instances-drawer-row {
  background-color: var(--semantic-color-surface-muted);
}

.drawer-cell {
  padding: 12px 20px !important;
}

.instances-drawer-container {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 14px;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.drawer-header h4 {
  margin: 0;
  font-size: var(--font-size-sm);
  color: var(--text-main);
}

.instances-subtable {
  width: 100%;
  font-size: var(--font-size-xs);
}

.subtable-actions {
  display: flex;
  gap: 6px;
}

.btn-xs {
  padding: 2px 6px;
  font-size: var(--font-size-2xs);
  border-radius: 4px;
}

.sug-terms-row {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
}

.sug-action-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 2px;
}

.meddra-suggestion-chip {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px 8px;
  margin-bottom: 4px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background-color: var(--semantic-color-backdrop);
  backdrop-filter: blur(4px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
  padding: 16px;
}

.modal {
  background: var(--semantic-color-surface);
  border-radius: 12px;
  width: 100%;
  max-width: 520px;
  border: 1px solid var(--semantic-color-border);
  box-shadow: var(--semantic-shadow-xl);
  overflow: hidden;
}

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: var(--primary);
  color: var(--semantic-color-text-on-primary);
  padding: 14px 20px;
  font-weight: 700;
}

.modal-header h3 {
  margin: 0;
  font-size: var(--font-size-md);
  color: inherit;
}

.btn-close {
  background: none;
  border: none;
  font-size: var(--font-size-lg);
  color: inherit;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 6px;
}

.modal-body {
  padding: 20px;
}

.modal-footer {
  padding: 12px 20px;
  background-color: var(--neutral-light);
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
