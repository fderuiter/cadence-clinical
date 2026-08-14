<template>
  <div class="arm-visualizer">
    <!-- Header / Summary -->
    <div class="visualizer-header">
      <div>
        <h4 class="title">Clinical Study Arms &amp; Epoch Progression</h4>
        <p class="subtitle">
          Timeline mapping of protocol epochs, crossover paths, and subject
          allocations.
        </p>
      </div>
      <div class="summary-stats">
        <span class="pill"
          ><strong>{{ arms.length }}</strong> Study Arms</span
        >
        <span class="pill"
          ><strong>{{ epochs.length }}</strong> Protocol Epochs</span
        >
        <span class="pill highlight"
          ><strong>{{ totalSampleSize }}</strong> Target Subjects</span
        >
      </div>
    </div>

    <!-- Arms Grid -->
    <div class="arms-grid">
      <div v-for="arm in arms" :key="arm.name" class="arm-card">
        <div class="arm-card-top">
          <span
            class="arm-type-badge"
            :class="`type-${arm.arm_type.toLowerCase()}`"
          >
            {{ formatArmType(arm.arm_type) }}
          </span>
          <span v-if="arm.target_sample_size" class="sample-badge">
            N = {{ arm.target_sample_size }}
          </span>
        </div>
        <h5 class="arm-name">{{ arm.name }}</h5>
        <p v-if="arm.description" class="arm-desc">{{ arm.description }}</p>

        <!-- Epoch Timeline Sequence for this Arm -->
        <div class="epoch-timeline-track">
          <div
            v-for="ep in sortedEpochs"
            :key="ep.name"
            class="epoch-block"
            :class="`epoch-${ep.epoch_type.toLowerCase()}`"
          >
            <div class="epoch-block-index">Epoch {{ ep.sequence_index }}</div>
            <div class="epoch-block-name">{{ ep.name }}</div>
            <div class="epoch-block-type">{{ ep.epoch_type }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Epoch Transition Map -->
    <div class="epoch-sequence-container">
      <h5 class="sequence-title">Epoch Sequence &amp; Crossover Path</h5>
      <div class="sequence-chain">
        <template v-for="(ep, idx) in sortedEpochs" :key="ep.name">
          <div class="sequence-step">
            <div
              class="step-badge"
              :class="`step-${ep.epoch_type.toLowerCase()}`"
            >
              {{ ep.sequence_index }}
            </div>
            <div class="step-info">
              <div class="step-name">{{ ep.name }}</div>
              <div class="step-type">{{ ep.epoch_type }}</div>
            </div>
          </div>
          <div v-if="idx < sortedEpochs.length - 1" class="sequence-arrow">
            ➜
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  arms: {
    type: Array,
    default: () => [],
  },
  epochs: {
    type: Array,
    default: () => [],
  },
});

const sortedEpochs = computed(() => {
  return [...props.epochs].sort(
    (a, b) => (a.sequence_index || 0) - (b.sequence_index || 0)
  );
});

const totalSampleSize = computed(() => {
  return props.arms.reduce(
    (acc, curr) => acc + (curr.target_sample_size || 0),
    0
  );
});

const formatArmType = (type) => {
  if (!type) return "ARM";
  return type.replace(/_/g, " ");
};
</script>

<style scoped>
.arm-visualizer {
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 16px;
  width: 100%;
}

.visualizer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  padding-bottom: 12px;
  flex-wrap: wrap;
  gap: 12px;
}

.title {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--primary, #0f172a);
}

.subtitle {
  margin: 4px 0 0 0;
  font-size: 0.85rem;
  color: #64748b;
}

.summary-stats {
  display: flex;
  gap: 8px;
}

.pill {
  background-color: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 0.8rem;
  color: #334155;
}

.pill.highlight {
  background-color: #ecfdf5;
  border-color: #a7f3d0;
  color: #065f46;
}

.arms-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.arm-card {
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 16px;
}

.arm-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.arm-type-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.type-experimental {
  background-color: #dbeafe;
  color: #1e40af;
}
.type-active_comparator {
  background-color: #fef3c7;
  color: #92400e;
}
.type-placebo_comparator {
  background-color: #f1f5f9;
  color: #475569;
}
.type-sham_comparator {
  background-color: #fce7f3;
  color: #9d174d;
}
.type-no_intervention {
  background-color: #f3f4f6;
  color: #6b7280;
}

.sample-badge {
  font-size: 0.8rem;
  font-weight: 600;
  color: #0f172a;
}

.arm-name {
  margin: 0 0 4px 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: #1e293b;
}

.arm-desc {
  margin: 0 0 12px 0;
  font-size: 0.8rem;
  color: #64748b;
  line-height: 1.4;
}

.epoch-timeline-track {
  display: flex;
  gap: 6px;
  margin-top: 12px;
  overflow-x: auto;
}

.epoch-block {
  flex: 1;
  min-width: 80px;
  padding: 8px 6px;
  border-radius: 4px;
  text-align: center;
  border: 1px solid transparent;
}

.epoch-block-index {
  font-size: 0.65rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.epoch-block-name {
  font-size: 0.75rem;
  font-weight: 600;
  margin: 2px 0;
}

.epoch-block-type {
  font-size: 0.65rem;
  opacity: 0.8;
}

.epoch-screening {
  background-color: #e0f2fe;
  border-color: #bae6fd;
  color: #0369a1;
}
.epoch-treatment {
  background-color: #dcfce7;
  border-color: #bbf7d0;
  color: #15803d;
}
.epoch-washout {
  background-color: #fef3c7;
  border-color: #fde68a;
  color: #b45309;
}
.epoch-follow_up {
  background-color: #f3e8ff;
  border-color: #e9d5ff;
  color: #7e22ce;
}
.epoch-run_in {
  background-color: #f1f5f9;
  border-color: #cbd5e1;
  color: #475569;
}

.epoch-sequence-container {
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 12px 16px;
}

.sequence-title {
  margin: 0 0 10px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: #334155;
}

.sequence-chain {
  display: flex;
  align-items: center;
  gap: 12px;
  overflow-x: auto;
}

.sequence-step {
  display: flex;
  align-items: center;
  gap: 8px;
  background-color: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  padding: 6px 10px;
  border-radius: 6px;
}

.step-badge {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  font-weight: 700;
}

.step-screening {
  background-color: #0284c7;
  color: #ffffff;
}
.step-treatment {
  background-color: #16a34a;
  color: #ffffff;
}
.step-washout {
  background-color: #d97706;
  color: #ffffff;
}
.step-follow_up {
  background-color: #9333ea;
  color: #ffffff;
}
.step-run_in {
  background-color: #64748b;
  color: #ffffff;
}

.step-name {
  font-size: 0.8rem;
  font-weight: 600;
  color: #1e293b;
}

.step-type {
  font-size: 0.7rem;
  color: #64748b;
}

.sequence-arrow {
  color: #94a3b8;
  font-size: 0.9rem;
}
</style>
