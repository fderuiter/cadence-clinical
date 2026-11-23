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

      <ParticipantSiteAssignment
        v-if="
          canManage &&
          !participant.site_id &&
          !record.loading &&
          !record.enrollmentId
        "
        :participant="participant"
        :study-code="canvas.studyCode"
      />

      <div
        v-if="!record.loading && record.enrollmentId"
        class="participant-governance"
        data-testid="participant-governance"
      >
        <div
          v-if="coveringParticipantLocks.length > 0"
          class="participant-locks-banner"
          data-testid="participant-locks-banner"
          role="status"
        >
          <strong>{{
            participantAssertionsLocked ? "Locked." : "Frozen."
          }}</strong>
          Values cannot be changed while a lock is in force.
          <span data-testid="participant-lock-scope-note">
            <template v-if="participantAssertionsLocked">
              Source verifications, verification withdrawals, and query changes
              are also blocked while a Locked assertion is in force.
            </template>
            <template v-else>
              Source verification and queries remain available while Frozen.
            </template>
          </span>
          <ul>
            <li
              v-for="(held, idx) in coveringParticipantLocks"
              :key="held.ref ?? `covering-${idx}`"
            >
              {{ held.kind }} on {{ held.scope_ref }} by
              {{ held.locked_by ?? "—" }} at {{ held.locked_at ?? "—" }}:
              {{ held.reason_for_change ?? "—" }}
            </li>
          </ul>
        </div>

        <ScopeLockControls
          :study-ref="studyRef"
          :scope-ref="`enrollment:${record.enrollmentId}`"
          scope-label="participant"
          :direct-locks="directEnrollmentLocks"
          :covering-locks="ancestorEnrollmentLocks"
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
  toCoveringScopeLocks,
  toDirectScopeLocks,
  type CoveringScopeLockItem,
  type DirectScopeLockItem,
} from "../../canvas/kernelLocks";
import { getCachedEnrollmentStudySiteRef } from "../../canvas/kernelStudy";
import { participantStateLabel } from "../../canvas/lifecycle";
import { parseNodeRef } from "../../canvas/nodeRef";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useStudyCanvasStore } from "../../stores/studyCanvas";
import ParticipantFormsPanel from "./ParticipantFormsPanel.vue";
import ParticipantSiteAssignment from "./ParticipantSiteAssignment.vue";
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

const zoomStudySiteRef = ref<string | null>(null);
const activeStudyLocks = ref<KernelLock[]>([]);
const casebookFormat = ref<CasebookExportFormat>("html");
const casebookBusy = ref(false);
const casebookOutcome = ref<{
  kind: "ok" | "error";
  message: string;
  sha256?: string | null;
} | null>(null);

let zoomSeq = 0;

function stripNodeVersion(refText: string | null | undefined): string | null {
  if (!refText) return null;
  const parsed = parseNodeRef(refText);
  if (parsed) return `${parsed.kind}:${parsed.id}`;
  const at = refText.indexOf("@");
  return at < 0 ? refText : refText.slice(0, at);
}

const { studyRef, runForCurrentStudy } = useKernelStudyRef(
  () => canvas.studyCode,
  {
    onReset: () => {
      zoomSeq++;
      zoomStudySiteRef.value = null;
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

const enrollmentRef = computed(() =>
  record.value.enrollmentId ? `enrollment:${record.value.enrollmentId}` : ""
);

const lockDerivedStudySiteRef = computed(() => {
  if (!enrollmentRef.value) return null;
  for (const lock of activeStudyLocks.value) {
    if (
      !lock.masked &&
      lock.study_site_ref &&
      (lock.enrollment_ref === enrollmentRef.value ||
        lock.scope_ref === enrollmentRef.value)
    ) {
      return stripNodeVersion(lock.study_site_ref);
    }
  }
  return null;
});

const effectiveStudySiteRef = computed(() => {
  if (zoomStudySiteRef.value) return zoomStudySiteRef.value;
  if (
    record.value.enrollmentId &&
    typeof getCachedEnrollmentStudySiteRef === "function"
  ) {
    const cached = getCachedEnrollmentStudySiteRef(record.value.enrollmentId);
    if (cached) return stripNodeVersion(cached);
  }
  return lockDerivedStudySiteRef.value;
});

const coveringParticipantLocks = computed<KernelLock[]>(() => {
  if (!enrollmentRef.value) return [];
  const siteRef = effectiveStudySiteRef.value;
  const cleanStudyRef = stripNodeVersion(studyRef.value);
  return activeStudyLocks.value.filter((l) => {
    if (l.masked || l.released || !l.scope_ref) return false;
    const cleanScopeRef = stripNodeVersion(l.scope_ref);
    if (cleanStudyRef && cleanScopeRef === cleanStudyRef) return true;
    if (siteRef && cleanScopeRef === siteRef) return true;
    return cleanScopeRef === enrollmentRef.value;
  });
});

const participantAssertionsLocked = computed(() =>
  coveringParticipantLocks.value.some((l) => l.kind === "Locked")
);

const directEnrollmentLocks = computed<DirectScopeLockItem[]>(() =>
  toDirectScopeLocks(coveringParticipantLocks.value, enrollmentRef.value)
);

const ancestorEnrollmentLocks = computed<CoveringScopeLockItem[]>(() =>
  toCoveringScopeLocks(coveringParticipantLocks.value, enrollmentRef.value)
);

async function refreshEnrollmentLocks() {
  activeStudyLocks.value = await fetchStudyLocks(studyRef.value, {
    activeOnly: true,
  }).catch(() => []);
}

watch(
  () => record.value.enrollmentId,
  () => {
    zoomSeq++;
    zoomStudySiteRef.value = null;
    casebookOutcome.value = null;
  }
);

watch(
  [() => record.value.enrollmentId, () => activeStudyLocks.value],
  async ([enrollmentId, locks]) => {
    if (!enrollmentId || zoomStudySiteRef.value) return;
    if (
      typeof getCachedEnrollmentStudySiteRef === "function" &&
      getCachedEnrollmentStudySiteRef(enrollmentId)
    ) {
      return;
    }
    if (lockDerivedStudySiteRef.value) return;
    const hasActiveSiteLock = locks.some(
      (l) =>
        !l.masked &&
        !l.released &&
        (l.scope_kind === "study_site" ||
          l.scope_ref?.startsWith("study_site:"))
    );
    if (!hasActiveSiteLock || typeof kernelApi.getZoomPath !== "function") {
      return;
    }
    const seq = ++zoomSeq;
    try {
      const zoomSteps = await kernelApi.getZoomPath(
        `enrollment:${enrollmentId}`
      );
      if (seq !== zoomSeq || record.value.enrollmentId !== enrollmentId) return;
      const siteStep = (zoomSteps ?? []).find(
        (step) => step.level === "study_site" && !step.mask && step.node
      );
      zoomStudySiteRef.value = stripNodeVersion(siteStep?.node ?? null);
    } catch {
      if (seq === zoomSeq) {
        zoomStudySiteRef.value = null;
      }
    }
  },
  { immediate: true }
);

watch(
  () => props.participant.id,
  () => {
    zoomSeq++;
    zoomStudySiteRef.value = null;
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

.participant-locks-banner {
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
}

.participant-locks-banner ul {
  margin: 6px 0 0;
  padding-left: 18px;
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
  box-sizing: border-box;
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
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.casebook-outcome code {
  overflow-wrap: anywhere;
  word-break: break-word;
}

.casebook-digest-disclosure summary {
  min-height: var(--control-target-min, 24px);
  padding: 4px 0;
  cursor: pointer;
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
