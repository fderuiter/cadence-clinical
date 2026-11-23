<template>
  <section
    id="participant-record"
    class="record"
    aria-labelledby="participant-record-title"
    :aria-busy="record.loading"
  >
    <header class="record-header">
      <h2 id="participant-record-title">
        {{ participant.subject_id }}
      </h2>
      <span class="record-meta">
        {{ participantStateLabel(participant.status) }} ·
        {{ participant.site_id ? `Site ${participant.site_id}` : "No site" }}
      </span>
    </header>

    <p v-if="record.error" id="record-error" class="status-line" role="status">
      {{ participant.subject_id }}'s data couldn't be loaded.
      <button
        id="btn-retry-record"
        type="button"
        class="btn btn-secondary"
        @click="emit('retry')"
      >
        Try again
      </button>
    </p>

    <template v-else>
      <p v-if="record.loading" class="status-line" role="status">
        Loading {{ participant.subject_id }}'s data...
      </p>

      <div
        v-if="!record.loading && record.enrollmentId"
        class="participant-governance"
        data-testid="participant-governance"
      >
        <ScopeLockControls
          :study-ref="studyRef"
          :scope-ref="`enrollment:${record.enrollmentId}`"
          scope-label="participant"
          :direct-locks="directEnrollmentLocks"
          id-prefix="participant-lock"
          @changed="refreshEnrollmentLocks"
        />

        <div
          v-if="studyRef"
          class="casebook-bar"
          data-testid="participant-casebook-panel"
        >
          <label for="participant-casebook-format" class="casebook-label">
            Casebook format
          </label>
          <select
            id="participant-casebook-format"
            v-model="casebookFormat"
            class="casebook-select"
            :disabled="casebookBusy"
          >
            <option value="pdf">Certified PDF casebook (.pdf)</option>
            <option value="html">Printable HTML casebook</option>
            <option value="odm_xml">CDISC ODM v1.3.2 XML casebook</option>
            <option value="json">Structured JSON casebook</option>
          </select>
          <button
            id="btn-export-casebook"
            type="button"
            class="btn btn-secondary"
            :disabled="casebookBusy"
            @click="exportCasebook"
          >
            {{ casebookBusy ? "Exporting casebook…" : "Export casebook" }}
          </button>
          <span
            v-if="casebookOutcome"
            data-testid="participant-casebook-outcome"
            :class="[
              'casebook-outcome',
              casebookOutcome.kind === 'ok'
                ? 'casebook-outcome--ok'
                : 'casebook-outcome--error',
            ]"
            :role="casebookOutcome.kind === 'ok' ? 'status' : 'alert'"
            aria-live="polite"
          >
            {{ casebookOutcome.message }}
            <code
              v-if="casebookOutcome.sha256"
              data-testid="participant-casebook-sha256"
            >
              SHA-256: {{ casebookOutcome.sha256 }}
            </code>
          </span>
        </div>
      </div>

      <ParticipantFormsPanel
        :study-code="canvas.studyCode"
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
        :enrollment-loading="record.loading"
        :form-location="formLocation"
        :new-form-location="newFormLocation"
        :new-form-cell-location="newFormCellLocation"
        :handoff-location="handoffLocation"
        @forms-loaded="onFormsLoaded"
      />

      <QueriesPanel
        :study-code="canvas.studyCode"
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
        :enrollment-loading="record.loading"
        :can-manage="canManage"
        :form-titles="formTitles"
        :form-location="formLocation"
        :observation-location="observationLocation"
      />
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfFormSummary } from "../../api/ecrfFormsApi";
import type { SubjectSummary } from "../../api/executionApi";
import {
  kernelApi,
  triggerBrowserDownload,
  type CasebookExportFormat,
  type KernelLock,
} from "../../api/kernelApi";
import {
  fetchStudyLocks,
  toDirectScopeLocks,
  type DirectScopeLockItem,
} from "../../canvas/kernelLocks";
import { participantStateLabel } from "../../canvas/lifecycle";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import ParticipantFormsPanel from "./ParticipantFormsPanel.vue";
import QueriesPanel from "./QueriesPanel.vue";
import ScopeLockControls from "./ScopeLockControls.vue";

/**
 * One participant's data inside Clinical Canvas: opened case report forms, locks, casebooks, and queries on the
 * participant's enrollment (ADR-2622), linking directly to forms and observations in the zoom path.
 */
const props = defineProps<{
  participant: SubjectSummary;
  canManage: boolean;
  formLocation: (formId: string, itemId?: string | null) => RouteLocationRaw;
  newFormLocation: RouteLocationRaw;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  handoffLocation?: RouteLocationRaw | null;
  observationLocation: (
    observationId: string,
    formId: string,
    itemId?: string | null
  ) => RouteLocationRaw;
}>();
const emit = defineEmits<{
  retry: [];
}>();

const canvas = useStudyCanvasStore();
const record = computed(() => canvas.record);
const formTitles = ref<Record<string, string>>({});

function onFormsLoaded(summaries: EcrfFormSummary[]) {
  const next: Record<string, string> = {};
  for (const summary of summaries) {
    const base = summary.collection_title || summary.collection_spec_id;
    next[summary.form_id] = summary.planned_activity_id
      ? `${base} · ${summary.planned_activity_id}`
      : base;
  }
  formTitles.value = next;
}

const activeStudyLocks = ref<KernelLock[]>([]);
const casebookFormat = ref<CasebookExportFormat>("html");
const casebookBusy = ref(false);
const casebookOutcome = ref<{
  kind: "ok" | "error";
  message: string;
  sha256?: string | null;
} | null>(null);

const { studyRef, runForCurrentStudy } = useKernelStudyRef(
  () => canvas.studyCode,
  {
    onReset: () => {
      casebookOutcome.value = null;
      activeStudyLocks.value = [];
    },
    onResolved: async (resolved, { isCurrent }) => {
      const locks = await fetchStudyLocks(resolved, {
        activeOnly: true,
      }).catch(() => []);
      if (isCurrent()) {
        activeStudyLocks.value = locks;
      }
    },
  }
);

const directEnrollmentLocks = computed<DirectScopeLockItem[]>(() =>
  toDirectScopeLocks(
    activeStudyLocks.value,
    record.value.enrollmentId ? `enrollment:${record.value.enrollmentId}` : ""
  )
);

async function refreshEnrollmentLocks() {
  activeStudyLocks.value = await fetchStudyLocks(studyRef.value, {
    activeOnly: true,
  }).catch(() => []);
}

watch(
  () => record.value.enrollmentId,
  () => {
    casebookOutcome.value = null;
  }
);

watch(
  () => props.participant.id,
  () => {
    casebookOutcome.value = null;
    if (studyRef.value) {
      void runForCurrentStudy(async (resolved, isCurrent) => {
        const locks = await fetchStudyLocks(resolved, {
          activeOnly: true,
        }).catch(() => []);
        if (isCurrent()) {
          activeStudyLocks.value = locks;
        }
      });
    }
  }
);

watch(
  [
    () => canvas.studyCode,
    () => props.participant.id,
    () => Boolean(props.newFormCellLocation),
  ],
  ([code, , needsSchedule]) => {
    casebookOutcome.value = null;
    if (code && needsSchedule) {
      void canvas.loadScheduleOptions(code).catch(() => null);
    }
  },
  { immediate: true }
);

async function exportCasebook() {
  if (!record.value.enrollmentId || casebookBusy.value) return;
  casebookBusy.value = true;
  casebookOutcome.value = null;
  try {
    const artifact = await kernelApi.streamEnrollmentCasebook({
      enrollmentRef: `enrollment:${record.value.enrollmentId}`,
      format: casebookFormat.value,
    });
    triggerBrowserDownload(artifact.blob, artifact.filename);
    casebookOutcome.value = {
      kind: "ok",
      message: `Downloaded ${artifact.filename}.`,
      sha256: artifact.contentSha256,
    };
  } catch (err) {
    casebookOutcome.value = {
      kind: "error",
      message: err instanceof Error ? err.message : String(err),
    };
  } finally {
    casebookBusy.value = false;
  }
}
</script>

<style scoped>
.record {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.record-header {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 12px;
}

.record-header h2 {
  margin: 0;
  font-size: 1.1rem;
  font-family: var(--font-mono);
}

.record-meta,
.status-line {
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.status-line {
  margin: 0;
}

.participant-governance {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.casebook-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.casebook-label {
  font-size: 0.84rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.casebook-select {
  max-width: 100%;
  min-width: 0;
  min-height: var(--surface-input-min-height, 44px);
  box-sizing: border-box;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: var(--surface-input-font-size, 1rem);
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.casebook-outcome {
  font-size: 0.82rem;
  padding: 4px 8px;
  border-radius: 4px;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.casebook-outcome code {
  font-family: var(--font-mono);
  overflow-wrap: anywhere;
  word-break: break-word;
}

.casebook-outcome--ok {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.casebook-outcome--error {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

h3 {
  margin: 0 0 8px;
  font-size: 1rem;
}
</style>
