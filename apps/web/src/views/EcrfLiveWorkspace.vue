<template>
  <div>
    <!-- Active User Role Badge -->
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
    </div>

    <div
      class="ecrf-workspace-layout"
      style="display: flex; gap: 24px; align-items: flex-start; flex-wrap: wrap"
    >
      <div class="ecrf-form-column" style="flex: 1 1 580px; min-width: 0">
        <!-- Outside demo builds the subject list comes from Execution for the chosen study (#4828). -->
        <StudySubjectsBar @loaded="selectListedSubject" />

        <!-- Participant selector & Clinical Canvas handoff outside demo builds -->
        <section
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
              :disabled="
                scopeStore.subjectsLoading || scopeStore.subjects.length === 0
              "
              style="
                flex: 1 1 14rem;
                min-width: 0;
                min-height: var(--semantic-touch-target-staff);
                padding: 6px 8px;
                font-size: 1rem;
                border: 1px solid var(--semantic-color-border-control);
                border-radius: 4px;
              "
            >
              <option v-if="scopeStore.subjects.length === 0" value="">
                No subjects listed
              </option>
              <option
                v-for="sub in scopeStore.subjects"
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
                display: inline-flex;
                align-items: center;
                min-height: var(--semantic-touch-target-staff);
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
                display: inline-flex;
                align-items: center;
                min-height: var(--semantic-touch-target-staff);
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

        <!-- Participant's opened case report forms outside demo mode when no single form is selected -->
        <ParticipantFormsPanel
          v-if="!routeQuery('formId') && resolvedEnrollmentId"
          style="margin-bottom: 16px"
          :study-code="scopeStore.activeStudyId.trim() || undefined"
          :subject-code="selectedSubjectId || 'participant'"
          :enrollment-id="resolvedEnrollmentId"
          :form-location="ecrfFormLocation"
          :new-form-location="null"
        />

        <!-- The participant's case report form, a kernel form instance saved a page at a time (ADR-2622). -->
        <EcrfFormPanel
          :form-id="routeQuery('formId')"
          :enrollment-id="resolvedEnrollmentId || null"
          :enrollment-context="ecrfEnrollmentContext"
          :schedule-loading="
            !routeQuery('formId') && canvasStore.scheduleLoading
          "
          :schedule-error="
            !routeQuery('formId') ? canvasStore.scheduleError : null
          "
          :collection-spec-id="routeQuery('collectionSpecId')"
          :planned-activity-id="
            routeQuery('plannedActivityId') ?? routeQuery('visitId')
          "
          :highlight-item-id="routeQuery('itemId')"
          :form-location="ecrfFormLocation"
          :observation-location="ecrfObservationLocation"
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
          @retry-schedule="onRetryScheduleOptions"
          @dirty-change="emit('dirty-change', $event)"
          @form-loaded="emit('form-loaded', $event)"
          @closed="emit('closed')"
        />
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, getCurrentInstance, onMounted, ref, watch } from "vue";
import { useRoute } from "vue-router";
import EcrfFormPanel from "../components/ecrf/EcrfFormPanel.vue";
import ParticipantFormsPanel from "../components/canvas/ParticipantFormsPanel.vue";
import StudySubjectsBar from "../components/clinical/StudySubjectsBar.vue";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import { useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import {
  canManageParticipants,
  canManageQueries,
  canVerifySource,
} from "../canvas/authority";
import { findKernelEnrollmentRef } from "../canvas/kernelStudy";
import { collectionInstanceRef, observationNodeRef } from "../canvas/nodeRef";

const props = defineProps({
  loadedFormId: { type: String, default: null },
  loadedFormTitle: { type: String, default: "" },
});

const emit = defineEmits(["dirty-change", "form-loaded", "closed"]);

const scopeStore = useClinicalScopeStore();
const authStore = useAuthStore();
const canvasStore = useStudyCanvasStore();
const route = useRoute();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();
const instance = getCurrentInstance();
const routerLinkComponent =
  instance?.appContext.components.RouterLink ||
  instance?.appContext.components["router-link"] ||
  null;

function routeQuery(name) {
  const value = route?.query?.[name];
  return typeof value === "string" && value.length > 0 ? value : null;
}

const routeStudyId = ref(routeQuery("studyId") ?? "");
const routeSubjectId = ref(
  routeQuery("subjectId") ?? routeQuery("participant") ?? ""
);
const routeEnrollmentId = ref(routeQuery("enrollmentId") ?? "");

const selectedSubjectId = ref(
  routeSubjectId.value || scopeStore.activeSubjectId || ""
);
const resolvedEnrollmentId = ref(routeEnrollmentId.value);
const enrollmentStatusMessage = ref("");
const selectionChangedFromInitial = ref(false);
let enrollmentLookupSeq = 0;

const activeUserRole = computed(() => {
  if (authStore.isAuthenticated) {
    const roles = authStore.normalizedRoles || [];
    if (roles.includes("cra") || roles.includes("monitor")) return "cra";
    if (roles.includes("site_investigator") || roles.includes("crc"))
      return "site_investigator";
    return roles[0] || "site_investigator";
  }
  return "site_investigator";
});

const selectedSubject = computed(
  () =>
    scopeStore.subjects.find((s) => s.id === selectedSubjectId.value) ?? null
);

const scheduleForActiveStudy = computed(() => {
  const code = scopeStore.activeStudyId.trim();
  if (!code) return null;
  return Object.prototype.hasOwnProperty.call(
    canvasStore.scheduleOptionsByStudy,
    code
  )
    ? canvasStore.scheduleOptionsByStudy[code]
    : canvasStore.studyCode === code || !canvasStore.studyCode
      ? canvasStore.scheduleOptions
      : null;
});

const ecrfEnrollmentContext = computed(() => {
  if (!resolvedEnrollmentId.value) return null;
  return {
    enrollmentId: resolvedEnrollmentId.value,
    subjectCode: selectedSubjectId.value.trim() || undefined,
    collectionSpecs: scheduleForActiveStudy.value?.collectionSpecs ?? [],
    plannedActivities: scheduleForActiveStudy.value?.plannedActivities ?? [],
    scheduleLoading: !routeQuery("formId") && canvasStore.scheduleLoading,
    scheduleError: !routeQuery("formId") ? canvasStore.scheduleError : null,
  };
});

function onRetryScheduleOptions() {
  const code = scopeStore.activeStudyId.trim();
  if (code) {
    void canvasStore.retryScheduleOptions(code);
  }
}

watch(
  [() => scopeStore.activeStudyId.trim(), () => routeQuery("formId")],
  ([code, activeFormId]) => {
    if (code && !activeFormId) {
      void canvasStore.loadScheduleOptions(code);
    }
  },
  { immediate: true }
);

function buildEcrfScopeQuery(extra = {}) {
  const query = {};
  const studyCode =
    scopeStore.activeStudyId.trim() || routeQuery("studyId") || "";
  if (studyCode) query.studyId = studyCode;
  const siteCode =
    selectedSubject.value?.siteId ||
    scopeStore.activeSiteId.trim() ||
    routeQuery("siteId") ||
    "";
  if (siteCode) query.siteId = siteCode;
  const subjectCode =
    selectedSubjectId.value.trim() || routeQuery("subjectId") || "";
  if (subjectCode) query.subjectId = subjectCode;
  const enrollment =
    resolvedEnrollmentId.value || routeQuery("enrollmentId") || "";
  if (enrollment) query.enrollmentId = enrollment;
  for (const [key, val] of Object.entries(extra)) {
    if (val !== undefined && val !== null && val !== "") {
      query[key] = String(val);
    }
  }
  return query;
}

function buildEcrfReturnUrl(formId, itemId) {
  const query = buildEcrfScopeQuery({
    formId,
    itemId: itemId || undefined,
  });
  const params = new URLSearchParams();
  for (const [key, val] of Object.entries(query)) {
    if (val) params.set(key, String(val));
  }
  const qs = params.toString();
  return qs ? `/ecrf?${qs}` : "/ecrf";
}

function ecrfFormLocation(formId, itemId) {
  if (!formId) return null;
  return {
    path: "/ecrf",
    query: buildEcrfScopeQuery({
      formId,
      itemId: itemId || undefined,
    }),
  };
}

function ecrfObservationLocation(observationId, formId, itemId) {
  const obsRef = observationNodeRef(observationId);
  if (!obsRef) return null;
  const effectiveFormId =
    formId || props.loadedFormId || routeQuery("formId") || "";
  const effectiveItemId = itemId || routeQuery("itemId") || "";
  const formRef = effectiveFormId
    ? collectionInstanceRef(effectiveFormId)
    : null;
  const studyCode =
    scopeStore.activeStudyId.trim() || routeQuery("studyId") || "";
  const returnTo = buildEcrfReturnUrl(effectiveFormId, effectiveItemId);
  const returnLabel = props.loadedFormTitle
    ? `Back to ${props.loadedFormTitle}`
    : "Back to form";
  return {
    name: "observation",
    params: { observationRef: obsRef },
    query: {
      ...(studyCode ? { studyId: studyCode } : {}),
      ...(formRef ? { form: formRef } : {}),
      ...(effectiveItemId ? { item: effectiveItemId } : {}),
      returnTo,
      returnLabel,
    },
  };
}

const showManageParticipantsLink = computed(
  () => Boolean(authStore.isAuthenticated) && canManageParticipants(authStore)
);

const manageParticipantsLocation = computed(() => {
  const code = scopeStore.activeStudyId.trim();
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
  const code = scopeStore.activeStudyId.trim();
  if (code) {
    return `${basePrefix}studies/${encodeURIComponent(code)}?stage=conduct&section=participants`;
  }
  return `${basePrefix}studies`;
});

function selectListedSubject() {
  if (!scopeStore.subjects.some((s) => s.id === selectedSubjectId.value)) {
    selectedSubjectId.value = scopeStore.subjects[0]?.id ?? "";
  }
}

async function resolveSelectedEnrollment() {
  const studyCode = scopeStore.activeStudyId.trim();
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
    () => scopeStore.activeStudyId,
    selectedSubjectId,
    () => selectedSubject.value?.siteId ?? "",
  ],
  ([nextStudy, nextSubject, nextSubjectSite], [prevStudy, prevSubject]) => {
    if (nextSubject !== scopeStore.activeSubjectId) {
      scopeStore.activeSubjectId = nextSubject;
    }
    if (nextSubjectSite && nextSubjectSite !== scopeStore.activeSiteId) {
      scopeStore.activeSiteId = nextSubjectSite;
    }
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
    () => routeQuery("siteId") ?? routeQuery("site") ?? "",
    () => routeQuery("subjectId") ?? routeQuery("participant") ?? "",
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

    scopeStore.activeSiteId = nextSite;
    const studyChanged = Boolean(
      nextStudy && nextStudy !== scopeStore.activeStudyId
    );
    if (nextStudy) {
      setActiveStudyCode(nextStudy);
      scopeStore.activeStudyId = nextStudy;
    } else if (prevStudy && !activeStudyCode.value) {
      scopeStore.activeStudyId = "";
    }
    scopeStore.activeSubjectId = nextSubject;
    selectedSubjectId.value = nextSubject;
    if (studyChanged && nextStudy) {
      await scopeStore.loadSubjects(nextStudy);
    }
    if (nextSubject || studyChanged) {
      selectListedSubject();
    }
    await resolveSelectedEnrollment();
  }
);

onMounted(async () => {
  if (route && route.query) {
    if (route.query.studyId) {
      const incomingStudy = String(route.query.studyId).trim();
      if (incomingStudy) {
        setActiveStudyCode(incomingStudy);
        scopeStore.activeStudyId = incomingStudy;
      }
    } else if (!scopeStore.activeStudyId && activeStudyCode.value) {
      scopeStore.activeStudyId = activeStudyCode.value;
    }
    const incomingSite = routeQuery("siteId") ?? routeQuery("site");
    if (incomingSite) {
      scopeStore.activeSiteId = incomingSite;
    }
    const incomingSubject =
      routeQuery("subjectId") ?? routeQuery("participant");
    if (incomingSubject) {
      scopeStore.activeSubjectId = incomingSubject;
      selectedSubjectId.value = incomingSubject;
    }
  } else if (!scopeStore.activeStudyId && activeStudyCode.value) {
    scopeStore.activeStudyId = activeStudyCode.value;
  }
  if (scopeStore.activeStudyId) {
    await scopeStore.loadSubjects(scopeStore.activeStudyId);
  }
  selectListedSubject();
  await resolveSelectedEnrollment();
});
</script>
