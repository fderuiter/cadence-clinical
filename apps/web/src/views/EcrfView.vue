<template>
  <div id="section-ecrf" class="dashboard-section active">
    <div class="section-header">
      <h2>{{ $t("ecrf.title") }}</h2>
      <p>
        {{ $t("ecrf.description") }}
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
        background-color: var(--semantic-color-surface-muted);
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: 20px;
      "
    >
      <div style="font-size: 0.9rem">
        {{ $t("ecrf.activeRole") }}
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

      <!-- Demo role tester select & EHR Reconciliation Drawer toggle -->
      <div
        v-if="demoBuild"
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
      <!-- Left Column: Kernel eCRF Form Outside Demo Builds; Persona Walkthrough in Demo Builds -->
      <div class="ecrf-form-column" style="flex: 1 1 580px; min-width: 0">
        <!-- Outside demo builds the subject list comes from Execution for the chosen study (#4828). -->
        <StudySubjectsBar v-if="!demoBuild" @loaded="selectListedSubject" />

        <!-- Participant selector & Clinical Canvas handoff outside demo builds -->
        <section
          v-if="!demoBuild"
          id="ecrf-participant-bar"
          aria-label="Participant selection"
          style="
            display: flex;
            flex-direction: column;
            gap: 8px;
            padding: 12px 16px;
            margin-bottom: 16px;
            border: 1px solid var(--border);
            border-radius: 8px;
            background: var(--semantic-color-surface);
          "
        >
          <div
            style="
              display: flex;
              flex-wrap: wrap;
              align-items: center;
              gap: 10px;
            "
          >
            <label
              for="ecrf-subject-selector"
              style="font-size: 0.85rem; font-weight: 600"
            >
              Participant
            </label>
            <select
              id="ecrf-subject-selector"
              v-model="selectedSubjectId"
              :disabled="store.subjectsLoading || store.subjects.length === 0"
              style="
                flex: 1 1 14rem;
                min-width: 0;
                padding: 6px 8px;
                border: 1px solid var(--semantic-color-border-control);
                border-radius: 4px;
              "
            >
              <option v-if="store.subjects.length === 0" value="">
                No subjects listed
              </option>
              <option
                v-for="sub in store.subjects"
                :key="sub.id"
                :value="sub.id"
              >
                {{ sub.id }}{{ sub.siteId ? ` · Site ${sub.siteId}` : ""
                }}{{ sub.status ? ` (${sub.status})` : "" }}
              </option>
            </select>

            <component
              :is="routerLinkComponent"
              v-if="showManageParticipantsLink && routerLinkComponent"
              id="ecrf-manage-participants-link"
              :to="manageParticipantsLocation"
              style="
                font-size: 0.85rem;
                font-weight: 600;
                color: var(--semantic-color-interactive-primary);
                text-decoration: underline;
              "
            >
              Manage participants in Clinical Canvas
            </component>
            <a
              v-else-if="showManageParticipantsLink"
              id="ecrf-manage-participants-link"
              :href="manageParticipantsHref"
              style="
                font-size: 0.85rem;
                font-weight: 600;
                color: var(--semantic-color-interactive-primary);
                text-decoration: underline;
              "
            >
              Manage participants in Clinical Canvas
            </a>
          </div>

          <p
            v-if="enrollmentStatusMessage"
            id="ecrf-enrollment-status"
            role="status"
            aria-live="polite"
            style="margin: 0; font-size: 0.85rem; color: var(--neutral-dark)"
          >
            {{ enrollmentStatusMessage }}
          </p>
        </section>

        <!-- CRC Persona Component: Reusable Subject Registration & Screening Card (demo only) -->
        <SubjectEnrollmentForm
          v-if="demoBuild"
          :can-enroll="
            authStore.isCrc ||
            activeUserRole === 'site_investigator' ||
            activeUserRole === 'crc'
          "
          :screening-result="screeningResult"
          :is-enrolling="isEnrolling"
          @enroll="onSubjectEnroll"
          @screen="onSubjectScreen"
        />

        <!-- The participant's case report form, a kernel form instance saved a page at a time (ADR-2622). -->
        <EcrfFormPanel
          :form-id="routeQuery('formId')"
          :enrollment-id="activeEnrollmentProp"
          :collection-spec-id="routeQuery('collectionSpecId')"
          :planned-activity-id="
            routeQuery('plannedActivityId') ?? routeQuery('visitId')
          "
          :highlight-item-id="routeQuery('itemId')"
          :can-manage-queries="
            authStore.isAuthenticated
              ? canManageQueries(authStore)
              : activeUserRole === 'cra'
          "
          :can-verify-source="
            authStore.isAuthenticated
              ? canVerifySource(authStore)
              : activeUserRole === 'cra'
          "
          @dirty-change="formPanelDirty = $event"
          @form-loaded="onFormPanelLoaded"
          @closed="onFormPanelClosed"
        />

        <CrcFormRenderer
          v-if="demoBuild"
          v-model:selected-subject-id="selectedSubjectId"
          v-model:selected-visit-id="selectedVisitId"
          v-model:show-econsent-modal="showEconsentModal"
          v-model:show-paper-icf-modal="showPaperIcfModal"
          v-model:econsent-signer-name="econsentSignerName"
          v-model:paper-icf-date="paperIcfDate"
          v-model:paper-icf-note="paperIcfNote"
          :store="store"
          :is-reconsent-gated="isReconsentGated"
          :reconsent-submitting="reconsentSubmitting"
          :lookup-statuses="lookupStatuses"
          :get-validation-error="getValidationError"
          :is-cra-user="isCraUser"
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

      <!-- Right Column: PI Persona Component & CDASH Metadata Tools (demo only) -->
      <div
        v-if="demoBuild"
        class="ecrf-side-column"
        style="
          flex: 1 1 380px;
          min-width: 0;
          display: flex;
          flex-direction: column;
          gap: 20px;
        "
      >
        <!-- Live Form State & CDASH Meta Card -->
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
                color: var(--primary);
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
              <li>
                {{ $t("ecrf.birthDateRule") }}
              </li>
              <li>
                {{ $t("ecrf.systolicBpRule") }}
              </li>
              <li>
                {{ $t("ecrf.diastolicBpRule") }}
              </li>
              <li>
                {{ $t("ecrf.pulseRateRule") }}
              </li>
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
                color: var(--primary);
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

    <!-- Full-Width Bottom Section: Study Designer Persona Component (demo only) -->
    <div v-if="demoBuild" style="margin-top: 24px">
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

    <!-- Reason for Change Modal Dialog (demo sample form & SDV only) -->
    <ReasonModal
      v-if="demoBuild"
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data field."
      :options="ecrfReasonOptions"
      default-option="Initial Entry"
      @confirm="saveChange"
      @cancel="cancelChange"
    />

    <!-- eSource EHR Source Data Reconciliation Drawer (demo only) -->
    <EhrReconciliationDrawer
      v-if="demoBuild"
      :is-open="showEhrReconciliationDrawer"
      :study-id="store.currentStudyId || 'STUDY-101'"
      :subject-id="selectedSubjectId"
      @close="showEhrReconciliationDrawer = false"
    />
  </div>
</template>

<script setup>
import {
  ref,
  reactive,
  watch,
  onMounted,
  onUnmounted,
  computed,
  getCurrentInstance,
} from "vue";
import * as VueRouter from "vue-router";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { useToastStore } from "../stores/toasts.js";
import { confirmDialog } from "../composables/useConfirmDialog.js";
import { validateField, debounce } from "ui";
import { evaluateAST } from "../evaluator.js";
import { terminologyClient } from "../api/terminologyClient";
import ReasonModal from "../components/ReasonModal.vue";

// Import Persona Sub-Components
import CrcFormRenderer from "../components/persona/CrcFormRenderer.vue";
import EcrfFormPanel from "../components/ecrf/EcrfFormPanel.vue";
import DesignerSchemaPanel from "../components/persona/DesignerSchemaPanel.vue";
import SubjectEnrollmentForm from "../components/clinical/SubjectEnrollmentForm.vue";
import StudySubjectsBar from "../components/clinical/StudySubjectsBar.vue";
import EhrReconciliationDrawer from "../components/clinical/EhrReconciliationDrawer.vue";

// Import Domain Composables
import { useConsentGating } from "../composables/useConsentGating";
import { useVerification } from "../composables/useVerification";
import { useSchemaIngestion } from "../composables/useSchemaIngestion";
import { useEcrfDrafts } from "../composables/useEcrfDrafts";
import {
  allowUnbackedAction,
  unbackedActionsEnabled,
} from "../utils/unbackedActions.js";
import {
  canManageParticipants,
  canManageQueries,
  canVerifySource,
} from "../canvas/authority";
import { findKernelEnrollmentRef } from "../canvas/kernelStudy";

const { useRoute, onBeforeRouteLeave } = VueRouter;
const useRouter = "useRouter" in VueRouter ? VueRouter.useRouter : undefined;
const onBeforeRouteUpdate =
  "onBeforeRouteUpdate" in VueRouter
    ? VueRouter.onBeforeRouteUpdate
    : undefined;

const store = useClinicalStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const route = useRoute();
const router = typeof useRouter === "function" ? useRouter() : null;
const demoBuild = unbackedActionsEnabled();
const instance = getCurrentInstance();
const routerLinkComponent =
  instance?.appContext.components.RouterLink ||
  instance?.appContext.components["router-link"] ||
  null;

const showEhrReconciliationDrawer = ref(false);

// Whether the case report form has entries that are not saved yet.
const formPanelDirty = ref(false);
const loadedFormId = ref(null);

/** One route query value as a string, or null. */
function routeQuery(name, targetRoute = route) {
  const value = targetRoute?.query?.[name];
  return typeof value === "string" && value.length > 0 ? value : null;
}

const routeStudyId = ref(routeQuery("studyId") ?? "");
const routeSubjectId = ref(routeQuery("subjectId") ?? "");
const routeEnrollmentId = ref(routeQuery("enrollmentId") ?? "");

watch(
  () => routeQuery("formId"),
  (next) => {
    if (!next) {
      loadedFormId.value = null;
    }
  }
);

function onFormPanelLoaded(loaded) {
  const nextId = loaded?.form_id || null;
  loadedFormId.value = nextId;
  if (!nextId || routeQuery("formId") === nextId) return;
  if (typeof router?.replace === "function") {
    void router.replace({
      query: {
        ...(route?.query || {}),
        formId: nextId,
      },
    });
  }
}

function onFormPanelClosed() {
  formPanelDirty.value = false;
  loadedFormId.value = null;
  if (!routeQuery("formId")) return;
  if (typeof router?.replace === "function") {
    const nextQuery = { ...(route?.query || {}) };
    delete nextQuery.formId;
    void router.replace({ query: nextQuery });
  }
}

// Selected Subject & Visit State
const selectedSubjectId = ref(
  demoBuild ? store.activeSubjectId || "SUBJ-001" : routeSubjectId.value || ""
);
const selectedVisitId = ref("Screening");

const currentStudyId = computed(
  () =>
    store.activeStudyId ||
    store.currentStudyId ||
    store.currentUsdm?.studyId ||
    "STUDY-USDM-001"
);

// Non-demo builds never persist or restore sample-form drafts in localStorage (ADR-2622).
const draftStudyScope = computed(() => (demoBuild ? currentStudyId.value : ""));

// Domain Composable: Draft Persistence & Dirty Tracking (demo sample form only)
const {
  dirtyFields,
  isDirty,
  draftRestored,
  setInitialValues,
  restoreDraft,
  clearDraft,
} = useEcrfDrafts(
  draftStudyScope,
  selectedSubjectId,
  selectedVisitId,
  computed(() => store.formValues)
);

// Kernel enrollment resolution for the selected participant outside demo mode
const resolvedEnrollmentId = ref(routeEnrollmentId.value);
const enrollmentStatusMessage = ref("");
const selectionChangedFromInitial = ref(false);
let enrollmentLookupSeq = 0;

const selectedSubject = computed(
  () => store.subjects.find((s) => s.id === selectedSubjectId.value) ?? null
);

const activeEnrollmentProp = computed(() => {
  if (demoBuild) return routeQuery("enrollmentId");
  return resolvedEnrollmentId.value || null;
});

const showManageParticipantsLink = computed(
  () =>
    !demoBuild &&
    Boolean(authStore.isAuthenticated) &&
    canManageParticipants(authStore)
);

const manageParticipantsLocation = computed(() => {
  const code = store.activeStudyId.trim();
  if (code) {
    return {
      path: `/studies/${encodeURIComponent(code)}`,
      query: { stage: "conduct", section: "participants" },
    };
  }
  return { path: "/studies" };
});

const basePrefix = (import.meta.env.BASE_URL || "/").replace(/\/?$/, "/");
const manageParticipantsHref = computed(() => {
  const code = store.activeStudyId.trim();
  if (code) {
    return `${basePrefix}studies/${encodeURIComponent(code)}?stage=conduct&section=participants`;
  }
  return `${basePrefix}studies`;
});

async function resolveSelectedEnrollment() {
  if (demoBuild) return;
  const studyCode = store.activeStudyId.trim();
  const subjectCode = selectedSubjectId.value.trim();

  if (
    routeEnrollmentId.value &&
    !selectionChangedFromInitial.value &&
    ((studyCode && studyCode !== routeStudyId.value) ||
      (subjectCode && subjectCode !== routeSubjectId.value))
  ) {
    selectionChangedFromInitial.value = true;
  }

  if (!studyCode || !subjectCode) {
    enrollmentLookupSeq++;
    if (
      routeEnrollmentId.value &&
      !selectionChangedFromInitial.value &&
      !studyCode &&
      !subjectCode
    ) {
      resolvedEnrollmentId.value = routeEnrollmentId.value;
      enrollmentStatusMessage.value = "";
      return;
    }
    resolvedEnrollmentId.value = "";
    enrollmentStatusMessage.value = "";
    return;
  }

  if (
    routeEnrollmentId.value &&
    !selectionChangedFromInitial.value &&
    (!routeStudyId.value || studyCode === routeStudyId.value) &&
    (!routeSubjectId.value || subjectCode === routeSubjectId.value)
  ) {
    enrollmentLookupSeq++;
    resolvedEnrollmentId.value = routeEnrollmentId.value;
    enrollmentStatusMessage.value = "";
    return;
  }

  const seq = ++enrollmentLookupSeq;
  const siteCode =
    selectedSubject.value?.siteId ||
    (subjectCode === routeSubjectId.value ? routeQuery("siteId") : null) ||
    null;

  enrollmentStatusMessage.value = `Resolving kernel enrollment for ${subjectCode}…`;

  try {
    const refResult = await findKernelEnrollmentRef(
      studyCode,
      siteCode,
      subjectCode
    );
    if (seq !== enrollmentLookupSeq) return;
    if (refResult?.id) {
      resolvedEnrollmentId.value = refResult.id;
      enrollmentStatusMessage.value = "";
    } else {
      resolvedEnrollmentId.value = "";
      enrollmentStatusMessage.value = `No kernel enrollment found for ${subjectCode} in ${studyCode}.`;
    }
  } catch (err) {
    if (seq !== enrollmentLookupSeq) return;
    resolvedEnrollmentId.value = "";
    const detail = err instanceof Error ? err.message : String(err);
    enrollmentStatusMessage.value = `Could not resolve kernel enrollment for ${subjectCode}: ${detail}`;
  }
}

watch(
  [
    () => store.activeStudyId,
    selectedSubjectId,
    () => selectedSubject.value?.siteId ?? "",
  ],
  ([nextStudy, nextSubject], [prevStudy, prevSubject]) => {
    if (
      prevStudy !== undefined &&
      (nextStudy !== prevStudy || nextSubject !== prevSubject)
    ) {
      if (
        nextStudy !== routeStudyId.value ||
        nextSubject !== routeSubjectId.value
      ) {
        selectionChangedFromInitial.value = true;
      }
    }
    void resolveSelectedEnrollment();
  }
);

watch(
  [
    () => routeQuery("studyId") ?? "",
    () => routeQuery("siteId") ?? "",
    () => routeQuery("subjectId") ?? "",
    () => routeQuery("enrollmentId") ?? "",
  ],
  async (
    [nextStudy, nextSite, nextSubject, nextEnrollment],
    [prevStudy, prevSite, prevSubject, prevEnrollment]
  ) => {
    if (
      nextStudy === prevStudy &&
      nextSite === prevSite &&
      nextSubject === prevSubject &&
      nextEnrollment === prevEnrollment
    ) {
      return;
    }
    routeStudyId.value = nextStudy;
    routeSubjectId.value = nextSubject;
    routeEnrollmentId.value = nextEnrollment;
    selectionChangedFromInitial.value = false;

    if (nextSite) {
      store.activeSiteId = nextSite;
    }
    const studyChanged = Boolean(
      nextStudy && nextStudy !== store.activeStudyId
    );
    if (nextStudy) {
      store.activeStudyId = nextStudy;
    }
    if (nextSubject) {
      store.activeSubjectId = nextSubject;
      selectedSubjectId.value = nextSubject;
    }
    if (!demoBuild) {
      if (studyChanged) {
        await store.loadSubjects(nextStudy);
      }
      selectListedSubject();
      await resolveSelectedEnrollment();
    }
  }
);

// Screening and Enrollment State (CRC)
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
  // No service evaluates eligibility yet; the store says so and returns no result.
  screeningResult.value = store.screenSubject();
}

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
  return demoBuild ? demoRole.value : "site_investigator";
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
  pendingSdvToggle,
  isCraUser,
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

// Domain Composable 4: Protocol Schema Ingestion & Review (Study Designer). reactive() unwraps its refs for the template.
const schema = reactive(useSchemaIngestion(store));

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

// Consolidated lookup validation and state management (demo only)
const lookupStatuses = ref({});
const lookupRequestCounters = reactive({});
const debouncedLookups = {};

async function performConceptCodeValidation(fieldId, value) {
  if (!demoBuild) return;
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
  if (!demoBuild) return;
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  getDebouncedLookup(fieldId)(value);
}

// eCRF Session Management (demo sample form only)
const ecrfSessions = reactive({});

function getSessionKey() {
  return `${selectedSubjectId.value}:${selectedVisitId.value}`;
}

function loadEcrfSession() {
  if (!demoBuild) return;
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

  // Establish baseline values for calculating dirty state
  setInitialValues(store.formValues);

  // Auto-restore draft from localStorage if present for this session
  restoreDraft();

  store.evaluateRules();

  store.ecrfFields.forEach((field) => {
    if (field.type === "concept_code" && store.formValues[field.id]) {
      performConceptCodeValidation(field.id, store.formValues[field.id]);
    }
  });
}

if (demoBuild) {
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
}

onMounted(async () => {
  window.addEventListener("beforeunload", handleBeforeUnload);
  if (route && route.query) {
    if (route.query.studyId) {
      const incomingStudy = String(route.query.studyId);
      store.activeStudyId = incomingStudy;
      if (demoBuild) store.currentStudyId = incomingStudy;
    }
    if (route.query.siteId) store.activeSiteId = String(route.query.siteId);
    if (route.query.subjectId) {
      const sId = String(route.query.subjectId);
      store.activeSubjectId = sId;
      selectedSubjectId.value = sId;
    }
    const incomingVisit = route.query.visitId || route.query.plannedActivityId;
    if (demoBuild && incomingVisit) {
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
  if (!demoBuild && store.activeStudyId) {
    await store.loadSubjects(store.activeStudyId);
  }
  selectListedSubject();
  if (!demoBuild) {
    await resolveSelectedEnrollment();
  }
});

onUnmounted(() => {
  window.removeEventListener("beforeunload", handleBeforeUnload);
});

function shouldSuppressUnsavedGuard() {
  return Boolean(authStore.sessionExpired || authStore.isLoggingOut);
}

const hasUnsavedChanges = computed(
  () => formPanelDirty.value || (demoBuild && isDirty.value)
);

function confirmLeaveDirtyDraft() {
  return confirmDialog({
    title: "Unsaved eCRF Changes",
    message:
      "You have unsaved eCRF draft changes. Are you sure you want to leave?",
    confirmLabel: "Leave Without Saving",
    cancelLabel: "Stay on Form",
    variant: "warning",
  });
}

// Route & Browser Navigation Guards
if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave(() => {
    if (hasUnsavedChanges.value && !shouldSuppressUnsavedGuard()) {
      return confirmLeaveDirtyDraft().then((confirmLeave) =>
        confirmLeave ? undefined : false
      );
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to, from) => {
    if (!formPanelDirty.value || shouldSuppressUnsavedGuard()) return;
    const nextFormId = routeQuery("formId", to);
    const currentFormId =
      loadedFormId.value ?? routeQuery("formId", from ?? route);
    if (nextFormId !== currentFormId) {
      return confirmLeaveDirtyDraft().then((confirmSwitch) =>
        confirmSwitch ? undefined : false
      );
    }
  });
}

function handleBeforeUnload(event) {
  if (hasUnsavedChanges.value && !shouldSuppressUnsavedGuard()) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

/**
 * Keeps the selected subject one the list holds: the first listed subject when the current one is
 * not in the study, or none when the study has no subjects. Then loads that subject's session.
 */
function selectListedSubject() {
  if (!store.subjects.some((s) => s.id === selectedSubjectId.value)) {
    selectedSubjectId.value = store.subjects[0]?.id ?? "";
  }
  if (demoBuild) {
    loadEcrfSession();
  }
}

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues, evaluateAST);
  return res.valid ? null : res.message;
}

// Reason Modal & Field Changes (demo only)
function handleFieldChange(field, newValue, targetEl) {
  if (!demoBuild) return;
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
    // #4827: SDV is not sent to the Execution service from here yet, so outside demo builds nothing changes.
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

// Sample-form queries
// These queries are local state on the sample form (store.formQueries and the local ledger); no service receives them,
// so outside demo builds they are refused (#4827). Real eCRF queries are raised, answered, reopened, closed and
// cancelled on the case report form's values above, through the Execution module (EcrfFormPanel, #5461).
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
    toastStore.push({
      tone: "error",
      title: "eCRF Validation Failed",
      message:
        "Cannot submit eCRF! The form contains validation errors:\n\n" +
        errMsgs.join("\n"),
    });
    return;
  }

  // The sample form is not a kernel form: values are entered and saved in the case report form above, a page at a
  // time (ADR-2622). Outside demo builds submitting the sample form is refused rather than sent anywhere.
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
</script>
