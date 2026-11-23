<template>
  <div class="body-map-widget-container">
    <span :id="labelId" class="widget-label">{{ label }}</span>
    <p :id="hintId" class="widget-hint">
      Click on the anatomical map or use arrow keys and Enter or Space to record
      pain/symptom location coordinates.
    </p>

    <div
      ref="canvasWrapperRef"
      class="canvas-wrapper"
      role="button"
      tabindex="0"
      :aria-label="label"
      :aria-describedby="hintId"
      @focus="isReticleActive = true"
      @click="onCanvasClick"
      @keydown="onCanvasKeydown"
    >
      <div class="anatomical-silhouette" aria-hidden="true">
        <div class="head-region" title="Head / Neck" />
        <div class="torso-region" title="Chest / Abdomen" />
        <div class="limbs-region" title="Arms / Legs" />
      </div>

      <!-- Keyboard / Single-Pointer Reticle Indicator -->
      <div
        class="reticle-cursor"
        :class="{ 'is-active': isReticleActive }"
        :style="{ left: `${reticleX}%`, top: `${reticleY}%` }"
        aria-hidden="true"
      />

      <!-- Placed Pin Markers -->
      <div
        v-for="(pin, index) in pins"
        :key="index"
        class="map-pin"
        :style="{ left: `${pin.x}%`, top: `${pin.y}%` }"
        :title="`Location #${index + 1}: (${pin.x.toFixed(1)}%, ${pin.y.toFixed(1)}%)`"
        aria-hidden="true"
      >
        <span>{{ index + 1 }}</span>
      </div>
    </div>

    <div class="reticle-readout" aria-hidden="true">
      Reticle: {{ reticleX.toFixed(1) }}%, {{ reticleY.toFixed(1) }}%
    </div>

    <div class="sr-only" role="status" aria-live="polite">
      {{ liveAnnouncement }}
    </div>

    <div v-if="pins.length > 0" class="pins-summary">
      <span class="pins-count">{{ pins.length }} location(s) recorded.</span>
      <button type="button" class="btn-clear" @click="clearPins">
        Clear Pins
      </button>
    </div>
  </div>
</template>

<script setup>
import { ref, useId, watch } from "vue";
import { useAccessibleViewport } from "ui";

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: "Interactive Body Map" },
});

const emit = defineEmits(["update:modelValue"]);

const uid = useId();
const labelId = `body-map-label-${uid}`;
const hintId = `body-map-hint-${uid}`;

const canvasWrapperRef = ref(null);
const pins = ref([...props.modelValue]);

watch(
  () => props.modelValue,
  (newVal) => {
    pins.value = Array.isArray(newVal) ? [...newVal] : [];
  }
);

const recordPin = (rawX, rawY) => {
  const x = Number.isFinite(rawX) ? Math.min(100, Math.max(0, rawX)) : 50;
  const y = Number.isFinite(rawY) ? Math.min(100, Math.max(0, rawY)) : 50;
  pins.value = [...pins.value, { x, y }];
  emit("update:modelValue", pins.value);
};

const {
  reticleX,
  reticleY,
  isReticleActive,
  liveAnnouncement,
  clientToViewport,
  placePoint,
  handleReticleKeydown,
  announce,
} = useAccessibleViewport({
  viewportRef: canvasWrapperRef,
  minX: 0,
  maxX: 100,
  minY: 0,
  maxY: 100,
  initialX: 50,
  initialY: 50,
  fallbackWidth: 240,
  fallbackHeight: 320,
  step: 5,
  shiftStep: 15,
  fineStep: 1,
  mode: "point",
  unit: "%",
  onConfirmPoint: ({ x, y }) => {
    recordPin(x, y);
  },
});

const onCanvasClick = (e) => {
  const pt = clientToViewport(e, canvasWrapperRef.value);
  placePoint({ x: pt.x, y: pt.y }, pt.validPointer ? "pointer" : "keyboard");
};

const onCanvasKeydown = (e) => {
  handleReticleKeydown(e);
};

const clearPins = () => {
  pins.value = [];
  emit("update:modelValue", []);
  announce("Cleared all recorded body map locations", { immediate: true });
};
</script>

<style scoped>
.body-map-widget-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs, 4px);
  width: 100%;
}

.widget-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.widget-hint {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 4px 0;
}

.canvas-wrapper {
  position: relative;
  width: 240px;
  height: 320px;
  border: 2px dashed var(--border);
  border-radius: var(--radius-md, 6px);
  background-color: var(--semantic-color-surface-muted);
  cursor: crosshair;
  margin: 0 auto;
  overflow: hidden;
}

.anatomical-silhouette {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  opacity: 0.4;
}

.head-region {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background-color: var(--semantic-color-text-secondary);
  margin-bottom: 8px;
}

.torso-region {
  width: 70px;
  height: 100px;
  border-radius: 12px;
  background-color: var(--semantic-color-text-secondary);
  margin-bottom: 8px;
}

.limbs-region {
  width: 90px;
  height: 120px;
  border-radius: 8px;
  background-color: var(--semantic-color-text-secondary);
}

.reticle-cursor {
  position: absolute;
  width: 18px;
  height: 18px;
  margin-left: -9px;
  margin-top: -9px;
  border: 2px solid var(--semantic-color-primary);
  border-radius: 50%;
  pointer-events: none;
  opacity: 0.65;
  box-shadow: 0 0 0 2px var(--semantic-color-surface);
}

.reticle-cursor.is-active,
.canvas-wrapper:focus-visible .reticle-cursor {
  opacity: 1;
  border-color: var(--semantic-color-accent);
}

.reticle-readout {
  font-size: 0.75rem;
  font-family: var(--font-mono);
  color: var(--semantic-color-text-secondary);
  text-align: center;
}

.map-pin {
  position: absolute;
  width: 20px;
  height: 20px;
  margin-left: -10px;
  margin-top: -10px;
  background-color: var(--error);
  color: var(--semantic-color-surface);
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.6875rem;
  font-weight: 700;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
  pointer-events: none;
}

.pins-summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 6px;
  font-size: 0.8125rem;
}

.btn-clear {
  background: none;
  border: none;
  color: var(--error);
  cursor: pointer;
  text-decoration: underline;
  padding: 2px 4px;
  min-height: var(--semantic-control-target-min);
  font-size: 0.8125rem;
}
</style>
