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
      <h2 id="observation-view-title" tabindex="-1">Observation</h2>

      <p v-if="loading && !observation" role="status">Loading observation…</p>
      <div v-else-if="loadError" class="observation-error" role="alert">
        <p class="observation-error-summary">
          The observation could not be loaded.
        </p>
        <SupportDetails :detail="loadError" />
      </div>

      <template v-if="observation">
        <dl class="observation-summary">
          <dt>Concept</dt>
          <dd data-testid="observation-concept">
            <NodeRefChip
              v-if="normalizedConceptRef"
              :node-ref="normalizedConceptRef"
            />
            <template v-else>{{ observation.concept }}</template>
          </dd>
          <dt>Current value</dt>
          <dd
            data-testid="observation-value"
            class="tabular-nums"
            :data-tabular-nums="isTabularObservation ? 'true' : undefined"
          >
            {{ formatObservationValue(observation.value) }}
          </dd>
          <dt>Version</dt>
          <dd data-testid="observation-version" class="tabular-nums">
            {{ observation.version }}
          </dd>
          <dt>Observed</dt>
          <dd class="observation-timestamp tabular-nums">
            {{ observation.observed_at }}
          </dd>
          <dt>Source verification</dt>
          <dd data-testid="observation-sdv">
            <template v-if="activeVerification">
              <span
                class="clinical-badge clinical-badge--verified"
                data-testid="observation-sdv-badge"
              >
                Verified (rev {{ currentRevisionNumber }})
              </span>
              <span class="observation-sdv-meta tabular-nums">
                by {{ activeVerification.asserted_by }} at
                {{ activeVerification.asserted_at }}
              </span>
            </template>
            <template v-else-if="supersededVerification">
              <span
                class="clinical-badge clinical-badge--warning"
                data-testid="observation-sdv-badge"
              >
                Needs re-verification
              </span>
              <span class="observation-sdv-meta tabular-nums">
                (verified at rev {{ supersededVerification.revision }} by
                {{ supersededVerification.asserted_by }})
              </span>
            </template>
            <span
              v-else-if="hasRecordedValue"
              class="clinical-badge clinical-badge--draft"
              data-testid="observation-sdv-badge"
            >
              Unverified
            </span>
            <span v-else data-testid="observation-sdv-badge">No value</span>
          </dd>
          <dt>Status</dt>
          <dd data-testid="observation-status">
            <template v-if="observationLocks.length > 0">
              <span
                v-for="held in observationLocks"
                :key="String(held.lock_id)"
                class="observation-lock tabular-nums"
              >
                {{ held.kind }} at {{ lockScopeLabel(held.scope) }} by
                <NodeRefChip
                  v-if="normalizeLookupNodeRef(String(held.locked_by))"
                  :node-ref="normalizeLookupNodeRef(String(held.locked_by))!"
                />
                <template v-else>{{ held.locked_by }}</template>
                at {{ held.locked_at }}
              </span>
            </template>
            <span v-else-if="!canCorrect" data-testid="observation-readonly">
              Read-only
            </span>
            <template v-else> Open for correction </template>
          </dd>
        </dl>

        <nav
          v-if="hasRouter && (owningFormLocation || studyQueriesLocation)"
          class="observation-context-links"
          data-testid="observation-context-links"
          aria-label="Related clinical workspaces"
        >
          <component
            :is="routerLinkTag"
            v-if="owningFormLocation"
            id="observation-open-form"
            class="observation-context-link"
            :to="owningFormLocation"
          >
            Open form{{ effectiveItemId ? ` (${effectiveItemId})` : "" }}
          </component>
          <component
            :is="routerLinkTag"
            v-if="studyQueriesLocation"
            id="observation-open-queries"
            class="observation-context-link"
            :to="studyQueriesLocation"
          >
            Open study queries
          </component>
        </nav>

        <ScopeLockControls
          :study-ref="observationStudyRef"
          :scope-ref="cleanObservationRef"
          scope-label="observation"
          :direct-locks="directObservationLocks"
          :covering-locks="ancestorObservationLocks"
          id-prefix="observation-lock"
          @changed="load"
        />

        <div
          v-if="activeVerification && !locked && canCorrect"
          id="observation-sdv-edit-warning"
          data-testid="observation-sdv-edit-warning"
          class="observation-sdv-warning tabular-nums"
          role="status"
        >
          This value is source data verified (revision
          {{ currentRevisionNumber }}). Saving a correction drops SDV
          verification to Needs re-verification and alerts the CRA.
        </div>

        <ObservationCorrectionForm
          v-if="!locked && canCorrect"
          :codelist-options="observation.expected_codelist_options"
          :new-value="newValue"
          :missing-reason="missingReason"
          :missing-detail="missingDetail"
          :saving="saving"
          :can-save="canSave"
          :parse-error="parseError"
          :correction-described-by="correctionDescribedBy"
          :is-tabular-observation="isTabularObservation"
          :unit="unit"
          @update:new-value="newValue = $event"
          @update:missing-reason="setMissingReason"
          @update:missing-detail="setMissingDetail"
          @submit="askReason"
        />

        <div
          v-if="outcome"
          data-testid="observation-outcome"
          :class="[
            'observation-outcome',
            'tabular-nums',
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

        <ObservationQueriesAndAssertions
          :open-query-threads="openQueryThreads"
          :observation-assertions="observationAssertions"
          :current-revision-number="currentRevisionNumber"
          :has-router="hasRouter"
          :router-link-tag="routerLinkTag"
          :owning-form-location="owningFormLocation"
          :study-queries-location="studyQueriesLocation"
        />

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
                v-for="revision in observationRevisions"
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
              <tr v-for="entry in observationAudit" :key="entry.sequence">
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
      :description="reasonModalDescription"
      confirm-text="Save correction"
      @confirm="onReason"
      @cancel="reasonOpen = false"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import ScopeLockControls from "../components/canvas/ScopeLockControls.vue";
import NodeRefChip from "../components/canvas/NodeRefChip.vue";
import SupportDetails from "../components/canvas/SupportDetails.vue";
import ObservationCorrectionForm from "../components/observation/ObservationCorrectionForm.vue";
import ObservationQueriesAndAssertions from "../components/observation/ObservationQueriesAndAssertions.vue";
import ReasonForChangeModal from "../components/ReasonForChangeModal.vue";
import { useObservationCorrection } from "../composables/useObservationCorrection";
import { useObservationScopeNavigation } from "../composables/useObservationScopeNavigation";
import {
  lockScopeLabel,
  useObservationViewModel,
} from "../composables/useObservationViewModel";
import type { KernelObservation } from "../api/kernelApi";
import { formatObservationValue } from "../api/observationValue";
import { normalizeLookupNodeRef } from "../canvas/nodeResolver";
import { canCorrectObservations } from "../canvas/authority";
import { useAuthStore } from "../stores/auth";

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
  (e: "dirty-change", dirty: boolean): void;
}>();

const authStore = useAuthStore();
const canCorrect = computed(() => canCorrectObservations(authStore));

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

const {
  activeVerification,
  ancestorObservationLocks,
  canSave,
  cleanObservationRef,
  correctionDescribedBy,
  currentRevisionNumber,
  directObservationLocks,
  discardCorrection,
  hasRecordedValue,
  isDirty,
  isTabularObservation,
  missingDetail,
  missingReason,
  newValue,
  normalizedConceptRef,
  observationAssertions,
  observationAudit,
  observationLocks,
  observationRevisions,
  openQueryThreads,
  parseError,
  parsed,
  reasonModalDescription,
  reasonOpen,
  setMissingDetail,
  setMissingReason,
  supersededVerification,
  unit,
} = useObservationViewModel({
  observation,
  locked,
  canCorrect,
  observationRef: () => props.observationRef,
  embedded: () => props.embedded,
  onDirtyChange: (dirty) => emit("dirty-change", dirty),
});

const {
  routerLinkTag,
  hasRouter,
  effectiveReturnTo,
  effectiveReturnLabel,
  observationStudyRef,
  effectiveItemId,
  owningFormLocation,
  studyQueriesLocation,
} = useObservationScopeNavigation({
  props,
  observation,
  isDirty,
  onLoaded: (obs) => emit("loaded", obs),
});

defineExpose({ dirty: isDirty, discardCorrection });

function askReason() {
  if (canSave.value && !saving.value) reasonOpen.value = true;
}

async function onReason(reason: string) {
  reasonOpen.value = false;
  if (parsed.value?.ok !== true) return;
  await correct(parsed.value.value, reason);
  if (outcome.value?.kind === "saved") discardCorrection();
}

function handleDemoReset() {
  discardCorrection();
  outcome.value = null;
  void load();
}

onMounted(() => {
  if (typeof window !== "undefined") {
    window.addEventListener("cadence:demo-reset", handleDemoReset);
  }
  void load();
});

onUnmounted(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("cadence:demo-reset", handleDemoReset);
  }
});

watch(
  () => props.observationRef,
  () => {
    discardCorrection();
    void load();
  }
);
</script>

<style scoped src="../components/observation/observationView.css"></style>
<style scoped>
[data-testid="observation-concept"],
[data-testid="observation-value"],
[data-testid="observation-version"],
.observation-timestamp,
#observation-new-value,
[data-testid="observation-unit"] {
  font-family: var(--semantic-font-mono);
}

[data-testid="observation-value"],
[data-testid="observation-version"],
.observation-timestamp,
.observation-lock,
#observation-new-value,
.observation-outcome {
  font-variant-numeric: tabular-nums;
}
</style>
