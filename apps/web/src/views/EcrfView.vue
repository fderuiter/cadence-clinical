<template>
  <div id="section-ecrf" class="dashboard-section active">
    <div class="section-header">
      <h2>eCRF Runtime Renderer</h2>
      <p>
        Live, dynamic data entry form populated from CDASH metadata with
        client-side field validation and real-time query management.
      </p>
    </div>

    <!-- Active User Role Badge & CRA Mode Toggle -->
    <div
      style="
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--spacing-sm);
        background-color: #f8fafc;
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: 20px;
      "
    >
      <div style="font-size: 0.9rem">
        Active Role:
        <span
          class="badge"
          style="
            background-color: var(--accent);
            color: white;
            padding: 4px 8px;
            border-radius: 4px;
            font-weight: bold;
          "
          >{{ activeUserRole.toUpperCase() }}</span
        >
      </div>

      <!-- Demo role tester select -->
      <div
        style="
          display: flex;
          gap: var(--spacing-xs);
          align-items: center;
          flex-wrap: wrap;
        "
      >
        <label
          for="role-tester-select"
          style="font-size: 0.8rem; font-weight: bold"
          >Demo Role Toggle:</label
        >
        <select
          id="role-tester-select"
          v-model="demoRole"
          style="
            padding: 4px 8px;
            border-radius: 4px;
            border: 1px solid var(--border);
          "
        >
          <option value="site_investigator">Site Coordinator / CRC</option>
          <option value="cra">CRA Monitor (SDV Enabled)</option>
        </select>
      </div>
    </div>

    <div
      class="ecrf-workspace-layout"
      style="display: flex; gap: 24px; align-items: flex-start; flex-wrap: wrap"
    >
      <!-- Left Column: CRC Persona Component & CRA Verification Bar Slot -->
      <div class="ecrf-form-column" style="flex: 1 1 580px; min-width: 0">
        <CrcFormRenderer
          v-model:selected-subject-id="selectedSubjectId"
          v-model:selected-visit-id="selectedVisitId"
          v-model:show-econsent-modal="showEconsentModal"
          v-model:show-paper-icf-modal="showPaperIcfModal"
          v-model:econsent-signer-name="econsentSignerName"
          v-model:paper-icf-date="paperIcfDate"
          v-model:paper-icf-note="paperIcfNote"
          v-model:selected-batch-fields="selectedBatchFields"
          :store="store"
          :is-reconsent-gated="isReconsentGated"
          :reconsent-submitting="reconsentSubmitting"
          :lookup-statuses="lookupStatuses"
          :get-validation-error="getValidationError"
          :is-cra-user="isCraUser"
          :is-authorized-for-bulk-sdv="isAuthorizedForBulkSdv"
          :sdv-states="sdvStates"
          :get-sdv-key="getSdvKey"
          @load-ecrf-session="loadEcrfSession"
          @open-econsent-modal="openEconsentModal"
          @open-paper-icf-modal="openPaperIcfModal"
          @handle-complete-reconsent="
            (method) => handleCompleteReconsent(method, store)
          "
          @handle-lookup-input="handleLookupInput"
          @handle-field-change="handleFieldChange"
          @create-query="createQuery"
          @respond-query="respondQuery"
          @close-query="closeQuery"
          @reopen-query="reopenQuery"
          @handle-sdv-toggle="onSdvToggle"
          @clear-form="clearForm"
          @submit-ecrf="submitEcrf"
        >
          <template #batch-sdv-bar>
            <!-- CRA Persona Component: Batch SDV Action Bar -->
            <CraVerificationConsole
              :selected-batch-fields="selectedBatchFields"
              :is-authorized-for-bulk-sdv="isAuthorizedForBulkSdv"
              @initiate-batch-verify="initiateBatchVerify"
            />
          </template>
        </CrcFormRenderer>
      </div>

      <!-- Right Column: PI Persona Component & CDASH Metadata Tools -->
      <div
        class="ecrf-side-column"
        style="
          flex: 1 1 380px;
          min-width: 0;
          display: flex;
          flex-direction: column;
          gap: 20px;
        "
      >
        <!-- PI Persona Component: Signature Worklist & Re-authentication Drawer/Modal -->
        <!-- prettier-ignore -->
        <PiSignatureDrawer v-model:signoff-target-type="signoffTargetType" v-model:signoff-target-id="signoffTargetId" v-model:custom-target-id="customTargetId" v-model:signoff-reason="signoffReason" v-model:reauth-username="reauthUsername" v-model:reauth-password="reauthPassword" v-model:reauth-totp="reauthTotp" v-model:simulate-delay="simulateDelay" :available-subjects="availableSubjects" :available-visits="availableVisits" :available-form-submissions="availableFormSubmissions" :valid-signing-reasons="validSigningReasons" :show-reauth-modal="showReauthModal" :reauth-error="reauthError" @submit-signoff="handleSignOffSubmit" @cancel-reauth="cancelReauth" @confirm-reauth="confirmReauth"/><!-- pragma: allowlist secret -->

        <!-- Live Form State & CDASH Meta Card -->
        <div
          class="card"
          style="display: flex; flex-direction: column; gap: 16px"
        >
          <div>
            <div class="card-title">CDASH Metadata Specification</div>
            <p style="font-size: 0.85rem; color: #475569; margin-bottom: 8px">
              The fields on the left are dynamically rendered using structural
              CDASH metadata tags (e.g. <code>DM.BRTHDT</code>,
              <code>VS.VSSBP</code>).
            </p>
          </div>

          <div
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              background-color: #f8fafc;
            "
          >
            <h3
              style="
                font-size: 0.9rem;
                font-weight: 700;
                margin-bottom: 8px;
                color: var(--primary);
              "
            >
              Real-time Field Validation Rules:
            </h3>
            <ul
              style="
                font-size: 0.8rem;
                padding-left: 20px;
                color: #475569;
                display: flex;
                flex-direction: column;
                gap: 6px;
              "
            >
              <li>
                <strong>Birth Date:</strong> Must match YYYY-MM-DD pattern.
              </li>
              <li>
                <strong>Systolic BP:</strong> Numeric value between 50 and 250
                mmHg.
              </li>
              <li>
                <strong>Diastolic BP:</strong> Numeric value between 30 and 150
                mmHg.
              </li>
              <li>
                <strong>Pulse Rate:</strong> Numeric value between 30 and 200
                bpm.
              </li>
            </ul>
          </div>

          <div
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              background-color: #f8fafc;
            "
          >
            <h3
              style="
                font-size: 0.9rem;
                font-weight: 700;
                margin-bottom: 8px;
                color: var(--primary);
              "
            >
              Query Management Actions:
            </h3>
            <p style="font-size: 0.8rem; color: #475569; line-height: 1.4">
              Click the 💬 / ⚠️ flags next to input fields to raise, answer,
              close, or reopen discrepancy notes. All query transitions are
              audit-logged in real-time.
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- Full-Width Bottom Section: Study Designer Persona Component -->
    <div style="margin-top: 24px">
      <DesignerSchemaPanel
        v-model:edit-item-value="editItemValue"
        v-model:edit-item-reason="editItemReason"
        v-model:reject-item-reason="rejectItemReason"
        v-model:promote-change-reason="promoteChangeReason"
        :store="store"
        :selected-file-name="selectedFileName"
        :editing-item-id="editingItemId"
        :rejecting-item-id="rejectingItemId"
        :unreviewed-count="unreviewedCount"
        :get-confidence-class="getConfidenceClass"
        :get-status-class="getStatusClass"
        @trigger-file-select="triggerFileSelect"
        @trigger-document-upload="triggerDocumentUpload"
        @accept-item="acceptItem"
        @start-edit-item="startEditItem"
        @cancel-edit-item="cancelEditItem"
        @save-edit-item="saveEditItem"
        @start-reject-item="startRejectItem"
        @cancel-reject-item="cancelRejectItem"
        @confirm-reject-item="confirmRejectItem"
        @promote-candidate="promoteCandidate"
      />
    </div>

    <!-- Reason for Change Modal Dialog -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data field."
      :options="ecrfReasonOptions"
      default-option="Initial Entry"
      @confirm="saveChange"
      @cancel="cancelChange"
    />

    <!-- Conflict Resolution Modal Dialog -->
    <ConflictResolutionModal
      :show="showConflictModal"
      :conflict="activeConflict"
      @confirm="handleResolveConflict"
      @cancel="handleCancelConflict"
    />
  </div>
</template>

<script setup>
import { ref, reactive, watch, onMounted, computed } from "vue";
import { useRoute } from "vue-router";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { soaClient } from "../api/soaClient";
import { validateField, debounce } from "ui";
import { evaluateAST } from "../evaluator.js";
import { terminologyClient } from "../api/terminologyClient";
import ReasonModal from "../components/ReasonModal.vue";
import ConflictResolutionModal from "../components/ConflictResolutionModal.vue";
import { useSyncStore } from "../stores/sync";
import { ClientSyncEngine } from "../utils/syncEngine";

// Import Persona Sub-Components
import CrcFormRenderer from "../components/persona/CrcFormRenderer.vue";
import CraVerificationConsole from "../components/persona/CraVerificationConsole.vue";
import PiSignatureDrawer from "../components/persona/PiSignatureDrawer.vue";
import DesignerSchemaPanel from "../components/persona/DesignerSchemaPanel.vue";

// Import Domain Composables
import { useConsentGating } from "../composables/useConsentGating";
import { useVerification } from "../composables/useVerification";
import { usePiSignoff } from "../composables/usePiSignoff";
import { useSchemaIngestion } from "../composables/useSchemaIngestion";

const store = useClinicalStore();
const authStore = useAuthStore();
const route = useRoute();

// Selected Subject & Visit State
const selectedSubjectId = ref("SUBJ-001");
const selectedVisitId = ref("Screening");

// Demo Role Toggle
const demoRole = ref("site_investigator");

const activeUserRole = computed(() => {
  if (authStore.isAuthenticated) {
    const roles = authStore.normalizedRoles || [];
    if (roles.includes("cra") || roles.includes("monitor")) return "cra";
    if (roles.includes("site_investigator") || roles.includes("crc"))
      return "site_investigator";
    return roles[0] || "site_investigator";
  }
  return demoRole.value;
});

// Domain Composable 1: Consent Gating (CRC)
const {
  isReconsentGated,
  showEconsentModal,
  showPaperIcfModal,
  reconsentSubmitting,
  econsentSignerName,
  paperIcfDate,
  paperIcfNote,
  openEconsentModal,
  openPaperIcfModal,
  handleCompleteReconsent,
} = useConsentGating(selectedSubjectId);

// Domain Composable 2: Verification & SDV (CRA)
const {
  sdvStates,
  selectedBatchFields,
  pendingSdvToggle,
  isCraUser,
  isAuthorizedForBulkSdv,
  handleSdvToggle: composableHandleSdvToggle,
  handleVerificationInvalidationOnEdit,
} = useVerification(activeUserRole);

function getSdvKey(fieldId) {
  return `${selectedSubjectId.value}:${selectedVisitId.value}:${fieldId}`;
}

function onSdvToggle(fieldId, checked) {
  composableHandleSdvToggle(fieldId, checked, () => {
    showReasonModal.value = true;
  });
}

function initiateBatchVerify() {
  if (selectedBatchFields.value.length === 0) {
    alert("No fields selected for batch verification!");
    return;
  }
  reauthAction.value = "BULK_SDV";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

// Domain Composable 3: PI Sign-Off Worklist & Re-authentication (PI)
const {
  signoffTargetType,
  signoffTargetId,
  customTargetId,
  signoffReason,
  availableSubjects,
  availableVisits,
  availableFormSubmissions,
  validSigningReasons,
  showReauthModal,
  reauthUsername,
  reauthPassword,
  reauthTotp,
  reauthError,
  reauthAction,
  pendingCloseQueryFieldId,
  simulateDelay,
  handleSignOffSubmit: composableHandleSignOffSubmit,
  cancelReauth,
} = usePiSignoff(store, authStore);

function handleSignOffSubmit() {
  composableHandleSignOffSubmit(
    (config) => {
      reauthAction.value = config.action;
      reauthUsername.value = config.username;
      reauthPassword.value = "";
      reauthTotp.value = "";
      reauthError.value = "";
      showReauthModal.value = true;
    },
    store.user.username || authStore.identity?.username || "fderuiter"
  );
}

// Domain Composable 4: Protocol Schema Ingestion & Review (Study Designer)
const {
  selectedFileName,
  editingItemId,
  editItemValue,
  editItemReason,
  rejectingItemId,
  rejectItemReason,
  promoteChangeReason,
  unreviewedCount,
  triggerFileSelect,
  triggerDocumentUpload,
  getConfidenceClass,
  getStatusClass,
  acceptItem,
  startEditItem,
  cancelEditItem,
  saveEditItem,
  startRejectItem,
  cancelRejectItem,
  confirmRejectItem,
  promoteCandidate,
} = useSchemaIngestion(store);

// Reason for Change Modal States
const ecrfReasonOptions = [
  { value: "Initial Entry", text: "Initial Data Entry" },
  { value: "Typographical Error", text: "Correction of typographical error" },
  { value: "Re-measurement", text: "Re-measurement of vitals" },
  { value: "Transcription Error", text: "Correction of transcription error" },
  { value: "Other", text: "Other (specify below)" },
];
const showReasonModal = ref(false);
const pendingValueChange = ref(null);

// Sync Store & Conflict Resolution
const syncStore = useSyncStore();
const syncEngine = new ClientSyncEngine();

if (typeof window !== "undefined") {
  window.syncStore = syncStore;
  window.syncEngine = syncEngine;
}

const showConflictModal = computed(
  () => syncStore.status === "CONFLICT_DETECTED"
);
const activeConflict = computed(() => syncStore.conflict);

async function handleResolveConflict({ strategy, reason }) {
  if (activeConflict.value && activeConflict.value.conflictItem) {
    await syncEngine.resolveConflict(
      activeConflict.value.conflictItem.deltaId,
      strategy,
      reason
    );
  }
}

function handleCancelConflict() {
  syncStore.setStatus("IDLE");
  syncStore.clearConflict();
}

// Consolidated lookup validation and state management
const lookupStatuses = ref({});
const lookupRequestCounters = reactive({});
const debouncedLookups = {};

async function performConceptCodeValidation(fieldId, value) {
  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  const nextRequestId = (lookupRequestCounters[fieldId] || 0) + 1;
  lookupRequestCounters[fieldId] = nextRequestId;

  lookupStatuses.value[fieldId] = {
    status: "loading",
    message: "Searching terminology database...",
  };

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      changeReason: "Validate code",
    });

    if (nextRequestId !== lookupRequestCounters[fieldId]) return;

    if (res.state === "VALID") {
      lookupStatuses.value[fieldId] = {
        status: "valid",
        message: `Code is valid: "${res.decode}"`,
      };
    } else if (res.state === "INVALID") {
      lookupStatuses.value[fieldId] = {
        status: "invalid",
        message: `Invalid code "${value}". Not found in NCI Thesaurus.`,
      };
    } else if (res.state === "DEGRADED") {
      lookupStatuses.value[fieldId] = {
        status: "degraded",
        message:
          res.error_message ||
          "Terminology service degraded. Validation offline.",
      };
    }
  } catch (error) {
    if (nextRequestId !== lookupRequestCounters[fieldId]) return;
    lookupStatuses.value[fieldId] = {
      status: "degraded",
      message:
        error.message || "Terminology service degraded. Validation offline.",
    };
  }
}

function getDebouncedLookup(fieldId) {
  if (!debouncedLookups[fieldId]) {
    debouncedLookups[fieldId] = debounce(async (value) => {
      await performConceptCodeValidation(fieldId, value);
    }, 300);
  }
  return debouncedLookups[fieldId];
}

function handleLookupInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  getDebouncedLookup(fieldId)(value);
}

// eCRF Session Management
const ecrfSessions = reactive({});

function getSessionKey() {
  return `${selectedSubjectId.value}:${selectedVisitId.value}`;
}

function loadEcrfSession() {
  const key = getSessionKey();
  if (!ecrfSessions[key]) {
    ecrfSessions[key] = {
      values: {},
      queries: {},
    };
    store.ecrfFields.forEach((f) => {
      ecrfSessions[key].values[f.id] = "";
    });
  }

  store.formValues = ecrfSessions[key].values;
  store.formQueries = ecrfSessions[key].queries;

  store.evaluateRules();

  store.ecrfFields.forEach((field) => {
    if (field.type === "concept_code" && store.formValues[field.id]) {
      performConceptCodeValidation(field.id, store.formValues[field.id]);
    }
  });
}

watch(
  () => store.formValues,
  (newValues) => {
    const key = getSessionKey();
    if (!ecrfSessions[key]) {
      ecrfSessions[key] = { values: {}, queries: {} };
    }
    ecrfSessions[key].values = newValues;
  },
  { deep: true }
);

watch(
  () => store.formQueries,
  (newQueries) => {
    const key = getSessionKey();
    if (!ecrfSessions[key]) {
      ecrfSessions[key] = { values: {}, queries: {} };
    }
    ecrfSessions[key].queries = newQueries;
  },
  { deep: true }
);

watch(
  () => store.formValues,
  () => {
    store.triggerValueChange();
  },
  { deep: true }
);

onMounted(() => {
  if (route && route.query) {
    if (route.query.studyId) store.activeStudyId = route.query.studyId;
    if (route.query.siteId) store.activeSiteId = route.query.siteId;
    if (route.query.subjectId) {
      const sId = String(route.query.subjectId);
      store.activeSubjectId = sId;
      selectedSubjectId.value = sId;
    }
    if (route.query.visitId) {
      store.activeVisitId = route.query.visitId;
      const vId = String(route.query.visitId);
      if (
        vId.toLowerCase().includes("week2") ||
        vId.toLowerCase().includes("week 2")
      )
        selectedVisitId.value = "Week2";
      else if (
        vId.toLowerCase().includes("week4") ||
        vId.toLowerCase().includes("week 4")
      )
        selectedVisitId.value = "Week4";
      else selectedVisitId.value = "Screening";
    }
  }
  loadEcrfSession();
});


function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues, evaluateAST);
  return res.valid ? null : res.message;
}

// Reason Modal & Field Changes
function handleFieldChange(field, newValue, targetEl) {
  const oldValue = store.formValues[field.id] || "";
  if (newValue === oldValue) return;

  if (oldValue !== "" && oldValue !== null && oldValue !== undefined) {
    pendingValueChange.value = {
      field,
      oldValue,
      newValue,
      targetEl,
    };
    showReasonModal.value = true;
  } else {
    commitChange(field, oldValue, newValue, "Initial Entry");
  }
}

function cancelChange() {
  if (pendingValueChange.value && pendingValueChange.value.targetEl) {
    if (pendingValueChange.value.targetEl.type === "radio") {
      // Vue handles radio binding automatically
    } else {
      pendingValueChange.value.targetEl.value =
        pendingValueChange.value.oldValue;
    }
  }
  showReasonModal.value = false;
  pendingValueChange.value = null;
  pendingSdvToggle.value = null;
}

function saveChange(finalReason) {
  if (pendingSdvToggle.value) {
    const { fieldId, checked } = pendingSdvToggle.value;
    const sKey = getSdvKey(fieldId);
    sdvStates[sKey] = checked;

    store.addLedgerBlock(
      "SDV_TOGGLE",
      {
        subjectId: selectedSubjectId.value,
        visitId: selectedVisitId.value,
        fieldId,
        is_sdv_verified: checked,
      },
      finalReason
    );

    pendingSdvToggle.value = null;
    showReasonModal.value = false;
    alert(`Source Document Verification (SDV) status updated and logged!`);
    return;
  }

  if (!pendingValueChange.value) return;

  commitChange(
    pendingValueChange.value.field,
    pendingValueChange.value.oldValue,
    pendingValueChange.value.newValue,
    finalReason
  );

  showReasonModal.value = false;
  pendingValueChange.value = null;
}

function commitChange(field, oldValue, newValue, reason) {
  store.formValues[field.id] = newValue;
  handleVerificationInvalidationOnEdit(
    field,
    oldValue,
    newValue,
    selectedSubjectId.value,
    selectedVisitId.value,
    store
  );
  store.addLedgerBlock(
    "FIELD_CHANGE",
    {
      fieldId: field.id,
      label: field.label,
      cdash: field.cdash || "",
      oldValue,
      newValue,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    reason
  );
}

// Query Operations
function createQuery(fieldId, msgFromComponent = null) {
  const msg = msgFromComponent !== null ? msgFromComponent : "";
  if (!msg) {
    alert("Please enter a discrepancy message!");
    return;
  }

  const queryObj = {
    status: "OPEN",
    message: msg,
    createdBy: `${activeUserRole.value} (Client Monitor)`,
    createdAt: new Date().toISOString().slice(0, 10),
  };

  store.formQueries[fieldId] = queryObj;
  store.addLedgerBlock(
    "QUERY_CREATE",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    `Raised discrepancy: "${msg}"`
  );
}

function respondQuery(fieldId, respFromComponent = null) {
  const resp = respFromComponent !== null ? respFromComponent : "";
  if (!resp) {
    alert("Please enter a response!");
    return;
  }

  const queryObj = store.formQueries[fieldId];
  queryObj.status = "ANSWERED";
  queryObj.response = resp;
  queryObj.respondedBy = "Clinical Investigator / CRC";
  queryObj.respondedAt = new Date().toISOString().slice(0, 10);

  store.addLedgerBlock(
    "QUERY_RESPOND",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    `Responded to query: "${resp}"`
  );
}

function closeQuery(fieldId) {
  pendingCloseQueryFieldId.value = fieldId;
  reauthAction.value = "CLOSE_QUERY";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

function reopenQuery(fieldId) {
  const queryObj = store.formQueries[fieldId];
  queryObj.status = "REOPENED";
  queryObj.message =
    queryObj.message + " [Reopened due to insufficient response]";

  store.addLedgerBlock(
    "QUERY_REOPEN",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    "Investigator response was rejected by clinical monitor."
  );
}

// Re-authentication confirmation handler
async function confirmReauth() {
  if (!reauthPassword.value) {
    reauthError.value = "Password is required.";
    return;
  }

  const username = reauthUsername.value;
  const password = reauthPassword.value;
  const totp = reauthTotp.value || null;
  const action = reauthAction.value;

  reauthPassword.value = "";

  if (action === "CLOSE_QUERY") {
    const fieldId = pendingCloseQueryFieldId.value;
    if (fieldId) {
      const queryObj = store.formQueries[fieldId];
      queryObj.status = "CLOSED";
      queryObj.closedBy = `${username} (CRA Monitor)`;
      queryObj.closedAt = new Date().toISOString().slice(0, 10);

      const fieldMeta = store.ecrfFields.find((f) => f.id === fieldId);
      const cdash = fieldMeta ? fieldMeta.cdash : "";
      const [domain, testCode] = cdash
        ? cdash.split(".")
        : ["VS", fieldId.toUpperCase()];

      store.addLedgerBlock(
        "QUERY_CLOSE",
        {
          fieldId,
          studyId: store.currentUsdm.studyId || "STUDY-USDM-001",
          subjectId: selectedSubjectId.value,
          visitId: selectedVisitId.value,
          domain,
          testCode,
          query: queryObj,
        },
        "Discrepancy resolved and closed permanently by monitor."
      );
      pendingCloseQueryFieldId.value = null;
    }

    showReauthModal.value = false;
    reauthError.value = "";
    alert(
      "Identity verified. Query closed and logged to cryptographic ledger."
    );
  } else if (action === "BATCH_SIGN_OFF") {
    try {
      reauthError.value = "";

      const studyId = store.currentUsdm.studyId || "STUDY-USDM-001";
      const targetType = signoffTargetType.value;
      const targetId =
        signoffTargetId.value === "custom"
          ? customTargetId.value
          : signoffTargetId.value;
      const targetIds = [targetId];
      const signingReason = signoffReason.value;

      const normStudy = studyId.trim();
      const normType = targetType.trim().toUpperCase();
      const normIds = [...targetIds]
        .map((id) => String(id).trim())
        .sort()
        .join(",");
      const normReason = signingReason.trim();
      const bindingStr = `${normStudy}:${normType}:${normIds}:${normReason}`;

      const msgBuffer = new TextEncoder().encode(bindingStr);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const batchId = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");

      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
          batchId,
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      const signoffRes = await soaClient.batchSignOff(
        {
          studyId,
          targetType,
          targetIds,
          signingReason,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "investigator",
          changeReason: signingReason,
          sigToken,
        },
        authStore.accessToken
      );

      await store.addLedgerBlock(
        "BATCH_SIGN_OFF_SUCCESS",
        {
          targetType: signoffTargetType.value,
          targetIds: [targetId],
          signingReason: signoffReason.value,
          result: signoffRes,
        },
        `PI electronic sign-off approved: ${signoffReason.value}`
      );

      showReauthModal.value = false;
      reauthTotp.value = "";
      alert(
        `Signature Token obtained successfully.\nBatch sign-off completed for ${signoffTargetType.value} ${targetId}!`
      );
    } catch (err) {
      reauthPassword.value = "";
      reauthTotp.value = "";

      if (err.message === "REAUTHENTICATION_REQUIRED" || err.status === 401) {
        reauthError.value =
          "Identity verification expired or invalid. Please try again.";
        showReauthModal.value = true;
      } else {
        reauthError.value = err.message || "Failed to complete batch sign-off.";
      }
    }
  } else if (action === "BULK_SDV") {
    try {
      reauthError.value = "";

      const studyId = store.currentUsdm.studyId || "STUDY-USDM-001";
      const fieldsToVerify = [...selectedBatchFields.value];
      const signingReason = "Batch Source Data Verification (SDV)";

      const normStudy = studyId.trim();
      const normFields = fieldsToVerify.sort().join(",");
      const bindingStr = `${normStudy}:SDV:${normFields}:${signingReason}`;

      const msgBuffer = new TextEncoder().encode(bindingStr);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const batchId = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");

      let tokenRequestedAt = Date.now();
      if (simulateDelay.value) {
        tokenRequestedAt -= 65000;
      }

      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
          batchId,
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      const elapsed = (Date.now() - tokenRequestedAt) / 1000;
      if (elapsed > 60) {
        throw new Error(
          "Compliance Lockout: The electronic signature verification token is older than 60 seconds."
        );
      }

      await soaClient.batchSignOff(
        {
          studyId,
          targetType: "FORM",
          targetIds: fieldsToVerify,
          signingReason,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "monitor",
          changeReason: signingReason,
          sigToken,
        },
        authStore.accessToken
      );

      for (const fieldId of fieldsToVerify) {
        const sKey = getSdvKey(fieldId);
        sdvStates[sKey] = true;

        store.addLedgerBlock(
          "SDV_TOGGLE",
          {
            subjectId: selectedSubjectId.value,
            visitId: selectedVisitId.value,
            fieldId,
            is_sdv_verified: true,
          },
          "Batch Source Data Verification (SDV) confirmed"
        );
      }

      selectedBatchFields.value = [];
      showReauthModal.value = false;
      reauthTotp.value = "";
      alert(
        `Identity verified. Batch Source Data Verification (SDV) completed successfully for selected fields!`
      );
    } catch (err) {
      reauthPassword.value = "";
      reauthTotp.value = "";

      if (err.message === "REAUTHENTICATION_REQUIRED" || err.status === 401) {
        reauthError.value =
          "Identity verification expired or invalid. Please try again.";
        showReauthModal.value = true;
      } else {
        reauthError.value = err.message || "Failed to complete batch SDV.";
      }
    }
  }
}

function clearForm() {
  store.ecrfFields.forEach((f) => {
    store.formValues[f.id] = "";
    delete store.formQueries[f.id];
  });
  store.addLedgerBlock(
    "FORM_CLEAR",
    {
      formId: "VS_DEMO",
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    "All eCRF form fields cleared by clinical staff."
  );
}

function submitEcrf() {
  let allValid = true;
  let errMsgs = [];

  store.ecrfFields.forEach((f) => {
    const res = validateField(
      f,
      store.formValues[f.id],
      store.formValues,
      evaluateAST
    );
    if (!res.valid) {
      allValid = false;
      errMsgs.push(`${f.label}: ${res.message}`);
    }
  });

  if (!allValid) {
    alert(
      "Cannot submit eCRF! The form contains validation errors:\n\n" +
        errMsgs.join("\n")
    );
    return;
  }

  store.addLedgerBlock(
    "SESSION_SUBMIT",
    {
      formId: "VS_DEMO",
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
      formValues: store.formValues,
      formQueries: store.formQueries,
    },
    "eCRF successfully verified, finalized, and electronically submitted."
  );

  alert(
    "eCRF Session successfully submitted to secure cryptographic database!"
  );
}
</script>
