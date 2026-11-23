<template>
  <div id="section-observation" class="dashboard-section active">
    <component
      :is="routerLinkTag"
      v-if="hasRouter && effectiveReturnTo"
      id="observation-back"
      class="back"
      :to="effectiveReturnTo"
    >
      {{ effectiveReturnLabel }}
    </component>

    <div class="card">
      <h2>Observation</h2>

      <p v-if="loading && !observation" role="status">Loading observation…</p>
      <p v-else-if="loadError" class="observation-error" role="alert">
        The observation could not be loaded: {{ loadError }}
      </p>

      <template v-if="observation">
        <dl class="observation-summary">
          <dt>Concept</dt>
          <dd data-testid="observation-concept">
            {{ observation.concept }}
          </dd>
          <dt>Current value</dt>
          <dd data-testid="observation-value">
            {{ formatObservationValue(observation.value) }}
          </dd>
          <dt>Version</dt>
          <dd data-testid="observation-version">
            {{ observation.version }}
          </dd>
          <dt>Observed</dt>
          <dd>{{ observation.observed_at }}</dd>
          <dt>Status</dt>
          <dd data-testid="observation-status">
            <template v-if="observation.locks.length > 0">
              <span
                v-for="held in observation.locks"
                :key="String(held.lock_id)"
                class="observation-lock"
              >
                {{ held.kind }} at {{ lockScopeLabel(held.scope) }} by
                {{ held.locked_by }} at {{ held.locked_at }}
              </span>
            </template>
            <span v-else-if="!canCorrect" data-testid="observation-readonly">
              Read-only
            </span>
            <template v-else> Open for correction </template>
          </dd>
        </dl>

        <ScopeLockControls
          :study-ref="observationStudyRef"
          :scope-ref="cleanObservationRef"
          scope-label="observation"
          :direct-locks="directObservationLocks"
          id-prefix="observation-lock"
          @changed="load"
        />

        <form
          v-if="!locked && canCorrect"
          class="observation-correction"
          @submit.prevent="askReason"
        >
          <label for="observation-new-value">Corrected value</label>
          <input
            id="observation-new-value"
            v-model="newValue"
            type="text"
            autocomplete="off"
            :disabled="saving"
            :aria-invalid="parseError !== null"
            aria-describedby="observation-new-value-hint"
          />
          <span v-if="unit" data-testid="observation-unit">{{ unit }}</span>
          <button
            id="observation-save"
            class="btn btn-primary"
            type="submit"
            :disabled="saving || !canSave"
          >
            {{ saving ? "Saving…" : "Save correction" }}
          </button>
          <p
            id="observation-new-value-hint"
            class="observation-hint"
            :role="parseError ? 'alert' : undefined"
          >
            {{ parseError ?? "" }}
          </p>
        </form>

        <div
          v-if="outcome"
          data-testid="observation-outcome"
          :class="[
            'observation-outcome',
            `observation-outcome--${outcome.kind}`,
          ]"
          :role="outcome.kind === 'saved' ? 'status' : 'alert'"
        >
          <template v-if="outcome.kind === 'saved'">
            Correction saved. The value is now
            {{ formatObservationValue(outcome.state.value) }} at version
            {{ outcome.state.version }}, with your reason in the audit trail.
          </template>
          <template v-else>
            {{ outcome.message }}
          </template>
        </div>

        <h3>Revisions</h3>
        <div
          class="observation-table-scroll"
          data-testid="observation-revisions-scroll"
          role="region"
          aria-label="Observation revisions"
          tabindex="0"
        >
          <table
            class="observation-revisions"
            data-testid="observation-revisions"
          >
            <thead>
              <tr>
                <th scope="col">Revision</th>
                <th scope="col">Value</th>
                <th scope="col">Reason</th>
                <th scope="col">Recorded by</th>
                <th scope="col">Recorded</th>
                <th scope="col">Superseded</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="revision in observation.revisions"
                :key="revision.number"
              >
                <td>{{ revision.number }}</td>
                <td>{{ formatObservationValue(revision.value) }}</td>
                <td>{{ revision.reason_for_change }}</td>
                <td>{{ revision.recorded_by }}</td>
                <td>{{ revision.recorded_at }}</td>
                <td>{{ revision.recorded_until ?? "Current" }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <h3>Audit trail</h3>
        <div
          class="observation-table-scroll"
          data-testid="observation-audit-scroll"
          role="region"
          aria-label="Observation audit trail"
          tabindex="0"
        >
          <table class="observation-audit" data-testid="observation-audit">
            <thead>
              <tr>
                <th scope="col">Sequence</th>
                <th scope="col">When</th>
                <th scope="col">Who</th>
                <th scope="col">Action</th>
                <th scope="col">Version</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in observation.audit" :key="entry.sequence">
                <td>{{ entry.sequence }}</td>
                <td>{{ entry.recorded_at }}</td>
                <td>{{ entry.actor_id }}</td>
                <td>{{ entry.action }} {{ entry.subject }}</td>
                <td>{{ entry.version ?? "" }}</td>
                <td>{{ entry.reason_for_change }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
    </div>

    <ReasonForChangeModal
      :show="reasonOpen"
      id-prefix="observation-"
      title="Reason for correction"
      confirm-text="Save correction"
      @confirm="onReason"
      @cancel="reasonOpen = false"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, onMounted, ref, watch } from "vue";
import type { RouteLocationRaw, Router } from "vue-router";
import ScopeLockControls, {
  type DirectScopeLockItem,
} from "../components/canvas/ScopeLockControls.vue";
import ReasonForChangeModal from "../components/ReasonForChangeModal.vue";
import { useObservationCorrection } from "../composables/useObservationCorrection";
import { kernelApi, type KernelObservation } from "../api/kernelApi";
import {
  correctionUnit,
  formatObservationValue,
  parseCorrection,
  sameObservationValue,
  type ExpectedItemSpec,
} from "../api/observationValue";
import {
  collectionInstanceId,
  collectionInstanceRef,
  observationNodeRef,
} from "../canvas/nodeRef";
import { canCorrectObservations } from "../canvas/authority";
import { findKernelStudyRef } from "../canvas/kernelStudy";
import { useAuthStore } from "../stores/auth";

/**
 * Corrects one kernel observation (ADR-2620). The value shown is always the kernel's: the screen keeps no local
 * copy, and reports a correction as saved only after the kernel has committed it. A correction keeps the current
 * value's kind (ADR-2623): a quantity is corrected in its unit, a code in its code system version. A Locked or
 * Frozen lock at the value, its participant, its site or its study (ADR-2621) closes it to correction.
 */
const props = defineProps<{
  observationRef: string;
  studyCode?: string | null;
  studyRef?: string | null;
  embedded?: boolean;
  returnTo?: RouteLocationRaw | null;
  returnLabel?: string;
}>();

const emit = defineEmits<{
  (e: "loaded", observation: KernelObservation): void;
}>();

const authStore = useAuthStore();
const canCorrect = computed(() => canCorrectObservations(authStore));

const instance = getCurrentInstance();
const activeRouter = (instance?.appContext.config.globalProperties.$router ??
  null) as Router | null;
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";
const hasRouter = computed(() => activeRouter !== null);

function queryString(name: string): string {
  const raw = activeRouter?.currentRoute.value?.query?.[name];
  return typeof raw === "string" ? raw.trim() : "";
}

function appendFormAndItemToReturnPath(
  rawPath: string,
  formVal: string,
  itemVal: string
): string {
  if (!formVal && !itemVal) return rawPath;
  try {
    const parsed = new URL(rawPath, "https://cadence.local");
    const formUuid = collectionInstanceId(formVal);
    const formRef = collectionInstanceRef(formVal) ?? formVal;
    if (parsed.pathname === "/ecrf") {
      if (formUuid && !parsed.searchParams.has("formId")) {
        parsed.searchParams.set("formId", formUuid);
      }
      if (itemVal && !parsed.searchParams.has("itemId")) {
        parsed.searchParams.set("itemId", itemVal);
      }
    } else if (parsed.pathname.startsWith("/studies/")) {
      if (formRef && !parsed.searchParams.has("form")) {
        parsed.searchParams.set("form", formRef);
      }
      if (itemVal && !parsed.searchParams.has("item")) {
        parsed.searchParams.set("item", itemVal);
      }
    }
    return `${parsed.pathname}${parsed.search}${parsed.hash}`;
  } catch {
    return rawPath;
  }
}

const effectiveReturnTo = computed<RouteLocationRaw | null>(() => {
  if (props.returnTo) return props.returnTo;
  if (!activeRouter) return null;
  const rawForm = queryString("form") || queryString("formId");
  const rawItem = queryString("item") || queryString("itemId");
  const queryReturn = activeRouter.currentRoute.value?.query?.returnTo;
  if (
    typeof queryReturn === "string" &&
    queryReturn.startsWith("/") &&
    !queryReturn.startsWith("//")
  ) {
    return appendFormAndItemToReturnPath(queryReturn, rawForm, rawItem);
  }
  const studyFromContext =
    (props.studyCode && props.studyCode.trim()) ||
    queryString("studyId") ||
    queryString("study");
  const formRef = rawForm ? (collectionInstanceRef(rawForm) ?? rawForm) : "";
  const formUuid = rawForm ? collectionInstanceId(rawForm) : null;
  if (studyFromContext && activeRouter.hasRoute("study")) {
    const sectionVal = queryString("section");
    const isDataWorklist =
      sectionVal === "queries" || sectionVal === "validation";
    const siteVal = queryString("site") || queryString("siteId");
    return {
      name: "study",
      params: { studyCode: studyFromContext },
      query: {
        stage: isDataWorklist ? "data" : queryString("stage") || "conduct",
        ...(isDataWorklist ? { section: sectionVal } : {}),
        ...(siteVal ? { site: siteVal } : {}),
        ...(formRef ? { form: formRef } : {}),
        ...(rawItem ? { item: rawItem } : {}),
      },
    };
  }
  if ((formUuid || rawItem) && activeRouter.hasRoute("ecrf")) {
    return {
      name: "ecrf",
      query: {
        ...(formUuid ? { formId: formUuid } : {}),
        ...(rawItem ? { itemId: rawItem } : {}),
      },
    };
  }
  if (activeRouter.hasRoute("study")) {
    return { name: "study" };
  }
  return "/";
});

const effectiveReturnLabel = computed(() => {
  if (props.returnTo) return props.returnLabel || "Back to study";
  const queryLabel = activeRouter?.currentRoute.value?.query?.returnLabel;
  if (typeof queryLabel === "string" && queryLabel.trim().length > 0) {
    return queryLabel.trim();
  }
  const rawForm = queryString("form") || queryString("formId");
  if (rawForm) return props.returnLabel || "Back to form";
  return props.returnLabel || "Back to studies";
});

const {
  observation,
  loading,
  saving,
  loadError,
  outcome,
  locked,
  load,
  correct,
} = useObservationCorrection(() => props.observationRef);

/** What a lock covers, in the words a data manager uses (spec §10.5). */
const SCOPE_LABELS: Record<string, string> = {
  study: "study level",
  study_site: "site level",
  enrollment: "participant level",
  collection_instance: "form level",
  observation: "this value",
};

const observationStudyRef = ref<string | null>(null);
const cleanObservationRef = computed(() => {
  const raw = observation.value?.ref ?? props.observationRef;
  const at = raw.indexOf("@");
  return at < 0 ? raw : raw.slice(0, at);
});

const directObservationLocks = computed<DirectScopeLockItem[]>(() => {
  const current = observation.value;
  if (!current) return [];
  return current.locks
    .filter((held) => String(held.scope) === cleanObservationRef.value)
    .map((held) => {
      const idText = String(held.lock_id);
      return {
        lockRef: idText.startsWith("assertion:")
          ? idText
          : `assertion:${idText}`,
        kind: held.kind,
      };
    });
});

let studyLookupSeq = 0;

watch(
  [() => props.studyRef, () => props.studyCode],
  async ([explicitStudyRef, code]) => {
    const seq = ++studyLookupSeq;
    if (explicitStudyRef) {
      observationStudyRef.value = explicitStudyRef;
      return;
    }
    if (!code) {
      if (props.embedded) observationStudyRef.value = null;
      return;
    }
    try {
      const resolved = await findKernelStudyRef(code);
      if (seq === studyLookupSeq) {
        observationStudyRef.value = resolved;
      }
    } catch {
      if (seq === studyLookupSeq) {
        observationStudyRef.value = null;
      }
    }
  },
  { immediate: true }
);

function lockScopeLabel(scope: unknown): string {
  const kind = String(scope).split(":")[0];
  return SCOPE_LABELS[kind] ?? kind;
}

const newValue = ref("");
const reasonOpen = ref(false);

const expectedSpec = computed<ExpectedItemSpec | null>(() => {
  const obs = observation.value;
  if (!obs) return null;
  return {
    valueKind: obs.expected_value_kind ?? null,
    unit: obs.expected_unit ?? null,
    codeSystemVersionId: obs.expected_code_system_version_id ?? null,
  };
});

const unit = computed(() =>
  correctionUnit(observation.value?.value, expectedSpec.value)
);

const parsed = computed(() => {
  const current = observation.value?.value;
  if (!current || newValue.value.trim().length === 0) return null;
  return parseCorrection(newValue.value, current, expectedSpec.value);
});

const parseError = computed(() =>
  parsed.value && !parsed.value.ok ? parsed.value.message : null
);

const canSave = computed(
  () =>
    parsed.value?.ok === true &&
    !sameObservationValue(parsed.value.value, observation.value?.value)
);

function askReason() {
  if (canSave.value && !saving.value) reasonOpen.value = true;
}

async function onReason(reason: string) {
  reasonOpen.value = false;
  if (parsed.value?.ok !== true) return;
  await correct(parsed.value.value, reason);
  if (outcome.value?.kind === "saved") newValue.value = "";
}

let redirectRequest = 0;

watch(
  observation,
  async (obs) => {
    if (!obs) return;
    emit("loaded", obs);
    if (
      props.embedded ||
      !activeRouter ||
      activeRouter.currentRoute.value.name !== "observation" ||
      typeof kernelApi.getZoomPath !== "function"
    ) {
      return;
    }
    const explicitReturn = activeRouter.currentRoute.value.query?.returnTo;
    if (
      typeof explicitReturn === "string" &&
      explicitReturn.startsWith("/") &&
      !explicitReturn.startsWith("//")
    ) {
      return;
    }
    const request = ++redirectRequest;
    try {
      const steps = await kernelApi.getZoomPath(obs.enrollment_ref);
      if (request !== redirectRequest) return;
      const studyStep = steps.find(
        (s) => s.level === "study" && s.mask === null
      );
      if (!props.studyRef && !props.studyCode) {
        observationStudyRef.value = studyStep?.ref ?? null;
      }
      const enrollmentStep = steps.find(
        (s) => s.level === "enrollment" && s.mask === null
      );
      if (!studyStep?.label || !enrollmentStep) return;
      const siteStep = steps.find(
        (s) => s.level === "study_site" && s.mask === null && s.label
      );
      const rawForm = queryString("form") || queryString("formId");
      const formRef = rawForm
        ? (collectionInstanceRef(rawForm) ?? rawForm)
        : "";
      const rawItem = queryString("item") || queryString("itemId");
      const sectionVal = queryString("section");
      const isDataWorklist =
        sectionVal === "queries" || sectionVal === "validation";
      const canonicalRef = observationNodeRef(obs.ref) ?? props.observationRef;
      await activeRouter.replace({
        name: "study",
        params: { studyCode: studyStep.label },
        query: {
          stage: isDataWorklist ? "data" : queryString("stage") || "conduct",
          ...(isDataWorklist ? { section: sectionVal } : {}),
          ...(siteStep?.label ? { site: siteStep.label } : {}),
          ...(formRef ? { form: formRef } : {}),
          ...(rawItem ? { item: rawItem } : {}),
          observation: canonicalRef,
        },
      });
    } catch {
      // Keep the standalone view if scope-path resolution is unauthorized or unavailable.
    }
  },
  { immediate: true }
);

onMounted(load);
watch(() => props.observationRef, load);
</script>

<style scoped>
.back {
  display: inline-block;
  margin-bottom: 12px;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.85rem;
}
.observation-summary {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 16px;
}
.observation-correction {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
  margin: 16px 0;
}
.observation-hint {
  flex-basis: 100%;
  margin: 0;
  color: var(--semantic-color-error-text-dark);
}
.observation-outcome {
  padding: 8px 12px;
  border-radius: 6px;
  margin-bottom: 16px;
}
.observation-outcome--saved {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.observation-outcome--stale,
.observation-outcome--locked,
.observation-outcome--failed,
.observation-error {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
#section-observation,
#section-observation > .card {
  min-width: 0;
  max-width: 100%;
}
.observation-table-scroll {
  overflow-x: auto;
  max-width: 100%;
  margin-bottom: 16px;
  padding: 2px;
}
.observation-table-scroll:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: 4px;
}
table {
  width: 100%;
  min-width: 46rem;
  border-collapse: collapse;
}
th,
td {
  text-align: left;
  padding: 4px 8px;
  border-bottom: 1px solid var(--semantic-color-border);
}
.observation-revisions th:nth-child(1),
.observation-revisions td:nth-child(1),
.observation-audit th:nth-child(1),
.observation-audit td:nth-child(1),
.observation-audit th:nth-child(5),
.observation-audit td:nth-child(5) {
  min-width: 5rem;
}
.observation-revisions th:nth-child(2),
.observation-revisions td:nth-child(2),
.observation-revisions th:nth-child(4),
.observation-revisions td:nth-child(4),
.observation-audit th:nth-child(3),
.observation-audit td:nth-child(3) {
  min-width: 7rem;
}
.observation-revisions th:nth-child(3),
.observation-revisions td:nth-child(3),
.observation-audit th:nth-child(4),
.observation-audit td:nth-child(4),
.observation-audit th:nth-child(6),
.observation-audit td:nth-child(6) {
  min-width: 9rem;
}
.observation-revisions th:nth-child(5),
.observation-revisions td:nth-child(5),
.observation-revisions th:nth-child(6),
.observation-revisions td:nth-child(6),
.observation-audit th:nth-child(2),
.observation-audit td:nth-child(2) {
  min-width: 8.5rem;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
