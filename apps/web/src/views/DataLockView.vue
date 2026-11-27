<template>
  <ConsolidatedStudyHeader
    view-id="data-lock-view"
    title="Data Lock Console"
    select-id="data-lock-study-select"
    choose-study-test-id="data-lock-choose-study"
    choose-study-message="Choose a study to inspect lock readiness and apply or release signed locks."
    canvas-link-test-id="open-canvas-locks-link"
    canvas-stage="data"
    canvas-section="locks"
  >
    <template #subtitle>
      Scoped <strong>Frozen</strong> and <strong>Locked</strong> assertions are
      governed directly through Clinical Canvas and the Clinical Kernel.
    </template>
    <template #extra-controls>
      <button
        type="button"
        class="btn btn-secondary"
        data-testid="open-lock-governance-modal"
        @click="isGovernanceModalOpen = true"
      >
        Part 11 Governance
      </button>
    </template>
    <template #default="{ studyCode }">
      <LocksSection :study-code="studyCode" />
    </template>
    <template #modals>
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
    </template>
  </ConsolidatedStudyHeader>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { ClinicalModal } from "ui";
import ConsolidatedStudyHeader from "../components/canvas/ConsolidatedStudyHeader.vue";
import LocksSection from "../components/canvas/LocksSection.vue";

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
