<script setup>
import { computed } from "vue";

const props = defineProps({
  score: {
    type: Number,
    required: true,
  },
  thresholds: {
    type: Object,
    default: () => ({
      high: 90,
      medium: 75,
    }),
  },
  showLabel: {
    type: Boolean,
    default: true,
  },
  showScore: {
    type: Boolean,
    default: true,
  },
  size: {
    type: String,
    default: "md", // 'sm', 'md', 'lg'
  },
});

const normalizedScore = computed(() => {
  const num = Number(props.score);
  if (isNaN(num)) return 0;
  if (num <= 1 && num > 0) {
    return Math.min(100, Math.max(0, Math.round(num * 100)));
  }
  return Math.min(100, Math.max(0, Math.round(num)));
});

const highThreshold = computed(() => props.thresholds?.high ?? 90);
const mediumThreshold = computed(() => props.thresholds?.medium ?? 75);

const confidenceTier = computed(() => {
  if (normalizedScore.value >= highThreshold.value) {
    return "high";
  }
  if (normalizedScore.value >= mediumThreshold.value) {
    return "medium";
  }
  return "low";
});

const tierLabel = computed(() => {
  switch (confidenceTier.value) {
    case "high":
      return "High Confidence";
    case "medium":
      return "Medium Confidence";
    case "low":
    default:
      return "Low Confidence";
  }
});

const ariaLabel = computed(() => {
  return `Confidence score: ${normalizedScore.value}%, ${tierLabel.value}`;
});
</script>

<template>
  <span
    class="confidence-badge"
    :class="[`confidence-${confidenceTier}`, `confidence-size-${size}`]"
    role="status"
    :aria-label="ariaLabel"
    :title="ariaLabel"
  >
    <span class="confidence-dot" aria-hidden="true"></span>
    <span v-if="showScore" class="confidence-score"
      >{{ normalizedScore }}%</span
    >
    <span v-if="showLabel" class="confidence-label">{{ tierLabel }}</span>
  </span>
</template>

<style scoped>
.confidence-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  border-radius: 9999px;
  font-family: var(--semantic-font-family);
  font-weight: 600;
  line-height: 1;
  border: 1px solid transparent;
  user-select: none;
  white-space: nowrap;
}

/* Sizes */
.confidence-size-sm {
  padding: 2px 6px;
  font-size: var(--font-size-2xs);
}
.confidence-size-sm .confidence-dot {
  width: 6px;
  height: 6px;
}

.confidence-size-md {
  padding: 3px 8px;
  font-size: var(--font-size-xs);
}
.confidence-size-md .confidence-dot {
  width: 7px;
  height: 7px;
}

.confidence-size-lg {
  padding: 5px 12px;
  font-size: var(--font-size-sm);
}
.confidence-size-lg .confidence-dot {
  width: 8px;
  height: 8px;
}

.confidence-dot {
  border-radius: 50%;
  display: inline-block;
}

/* High Confidence (Green >= 90%) */
.confidence-high {
  background-color: var(--color-success-bg);
  color: var(--color-success);
  border-color: var(--color-success);
}
.confidence-high .confidence-dot {
  background-color: var(--color-success);
}

/* Medium Confidence (Amber 75-89%) */
.confidence-medium {
  background-color: var(--color-warning-bg);
  color: var(--color-warning);
  border-color: var(--color-warning);
}
.confidence-medium .confidence-dot {
  background-color: var(--color-warning);
}

/* Low Confidence (Red < 75%) */
.confidence-low {
  background-color: var(--color-error-bg);
  color: var(--color-error);
  border-color: var(--color-error);
}
.confidence-low .confidence-dot {
  background-color: var(--color-error);
}

.confidence-score {
  font-variant-numeric: tabular-nums;
}
</style>
