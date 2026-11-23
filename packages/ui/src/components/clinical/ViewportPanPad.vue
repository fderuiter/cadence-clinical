<script setup>
const props = defineProps({
  step: {
    type: Number,
    default: 24,
  },
  labelPrefix: {
    type: String,
    default: "Pan image",
  },
  groupLabel: {
    type: String,
    default: "Directional pan controls",
  },
  showReset: {
    type: Boolean,
    default: false,
  },
  resetLabel: {
    type: String,
    default: "Center pan",
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  compact: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["pan", "reset"]);

function emitDirection(direction, dx, dy) {
  if (props.disabled) return;
  emit("pan", { direction, dx, dy });
}

function emitReset() {
  if (props.disabled) return;
  emit("reset");
}
</script>

<template>
  <div
    class="viewport-pan-pad"
    :class="{ 'is-compact': compact }"
    role="group"
    :aria-label="groupLabel"
  >
    <button
      type="button"
      class="pan-pad-btn pan-pad-up"
      :disabled="disabled"
      :aria-label="`${labelPrefix} up`"
      :title="`${labelPrefix} up`"
      data-pan-direction="up"
      @click="emitDirection('up', 0, -step)"
    >
      <svg
        class="pan-pad-icon"
        viewBox="0 0 16 16"
        width="14"
        height="14"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M4 10L8 6L12 10"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>

    <button
      type="button"
      class="pan-pad-btn pan-pad-left"
      :disabled="disabled"
      :aria-label="`${labelPrefix} left`"
      :title="`${labelPrefix} left`"
      data-pan-direction="left"
      @click="emitDirection('left', -step, 0)"
    >
      <svg
        class="pan-pad-icon"
        viewBox="0 0 16 16"
        width="14"
        height="14"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M10 4L6 8L10 12"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>

    <button
      v-if="showReset"
      type="button"
      class="pan-pad-btn pan-pad-center"
      :disabled="disabled"
      :aria-label="resetLabel"
      :title="resetLabel"
      data-pan-direction="reset"
      @click="emitReset"
    >
      <svg
        class="pan-pad-icon"
        viewBox="0 0 16 16"
        width="12"
        height="12"
        fill="none"
        aria-hidden="true"
      >
        <circle cx="8" cy="8" r="3" stroke="currentColor" stroke-width="2" />
      </svg>
    </button>

    <button
      type="button"
      class="pan-pad-btn pan-pad-down"
      :disabled="disabled"
      :aria-label="`${labelPrefix} down`"
      :title="`${labelPrefix} down`"
      data-pan-direction="down"
      @click="emitDirection('down', 0, step)"
    >
      <svg
        class="pan-pad-icon"
        viewBox="0 0 16 16"
        width="14"
        height="14"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M4 6L8 10L12 6"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>

    <button
      type="button"
      class="pan-pad-btn pan-pad-right"
      :disabled="disabled"
      :aria-label="`${labelPrefix} right`"
      :title="`${labelPrefix} right`"
      data-pan-direction="right"
      @click="emitDirection('right', step, 0)"
    >
      <svg
        class="pan-pad-icon"
        viewBox="0 0 16 16"
        width="14"
        height="14"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M6 4L10 8L6 12"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>
  </div>
</template>

<style scoped>
.viewport-pan-pad {
  display: inline-flex;
  align-items: center;
  gap: var(--semantic-spacing-2xs);
}

.pan-pad-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: var(--semantic-control-target-min);
  min-height: var(--semantic-control-target-min);
  padding: var(--semantic-spacing-2xs);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--semantic-border-radius-sm);
  cursor: pointer;
  transition: background var(--transition-fast);
}

.pan-pad-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.pan-pad-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.pan-pad-icon {
  display: block;
  flex-shrink: 0;
}
</style>
