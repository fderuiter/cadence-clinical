<template>
  <section
    id="study-pulse"
    class="pulse"
    aria-labelledby="study-pulse-title"
    :aria-busy="canvas.pulseLoading"
  >
    <h2 id="study-pulse-title" class="pulse-title" tabindex="-1">
      Participants
    </h2>

    <ClinicalLiveRegion
      :id="
        canvas.pulseError
          ? 'pulse-error'
          : canvas.pulse && canvas.pulse.total === 0
            ? 'pulse-empty'
            : ''
      "
      variant="inline"
      :politeness="canvas.pulseError ? 'assertive' : 'polite'"
      :tone="canvas.pulseError ? 'error' : 'neutral'"
      :active="
        Boolean(canvas.pulseError || !canvas.pulse || canvas.pulse.total === 0)
      "
      live-class="pulse-status"
      :polite-message="
        canvas.pulseError
          ? ''
          : !canvas.pulse
            ? 'Counting participants...'
            : canvas.pulse.total === 0
              ? `No participants in ${canvas.studyCode} yet.`
              : ''
      "
      :assertive-message="
        canvas.pulseError ? 'The participant counts couldn\'t be loaded.' : ''
      "
    >
      <template v-if="canvas.pulseError" #actions>
        <button
          id="btn-retry-pulse"
          type="button"
          class="btn btn-secondary"
          @click="canvas.loadPulse()"
        >
          Try again
        </button>
      </template>
    </ClinicalLiveRegion>
    <template
      v-if="!canvas.pulseError && canvas.pulse && canvas.pulse.total > 0"
    >
      <button
        id="pulse-total"
        type="button"
        class="pulse-metric pulse-total"
        @click="emit('drill', '')"
      >
        <span class="pulse-value">{{ canvas.pulse.total }}</span>
        <span class="pulse-label">in the study</span>
      </button>
      <ul class="pulse-states" aria-label="Participants by state">
        <li v-for="state in shownStates" :key="state.code">
          <button
            :id="`pulse-${state.code.toLowerCase()}`"
            type="button"
            class="pulse-metric"
            @click="emit('drill', state.code)"
          >
            <span class="pulse-value">{{ state.count }}</span>
            <span class="pulse-label">{{ state.label }}</span>
          </button>
        </li>
      </ul>
    </template>

    <p class="pulse-footnote">
      Additional quality, safety, site, and supply summaries appear here as
      study data is recorded.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { ClinicalLiveRegion } from "ui";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

/**
 * The first Study Pulse measure (docs/ux/CLINICAL_CANVAS.md §2.8): participants by state, as Execution counts
 * them. Selecting a number lists the participants behind it (UX-7).
 */
const emit = defineEmits<{ drill: [status: string] }>();
const canvas = useStudyCanvasStore();

// States nobody is in are left out rather than shown as a row of zeros.
const shownStates = computed(
  () => canvas.pulse?.byState.filter((state) => state.count > 0) ?? []
);
</script>

<style scoped>
.pulse {
  display: flex;
  flex-wrap: wrap;
  align-items: stretch;
  gap: 12px;
}

.pulse-title {
  flex-basis: 100%;
  margin: 0;
  font-size: 1rem;
}

.pulse-states {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.pulse-metric {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: min(100%, 8rem);
  max-width: 100%;
  box-sizing: border-box;
  padding: 10px 14px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: transparent;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.pulse-metric:hover,
.pulse-metric:focus-visible {
  border-color: var(--accent);
}

.pulse-total {
  background: var(--accent-bg);
}

.pulse-value {
  font-size: 1.5rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.pulse-label,
.pulse-status,
.pulse-footnote {
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.pulse-status,
.pulse-footnote {
  flex-basis: 100%;
  margin: 0;
}
</style>
