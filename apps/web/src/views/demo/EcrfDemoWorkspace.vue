<template>
  <div>
    <!-- Active User Role Badge & CRA Mode Toggle -->
    <div class="ecrf-role-banner">
      <div class="ecrf-role-banner__label">
        {{ $t("ecrf.activeRole") }}
        <span
          class="badge"
          style="
            background-color: var(--accent);
            color: var(--semantic-color-text-on-accent);
            padding: 4px 8px;
            border-radius: 4px;
            font-weight: bold;
          "
          >{{ activeUserRole.toUpperCase() }}</span
        >
      </div>

      <!-- Demo role tester select & EHR Reconciliation Drawer toggle -->
      <div
        style="
          display: flex;
          gap: var(--spacing-xs);
          align-items: center;
          flex-wrap: wrap;
        "
      >
        <button
          type="button"
          class="btn btn-secondary"
          style="
            font-size: 0.8rem;
            display: inline-flex;
            align-items: center;
            gap: 4px;
            padding: 6px 12px;
            font-weight: 600;
            cursor: pointer;
            border-radius: 4px;
            border: 1px solid var(--border);
          "
          @click="showEhrReconciliationDrawer = true"
        >
          🏥 {{ $t("ecrf.ehrReconciliation") }}
        </button>

        <label
          for="role-tester-select"
          style="font-size: 0.8rem; font-weight: bold"
          >{{ $t("ecrf.demoRoleToggle") }}</label
        >
        <select
          id="role-tester-select"
          v-model="demoRole"
          style="
            padding: 4px 8px;
            border-radius: 4px;
            border: 1px solid var(--border);
          "
          @change="onRoleTesterChange"
        >
          <option value="site_investigator">{{ $t("ecrf.roleCrc") }}</option>
          <option value="cra">{{ $t("ecrf.roleCra") }}</option>
        </select>
      </div>
    </div>

    <div
      class="ecrf-workspace-layout"
      style="display: flex; gap: 24px; align-items: flex-start; flex-wrap: wrap"
    >
      <!-- Left Column: Persona Walkthrough in Demo Builds -->
      <div class="ecrf-form-column" style="flex: 1 1 580px; min-width: 0">
        <SubjectEnrollmentForm
          :can-enroll="canEnrollSubjects"
          :screening-result="screeningResult"
          :is-enrolling="isEnrolling"
          @enroll="onSubjectEnroll"
          @screen="onSubjectScreen"
        />

        <EcrfFormPanel
          :form-id="routeQuery('formId')"
          :enrollment-id="routeQuery('enrollmentId')"
          :collection-spec-id="routeQuery('collectionSpecId')"
          :planned-activity-id="
            routeQuery('plannedActivityId') ?? routeQuery('visitId')
          "
          :highlight-item-id="routeQuery('itemId')"
          :can-manage-queries="canManageFieldQueries"
          :can-verify-source="canVerifyFieldSource"
          @dirty-change="formPanelDirty = $event"
          @form-loaded="emit('form-loaded', $event)"
          @closed="onFormClosed"
        />

        <CrcFormRenderer
          v-model:selected-subject-id="selectedSubjectId"
          v-model:selected-visit-id="selectedVisitId"
          v-model:show-econsent-modal="showEconsentModal"
          v-model:show-paper-icf-modal="showPaperIcfModal"
          v-model:econsent-signer-name="econsentSignerName"
          v-model:paper-icf-date="paperIcfDate"
          v-model:paper-icf-note="paperIcfNote"
          :store="store"
          :can-enroll="canEnrollSubjects"
          :can-edit-form="canEditFormValues"
          :can-manage-queries="canManageFieldQueries"
          :is-reconsent-gated="isReconsentGated"
          :reconsent-submitting="reconsentSubmitting"
          :lookup-statuses="lookupStatuses"
          :get-validation-error="getValidationError"
          :is-cra-user="canVerifyFieldSource"
          :sdv-states="sdvStates"
          :get-sdv-key="getSdvKey"
          :dirty-fields="dirtyFields"
          :is-dirty="isDirty"
          :draft-restored="draftRestored"
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
        />
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
        <div
          class="card"
          style="display: flex; flex-direction: column; gap: 16px"
        >
          <div>
            <div class="card-title">{{ $t("ecrf.cdashTitle") }}</div>
            <p
              style="
                font-size: 0.85rem;
                color: var(--semantic-color-text-secondary);
                margin-bottom: 8px;
              "
            >
              {{ $t("ecrf.cdashDesc") }}
            </p>
          </div>

          <div
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              background-color: var(--semantic-color-surface-muted);
            "
          >
            <h3
              style="
                font-size: 0.9rem;
                font-weight: 700;
                margin-bottom: 8px;
                color: var(--text-main);
              "
            >
              {{ $t("ecrf.validationRulesTitle") }}
            </h3>
            <ul
              style="
                font-size: 0.8rem;
                padding-left: 20px;
                color: var(--semantic-color-text-secondary);
                display: flex;
                flex-direction: column;
                gap: 6px;
              "
            >
              <li>{{ $t("ecrf.birthDateRule") }}</li>
              <li>{{ $t("ecrf.systolicBpRule") }}</li>
              <li>{{ $t("ecrf.diastolicBpRule") }}</li>
              <li>{{ $t("ecrf.pulseRateRule") }}</li>
            </ul>
          </div>

          <div
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              background-color: var(--semantic-color-surface-muted);
            "
          >
            <h3
              style="
                font-size: 0.9rem;
                font-weight: 700;
                margin-bottom: 8px;
                color: var(--text-main);
              "
            >
              {{ $t("ecrf.queryManagementTitle") }}
            </h3>
            <p
              style="
                font-size: 0.8rem;
                color: var(--semantic-color-text-secondary);
                line-height: 1.4;
              "
            >
              {{ $t("ecrf.queryManagementDesc") }}
            </p>
          </div>
        </div>
      </div>
    </div>

    <!-- Full-Width Bottom Section: Study Designer Persona Component -->
    <div v-if="showDesignerSchemaPanel" style="margin-top: 24px">
      <DesignerSchemaPanel
        v-model:import-change-reason="schema.importChangeReason"
        v-model:selected-template-ids="schema.selectedTemplateIds"
        v-model:edit-item-value="schema.editItemValue"
        v-model:edit-item-reason="schema.editItemReason"
        v-model:reject-item-reason="schema.rejectItemReason"
        v-model:promote-change-reason="schema.promoteChangeReason"
        :store="store"
        :selected-file-name="schema.selectedFileName"
        :available-templates="schema.availableTemplates"
        :importing-draft="schema.importingDraft"
        :import-success-message="schema.importSuccessMessage"
        :import-error-message="schema.importErrorMessage"
        :imported-draft-summary="schema.importedDraftSummary"
        :editing-item-id="schema.editingItemId"
        :rejecting-item-id="schema.rejectingItemId"
        :unreviewed-count="schema.unreviewedCount"
        :get-confidence-class="schema.getConfidenceClass"
        :get-status-class="schema.getStatusClass"
        @register-file-input="schema.setFileInputElement"
        @load-templates="schema.loadProtocolTemplates"
        @trigger-document-upload="schema.triggerDocumentUpload"
        @import-selected-templates="schema.importSelectedTemplates"
        @accept-item="schema.acceptItem"
        @start-edit-item="schema.startEditItem"
        @cancel-edit-item="schema.cancelEditItem"
        @save-edit-item="schema.saveEditItem"
        @start-reject-item="schema.startRejectItem"
        @cancel-reject-item="schema.cancelRejectItem"
        @confirm-reject-item="schema.confirmRejectItem"
        @promote-candidate="schema.promoteCandidate"
      />
    </div>

    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data field."
      :options="ecrfReasonOptions"
      default-option="Initial Entry"
      @confirm="saveChange"
      @cancel="cancelChange"
    />

    <EhrReconciliationDrawer
      :is-open="showEhrReconciliationDrawer"
      :study-id="currentStudyId"
      :subject-id="selectedSubjectId"
      @close="showEhrReconciliationDrawer = false"
    />
  </div>
</template>

<script setup>
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { validateField, debounce } from "ui";
import { useClinicalStore } from "../../stores/clinical";
import { useAuthStore } from "../../stores/auth";
import { useToastStore } from "../../stores/toasts.js";
import { evaluateAST } from "../../evaluator.js";
import { terminologyClient } from "../../api/terminologyClient";
import ReasonModal from "../../components/ReasonModal.vue";
import CrcFormRenderer from "../../components/persona/CrcFormRenderer.vue";
import EcrfFormPanel from "../../components/ecrf/EcrfFormPanel.vue";
import DesignerSchemaPanel from "../../components/persona/DesignerSchemaPanel.vue";
import SubjectEnrollmentForm from "../../components/clinical/SubjectEnrollmentForm.vue";
import EhrReconciliationDrawer from "../../components/clinical/EhrReconciliationDrawer.vue";
import { useActiveStudyScope } from "../../composables/useActiveStudyScope";
import { useConsentGating } from "../../composables/useConsentGating";
import { useVerification } from "../../composables/useVerification";
import { useSchemaIngestion } from "../../composables/useSchemaIngestion";
import { useEcrfDrafts } from "../../composables/useEcrfDrafts";
import { scopedKey, storageScopeOf } from "../../utils/userScopedStorage.js";
import { allowUnbackedAction } from "../../utils/unbackedActions.js";
import { canManageQueries, canVerifySource } from "../../canvas/authority";

const emit = defineEmits([
  "dirty-change",
  "panel-dirty-change",
  "form-loaded",
  "closed",
]);

const store = useClinicalStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const route = useRoute();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();

const showEhrReconciliationDrawer = ref(false);
const formPanelDirty = ref(false);

function routeQuery(name) {
  const value = route?.query?.[name];
  return typeof value === "string" && value.length > 0 ? value : null;
}

function onFormClosed() {
  formPanelDirty.value = false;
  emit("closed");
}

const selectedSubjectId = ref(store.activeSubjectId || "SUBJ-001");
const selectedVisitId = ref("Screening");

watch(selectedSubjectId, (next) => {
  if (next && next !== store.activeSubjectId) {
    store.activeSubjectId = next;
  }
});

watch(
  () => store.activeSubjectId,
  (next) => {
    if (next && next !== selectedSubjectId.value) {
      selectedSubjectId.value = next;
    }
  }
);

const currentStudyId = computed(
  () =>
    activeStudyCode.value ||
    store.activeStudyId ||
    store.currentStudyId ||
    store.currentUsdm?.studyId ||
    "STUDY-USDM-001"
);

const {
  dirtyFields,
  isDirty,
  draftRestored,
  setInitialValues,
  restoreDraft,
  clearDraft,
} = useEcrfDrafts(
  currentStudyId,
  selectedSubjectId,
  selectedVisitId,
  computed(() => store.formValues)
);

watch(
  formPanelDirty,
  (next) => {
    emit("panel-dirty-change", next);
  },
  { immediate: true }
);

watch(
  () => Boolean(formPanelDirty.value || isDirty.value),
  (next) => {
    emit("dirty-change", next);
  },
  { immediate: true }
);

const screeningResult = ref(null);
const isEnrolling = ref(false);

async function onSubjectEnroll(payload) {
  isEnrolling.value = true;
  try {
    const res = await store.enrollSubject({
      id: payload.subject_id,
      siteId: "SITE-101",
      protocolVersion: "1.0.0",
      reason: `Subject ${payload.subject_id} registered and enrolled in ${payload.study_id}.`,
    });
    if (res) {
      selectedSubjectId.value = payload.subject_id;
    }
  } catch (err) {
    console.error("Enrollment failed:", err);
  } finally {
    isEnrolling.value = false;
  }
}

function onSubjectScreen() {
  screeningResult.value = store.screenSubject();
}

const demoRole = ref("site_investigator");
let syncedPersona = authStore.currentPersona;

function resyncWorkspaceForPersonaSwitch() {
  for (const key of Object.keys(ecrfSessions)) {
    delete ecrfSessions[key];
  }
  for (const key of Object.keys(sdvStates)) {
    delete sdvStates[key];
  }
  pendingValueChange.value = null;
  pendingSdvToggle.value = null;
  showReasonModal.value = false;
  selectListedSubject();
}

watch(
  () => authStore.currentPersona,
  (persona) => {
    if (!authStore.isAuthenticated) return;
    demoRole.value = persona === "cra_monitor" ? "cra" : "site_investigator";
    if (syncedPersona !== null && persona !== syncedPersona) {
      syncedPersona = persona;
      resyncWorkspaceForPersonaSwitch();
    } else {
      syncedPersona = persona;
    }
  },
  { immediate: true }
);

function onRoleTesterChange() {
  if (
    authStore.isAuthenticated &&
    authStore.isDemoMode &&
    !authStore.accessToken
  ) {
    const nextPersona = demoRole.value === "cra" ? "cra_monitor" : "site_crc";
    if (authStore.switchPersona(nextPersona)) {
      syncedPersona = authStore.currentPersona;
      resyncWorkspaceForPersonaSwitch();
    }
  }
}

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

const canEnrollSubjects = computed(() => {
  if (authStore.isAuthenticated) {
    return Boolean(authStore.isCrc);
  }
  return (
    activeUserRole.value === "site_investigator" ||
    activeUserRole.value === "crc"
  );
});

const canEditFormValues = computed(() => {
  if (authStore.isAuthenticated) {
    return Boolean(authStore.isCrc || authStore.hasRole("sponsor_admin"));
  }
  return true;
});

const canManageFieldQueries = computed(() =>
  authStore.isAuthenticated
    ? canManageQueries(authStore)
    : activeUserRole.value === "cra"
);

const canVerifyFieldSource = computed(() =>
  authStore.isAuthenticated
    ? canVerifySource(authStore)
    : activeUserRole.value === "cra"
);

const showDesignerSchemaPanel = computed(
  () => !authStore.isAuthenticated || Boolean(authStore.canManageWorkspaceSites)
);

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

const {
  sdvStates,
  pendingSdvToggle,
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

const schema = reactive(useSchemaIngestion(store));

const ecrfReasonOptions = [
  { value: "Initial Entry", text: "Initial Data Entry" },
  { value: "Typographical Error", text: "Correction of typographical error" },
  { value: "Re-measurement", text: "Re-measurement of vitals" },
  { value: "Transcription Error", text: "Correction of transcription error" },
  { value: "Other", text: "Other (specify below)" },
];
const showReasonModal = ref(false);
const pendingValueChange = ref(null);

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

const ecrfSessions = reactive({});

function getSessionKey() {
  const scopePrefix =
    scopedKey(storageScopeOf(authStore), "ecrf_session") ||
    (authStore.isAuthenticated
      ? `persona:${authStore.currentPersona || "default"}`
      : "guest");
  return `${scopePrefix}:${selectedSubjectId.value}:${selectedVisitId.value}`;
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

  setInitialValues(store.formValues);
  restoreDraft();
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

watch(
  () => scopedKey(storageScopeOf(authStore), "ecrf_draft"),
  (nextKey, prevKey) => {
    if (nextKey !== prevKey) {
      loadEcrfSession();
    }
  }
);

function selectListedSubject() {
  if (!store.subjects.some((s) => s.id === selectedSubjectId.value)) {
    selectedSubjectId.value = store.subjects[0]?.id ?? "";
  }
  loadEcrfSession();
}

function handleDemoReset() {
  for (const key of Object.keys(ecrfSessions)) {
    delete ecrfSessions[key];
  }
  for (const key of Object.keys(sdvStates)) {
    delete sdvStates[key];
  }
  screeningResult.value = null;
  pendingValueChange.value = null;
  pendingSdvToggle.value = null;
  showReasonModal.value = false;
  clearDraft();
  selectedSubjectId.value = store.subjects[0]?.id ?? "SUBJ-001";
  selectedVisitId.value = "Screening";
  loadEcrfSession();
}

onMounted(() => {
  window.addEventListener("cadence:demo-reset", handleDemoReset);
  if (route && route.query) {
    if (route.query.studyId) {
      const incomingStudy = String(route.query.studyId).trim();
      if (incomingStudy) {
        setActiveStudyCode(incomingStudy);
        store.activeStudyId = incomingStudy;
        store.currentStudyId = incomingStudy;
      }
    }
    if (route.query.siteId) {
      store.activeSiteId = String(route.query.siteId);
    }
    if (route.query.subjectId) {
      const sId = String(route.query.subjectId);
      store.activeSubjectId = sId;
      selectedSubjectId.value = sId;
    }
    const incomingVisit = route.query.visitId || route.query.plannedActivityId;
    if (incomingVisit) {
      const vId = String(incomingVisit);
      store.activeVisitId = vId;
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
  selectListedSubject();
});

onUnmounted(() => {
  window.removeEventListener("cadence:demo-reset", handleDemoReset);
});

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues, evaluateAST);
  return res.valid ? null : res.message;
}

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
    if (pendingValueChange.value.targetEl.type !== "radio") {
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
    if (!allowUnbackedAction("Recording source data verification")) {
      pendingSdvToggle.value = null;
      showReasonModal.value = false;
      return;
    }
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
    toastStore.push({
      tone: "success",
      title: "SDV updated",
      message: "Source Document Verification (SDV) status updated and logged!",
    });
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
  if (!allowUnbackedAction("Editing the sample form")) return;
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

function createQuery(fieldId, msgFromComponent = null) {
  const msg = msgFromComponent !== null ? msgFromComponent : "";
  if (!msg) {
    toastStore.push({
      tone: "error",
      title: "Message required",
      message: "Please enter a discrepancy message!",
    });
    return;
  }
  if (!allowUnbackedAction("Raising a query")) return;

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
    toastStore.push({
      tone: "error",
      title: "Response required",
      message: "Please enter a response!",
    });
    return;
  }
  if (!allowUnbackedAction("Answering a query")) return;

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
  if (!allowUnbackedAction("Closing a query")) return;
  const queryObj = store.formQueries[fieldId];
  if (!queryObj) return;
  queryObj.status = "CLOSED";
  queryObj.closedBy = `${activeUserRole.value} (Client Monitor)`;
  queryObj.closedAt = new Date().toISOString().slice(0, 10);
  store.addLedgerBlock(
    "QUERY_CLOSE",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    "Discrepancy resolved and closed by monitor."
  );
}

function reopenQuery(fieldId) {
  if (!allowUnbackedAction("Reopening a query")) return;
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

function clearForm() {
  store.ecrfFields.forEach((f) => {
    store.formValues[f.id] = "";
    delete store.formQueries[f.id];
  });
  clearDraft();
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

async function submitEcrf() {
  let allValid = true;
  const errMsgs = [];

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
    toastStore.push({
      tone: "error",
      title: "eCRF Validation Failed",
      message:
        "Cannot submit eCRF! The form contains validation errors:\n\n" +
        errMsgs.join("\n"),
    });
    return;
  }

  if (!allowUnbackedAction("Submitting the sample form")) return;

  await store.addLedgerBlock(
    "SESSION_SUBMIT",
    {
      formId: "VS_DEMO",
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
      formValues: store.formValues,
      formQueries: store.formQueries,
      synced: false,
    },
    "Sample eCRF session submitted in the demo."
  );
  toastStore.push({
    tone: "success",
    title: "Sample form submitted",
    message: "The demo recorded the sample form in its local ledger only.",
  });
  clearDraft();
}

defineExpose({
  getSdvKey,
  sdvStates,
  commitChange,
  saveChange,
  submitEcrf,
  onSubjectScreen,
  screeningResult,
  createQuery,
  respondQuery,
  closeQuery,
  reopenQuery,
  isReconsentGated,
  handleCompleteReconsent: (method, targetStore = store) =>
    handleCompleteReconsent(method, targetStore),
});
</script>

<style scoped src="../../components/ecrf/ecrfFormBanners.css"></style>
