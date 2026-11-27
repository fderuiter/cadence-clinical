<template>
  <div
    id="casebook-summary"
    class="casebook-summary"
    data-testid="casebook-summary"
  >
    <span class="casebook-protocol-title">
      {{ protocolTitle }}
    </span>
    <div class="casebook-rollups">
      <span
        class="clinical-badge clinical-badge--verified cb-badge cb-badge--complete"
      >
        {{ rollups.scheduledCompleteCells }}/{{ rollups.totalScheduledCells }}
        scheduled complete
      </span>
      <span
        v-if="rollups.incompleteFormsCount > 0"
        class="clinical-badge clinical-badge--warning cb-badge cb-badge--incomplete"
      >
        {{ rollups.incompleteFormsCount }} incomplete
      </span>
      <span
        v-if="rollups.totalOpenQueries > 0"
        class="clinical-badge clinical-badge--queried cb-badge cb-badge--query-open"
      >
        {{ rollups.totalOpenQueries }} open
        {{ rollups.totalOpenQueries === 1 ? "query" : "queries" }}
      </span>
      <span
        v-if="rollups.totalErrorFindings > 0"
        class="clinical-badge clinical-badge--error cb-badge cb-badge--error-finding"
        data-testid="casebook-error-findings"
      >
        {{ rollups.totalErrorFindings }} error
        {{ rollups.totalErrorFindings === 1 ? "finding" : "findings" }}
      </span>
      <span
        v-if="rollups.verifiedFormsCount > 0"
        class="clinical-badge clinical-badge--verified cb-badge cb-badge--verified"
      >
        {{ rollups.verifiedFormsCount }} SDV verified
      </span>
      <span
        v-if="rollups.signedFormsCount > 0"
        class="clinical-badge clinical-badge--signed cb-badge cb-badge--signed"
      >
        {{ rollups.signedFormsCount }} signed
      </span>
      <span
        v-if="rollups.lockedFormsCount > 0"
        class="clinical-badge clinical-badge--locked cb-badge cb-badge--locked"
      >
        {{ rollups.lockedFormsCount }} locked/frozen
      </span>
      <span
        v-if="rollups.windowDeviationFormsCount > 0"
        class="clinical-badge clinical-badge--warning cb-badge cb-badge--window-deviation"
        data-testid="casebook-window-deviations"
      >
        {{ rollups.windowDeviationFormsCount }} visit window
        {{
          rollups.windowDeviationFormsCount === 1 ? "deviation" : "deviations"
        }}
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { CasebookRollups } from "./useParticipantCasebook";

defineProps<{
  protocolTitle: string;
  rollups: CasebookRollups;
}>();
</script>

<style scoped>
.casebook-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px 12px;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  font-size: 0.82rem;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
  font-variant-numeric: tabular-nums;
}

.casebook-protocol-title {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.casebook-rollups {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.cb-badge {
  font-variant-numeric: tabular-nums;
}
</style>
