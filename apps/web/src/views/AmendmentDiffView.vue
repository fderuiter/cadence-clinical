<template>
  <main id="amendment-diff-view" class="canvas-consolidated-view">
    <header class="consolidated-header">
      <div>
        <h1>Protocol Amendments</h1>
        <p class="consolidated-subtitle">
          Protocol amendment forking, simulation impact review,
          <strong>21 CFR Part 11</strong> approval, and site effectivity rollout
          are governed directly through Clinical Canvas and the Clinical Kernel.
        </p>
      </div>
      <div class="consolidated-controls">
        <ClinicalInput
          id="amendment-diff-study-select"
          v-model="selectedStudyCode"
          label="Study"
          type="select"
          density="compact"
          :show-query-flag="false"
          :disabled="studiesLoading"
        >
          <option value="">{{ studyPlaceholder }}</option>
          <option
            v-for="item in studyOptions"
            :key="item.key"
            :value="item.code"
          >
            {{ item.label }}
          </option>
        </ClinicalInput>
        <button
          type="button"
          class="btn btn-secondary"
          data-testid="open-amendment-governance-modal"
          @click="isGovernanceModalOpen = true"
        >
          Part 11 Governance
        </button>
        <router-link
          v-if="selectedStudyCode"
          class="btn btn-secondary"
          data-testid="open-canvas-protocol-versions-link"
          :to="canvasProtocolVersionsLocation"
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
      data-testid="amendment-diff-choose-study"
    >
      Choose a study to inspect protocol versions, fork an amendment draft, and
      review simulation readiness.
    </p>
    <ProtocolVersionsSection
      v-else-if="selectedStudyCode"
      :study-code="selectedStudyCode"
      :selected-ref="selectedVersionRef"
      @select="(version) => (selectedVersionRef = version?.ref || '')"
    />

    <ClinicalModal
      v-model="isGovernanceModalOpen"
      :teleport="false"
      title="21 CFR Part 11 Protocol Amendment Governance"
      :show-footer="false"
    >
      <div class="governance-modal-body">
        <p>
          <strong>Forked amendments</strong> clone an approved parent protocol
          version into an editable draft with explicit regulatory classification
          and participant transition policy.
        </p>
        <p>
          <strong>Simulation review gate</strong> requires a saved
          <code>SimulationReport</code> artifact and a signed
          <code>Reviewed</code> attestation before step-up
          <code>X-Sig-Token</code> approval of any forked amendment.
        </p>
      </div>
    </ClinicalModal>
  </main>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalInput, ClinicalModal } from "ui";
import ProtocolVersionsSection from "../components/canvas/ProtocolVersionsSection.vue";
import { useConsolidatedStudyList } from "../composables/useConsolidatedStudyList";
import { useScopedNavigation } from "../composables/useScopedNavigation";

const { studies, studiesLoading, studiesError, selectedStudyCode } =
  useConsolidatedStudyList();
const { scopedStudyLocation } = useScopedNavigation(
  () => selectedStudyCode.value
);
const selectedVersionRef = ref("");
const isGovernanceModalOpen = ref(false);
const studyPlaceholder = computed(() =>
  studiesLoading.value ? "Loading studies…" : "Select a study…"
);
const studyOptions = computed(() =>
  studies.value.map((study) => ({
    key: study.ref || study.code,
    code: study.code,
    label: `${study.code} — ${study.title}`,
  }))
);

watch(selectedStudyCode, () => {
  selectedVersionRef.value = "";
});

const canvasProtocolVersionsLocation = computed(() =>
  scopedStudyLocation({
    stage: "design",
    section: "protocol-versions",
    ...(selectedVersionRef.value ? { node: selectedVersionRef.value } : {}),
  })
);
</script>

<style scoped>
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

<style scoped src="./consolidatedCanvasView.css"></style>
