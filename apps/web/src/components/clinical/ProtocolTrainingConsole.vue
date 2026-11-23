<template>
  <div class="training-console-card card">
    <!-- Header -->
    <div class="console-header">
      <div class="header-titles">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R2) §4</span>
          <span class="badge badge-standard">DIA TMF 05.02.04</span>
          <span
            class="badge"
            :class="isGated ? 'badge-locked' : 'badge-unlocked'"
          >
            {{
              isGated
                ? "🔒 Qualification Pending"
                : "🟢 Qualification Completed"
            }}
          </span>
        </div>
        <h2 class="console-title">
          Protocol Amendment Training &amp; Qualification Console
        </h2>
        <p class="console-subtitle">
          Procedural delta curricula, multi-site role-based batch assignment,
          and 21 CFR Part 11 Principal Investigator delegation sign-off.
        </p>
      </div>

      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="loading"
          @click="refreshData"
        >
          <span class="btn-icon" aria-hidden="true">🔄</span> Refresh
        </button>
      </div>
    </div>

    <!-- Active Protocol / Assignment Selector -->
    <div class="assignment-selector-panel">
      <div class="form-group">
        <label for="assignment-select"
          >Active Training Assignment / Curriculum:</label
        >
        <select
          id="assignment-select"
          v-model="selectedAssignmentId"
          class="select-input"
          @change="onAssignmentChange"
        >
          <option v-for="asn in assignments" :key="asn.id" :value="asn.id">
            [{{ formatClinicalStatus(asn.status) }}]
            {{ asn.curriculum?.title || asn.study_id + " Protocol Training" }} —
            Site {{ asn.site_id }}
          </option>
          <option v-if="assignments.length === 0" value="" disabled>
            No active training assignments found for current user
          </option>
        </select>
      </div>

      <div v-if="curricula.length > 0" class="form-group">
        <label for="curriculum-select">Study Curriculum:</label>
        <select
          id="curriculum-select"
          v-model="selectedCurriculumId"
          class="select-input"
          @change="onCurriculumChange"
        >
          <option v-for="cur in curricula" :key="cur.id" :value="cur.id">
            {{ cur.title }} (v{{ cur.protocol_version }})
          </option>
        </select>
      </div>

      <div class="status-summary-chips">
        <div class="summary-chip">
          <span class="chip-label">Status:</span>
          <span
            class="chip-value"
            :class="
              'status-' + (activeAssignment?.status || 'PENDING').toLowerCase()
            "
          >
            {{ formatClinicalStatus(activeAssignment?.status || "PENDING") }}
          </span>
        </div>
        <div class="summary-chip">
          <span class="chip-label">Protocol Version:</span>
          <span class="chip-value">
            {{ activeCurriculum?.protocol_version || "—" }}
          </span>
        </div>
        <div class="summary-chip">
          <span class="chip-label">Quiz Score:</span>
          <span class="chip-value">
            {{
              activeAssignment?.quiz_score != null
                ? Math.round(activeAssignment.quiz_score * 100) + "%"
                : "Not Recorded"
            }}
          </span>
        </div>
      </div>
    </div>

    <!-- Main Learning & Governance Workspace -->
    <div v-if="activeCurriculum" class="training-workspace">
      <!-- Navigation Tabs: Live .NET routes only (Modules | Batch Assignments | Delegation Bulk Sign-Off) -->
      <div class="tab-bar">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'modules' }"
          @click="activeTab = 'modules'"
        >
          📖 Procedural Modules ({{ activeCurriculum.modules?.length || 0 }})
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'batch' }"
          @click="activeTab = 'batch'"
        >
          👥 Batch Assignments
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'delegations' }"
          @click="
            loadDelegations();
            activeTab = 'delegations';
          "
        >
          ✍️ Delegation Bulk Sign-Off
        </button>
      </div>

      <!-- TAB 1: MODULES -->
      <div v-if="activeTab === 'modules'" class="tab-content modules-layout">
        <div class="module-sidebar">
          <h4 class="sidebar-heading">Curriculum Sections</h4>
          <ul class="module-list">
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <li
              v-for="(mod, idx) in activeCurriculum.modules"
              :key="mod.id"
              class="module-item"
              :class="{ active: selectedModuleIndex === idx }"
              @click="selectModule(idx)"
            >
              <span class="module-num">{{ idx + 1 }}</span>
              <div class="module-meta">
                <span class="module-title">{{ mod.title }}</span>
                <span
                  class="badge badge-sm"
                  :class="
                    'badge-mod-' + (mod.module_type || 'default').toLowerCase()
                  "
                >
                  {{ formatClinicalEnum(mod.module_type) }}
                </span>
              </div>
            </li>
          </ul>
        </div>

        <div class="module-reader">
          <div v-if="currentModule" class="reader-content">
            <div class="reader-header">
              <div class="reader-title-group">
                <span class="badge badge-delta">{{
                  formatClinicalEnum(currentModule.module_type)
                }}</span>
                <h3 class="reader-title">
                  {{ currentModule.title }}
                </h3>
              </div>
              <div class="reader-timer">
                Min Reading Guidance:
                <strong
                  >{{ currentModule.min_reading_time_seconds || 30 }}s</strong
                >
              </div>
            </div>

            <div class="markdown-body">
              <pre class="content-text">{{ currentModule.content_text }}</pre>
            </div>

            <div class="reader-footer">
              <button
                class="btn btn-secondary"
                :disabled="selectedModuleIndex <= 0"
                @click="selectModule(selectedModuleIndex - 1)"
              >
                ⬅️ Previous Section
              </button>
              <button
                v-if="
                  activeCurriculum.modules &&
                  selectedModuleIndex < activeCurriculum.modules.length - 1
                "
                class="btn btn-primary"
                @click="selectModule(selectedModuleIndex + 1)"
              >
                Next Section ➡️
              </button>
            </div>
          </div>
          <div v-else class="reader-content">
            <div class="reader-header">
              <div class="reader-title-group">
                <span class="badge badge-delta"
                  >v{{ activeCurriculum.protocol_version }}</span
                >
                <h3 class="reader-title">
                  {{ activeCurriculum.title }}
                </h3>
              </div>
            </div>
            <div class="markdown-body">
              <pre class="content-text">{{
                activeCurriculum.description ||
                "No procedural modules attached to this curriculum."
              }}</pre>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 2: BATCH ASSIGNMENT WORKSPACE -->
      <div v-if="activeTab === 'batch'" class="tab-content batch-layout">
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
            <div class="filter-col">
              <div class="filter-title">Target Sites (Multi-Select):</div>
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
            </div>

            <div class="filter-col">
              <div class="filter-title">Clinical Roles (Multi-Select):</div>
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
            </div>
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
              <span v-if="computedBatchCount > 200" class="badge badge-error"
                >⚠️ Exceeds 200 Synchronous Limit</span
              >
            </div>
            <button
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
              <span>Execute Batch Assignment 🚀</span>
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
            <button class="btn btn-secondary btn-sm" @click="loadDelegations">
              🔄 Refresh Log
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
              ✍️ Execute Part 11 Bulk Signature
            </button>
          </div>

          <!-- Last Bulk Sign Response Manifest -->
          <div
            v-if="bulkSignResult"
            class="bulk-sign-result alert-banner success-banner"
          >
            <div class="banner-content">
              <span class="banner-icon">✅</span>
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
          <span v-else>Confirm &amp; Deploy Batch 🚀</span>
        </button>
      </template>
    </ClinicalModal>

    <!-- Bulk Delegation Sign-Off Modal -->
    <ClinicalModal
      v-model="showBulkSignModal"
      :teleport="false"
      :dismissible="!submittingBulkSign"
      dialog-class="modal-card"
      title="21 CFR Part 11 Bulk Delegation Log Sign-Off"
    >
      <p class="modal-description">
        Executing bulk electronic sign-off for
        <strong
          >{{ selectedDelegationIds.length }} pending delegation records</strong
        >. This operation generates an immutable SHA-256 digital seal and routes
        signed records to eTMF Zone 5 (Artifact 05.02.04).
      </p>
      <p v-if="bulkSignError" class="badge badge-error" role="alert">
        {{ bulkSignError }}
      </p>
      <div class="form-group">
        <label for="bulk-signer-name">Signer Printed Name:</label>
        <input
          id="bulk-signer-name"
          v-model="bulkSignForm.signerName"
          type="text"
          class="text-input"
          :disabled="submittingBulkSign"
          placeholder="Your full name"
        />
      </div>
      <div class="form-group">
        <label for="bulk-signer-role">Clinical Authority Role:</label>
        <input
          id="bulk-signer-role"
          v-model="bulkSignForm.signerRole"
          type="text"
          class="text-input"
          :disabled="submittingBulkSign"
          placeholder="Principal Investigator"
        />
      </div>
      <div class="form-group">
        <label for="bulk-password">21 CFR Part 11 Password Credential:</label>
        <input
          id="bulk-password"
          v-model="bulkSignForm.password"
          type="password"
          class="text-input"
          :disabled="submittingBulkSign"
          placeholder="Enter password to verify identity"
        />
      </div>
      <div class="form-group">
        <span>Legal Attestation Statement:</span>
        <div class="statement-box">
          "I confirm that I have reviewed and approved the delegated duties for
          the selected site staff personnel in accordance with 21 CFR Part 11
          and ICH GCP E6(R2)."
        </div>
      </div>
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="submittingBulkSign"
          @click="showBulkSignModal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-success"
          :disabled="
            submittingBulkSign ||
            !bulkSignForm.password ||
            !bulkSignForm.signerName
          "
          @click="handleExecuteBulkSignOff"
        >
          <span v-if="submittingBulkSign">Executing Part 11 Signature...</span>
          <span v-else>Confirm &amp; Execute Signature ✍️</span>
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { ClinicalModal } from "ui";
import { computed, onMounted, ref, watch } from "vue";
import { signatureApi } from "../../api/signatureApi";
import { trainingApi } from "../../api/training";
import {
  TRAINING_ASSIGN_ROLE_REASON,
  TRAINING_DELEGATION_SIGN_ROLE_REASON,
  canAssignTrainingCurriculum,
  canSignTrainingDelegations,
} from "../../canvas/authority";
import { useActiveStudyScope } from "../../composables/useActiveStudyScope";
import { useAuthStore } from "../../stores/auth";
import {
  formatClinicalEnum,
  formatClinicalStatus,
  formatRoleLabel,
} from "../../utils/clinicalLabels";
import {
  DELEGATION_SIGN_OFF_ACTION,
  DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
  delegationBatchBindingId,
} from "../../utils/signatureBinding";
import SupportDetails from "../canvas/SupportDetails.vue";

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

const authStore = useAuthStore();
const { activeStudyCode } = useActiveStudyScope();
const effectiveStudyId = computed(
  () => (props.studyId || "").trim() || activeStudyCode.value
);
const canAssignCurriculum = computed(() =>
  canAssignTrainingCurriculum(authStore)
);
const canSignDelegations = computed(() =>
  canSignTrainingDelegations(authStore)
);

const loading = ref(false);
const assignments = ref([]);
const curricula = ref([]);
const selectedAssignmentId = ref("");
const selectedCurriculumId = ref("");
const activeAssignment = ref(null);
const activeCurriculum = ref(null);
const delegations = ref([]);
const activeTab = ref("modules");
const selectedModuleIndex = ref(0);
const isGated = ref(true);

const currentModule = computed(() => {
  if (!activeCurriculum.value?.modules) return null;
  return activeCurriculum.value.modules[selectedModuleIndex.value] || null;
});

async function refreshData() {
  loading.value = true;
  try {
    // The signed-in identity is the only source of who is training (#5400): no invented user stands in for it.
    const userId = authStore.identity?.sub || authStore.identity?.username;
    bulkSignForm.value.signerName = authStore.identity?.username || "";
    bulkSignForm.value.signerRole = formatRoleLabel(authStore.activeRole || "");
    if (!userId) {
      assignments.value = [];
      curricula.value = [];
      activeAssignment.value = null;
      activeCurriculum.value = null;
      return;
    }

    const asnQuery = { user_id: userId };
    if (effectiveStudyId.value) asnQuery.study_id = effectiveStudyId.value;
    if (props.siteId) asnQuery.site_id = props.siteId;

    const asnList = await trainingApi.listAssignments(asnQuery);
    assignments.value = Array.isArray(asnList) ? asnList : [];

    if (assignments.value.length > 0 && !selectedAssignmentId.value) {
      selectedAssignmentId.value = assignments.value[0].id;
    }

    if (selectedAssignmentId.value) {
      await loadAssignmentDetails(selectedAssignmentId.value);
    } else if (effectiveStudyId.value) {
      const curQuery = { study_id: effectiveStudyId.value };
      if (props.protocolVersion) {
        curQuery.protocol_version = props.protocolVersion;
      }
      const curList = await trainingApi.listCurricula(curQuery);
      curricula.value = Array.isArray(curList) ? curList : [];
      if (curricula.value.length > 0) {
        selectedCurriculumId.value = curricula.value[0].id;
        await loadCurriculumDetails(selectedCurriculumId.value);
      }
    }

    await loadDelegations();
  } catch (err) {
    console.error("Failed to load training assignments:", err);
  } finally {
    loading.value = false;
  }
}

async function loadAssignmentDetails(asnId) {
  try {
    const detail = await trainingApi.getAssignmentDetail(asnId);
    activeAssignment.value = detail;
    activeCurriculum.value = detail.curriculum || null;
    isGated.value = detail.status !== "COMPLETED";
  } catch (err) {
    console.error("Error loading assignment detail:", err);
  }
}

async function loadCurriculumDetails(curriculumId) {
  try {
    const detail = await trainingApi.getCurriculum(curriculumId);
    activeCurriculum.value = detail;
  } catch (err) {
    console.error("Error loading curriculum detail:", err);
  }
}

function onAssignmentChange() {
  if (selectedAssignmentId.value) {
    loadAssignmentDetails(selectedAssignmentId.value);
    selectedModuleIndex.value = 0;
  }
}

function onCurriculumChange() {
  if (selectedCurriculumId.value) {
    loadCurriculumDetails(selectedCurriculumId.value);
    selectedModuleIndex.value = 0;
  }
}

function selectModule(index) {
  selectedModuleIndex.value = index;
}

watch(
  () => [effectiveStudyId.value, props.siteId, props.protocolVersion],
  () => {
    selectedAssignmentId.value = "";
    selectedCurriculumId.value = "";
    refreshData();
  }
);

onMounted(() => {
  refreshData();
});

// Batch Curriculum Assignment State. The audience comes from the study's delegation log (#5400): the sites and roles
// offered are the ones staff actually hold, nothing is pre-selected, and nothing is offered without an active curriculum.
const distinctSorted = (values) =>
  [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b));
const availableSites = computed(() =>
  distinctSorted(delegations.value.map((d) => d.site_id))
);
const availableRoles = computed(() =>
  distinctSorted(delegations.value.map((d) => d.role))
);
const batchForm = ref({
  selectedSites: [],
  selectedRoles: [],
  reasonForChange: "",
});
const showBatchModal = ref(false);
const submittingBatch = ref(false);
const batchResult = ref(null);
const batchError = ref("");

// The people a batch would reach: distinct staff per site on the delegation log matching the selected filters.
const computedBatchCount = computed(() => {
  const sites = batchForm.value.selectedSites;
  const roles = batchForm.value.selectedRoles;
  if (sites.length === 0 && roles.length === 0) return 0;
  const reached = new Set(
    delegations.value
      .filter((d) => sites.length === 0 || sites.includes(d.site_id))
      .filter((d) => roles.length === 0 || roles.includes(d.role))
      .map((d) => `${d.staff_user_id}|${d.site_id}`)
  );
  return reached.size;
});

async function handleExecuteBatchAssign() {
  if (!activeCurriculum.value) return;
  submittingBatch.value = true;
  batchError.value = "";
  try {
    const res = await trainingApi.batchAssignCurriculum({
      curriculum_id: activeCurriculum.value.id,
      study_id: activeCurriculum.value.study_id,
      site_ids: batchForm.value.selectedSites,
      roles: batchForm.value.selectedRoles,
      reason_for_change: batchForm.value.reasonForChange,
    });
    batchResult.value = res;
    showBatchModal.value = false;
    await refreshData();
  } catch (err) {
    batchError.value = err?.message || "The batch assignment was refused.";
    console.error("Batch assignment error:", err);
  } finally {
    submittingBatch.value = false;
  }
}

// Bulk Delegation Log Sign-Off State
const selectedDelegationIds = ref([]);
const loadingDelegations = ref(false);
const showBulkSignModal = ref(false);
const submittingBulkSign = ref(false);
const bulkSignResult = ref(null);
const bulkSignError = ref("");
const bulkSignForm = ref({
  signerName: authStore.identity?.username || "",
  signerRole: authStore.activeRole || "",
  password: "",
  reasonForChange:
    "21 CFR Part 11 Principal Investigator bulk delegation sign-off",
});

const isAllDelegationsSelected = computed(() => {
  const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
  return (
    pending.length > 0 && selectedDelegationIds.value.length === pending.length
  );
});

function toggleSelectAllDelegations() {
  const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
  if (selectedDelegationIds.value.length === pending.length) {
    selectedDelegationIds.value = [];
  } else {
    selectedDelegationIds.value = pending.map((d) => d.id);
  }
}

async function loadDelegations() {
  const studyId = effectiveStudyId.value || activeCurriculum.value?.study_id;
  if (!studyId) {
    delegations.value = [];
    return;
  }
  loadingDelegations.value = true;
  try {
    const delQuery = { study_id: studyId };
    if (props.siteId) {
      delQuery.site_id = props.siteId;
    }
    const res = await trainingApi.listDelegations(delQuery);
    delegations.value = Array.isArray(res) ? res : [];
  } catch (err) {
    console.error("Failed to load delegations:", err);
  } finally {
    loadingDelegations.value = false;
  }
}

// The site of the selected delegations when they share one; a sign-off across sites names none.
function singleSite(delegationIds) {
  const sites = new Set(
    delegations.value
      .filter((d) => delegationIds.includes(d.id))
      .map((d) => d.site_id)
  );
  return sites.size === 1 ? [...sites][0] : null;
}

async function handleExecuteBulkSignOff() {
  if (selectedDelegationIds.value.length === 0) return;
  submittingBulkSign.value = true;
  bulkSignError.value = "";
  try {
    const delegationIds = [...selectedDelegationIds.value];
    const verification = await signatureApi.verifySignature({
      username: authStore.identity?.username || "",
      password: bulkSignForm.value.password,
      action: DELEGATION_SIGN_OFF_ACTION,
      semanticAction: DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
      batchId: await delegationBatchBindingId(delegationIds),
    });
    bulkSignForm.value.password = "";
    const res = await trainingApi.bulkSignOffDelegations(
      {
        delegation_ids: delegationIds,
        study_id:
          effectiveStudyId.value || activeCurriculum.value?.study_id || null,
        site_id: singleSite(delegationIds),
        signature_payload: {
          meaning:
            "I confirm that I have reviewed and approved the delegated duties for the selected site staff personnel in accordance with 21 CFR Part 11 and ICH GCP E6(R2).",
          reason_for_change: bulkSignForm.value.reasonForChange,
        },
      },
      verification.sig_token
    );
    bulkSignResult.value = res;
    showBulkSignModal.value = false;
    selectedDelegationIds.value = [];
    await loadDelegations();
  } catch (err) {
    bulkSignError.value = err?.message || "Bulk delegation sign-off failed.";
    console.error("Bulk sign-off error:", err);
  } finally {
    submittingBulkSign.value = false;
  }
}
</script>

<style scoped>
.training-console-card {
  padding: var(--spacing-lg, 24px);
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
  font-family: inherit;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  border-bottom: 1px solid var(--border);
  padding-bottom: var(--spacing-md, 16px);
}

.btn-secondary {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.badge-gxp {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-weight: 700;
}

.badge-regulatory {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.badge-locked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-weight: 700;
}

.badge-unlocked {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  font-weight: 700;
}

.console-title {
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 4px 0;
}

.console-subtitle {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.assignment-selector-panel {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  background: var(--semantic-color-surface-header);
  padding: 12px 16px;
  border-radius: var(--radius-sm, 6px);
  border: 1px solid var(--border);
  flex-wrap: wrap;
}

.select-input {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 4px);
  background: var(--semantic-color-surface);
  font-size: 0.9rem;
  min-width: 320px;
}

.status-summary-chips {
  display: flex;
  gap: 12px;
}

.summary-chip {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  padding: 4px 10px;
  border-radius: 4px;
}

.chip-label {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.chip-value {
  font-size: 0.85rem;
  font-weight: 700;
}

.status-completed {
  color: var(--semantic-color-success);
}

.status-in_progress {
  color: var(--semantic-color-primary);
}

.status-pending {
  color: var(--semantic-color-warning);
}

.status-failed {
  color: var(--semantic-color-error);
}

.tab-bar {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--border);
  margin-bottom: 16px;
}

.tab-btn {
  padding: 10px 18px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  margin-bottom: -2px;
}

.tab-btn.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

.modules-layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 20px;
}

.module-sidebar {
  border-right: 1px solid var(--border);
  padding-right: 16px;
}

.sidebar-heading {
  font-size: 0.9rem;
  text-transform: uppercase;
  color: var(--semantic-color-text-secondary);
  margin-top: 0;
  margin-bottom: 10px;
}

.module-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.module-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.2s;
}

.module-item:hover {
  background: var(--semantic-color-surface-header);
}

.module-item.active {
  background: var(--semantic-color-info-bg);
  border-color: var(--semantic-color-info-border);
}

.module-num {
  font-weight: 700;
  color: var(--primary);
}

.module-meta {
  display: flex;
  flex-direction: column;
}

.module-title {
  font-size: 0.85rem;
  font-weight: 600;
}

.badge-sm {
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-mod-procedure_delta {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-mod-protocol_overview {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.module-reader {
  display: flex;
  flex-direction: column;
}

.reader-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--border);
  padding-bottom: 12px;
  margin-bottom: 16px;
}

.reader-title {
  margin: 4px 0 0 0;
  font-size: 1.15rem;
}

.content-text {
  white-space: pre-wrap;
  font-family: inherit;
  font-size: 0.95rem;
  line-height: 1.6;
  background: var(--semantic-color-surface-muted);
  padding: 16px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
}

.reader-footer {
  display: flex;
  justify-content: space-between;
  margin-top: 20px;
}

/* Quiz Styles */
.quiz-header-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  padding: 16px;
  border-radius: 6px;
  margin-bottom: 20px;
}

.quiz-score-badge {
  text-align: center;
  padding: 10px 20px;
  border-radius: 6px;
}

.score-passed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.score-failed {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
}

.score-number {
  font-size: 1.8rem;
  font-weight: 800;
}

.score-label {
  font-size: 0.8rem;
  font-weight: 700;
}

.quiz-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 16px;
  margin-bottom: 16px;
}

.question-title {
  font-weight: 600;
  font-size: 1rem;
  margin-bottom: 12px;
}

.options-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.option-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}

.option-item.selected {
  background: var(--semantic-color-info-bg);
  border-color: var(--semantic-color-interactive-primary);
}

.option-item.correct {
  background: var(--semantic-color-success-bg);
  border-color: var(--semantic-color-success);
}

.option-item.incorrect {
  background: var(--semantic-color-error-bg);
  border-color: var(--semantic-color-error);
}

.rationale-box {
  margin-top: 10px;
  background: var(--semantic-color-surface-muted);
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.quiz-actions-bar {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
}

/* Certificate Styles */
.certificate-paper {
  background: var(--semantic-color-surface);
  border: 2px solid var(--semantic-color-border-control);
  padding: 32px;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
}

.cert-header {
  text-align: center;
  border-bottom: 2px solid var(--semantic-color-text);
  padding-bottom: 16px;
  margin-bottom: 24px;
}

.cert-brand {
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 1px;
  color: var(--semantic-color-text-secondary);
}

.cert-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 6px 0;
}

.cert-subtitle {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.cert-lead {
  font-size: 1rem;
  line-height: 1.5;
}

.cert-table {
  width: 100%;
  border-collapse: collapse;
  margin: 20px 0;
}

.cert-table td {
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  font-size: 0.9rem;
}

.cert-signature-manifest {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  padding: 14px;
  border-radius: 6px;
  margin-top: 20px;
}

.manifest-header {
  font-weight: 700;
  margin-bottom: 8px;
  font-size: 0.9rem;
}

.manifest-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px 16px;
  font-size: 0.85rem;
}

.cert-routing-status {
  display: flex;
  gap: 12px;
  margin-top: 20px;
}

.routing-badge {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 6px 12px;
  border-radius: 4px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
}

/* Modal */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background: var(--semantic-color-surface);
  width: 520px;
  max-width: 90vw;
  border-radius: 8px;
  padding: 24px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.modal-close-btn {
  background: none;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
}

.modal-description {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
  margin-bottom: 16px;
}

.text-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  margin-top: 4px;
  font-size: 0.9rem;
}

.statement-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  padding: 10px 12px;
  border-radius: 4px;
  font-style: italic;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
  margin-top: 4px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
}

/* Inline Sync Error Banner Styles */
.sync-error-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
  padding: 12px 16px;
  border-radius: var(--radius-sm, 6px);
  gap: 12px;
  margin-bottom: 16px;
}

.banner-content {
  display: flex;
  align-items: center;
  gap: 12px;
}

.banner-icon {
  font-size: 1.25rem;
}

.banner-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.banner-title {
  font-size: 0.9rem;
  font-weight: 700;
}

.banner-message {
  font-size: 0.85rem;
  color: var(--semantic-color-error-text-deep);
}

.btn-retry {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  border: none;
  padding: 6px 14px;
  border-radius: 4px;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
}

.btn-retry:hover:not(:disabled) {
  background-color: var(--semantic-color-error);
}

.btn-retry:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Batch & Delegation Layouts */
.batch-layout,
.delegations-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.batch-card,
.delegations-card {
  padding: 20px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.section-desc {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  margin-top: 4px;
}

.batch-filter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
  background: var(--semantic-color-surface-muted);
  padding: 16px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
}

.filter-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
  margin-bottom: 8px;
  display: block;
}

.checkbox-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
  cursor: pointer;
}

.batch-preview-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  padding: 12px 16px;
  border-radius: 6px;
}

.preview-info {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 0.9rem;
  color: var(--semantic-color-info);
}

.badge-error {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
}

.batch-result-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  padding: 16px;
  border-radius: 6px;
}

.result-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.result-stats {
  display: flex;
  gap: 12px;
  margin-bottom: 12px;
}

.stat-pill {
  padding: 6px 12px;
  border-radius: 4px;
  font-weight: 700;
  font-size: 0.85rem;
}

.success-pill {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}
.warning-pill {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}
.info-pill {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.skipped-table-wrapper {
  margin-top: 12px;
}

.delegation-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.delegation-actions-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--semantic-color-surface-header);
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
}

.seal-hash {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-primary);
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
  font-size: 0.8rem;
}

.badge-role {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.75rem;
}

.data-table th,
.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-border);
  text-align: left;
  font-size: 0.875rem;
}

.alert-banner {
  padding: 12px 16px;
  border-radius: 6px;
  margin-top: 12px;
}

.success-banner {
  background: var(--semantic-color-success-bg);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}
</style>
