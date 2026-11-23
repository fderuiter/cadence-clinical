<template>
  <section
    id="study-exports-section"
    class="study-exports"
    aria-labelledby="study-exports-title"
    :aria-busy="studyLoading || streaming"
  >
    <header class="study-exports-header">
      <h2 id="study-exports-title">Exports</h2>
      <p class="study-exports-subtitle">
        Stream reproducible clinical snapshots in CSV, CDISC ODM v1.3.2 XML,
        CDISC Dataset-JSON, SAS/R statistical bundles, or regulatory Study
        Archive ZIPs.
      </p>
    </header>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <div
      v-else-if="studyError"
      id="study-exports-error"
      class="status-line error-banner"
      role="alert"
    >
      <p class="status-line">
        {{ studyCode }}'s exports couldn't be prepared.
        <button
          id="btn-retry-study-exports"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading"
          @click="loadStudyExportsContext(studyCode)"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="studyError" />
    </div>
    <p v-else-if="!studyRef" id="study-exports-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      snapshot exports cannot be streamed here.
    </p>

    <form
      v-else
      id="study-exports-form"
      class="exports-card"
      data-testid="study-exports-form"
      @submit.prevent="downloadSnapshot"
    >
      <div class="exports-grid">
        <div class="field-group">
          <label for="study-export-format">Export format</label>
          <select id="study-export-format" v-model="format">
            <option value="odm_xml">
              CDISC ODM v1.3.2 XML (MetaDataVersion + ClinicalData + Audit +
              Signatures)
            </option>
            <option value="dataset_json">
              CDISC Dataset-JSON v1.1 (Typed clinical item groups)
            </option>
            <option value="csv">
              CSV clinical dataset (Flat observation rows)
            </option>
            <option value="sas_bundle">
              SAS statistical bundle (.zip — CSV + Dataset-JSON + .sas syntax +
              manifest)
            </option>
            <option value="r_bundle">
              R statistical bundle (.zip — CSV + Dataset-JSON + .R syntax +
              manifest)
            </option>
            <option value="study_archive">
              Regulatory Study Archive (.zip — datasets + syntax + PDF/ODM
              casebooks + audit + manifest)
            </option>
          </select>
        </div>

        <div class="field-group">
          <label for="study-export-lock-filter">Lock filter</label>
          <select id="study-export-lock-filter" v-model="lockFilter">
            <option value="locked_only">Locked records only</option>
            <option value="frozen_or_locked">Frozen or locked records</option>
            <option value="all">All records (locked, frozen, and open)</option>
            <option value="unlocked_only">Unlocked records only</option>
          </select>
        </div>

        <div class="field-group">
          <label for="study-export-cut-preset">Snapshot cut</label>
          <select
            id="study-export-cut-preset"
            v-model="selectedPreset"
            @change="onPresetChange"
          >
            <option value="">Latest canonical truth (now)</option>
            <option
              v-for="lockOpt in lockTimestampOptions"
              :key="lockOpt.id"
              :value="lockOpt.lockedAt"
            >
              {{ lockOpt.label }}
            </option>
            <option value="__custom__">Custom ISO UTC timestamp…</option>
          </select>
        </div>

        <div class="field-group">
          <label for="study-export-as-of">As-of timestamp (ISO UTC)</label>
          <input
            id="study-export-as-of"
            v-model="asOf"
            type="text"
            autocomplete="off"
            placeholder="2026-06-01T12:00:00Z (optional)"
          />
        </div>
      </div>

      <p class="privacy-note">
        Direct participant identifiers stay protected; unauthorized sites and
        blinded treatment arms appear as explicit <code>masked</code>
        placeholders in place.
      </p>

      <div class="exports-actions">
        <button
          id="btn-stream-study-export"
          type="submit"
          class="btn btn-primary"
          :disabled="streaming"
        >
          {{ streaming ? "Streaming snapshot…" : "Stream snapshot export" }}
        </button>
      </div>

      <p
        v-if="outcome"
        id="study-exports-outcome"
        :class="[
          'status-line',
          outcome.kind === 'ok' ? 'ok-banner' : 'error-banner',
        ]"
        :role="outcome.kind === 'ok' ? 'status' : 'alert'"
        aria-live="polite"
      >
        {{ outcome.message }}
        <code v-if="outcome.sha256" data-testid="study-exports-sha256">
          SHA-256: {{ outcome.sha256 }}
        </code>
      </p>
    </form>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  kernelApi,
  triggerBrowserDownload,
  type KernelLock,
  type StudyExportFormat,
  type StudyExportLockFilter,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import SupportDetails from "./SupportDetails.vue";

const props = defineProps<{
  studyCode: string;
}>();

const studyRef = ref<string | null>(null);
const studyLoading = ref(false);
const studyError = ref<string | null>(null);
const knownLocks = ref<KernelLock[]>([]);

const format = ref<StudyExportFormat>("odm_xml");
const lockFilter = ref<StudyExportLockFilter>("locked_only");
const selectedPreset = ref("");
const asOf = ref("");
const streaming = ref(false);
const outcome = ref<{
  kind: "ok" | "error";
  message: string;
  sha256?: string | null;
} | null>(null);

const lockTimestampOptions = computed(() =>
  knownLocks.value
    .filter((l) => !l.masked && l.locked_at)
    .map((l, idx) => ({
      id: l.ref ?? `lock-${idx}`,
      lockedAt: l.locked_at as string,
      label: `${l.kind} (${l.scope_kind}) at ${l.locked_at}${l.released ? " [released]" : ""}`,
    }))
);

function onPresetChange() {
  if (selectedPreset.value && selectedPreset.value !== "__custom__") {
    asOf.value = selectedPreset.value;
  } else if (!selectedPreset.value) {
    asOf.value = "";
  }
}

let loadSeq = 0;

async function loadStudyExportsContext(code: string) {
  const seq = ++loadSeq;
  studyRef.value = null;
  studyError.value = null;
  knownLocks.value = [];
  outcome.value = null;
  if (!code) return;

  studyLoading.value = true;
  try {
    const resolved = await findKernelStudyRef(code);
    if (seq !== loadSeq) return;
    studyRef.value = resolved;
    if (resolved && typeof kernelApi.listStudyLocks === "function") {
      const list = await kernelApi.listStudyLocks(resolved).catch(() => null);
      if (seq === loadSeq && list?.locks) {
        knownLocks.value = list.locks;
      }
    }
  } catch (err) {
    if (seq !== loadSeq) return;
    studyError.value = err instanceof Error ? err.message : String(err);
  } finally {
    if (seq === loadSeq) studyLoading.value = false;
  }
}

watch(
  () => props.studyCode,
  (code) => {
    void loadStudyExportsContext(code);
  },
  { immediate: true }
);

async function downloadSnapshot() {
  if (!studyRef.value || streaming.value) return;
  streaming.value = true;
  outcome.value = null;
  try {
    const result = await kernelApi.streamStudyExport({
      studyRef: studyRef.value,
      format: format.value,
      lockFilter: lockFilter.value,
      asOf: asOf.value.trim() || undefined,
    });
    triggerBrowserDownload(result.blob, result.filename);
    outcome.value = {
      kind: "ok",
      message: `Streamed ${result.filename} (${result.blob.size} bytes).`,
      sha256: result.contentSha256,
    };
  } catch (err) {
    outcome.value = {
      kind: "error",
      message: err instanceof Error ? err.message : String(err),
    };
  } finally {
    streaming.value = false;
  }
}
</script>

<style scoped>
.study-exports {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.study-exports-header h2 {
  margin: 0;
  font-size: 1.2rem;
}

.study-exports-subtitle {
  margin: 4px 0 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.exports-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
}

.exports-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-group label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.field-group select,
.field-group input {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.privacy-note {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-body);
}

.status-line {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.ok-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.error-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>
