<template>
  <div class="body-map-widget-container">
    <span class="widget-label">{{ label }}</span>
    <p class="widget-hint">
      Click on the anatomical map to record pain/symptom location coordinates.
    </p>

    <div
      class="canvas-wrapper"
      role="button"
      tabindex="0"
      @click="onCanvasClick"
      @keydown.enter="onCanvasClick"
    >
      <div class="anatomical-silhouette">
        <div class="head-region" title="Head / Neck" />
        <div class="torso-region" title="Chest / Abdomen" />
        <div class="limbs-region" title="Arms / Legs" />
      </div>

      <!-- Placed Pin Markers -->
      <div
        v-for="(pin, index) in pins"
        :key="index"
        class="map-pin"
        :style="{ left: `${pin.x}%`, top: `${pin.y}%` }"
        :title="`Location #${index + 1}: (${pin.x.toFixed(1)}%, ${pin.y.toFixed(1)}%)`"
      >
        <span>{{ index + 1 }}</span>
      </div>
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
import { ref, watch } from "vue";

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: "Interactive Body Map" },
});

const emit = defineEmits(["update:modelValue"]);

const pins = ref([...props.modelValue]);

watch(
  () => props.modelValue,
  (newVal) => {
    pins.value = [...newVal];
  }
);

const onCanvasClick = (e) => {
  const rect = e.currentTarget.getBoundingClientRect();
  const x = ((e.clientX - rect.left) / rect.width) * 100;
  const y = ((e.clientY - rect.top) / rect.height) * 100;

  pins.value.push({ x, y });
  emit("update:modelValue", pins.value);
};

const clearPins = () => {
  pins.value = [];
  emit("update:modelValue", []);
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
  padding: 0;
  font-size: 0.8125rem;
}
</style>
