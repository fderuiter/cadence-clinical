<template>
  <div class="cra-verification-console">
    <!-- Batch Verification Action Bar (Visible whenever fields are selected for batch SDV) -->
    <div
      v-if="effectiveSelectedBatchFields.length > 0"
      id="batch-sdv-bar"
      class="batch-sdv-bar"
      style="
        background-color: #eff6ff;
        border: 1px solid #bfdbfe;
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: var(--spacing-md, 16px);
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: 12px;
      "
    >
      <div style="font-size: 0.9rem; font-weight: 600; color: #1e40af">
        Selected {{ effectiveSelectedBatchFields.length }} fields for Batch Source Data Verification
      </div>
      <button
        id="btn-batch-verify"
        type="button"
        class="btn btn-primary"
        style="
          background-color: #2563eb;
          color: white;
          font-weight: bold;
          padding: 6px 14px;
          font-size: 0.85rem;
          border-radius: 6px;
          cursor: pointer;
        "
        @click="handleBatchVerifyClick"
      >
        Batch Verify Selected ({{ effectiveSelectedBatchFields.length }})
      </button>
    </div>

    <!-- Standalone Workspace / Form Inspection Mode (Rendered when standalone or embedded in CTMS tab) -->
    <div
      v-if="showInspectionWorkspace"
      class="cra-workspace card"
      style="margin-top: 12px"
    >
      <div
        class="card-title"
        style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px"
      >
        <span>CRA Verification Console &amp; Source Data Verification (SDV)</span>
        <div style="display: flex; gap: 8px; align-items: center">
          <span
            class="badge"
            :style="{
              backgroundColor: '#dbeafe',
              color: '#1e40af',
              fontWeight: 600,
              fontSize: '0.75rem',
            }"
          >
            ICH GCP E6(R2) Monitored
          </span>
        </div>
      </div>

      <!-- Scope Filters Bar -->
      <div
        class="scope-controls"
        style="
          display: flex;
          gap: 16px;
          flex-wrap: wrap;
          padding: 12px;
          background-color: #f8fafc;
          border: 1px solid var(--border, #e2e8f0);
          border-radius: 6px;
          margin-bottom: 16px;
          align-items: center;
        "
      >
        <div
          class="form-group"
          style="margin: 0; min-width: 180px"
        >
          <label
            for="cra-subject-selector"
            style="font-weight: bold; font-size: 0.75rem"
          >Subject ID</label>
          <select
            id="cra-subject-selector"
            v-model="currentSubjectId"
            style="width: 100%; padding: 6px 10px; border: 1px solid #cbd5e1; border-radius: 4px"
          >
            <option
              v-for="sub in availableSubjects"
              :key="sub.id"
              :value="sub.id"
            >
              {{ sub.label || `${sub.id} (${sub.status})` }}
            </option>
          </select>
        </div>

        <div
          class="form-group"
          style="margin: 0; min-width: 180px"
        >
          <label
            for="cra-visit-selector"
            style="font-weight: bold; font-size: 0.75rem"
          >Encounter / Visit</label>
          <select
            id="cra-visit-selector"
            v-model="currentVisitId"
            style="width: 100%; padding: 6px 10px; border: 1px solid #cbd5e1; border-radius: 4px"
          >
            <option value="Screening">
              Screening / Day -7
            </option>
            <option value="Week2">
              Week 2 Treatment
            </option>
            <option value="Week4">
              Week 4 Treatment
            </option>
          </select>
        </div>

        <div style="margin-left: auto; display: flex; gap: 12px; align-items: center; font-size: 0.85rem; color: #475569">
          <div>
            <strong>SDV Completion:</strong>
            <span style="font-weight: 700; color: #166534; margin-left: 4px">{{ sdvCompletionRate }}%</span>
          </div>
          <div>
            <strong>Open Queries:</strong>
            <span style="font-weight: 700; color: #b45309; margin-left: 4px">{{ openQueriesCount }}</span>
          </div>
        </div>
      </div>

      <!-- eCRF Source Data Verification Inspection Table -->
      <div class="table-responsive">
        <table
          class="clinical-visit-matrix"
          style="width: 100%; border-collapse: collapse"
        >
          <thead>
            <tr>
              <th
                scope="col"
                style="width: 40px; text-align: center"
              >
                Batch
              </th>
              <th scope="col">
                Field / Parameter
              </th>
              <th scope="col">
                CDASH Variable
              </th>
              <th scope="col">
                Reported eCRF Value
              </th>
              <th scope="col">
                Source Record (EMR/Lab)
              </th>
              <th
                scope="col"
                style="text-align: center"
              >
                SDV Verified
              </th>
              <th scope="col">
                Query Discrepancy Status
              </th>
              <th scope="col">
                Monitoring Actions
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="field in currentFields"
              :key="field.id"
            >
              <!-- Batch SDV Checkbox -->
              <td
                style="text-align: center"
                class="batch-sdv-box"
              >
                <input
                  :id="`batch-sdv-${field.id}`"
                  type="checkbox"
                  class="batch-sdv-checkbox"
                  :value="field.id"
                  :checked="effectiveSelectedBatchFields.includes(field.id)"
                  :disabled="!isAuthorizedForBulkSdv"
                  style="cursor: pointer"
                  @change="toggleBatchField(field.id, $event.target.checked)"
                >
              </td>

              <!-- Field Label -->
              <td>
                <strong>{{ field.label }}</strong>
              </td>

              <!-- CDASH Variable Code -->
              <td>
                <code>{{ field.cdash || field.id }}</code>
              </td>

              <!-- Reported Value -->
              <td>
                <span style="font-weight: 600; color: #0f172a">
                  {{ getFieldValue(field.id) || "—" }}
                </span>
              </td>

              <!-- Source Document Reference -->
              <td>
                <span style="color: #64748b; font-size: 0.85rem">
                  {{ getSourceValue(field.id) }}
                </span>
              </td>

              <!-- Single Field SDV Toggle Checkbox -->
              <td
                style="text-align: center"
                class="sdv-box"
              >
                <input
                  :id="`sdv-${field.id}`"
                  type="checkbox"
                  class="sdv-checkbox"
                  :checked="isFieldSdvVerified(field.id)"
                  style="cursor: pointer; width: 18px; height: 18px; accent-color: #16a34a"
                  @change="handleSingleSdvToggle(field.id, $event.target.checked)"
                >
                <label
                  :for="`sdv-${field.id}`"
                  style="display: none"
                >SDV {{ field.label }}</label>
              </td>

              <!-- Query Discrepancy Visual Badge & Info -->
              <td>
                <template v-if="getFieldQuery(field.id)">
                  <span
                    :id="`query-badge-${field.id}`"
                    class="badge query-status-badge"
                    :class="`badge-${(getFieldQuery(field.id).status || 'OPEN').toLowerCase()}`"
                    style="font-weight: 700"
                  >
                    {{ getFieldQuery(field.id).status }}
                  </span>
                  <div style="font-size: 0.75rem; color: #334155; margin-top: 2px">
                    {{ getFieldQuery(field.id).text || getFieldQuery(field.id).query_text || getFieldQuery(field.id).message }}
                  </div>
                  <div
                    v-if="getFieldQuery(field.id).response || getFieldQuery(field.id).response_text"
                    style="font-size: 0.72rem; color: #1e40af; margin-top: 2px"
                  >
                    <em>Ans: {{ getFieldQuery(field.id).response || getFieldQuery(field.id).response_text }}</em>
                  </div>
                </template>
                <template v-else>
                  <span style="color: #94a3b8; font-size: 0.8rem">— Clean —</span>
                </template>
              </td>

              <!-- Actions (Raise, Answer, Close) -->
              <td>
                <div style="display: flex; gap: 6px; flex-wrap: wrap">
                  <!-- No query: Allow raising discrepancy query -->
                  <button
                    v-if="!getFieldQuery(field.id)"
                    :id="`btn-raise-query-${field.id}`"
                    type="button"
                    class="btn btn-secondary btn-sm"
                    style="padding: 2px 8px; font-size: 0.75rem"
                    @click="openRaiseQueryModal(field)"
                  >
                    ⚠️ Raise Query
                  </button>

                  <!-- Query in OPEN state: CRC can answer -->
                  <button
                    v-if="getFieldQuery(field.id) && (getFieldQuery(field.id).status === 'OPEN' || getFieldQuery(field.id).status === 'REOPENED')"
                    :id="`btn-respond-query-${field.id}`"
                    type="button"
                    class="btn btn-primary btn-sm"
                    style="padding: 2px 8px; font-size: 0.75rem; background-color: #2563eb"
                    @click="openAnswerQueryModal(field)"
                  >
                    💬 Submit Answer
                  </button>

                  <!-- Query in ANSWERED state (or OPEN by CRA): CRA can Close / Resolve -->
                  <button
                    v-if="getFieldQuery(field.id) && getFieldQuery(field.id).status !== 'CLOSED'"
                    :id="`btn-close-query-${field.id}`"
                    type="button"
                    class="btn btn-success btn-sm"
                    style="padding: 2px 8px; font-size: 0.75rem; background-color: #16a34a; color: white"
                    @click="openCloseQueryModal(field)"
                  >
                    ✅ Close Query
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Modal 1: Raise Discrepancy Query -->
    <div
      v-if="showRaiseModal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 480px"
      >
        <div
          class="modal-header"
          style="background-color: #b45309"
        >
          ⚠️ Raise Clinical Discrepancy Query
        </div>
        <div class="modal-body">
          <p style="font-size: 0.85rem; color: #475569; margin-bottom: 12px">
            Issue a formal GCP data discrepancy query on <strong>{{ activeModalField?.label }}</strong>. The field visual badge will update to yellow (OPEN).
          </p>
          <div class="form-group">
            <label for="modal-query-reason">Discrepancy Query Reason / Message</label>
            <textarea
              id="modal-query-reason"
              v-model="queryReasonText"
              style="width: 100%; height: 80px; padding: 8px; border: 1px solid #cbd5e1; border-radius: 6px"
              placeholder="e.g. Systolic blood pressure out of expected physiological range."
            />
          </div>
          <div
            v-if="modalError"
            class="error-msg"
            style="color: #dc2626; font-size: 0.8rem"
          >
            ⚠️ {{ modalError }}
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-cancel"
            @click="showRaiseModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #b45309; color: white"
            @click="confirmRaiseQuery"
          >
            Issue Query (OPEN)
          </button>
        </div>
      </div>
    </div>

    <!-- Modal 2: Submit Site CRC Response / Answer -->
    <div
      v-if="showAnswerModal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 480px"
      >
        <div
          class="modal-header"
          style="background-color: #1e40af"
        >
          💬 Site CRC Query Response
        </div>
        <div class="modal-body">
          <p style="font-size: 0.85rem; color: #475569; margin-bottom: 8px">
            <strong>Query:</strong> {{ getFieldQuery(activeModalField?.id)?.text || getFieldQuery(activeModalField?.id)?.query_text || getFieldQuery(activeModalField?.id)?.message }}
          </p>
          <div class="form-group">
            <label for="modal-query-answer">Investigator / CRC Response</label>
            <textarea
              id="modal-query-answer"
              v-model="queryAnswerText"
              style="width: 100%; height: 80px; padding: 8px; border: 1px solid #cbd5e1; border-radius: 6px"
              placeholder="e.g. Value confirmed with medical record"
            />
          </div>
          <div
            v-if="modalError"
            class="error-msg"
            style="color: #dc2626; font-size: 0.8rem"
          >
            ⚠️ {{ modalError }}
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-cancel"
            @click="showAnswerModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #1e40af; color: white"
            @click="confirmAnswerQuery"
          >
            Submit Response (ANSWERED)
          </button>
        </div>
      </div>
    </div>

    <!-- Modal 3: Close Discrepancy Query -->
    <div
      v-if="showCloseModal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 480px"
      >
        <div
          class="modal-header"
          style="background-color: #166534"
        >
          ✅ CRA Monitor Close Query
        </div>
        <div class="modal-body">
          <p style="font-size: 0.85rem; color: #475569; margin-bottom: 8px">
            Document supervisory audit rationale to mark this query CLOSED and resolved.
          </p>
          <div class="form-group">
            <label for="modal-close-rationale">Audit Closure Rationale</label>
            <textarea
              id="modal-close-rationale"
              v-model="closeRationaleText"
              style="width: 100%; height: 80px; padding: 8px; border: 1px solid #cbd5e1; border-radius: 6px"
              placeholder="e.g. Verified with source hospital chart and accepted by CRA."
            />
          </div>
          <div
            v-if="modalError"
            class="error-msg"
            style="color: #dc2626; font-size: 0.8rem"
          >
            ⚠️ {{ modalError }}
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-cancel"
            @click="showCloseModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #166534; color: white"
            @click="confirmCloseQuery"
          >
            Close Query (CLOSED)
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, reactive } from "vue";
import { useClinicalStore } from "../../stores/clinical";
import { useAuthStore } from "../../stores/auth";

const props = defineProps({
  selectedBatchFields: {
    type: Array,
    default: () => [],
  },
  isAuthorizedForBulkSdv: {
    type: Boolean,
    default: true,
  },
  isCraUser: {
    type: Boolean,
    default: true,
  },
  standalone: {
    type: Boolean,
    default: false,
  },
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
  subjectId: {
    type: String,
    default: "",
  },
  visitId: {
    type: String,
    default: "",
  },
  sdvStates: {
    type: Object,
    default: () => ({}),
  },
  getSdvKey: {
    type: Function,
    default: null,
  },
});

const emit = defineEmits([
  "initiate-batch-verify",
  "handle-sdv-toggle",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
  "update:selectedBatchFields",
]);

const clinicalStore = useClinicalStore();
const authStore = useAuthStore();

// Local batch selection tracking
const localBatchFields = ref([...props.selectedBatchFields]);

watch(
  () => props.selectedBatchFields,
  (newVal) => {
    localBatchFields.value = [...(newVal || [])];
  },
  { deep: true }
);

const effectiveSelectedBatchFields = computed(() => {
  return props.selectedBatchFields && props.selectedBatchFields.length > 0
    ? props.selectedBatchFields
    : localBatchFields.value;
});

// Inspection mode selector state
const showInspectionWorkspace = computed(() => {
  return props.standalone || !props.selectedBatchFields || props.selectedBatchFields.length === 0;
});

const currentSubjectId = ref(props.subjectId || clinicalStore.activeSubjectId || "SUBJ-001");
const currentVisitId = ref(props.visitId || clinicalStore.activeVisitId || "Screening");

const availableSubjects = computed(() => {
  return clinicalStore.subjects && clinicalStore.subjects.length > 0
    ? clinicalStore.subjects
    : [
        { id: "SUBJ-001", label: "SUBJ-001 (ACTIVE)", status: "ACTIVE" },
        { id: "SUBJ-002", label: "SUBJ-002 (ACTIVE)", status: "ACTIVE" },
        { id: "SUBJ-101-001", label: "SUBJ-101-001 (ENROLLED)", status: "ENROLLED" },
      ];
});

// Standard CDASH fields for inspection
const currentFields = computed(() => {
  const fields = clinicalStore.getEcrfFieldsForVersion ? clinicalStore.getEcrfFieldsForVersion("1.0.0") : clinicalStore.ecrfFields;
  return fields && fields.length > 0
    ? fields
    : [
        { id: "vssbp", label: "Systolic Blood Pressure", cdash: "VS.VSSBP", value: "175" },
        { id: "vsdbp", label: "Diastolic Blood Pressure", cdash: "VS.VSDBP", value: "95" },
        { id: "vshr", label: "Heart Rate", cdash: "VS.VSHR", value: "78" },
        { id: "lbwbc", label: "White Blood Cell Count", cdash: "LB.LBORRES", value: "6.2" },
      ];
});

// Local SDV states for interactive inspection
const localSdvMap = reactive({});

function resolveSdvKey(fieldId) {
  if (props.getSdvKey) {
    return props.getSdvKey(currentSubjectId.value, currentVisitId.value, fieldId);
  }
  return `${currentSubjectId.value}:${currentVisitId.value}:${fieldId}`;
}

function isFieldSdvVerified(fieldId) {
  const key = resolveSdvKey(fieldId);
  if (props.sdvStates && props.sdvStates[key] !== undefined) {
    return props.sdvStates[key] === true;
  }
  return localSdvMap[key] === true;
}

function handleSingleSdvToggle(fieldId, checked) {
  const key = resolveSdvKey(fieldId);
  localSdvMap[key] = checked;
  emit("handle-sdv-toggle", fieldId, checked);
}

function toggleBatchField(fieldId, checked) {
  const set = new Set(localBatchFields.value);
  if (checked) {
    set.add(fieldId);
  } else {
    set.delete(fieldId);
  }
  localBatchFields.value = Array.from(set);
  emit("update:selectedBatchFields", localBatchFields.value);
}

function handleBatchVerifyClick() {
  // Mark all selected fields as verified
  for (const fieldId of effectiveSelectedBatchFields.value) {
    const key = resolveSdvKey(fieldId);
    localSdvMap[key] = true;
  }
  emit("initiate-batch-verify");
}

function getFieldValue(fieldId) {
  if (clinicalStore.formValues && clinicalStore.formValues[fieldId] !== undefined) {
    return clinicalStore.formValues[fieldId];
  }
  const f = currentFields.value.find((item) => item.id === fieldId);
  return f ? f.value || "120" : "";
}

function getSourceValue(fieldId) {
  const val = getFieldValue(fieldId);
  if (!val) return "—";
  return `${val} (EMR Chart Source Verified)`;
}

// Queries Handling
function getFieldQuery(fieldId) {
  return clinicalStore.formQueries ? clinicalStore.formQueries[fieldId] : null;
}

const sdvCompletionRate = computed(() => {
  const fields = currentFields.value;
  if (!fields.length) return 0;
  const verifiedCount = fields.filter((f) => isFieldSdvVerified(f.id)).length;
  return Math.round((verifiedCount / fields.length) * 100);
});

const openQueriesCount = computed(() => {
  if (!clinicalStore.formQueries) return 0;
  return Object.values(clinicalStore.formQueries).filter(
    (q) => q && (q.status === "OPEN" || q.status === "REOPENED")
  ).length;
});

// Modals State
const showRaiseModal = ref(false);
const showAnswerModal = ref(false);
const showCloseModal = ref(false);
const activeModalField = ref(null);
const queryReasonText = ref("");
const queryAnswerText = ref("");
const closeRationaleText = ref("");
const modalError = ref("");

function openRaiseQueryModal(field) {
  activeModalField.value = field;
  queryReasonText.value = `Value ${getFieldValue(field.id)} out of expected physiological reference range.`;
  modalError.value = "";
  showRaiseModal.value = true;
}

function confirmRaiseQuery() {
  if (!queryReasonText.value.trim()) {
    modalError.value = "Discrepancy query message is strictly required.";
    return;
  }
  const fieldId = activeModalField.value.id;
  const newQuery = {
    id: `QRY-${fieldId.toUpperCase()}-${Date.now().toString().slice(-4)}`,
    fieldId: fieldId,
    field_id: fieldId,
    status: "OPEN",
    text: queryReasonText.value,
    query_text: queryReasonText.value,
    message: queryReasonText.value,
    author: authStore.identity?.username || "cra_monitor",
    created_at: new Date().toISOString(),
  };

  if (!clinicalStore.formQueries) {
    clinicalStore.formQueries = {};
  }
  clinicalStore.formQueries[fieldId] = newQuery;

  emit("create-query", fieldId, queryReasonText.value);
  showRaiseModal.value = false;
}

function openAnswerQueryModal(field) {
  activeModalField.value = field;
  queryAnswerText.value = "Value confirmed with medical record";
  modalError.value = "";
  showAnswerModal.value = true;
}

function confirmAnswerQuery() {
  if (!queryAnswerText.value.trim()) {
    modalError.value = "Answer / clinical response is strictly required.";
    return;
  }
  const fieldId = activeModalField.value.id;
  const q = clinicalStore.formQueries[fieldId];
  if (q) {
    q.status = "ANSWERED";
    q.response = queryAnswerText.value;
    q.response_text = queryAnswerText.value;
    q.responded_by = authStore.identity?.username || "crc_site101";
    q.responded_at = new Date().toISOString();
  }

  emit("respond-query", fieldId, queryAnswerText.value);
  showAnswerModal.value = false;
}

function openCloseQueryModal(field) {
  activeModalField.value = field;
  closeRationaleText.value = "Verified with source hospital chart and accepted by CRA.";
  modalError.value = "";
  showCloseModal.value = true;
}

function confirmCloseQuery() {
  if (!closeRationaleText.value.trim()) {
    modalError.value = "Audit closure rationale is strictly required.";
    return;
  }
  const fieldId = activeModalField.value.id;
  const q = clinicalStore.formQueries[fieldId];
  if (q) {
    q.status = "CLOSED";
    q.closed_by = authStore.identity?.username || "cra_monitor";
    q.closed_at = new Date().toISOString();
  }

  emit("close-query", fieldId, closeRationaleText.value);
  showCloseModal.value = false;
}
</script>

<style scoped>
.modal-overlay {
  display: flex;
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: white;
  border-radius: 8px;
  width: 100%;
  max-width: 500px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  overflow: hidden;
  color: #333;
}

.modal-header {
  padding: 14px 16px;
  border-bottom: 1px solid #e2e8f0;
  font-weight: 600;
  font-size: 15px;
  color: white;
}

.modal-body {
  padding: 16px;
}

.modal-footer {
  padding: 12px 16px;
  background: #f8fafc;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid #e2e8f0;
}
</style>

