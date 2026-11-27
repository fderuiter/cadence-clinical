<template>
  <div class="training-console-card card">
    <TrainingSelectorHeader
      v-model:selected-assignment-id="selectedAssignmentId"
      v-model:selected-curriculum-id="selectedCurriculumId"
      :loading="loading"
      :is-gated="isGated"
      :assignments="assignments"
      :curricula="curricula"
      :active-assignment="activeAssignment"
      :active-curriculum="activeCurriculum"
      @refresh="refreshData"
      @assignment-change="onAssignmentChange"
      @curriculum-change="onCurriculumChange"
    />

    <!-- Main Learning & Governance Workspace -->
    <div v-if="activeCurriculum" class="training-workspace">
      <ClinicalTabs
        :model-value="activeTab"
        :tabs="trainingTabs"
        variant="underline"
        aria-label="Protocol training workspace sections"
        id-prefix="training-console"
        tab-class="tab-btn"
        @update:model-value="handleTrainingTabChange"
      />

      <!-- TAB 1: MODULES -->
      <TrainingModulesReaderTab
        v-if="activeTab === 'modules'"
        v-model:selected-module-index="selectedModuleIndex"
        :active-curriculum="activeCurriculum"
      />

      <!-- TAB 2: BATCH ASSIGNMENT WORKSPACE -->
      <div
        v-if="activeTab === 'batch'"
        id="training-console-panel-batch"
        role="tabpanel"
        aria-labelledby="training-console-tab-batch"
        class="tab-content batch-layout"
      >
        <div class="batch-card card">
          <div class="batch-header">
            <h3>Multi-Site &amp; Role Batch Curriculum Assignment</h3>
            <p class="section-desc">
              Select target clinical trial sites and roles to batch-assign
              training curricula. Bypasses duplicate active assignments
              automatically with a 200 target personnel limit.
            </p>
          </div>

          <p
            v-if="availableSites.length === 0"
            class="text-muted batch-empty"
            data-testid="batch-empty"
          >
            No staff are on this study's delegation log yet, so there is no one
            to assign this curriculum to.
          </p>

          <div v-else class="batch-filter-grid">
            <fieldset class="filter-col form-fieldset">
              <legend class="filter-title">Target Sites (Multi-Select):</legend>
              <div class="checkbox-group">
                <label
                  v-for="site in availableSites"
                  :key="site"
                  class="checkbox-label"
                >
                  <input
                    v-model="batchForm.selectedSites"
                    type="checkbox"
                    :value="site"
                  />
                  <span>{{ site }}</span>
                </label>
              </div>
            </fieldset>

            <fieldset class="filter-col form-fieldset">
              <legend class="filter-title">
                Clinical Roles (Multi-Select):
              </legend>
              <div class="checkbox-group">
                <label
                  v-for="role in availableRoles"
                  :key="role"
                  class="checkbox-label"
                >
                  <input
                    v-model="batchForm.selectedRoles"
                    type="checkbox"
                    :value="role"
                  />
                  <span>{{ formatRoleLabel(role) }}</span>
                </label>
              </div>
            </fieldset>
          </div>

          <p
            v-if="!canAssignCurriculum"
            id="training-assign-role-note"
            class="role-note"
            role="note"
          >
            {{ TRAINING_ASSIGN_ROLE_REASON }}
          </p>

          <div class="batch-preview-bar">
            <div class="preview-info">
              <span
                >Target Scope:
                <strong>{{ computedBatchCount }} personnel</strong> ({{
                  batchForm.selectedSites.length
                }}
                sites &times; {{ batchForm.selectedRoles.length }} roles)</span
              >
              <span v-if="computedBatchCount > 200" class="badge badge-error">
                Exceeds 200 Synchronous Limit</span
              >
            </div>
            <button
              type="button"
              class="btn btn-primary"
              :disabled="
                !canAssignCurriculum ||
                computedBatchCount === 0 ||
                computedBatchCount > 200 ||
                submittingBatch
              "
              :title="
                !canAssignCurriculum ? TRAINING_ASSIGN_ROLE_REASON : undefined
              "
              @click="showBatchModal = true"
            >
              <span>Execute Batch Assignment </span>
            </button>
          </div>

          <p v-if="batchError" class="badge badge-error" role="alert">
            {{ batchError }}
          </p>

          <!-- Batch Execution Result Manifest -->
          <div v-if="batchResult" class="batch-result-card">
            <div class="result-header">
              <h4>Batch Assignment Summary</h4>
              <SupportDetails
                v-if="batchResult.batch_transaction_id"
                summary-label="Transaction reference"
                :detail="`Tx ID: ${batchResult.batch_transaction_id}`"
                :reference="batchResult.batch_transaction_id"
              />
            </div>
            <div class="result-stats">
              <div class="stat-pill success-pill">
                Assigned: {{ batchResult.total_assigned }}
              </div>
              <div class="stat-pill warning-pill">
                Skipped Duplicates: {{ batchResult.total_skipped }}
              </div>
              <div class="stat-pill info-pill">
                Total Eligible: {{ batchResult.total_eligible }}
              </div>
            </div>
            <div
              v-if="
                batchResult.skipped_items &&
                batchResult.skipped_items.length > 0
              "
              class="skipped-table-wrapper"
            >
              <h5>Skipped Duplicate Assignments</h5>
              <table class="data-table">
                <thead>
                  <tr>
                    <th>User ID</th>
                    <th>Site</th>
                    <th>Role</th>
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(item, idx) in batchResult.skipped_items"
                    :key="idx"
                  >
                    <td>{{ item.user_id }}</td>
                    <td>{{ item.site_id }}</td>
                    <td>{{ formatRoleLabel(item.user_role) }}</td>
                    <td>
                      <span class="badge badge-warning">{{ item.reason }}</span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 3: DELEGATION BULK SIGN-OFF WORKSPACE -->
      <div
        v-if="activeTab === 'delegations'"
        id="training-console-panel-delegations"
        role="tabpanel"
        aria-labelledby="training-console-tab-delegations"
        class="tab-content delegations-layout"
      >
        <div class="delegations-card card">
          <div class="delegation-header">
            <div>
              <h3>
                21 CFR Part 11 Principal Investigator Delegation Log Sign-Off
              </h3>
              <p class="section-desc">
                Review pending site staff delegation duties and execute legally
                binding electronic bulk signatures with digital seals (SHA-256)
                routed to eTMF Zone 5 (Artifact 05.02.04).
              </p>
            </div>
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              @click="loadDelegations"
            >
              Refresh Log
            </button>
          </div>

          <div class="delegations-table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th style="width: 40px">
                    <input
                      type="checkbox"
                      aria-label="Select all delegations"
                      :checked="isAllDelegationsSelected"
                      @change="toggleSelectAllDelegations"
                    />
                  </th>
                  <th>Staff Member</th>
                  <th>Role</th>
                  <th>Site ID</th>
                  <th>Task &amp; Duties</th>
                  <th>Status</th>
                  <th>Digital Seal</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="del in delegations" :key="del.id">
                  <td>
                    <input
                      v-model="selectedDelegationIds"
                      type="checkbox"
                      :value="del.id"
                      :aria-label="`Select delegation ${del.id}`"
                      :disabled="del.status === 'COMPLETED'"
                    />
                  </td>
                  <td>
                    <strong>{{ del.staff_name }}</strong><br /><small
                      class="text-muted"
                      >{{ del.staff_user_id }}</small
                    >
                  </td>
                  <td>
                    <span class="badge badge-role">{{
                      formatRoleLabel(del.role)
                    }}</span>
                  </td>
                  <td>{{ del.site_id }}</td>
                  <td>
                    <strong>{{
                      formatClinicalEnum(del.task_code)
                    }}</strong><br /><small>{{ del.duties }}</small>
                  </td>
                  <td>
                    <span
                      class="badge"
                      :class="
                        del.status === 'COMPLETED'
                          ? 'badge-success'
                          : 'badge-warning'
                      "
                    >
                      {{ formatClinicalStatus(del.status) }}
                    </span>
                  </td>
                  <td>
                    <template v-if="del.pi_signature_hash">
                      <span class="badge badge-success">Sealed</span>
                      <SupportDetails
                        summary-label="Digital seal"
                        :reference="del.pi_signature_hash"
                      />
                    </template>
                    <span v-else class="text-muted">Unsigned</span>
                  </td>
                </tr>
                <tr v-if="delegations.length === 0">
                  <td colspan="7" class="text-center">
                    No delegation records found.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <p
            v-if="!canSignDelegations"
            id="training-delegation-role-note"
            class="role-note"
            role="note"
          >
            {{ TRAINING_DELEGATION_SIGN_ROLE_REASON }}
          </p>

          <div class="delegation-actions-bar">
            <span
              >Selected for Sign-Off:
              <strong>{{ selectedDelegationIds.length }} records</strong></span
            >
            <button
              type="button"
              class="btn btn-success"
              :disabled="
                !canSignDelegations || selectedDelegationIds.length === 0
              "
              :title="
                !canSignDelegations
                  ? TRAINING_DELEGATION_SIGN_ROLE_REASON
                  : undefined
              "
              @click="showBulkSignModal = true"
            >
              Execute Part 11 Bulk Signature
            </button>
          </div>

          <!-- Last Bulk Sign Response Manifest -->
          <div
            v-if="bulkSignResult"
            class="bulk-sign-result alert-banner success-banner"
          >
            <div class="banner-content">
              <span class="banner-icon"
                ><ClinicalIcon name="check-circle" :size="20"
              /></span>
              <div>
                <strong>Bulk Delegation Sign-Off Executed Successfully</strong
                ><br />
                <small>
                  Signed Records: {{ bulkSignResult.total_signed }} | eTMF
                  Artifact: Zone 5 ({{ bulkSignResult.etmf_artifact_id }})
                </small>
                <SupportDetails
                  summary-label="Signature verification details"
                  :detail="`Batch Tx: ${bulkSignResult.batch_transaction_id}`"
                  :reference="bulkSignResult.sha256_seal"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Batch Assignment Modal -->
    <ClinicalModal
      v-model="showBatchModal"
      :teleport="false"
      :dismissible="!submittingBatch"
      dialog-class="modal-card"
      title="Confirm Batch Curriculum Assignment"
    >
      <p class="modal-description">
        You are about to assign curriculum
        <strong>{{ activeCurriculum?.title || "Protocol Training" }}</strong>
        across
        <strong>{{ batchForm.selectedSites.length }} sites</strong> and
        <strong>{{ batchForm.selectedRoles.length }} roles</strong> ({{
          computedBatchCount
        }}
        target personnel).
      </p>
      <div class="form-group">
        <label for="batch-reason">Reason for Change / Audit Note:</label>
        <input
          id="batch-reason"
          v-model="batchForm.reasonForChange"
          type="text"
          class="text-input"
          :disabled="submittingBatch"
          placeholder="e.g. Protocol Amendment V3.0 Deployment"
        />
      </div>
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="submittingBatch"
          @click="showBatchModal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="submittingBatch || !batchForm.reasonForChange.trim()"
          @click="handleExecuteBatchAssign"
        >
          <span v-if="submittingBatch">Deploying Batch...</span>
          <span v-else>Confirm &amp; Deploy Batch </span>
        </button>
      </template>
    </ClinicalModal>

    <!-- Bulk Delegation Sign-Off Modal -->
    <TrainingBulkSignModal
      v-model="showBulkSignModal"
      v-model:signer-name="bulkSignForm.signerName"
      v-model:signer-role="bulkSignForm.signerRole"
      v-model:signer-credential="bulkSignForm.password"
      :selected-count="selectedDelegationIds.length"
      :bulk-sign-error="bulkSignError"
      :submitting-bulk-sign="submittingBulkSign"
      @submit="handleExecuteBulkSignOff"
    />
  </div>
</template>

<script setup>
import { ClinicalIcon, ClinicalModal, ClinicalTabs } from "ui";
import {
  TRAINING_ASSIGN_ROLE_REASON,
  TRAINING_DELEGATION_SIGN_ROLE_REASON,
} from "../../canvas/authority";
import {
  formatClinicalEnum,
  formatClinicalStatus,
  formatRoleLabel,
} from "../../utils/clinicalLabels";
import SupportDetails from "../canvas/SupportDetails.vue";
import TrainingBulkSignModal from "./training/TrainingBulkSignModal.vue";
import TrainingModulesReaderTab from "./training/TrainingModulesReaderTab.vue";
import TrainingSelectorHeader from "./training/TrainingSelectorHeader.vue";
import { useProtocolTrainingConsole } from "./training/useProtocolTrainingConsole";

const props = defineProps({
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
  protocolVersion: {
    type: String,
    default: "",
  },
});

const {
  activeAssignment,
  activeCurriculum,
  activeTab,
  assignments,
  availableRoles,
  availableSites,
  batchError,
  batchForm,
  batchResult,
  bulkSignError,
  bulkSignForm,
  bulkSignResult,
  canAssignCurriculum,
  canSignDelegations,
  computedBatchCount,
  curricula,
  delegations,
  handleExecuteBatchAssign,
  handleExecuteBulkSignOff,
  handleTrainingTabChange,
  isAllDelegationsSelected,
  isGated,
  loadDelegations,
  loading,
  onAssignmentChange,
  onCurriculumChange,
  refreshData,
  selectedAssignmentId,
  selectedCurriculumId,
  selectedDelegationIds,
  selectedModuleIndex,
  showBatchModal,
  showBulkSignModal,
  submittingBatch,
  submittingBulkSign,
  toggleSelectAllDelegations,
  trainingTabs,
} = useProtocolTrainingConsole(props);
</script>

<style scoped src="./protocolTrainingConsole.css"></style>
