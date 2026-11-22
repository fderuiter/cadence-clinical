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

    <p v-if="record.loading" class="status-line" role="status">
      Loading {{ participant.subject_id }}'s data...
    </p>
    <p
      v-else-if="record.error"
      id="record-error"
      class="status-line"
      role="status"
    >
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
      <div
        v-if="record.enrollmentId"
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
            <span>{{ casebookOutcome.message }}</span>
            <details
              v-if="casebookOutcome.sha256"
              class="casebook-digest-disclosure"
            >
              <summary>Verification digest</summary>
              <code data-testid="participant-casebook-sha256">
                SHA-256: {{ casebookOutcome.sha256 }}
              </code>
            </details>
          </span>
        </div>
      </div>

      <ParticipantFormsPanel
        :study-code="canvas.studyCode"
        :subject-code="participant.subject_id"
        :enrollment-id="record.enrollmentId"
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
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { participantStateLabel } from "../../canvas/lifecycle";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import ParticipantFormsPanel from "./ParticipantFormsPanel.vue";
import QueriesPanel from "./QueriesPanel.vue";
import ScopeLockControls, {
  type DirectScopeLockItem,
} from "./ScopeLockControls.vue";

/**
 * One participant's data inside Clinical Canvas: opened case report forms, locks, casebooks, and queries on the
 * participant's enrollment (ADR-2622), linking directly to forms and observations in the zoom path.
 */
defineProps<{
  participant: SubjectSummary;
  canManage: boolean;
  formLocation: (formId: string) => RouteLocationRaw;
  newFormLocation: RouteLocationRaw;
  newFormCellLocation?: (
    collectionSpecId: string,
    plannedActivityId?: string | null
  ) => RouteLocationRaw;
  handoffLocation?: RouteLocationRaw | null;
  observationLocation: (
    observationId: string,
    formId: string
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

const studyRef = ref<string | null>(null);
const activeStudyLocks = ref<KernelLock[]>([]);
const casebookFormat = ref<CasebookExportFormat>("html");
const casebookBusy = ref(false);
const casebookOutcome = ref<{
  kind: "ok" | "error";
  message: string;
  sha256?: string | null;
} | null>(null);

const directEnrollmentLocks = computed<DirectScopeLockItem[]>(() => {
  const enrollmentRef = record.value.enrollmentId
    ? `enrollment:${record.value.enrollmentId}`
    : "";
  if (!enrollmentRef) return [];
  return activeStudyLocks.value
    .filter(
      (l) => !l.masked && !l.released && l.ref && l.scope_ref === enrollmentRef
    )
    .map((l) => ({ lockRef: l.ref as string, kind: l.kind }));
});

async function refreshEnrollmentLocks() {
  if (!studyRef.value || typeof kernelApi.listStudyLocks !== "function") {
    activeStudyLocks.value = [];
    return;
  }
  try {
    const res = await kernelApi.listStudyLocks(studyRef.value, {
      activeOnly: true,
    });
    activeStudyLocks.value = res?.locks ?? [];
  } catch {
    activeStudyLocks.value = [];
  }
}

let lookupSeq = 0;

watch(
  [() => canvas.studyCode, () => record.value.enrollmentId],
  async ([code, enrollmentId]) => {
    const seq = ++lookupSeq;
    casebookOutcome.value = null;
    if (!code || !enrollmentId) {
      studyRef.value = null;
      activeStudyLocks.value = [];
      return;
    }
    try {
      const resolved = await findKernelStudyRef(code);
      if (seq !== lookupSeq) return;
      studyRef.value = resolved;
      if (resolved) {
        await refreshEnrollmentLocks();
      }
    } catch {
      if (seq === lookupSeq) studyRef.value = null;
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
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
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
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.casebook-outcome {
  font-size: 0.82rem;
  padding: 4px 8px;
  border-radius: 4px;
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
