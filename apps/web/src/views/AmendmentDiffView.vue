<template>
  <ConsolidatedStudyHeader
    view-id="amendment-diff-view"
    title="Protocol Amendments"
    select-id="amendment-diff-study-select"
    choose-study-test-id="amendment-diff-choose-study"
    choose-study-message="Choose a study to inspect protocol versions, fork an amendment draft, and review simulation readiness."
    canvas-link-test-id="open-canvas-protocol-versions-link"
    canvas-stage="design"
    canvas-section="protocol-versions"
    :canvas-node="selectedVersionRef"
    @study-change="selectedVersionRef = ''"
  >
    <template #subtitle>
      Protocol amendment forking, simulation impact review,
      <strong>21 CFR Part 11</strong> approval, and site effectivity rollout are
      governed directly through Clinical Canvas and the Clinical Kernel.
    </template>
    <template #extra-controls>
      <button
        type="button"
        class="btn btn-secondary"
        data-testid="open-amendment-governance-modal"
        @click="isGovernanceModalOpen = true"
      >
        Part 11 Governance
      </button>
    </template>
    <template #default="{ studyCode }">
      <ProtocolVersionsSection
        :study-code="studyCode"
        :selected-ref="selectedVersionRef"
        @select="(version) => (selectedVersionRef = version?.ref || '')"
      />
    </template>
    <template #modals>
      <ClinicalModal
        v-model="isGovernanceModalOpen"
        :teleport="false"
        title="21 CFR Part 11 Protocol Amendment Governance"
        :show-footer="false"
      >
        <div class="governance-modal-body">
          <p>
            <strong>Forked amendments</strong> clone an approved parent protocol
            version into an editable draft with explicit regulatory
            classification and participant transition policy.
          </p>
          <p>
            <strong>Simulation review gate</strong> requires a saved
            <code>SimulationReport</code> artifact and a signed
            <code>Reviewed</code> attestation before step-up
            <code>X-Sig-Token</code> approval of any forked amendment.
          </p>
        </div>
      </ClinicalModal>
    </template>
  </ConsolidatedStudyHeader>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { ClinicalModal } from "ui";
import ConsolidatedStudyHeader from "../components/canvas/ConsolidatedStudyHeader.vue";
import ProtocolVersionsSection from "../components/canvas/ProtocolVersionsSection.vue";

const selectedVersionRef = ref("");
const isGovernanceModalOpen = ref(false);
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
