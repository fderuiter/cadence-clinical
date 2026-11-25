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
          :to="canvasLocksLocation"
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
import { computed, ref } from "vue";
import { ClinicalModal } from "ui";
import LocksSection from "../components/canvas/LocksSection.vue";
import { useConsolidatedStudyList } from "../composables/useConsolidatedStudyList";
import { useScopedNavigation } from "../composables/useScopedNavigation";

const { studies, studiesLoading, studiesError, selectedStudyCode } =
  useConsolidatedStudyList();
const { scopedStudyLocation } = useScopedNavigation(
  () => selectedStudyCode.value
);
const canvasLocksLocation = computed(() =>
  scopedStudyLocation({ stage: "data", section: "locks" })
);
const isGovernanceModalOpen = ref(false);
</script>

<style scoped>
.governance-modal-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-body);
}

.governance-modal-body p {
  margin: 0;
}
</style>

<style scoped src="./consolidatedCanvasView.css"></style>
