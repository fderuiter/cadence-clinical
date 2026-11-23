<template>
  <main id="data-lock-view" class="canvas-consolidated-view">
    <header class="consolidated-header">
      <div>
        <h1>Data Lock Console</h1>
        <p class="consolidated-subtitle">
          Scoped <strong>Frozen</strong> and <strong>Locked</strong> assertions
          are governed directly through Clinical Canvas and the Clinical Kernel.
        </p>
      </div>
      <div class="consolidated-controls">
        <label for="data-lock-study-select">Study</label>
        <select
          id="data-lock-study-select"
          v-model="selectedStudyCode"
          data-testid="data-lock-study-select"
          :disabled="studiesLoading"
        >
          <option value="">
            {{ studiesLoading ? "Loading studies…" : "Select a study…" }}
          </option>
          <option
            v-for="study in studies"
            :key="study.ref || study.code"
            :value="study.code"
          >
            {{ study.code }} — {{ study.title }}
          </option>
        </select>
        <button
          type="button"
          class="btn btn-secondary"
          data-testid="open-lock-governance-modal"
          @click="isGovernanceModalOpen = true"
        >
          Part 11 Governance
        </button>
        <router-link
          v-if="selectedStudyCode"
          class="btn btn-secondary"
          data-testid="open-canvas-locks-link"
          :to="{
            name: 'study',
            params: { studyCode: selectedStudyCode },
            query: { stage: 'data', section: 'locks' },
          }"
        >
          Open in Clinical Canvas
        </router-link>
      </div>
    </header>

    <p v-if="studiesError" class="status-line error-banner" role="alert">
      {{ studiesError }}
    </p>
    <p
      v-else-if="!selectedStudyCode && !studiesLoading"
      class="status-line"
      data-testid="data-lock-choose-study"
    >
      Choose a study to inspect lock readiness and apply or release signed
      locks.
    </p>
    <LocksSection
      v-else-if="selectedStudyCode"
      :study-code="selectedStudyCode"
    />

    <ClinicalModal
      v-model="isGovernanceModalOpen"
      :teleport="false"
      title="21 CFR Part 11 Lock & Freeze Governance"
      :show-footer="false"
    >
      <div class="governance-modal-body">
        <p>
          <strong>Frozen</strong> scopes place a monitoring hold on new entry
          and corrections while permitting open discrepancy queries to be
          answered and closed.
        </p>
        <p>
          <strong>Locked</strong> scopes require zero open queries and zero
          missing required items across the target subtree, recorded with a
          single-use <code>X-Sig-Token</code> electronic signature and signed
          <code>X-Change-Reason</code>.
        </p>
      </div>
    </ClinicalModal>
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ClinicalModal } from "ui";
import { kernelApi, type KernelStudySummary } from "../api/kernelApi";
import LocksSection from "../components/canvas/LocksSection.vue";
import { useStudyCanvasStore } from "../stores/studyCanvas";

const canvasStore = useStudyCanvasStore();
const studies = ref<KernelStudySummary[]>([]);
const studiesLoading = ref(false);
const studiesError = ref<string | null>(null);
const selectedStudyCode = ref(canvasStore.studyCode || "");
const isGovernanceModalOpen = ref(false);

onMounted(async () => {
  if (typeof kernelApi.listStudies !== "function") return;
  studiesLoading.value = true;
  studiesError.value = null;
  try {
    const res = await kernelApi.listStudies();
    studies.value = res ?? [];
    if (!selectedStudyCode.value && studies.value.length > 0) {
      selectedStudyCode.value = studies.value[0].code;
    }
  } catch (err) {
    studiesError.value = err instanceof Error ? err.message : String(err);
  } finally {
    studiesLoading.value = false;
  }
});
</script>

<style scoped>
.canvas-consolidated-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px;
}

.consolidated-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.consolidated-header h1 {
  margin: 0;
  font-size: 1.35rem;
}

.consolidated-subtitle {
  margin: 4px 0 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
}

.consolidated-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.consolidated-controls label {
  font-size: 0.84rem;
  font-weight: 600;
}

.consolidated-controls select {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.status-line {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
}

.error-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.governance-modal-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
}

.governance-modal-body p {
  margin: 0;
}
</style>
