<template>
  <div class="training-console-card card">
    <TrainingSelectorHeader
      v-model:selected-assignment-id="selectedAssignmentId"
      v-model:selected-curriculum-id="selectedCurriculumId"
      :loading="loading"
      :is-gated="isGated"
      :can-assign-curriculum="canAssignCurriculum"
      :assignments="assignments"
      :curricula="curricula"
      :active-assignment="activeAssignment"
      :active-curriculum="activeCurriculum"
      :live-quiz-percent="liveQuizPercent"
      @refresh="refreshData"
      @open-author-modal="showAuthorModal = true"
      @assignment-change="onAssignmentChange"
      @curriculum-change="onCurriculumChange"
    />

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

      <TrainingModulesReaderTab
        v-if="activeTab === 'modules'"
        v-model:selected-module-index="selectedModuleIndex"
        v-model:attestation-meaning="attestationForm.meaning"
        v-model:attestation-reason="attestationForm.reasonForChange"
        v-model:attestation-credential="attestationForm.signerCredential"
        :active-curriculum="activeCurriculum"
        :active-assignment="activeAssignment"
        :quiz-questions="quizQuestions"
        :quiz-answers="quizAnswers"
        :quiz-ready="quizReady"
        :submitting-completion="submittingCompletion"
        :completion-result="completionResult"
        :completion-error="completionError"
        @answer-quiz="setQuizAnswer"
        @submit-attestation="handleCompleteAssignment"
      />

      <div
        v-if="activeTab === 'batch'"
        id="training-console-panel-batch"
        role="tabpanel"
        aria-labelledby="training-console-tab-batch"
        class="tab-content batch-layout"
      >
        <div class="batch-card card">
          <div class="batch-header">
            <h3>Kernel Site-Grant Batch Curriculum Assignment</h3>
            <p class="section-desc">
              Select target clinical trial sites and roles resolved from active
              canonical Kernel grants. Duplicate active assignments are skipped
              automatically with a 200 target personnel limit.
            </p>
          </div>

          <p
            v-if="availableSites.length === 0"
            class="text-muted batch-empty"
            data-testid="batch-empty"
          >
            No staff hold active Kernel site grants on this study yet, so there
            is no one to assign this curriculum to.
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
                  <span class="font-mono">{{ site }}</span>
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
            <div class="preview-info tabular-nums">
              <span>
                Target Scope:
                <strong class="tabular-nums"
                  >{{ computedBatchCount }} personnel</strong
                >
                ({{ batchForm.selectedSites.length }} sites &times;
                {{ batchForm.selectedRoles.length }} roles)
              </span>
              <span
                v-if="computedBatchCount > 200"
                class="badge badge-error tabular-nums"
              >
                Exceeds 200 Synchronous Limit
              </span>
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
              <span>Execute Batch Assignment</span>
            </button>
          </div>

          <p v-if="batchError" class="badge badge-error" role="alert">
            {{ batchError }}
          </p>

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
            <div class="result-stats tabular-nums">
              <div class="stat-pill success-pill tabular-nums">
                Assigned: {{ batchResult.total_assigned }}
              </div>
              <div class="stat-pill warning-pill tabular-nums">
                Skipped Duplicates: {{ batchResult.total_skipped }}
              </div>
              <div class="stat-pill info-pill tabular-nums">
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
                    <td class="font-mono">{{ item.user_id }}</td>
                    <td class="font-mono">{{ item.site_id }}</td>
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
    </div>

    <div
      v-else-if="!loading"
      data-testid="training-empty-curriculum"
      class="empty-curriculum-state"
    >
      <p class="text-muted">
        No active protocol training curriculum is published for this study scope
        yet.
      </p>
      <button
        v-if="canAssignCurriculum"
        type="button"
        class="btn btn-primary"
        @click="showAuthorModal = true"
      >
        Author First Protocol Curriculum
      </button>
    </div>

    <ClinicalModal
      v-model="showBatchModal"
      :teleport="false"
      :dismissible="!submittingBatch"
      dialog-class="modal-card"
      title="Confirm Batch Curriculum Assignment"
    >
      <p class="modal-description tabular-nums">
        You are about to assign curriculum
        <strong>{{ activeCurriculum?.title || "Protocol Training" }}</strong>
        across
        <strong class="tabular-nums"
          >{{ batchForm.selectedSites.length }} sites</strong
        >
        and
        <strong class="tabular-nums"
          >{{ batchForm.selectedRoles.length }} roles</strong
        >
        ({{ computedBatchCount }} target personnel).
      </p>
      <div class="form-group">
        <label for="batch-reason">Reason for Change / Audit Note:</label>
        <input
          id="batch-reason"
          v-model="batchForm.reasonForChange"
          type="text"
          class="text-input control-touch"
          :disabled="submittingBatch"
          placeholder="e.g. Protocol Amendment V2.0 Deployment"
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
          <span v-else>Confirm &amp; Deploy Batch</span>
        </button>
      </template>
    </ClinicalModal>

    <TrainingCurriculumAuthorModal
      v-model="showAuthorModal"
      v-model:form="authorForm"
      :study-id="effectiveStudyId"
      :submitting="submittingCurriculum"
      :error-message="authorError"
      @submit="handleAuthorCurriculum"
    />
  </div>
</template>

<script setup>
import { ClinicalModal, ClinicalTabs } from "ui";
import { TRAINING_ASSIGN_ROLE_REASON } from "../../canvas/authority";
import { formatRoleLabel } from "../../utils/clinicalLabels";
import SupportDetails from "../canvas/SupportDetails.vue";
import TrainingCurriculumAuthorModal from "./training/TrainingCurriculumAuthorModal.vue";
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

const emit = defineEmits(["training-updated"]);

const {
  activeAssignment,
  activeCurriculum,
  activeTab,
  assignments,
  attestationForm,
  authorError,
  authorForm,
  availableRoles,
  availableSites,
  batchError,
  batchForm,
  batchResult,
  canAssignCurriculum,
  completionError,
  completionResult,
  computedBatchCount,
  curricula,
  effectiveStudyId,
  handleAuthorCurriculum,
  handleCompleteAssignment,
  handleExecuteBatchAssign,
  handleTrainingTabChange,
  isGated,
  liveQuizPercent,
  loading,
  onAssignmentChange,
  onCurriculumChange,
  quizAnswers,
  quizQuestions,
  quizReady,
  refreshData,
  selectedAssignmentId,
  selectedCurriculumId,
  selectedModuleIndex,
  setQuizAnswer,
  showAuthorModal,
  showBatchModal,
  submittingBatch,
  submittingCompletion,
  submittingCurriculum,
  trainingTabs,
} = useProtocolTrainingConsole(props, emit);
</script>

<style scoped src="./protocolTrainingConsole.css"></style>
