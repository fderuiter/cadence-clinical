<template>
  <div
    v-if="variant === 'segmented'"
    :id="id || undefined"
    role="group"
    :aria-label="groupAriaLabel"
    class="clinical-theme-toggle-group"
    :class="densityClass"
    :data-theme-preference="preference"
    :data-resolved-theme="resolvedTheme"
  >
    <button
      v-for="option in modeOptions"
      :key="option.value"
      type="button"
      class="clinical-theme-toggle-segment"
      :class="{ 'is-active': preference === option.value }"
      :aria-pressed="preference === option.value ? 'true' : 'false'"
      :data-theme-option="option.value"
      @click="selectMode(option.value)"
    >
      <component
        :is="option.icon"
        :size="iconSize"
        :stroke-width="1.75"
        aria-hidden="true"
        focusable="false"
        class="clinical-theme-toggle-icon"
      />
      <span class="clinical-theme-toggle-label">{{ option.label }}</span>
    </button>
  </div>
  <button
    v-else
    :id="id || undefined"
    type="button"
    class="clinical-theme-toggle-btn"
    :class="[densityClass, { 'icon-only': !showLabel }]"
    :aria-label="!showLabel ? cycleAriaLabel : undefined"
    :title="cycleAriaLabel"
    :data-theme-preference="preference"
    :data-resolved-theme="resolvedTheme"
    @click="handleCycle"
  >
    <component
      :is="activeIcon"
      :size="iconSize"
      :stroke-width="1.75"
      aria-hidden="true"
      focusable="false"
      class="clinical-theme-toggle-icon"
    />
    <template v-if="showLabel">
      <span class="clinical-theme-toggle-label"
        >Theme: {{ activeModeLabel }}</span
      >
      <span class="sr-only">{{ srCycleSuffix }}</span>
    </template>
  </button>
</template>

<script setup>
import { computed } from "vue";
import { Monitor, Moon, Sun } from "lucide-vue-next";
import { cycleThemePreference, useTheme } from "../../composables/useTheme.js";

const props = defineProps({
  id: {
    type: String,
    default: "",
  },
  density: {
    type: String,
    default: "compact",
    validator: (value) => ["compact", "touch"].includes(value),
  },
  variant: {
    type: String,
    default: "cycle",
    validator: (value) => ["cycle", "segmented"].includes(value),
  },
  showLabel: {
    type: Boolean,
    default: true,
  },
  groupAriaLabel: {
    type: String,
    default: "Color theme",
  },
});

const emit = defineEmits(["change"]);

const { preference, resolvedTheme, setTheme, cycleTheme } = useTheme();

const MODE_META = {
  light: { label: "Light", icon: Sun },
  dark: { label: "Dark", icon: Moon },
  auto: { label: "Auto", icon: Monitor },
};

const modeOptions = [
  { value: "light", label: "Light", icon: Sun },
  { value: "dark", label: "Dark", icon: Moon },
  { value: "auto", label: "Auto", icon: Monitor },
];

const densityClass = computed(() =>
  props.density === "touch"
    ? "clinical-theme-toggle--touch"
    : "clinical-theme-toggle--compact"
);

const iconSize = computed(() => (props.density === "touch" ? 18 : 16));

const activeIcon = computed(
  () => (MODE_META[preference.value] || MODE_META.auto).icon
);

const activeModeLabel = computed(
  () => (MODE_META[preference.value] || MODE_META.auto).label
);

const nextModeLabel = computed(() => {
  const next = cycleThemePreference(preference.value);
  return (MODE_META[next] || MODE_META.auto).label.toLowerCase();
});

const resolvedSummary = computed(() =>
  preference.value === "auto" ? ` (${resolvedTheme.value})` : ""
);

const cycleAriaLabel = computed(
  () =>
    `Theme: ${activeModeLabel.value}${resolvedSummary.value}. Switch to ${nextModeLabel.value} theme`
);

const srCycleSuffix = computed(
  () => `${resolvedSummary.value}. Switch to ${nextModeLabel.value} theme`
);

function handleCycle() {
  const next = cycleTheme();
  emit("change", next);
}

function selectMode(mode) {
  const next = setTheme(mode);
  emit("change", next);
}
</script>

<style scoped>
.clinical-theme-toggle-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--semantic-radius-sm, 6px);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: var(--semantic-font-family);
  font-size: var(--semantic-font-size-xs, 13px);
  font-weight: 600;
  line-height: 1;
  cursor: pointer;
  white-space: nowrap;
  flex-shrink: 0;
  box-sizing: border-box;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.clinical-theme-toggle-btn:not(:disabled):hover {
  background-color: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-primary);
}

.clinical-theme-toggle-btn.clinical-theme-toggle--compact {
  min-width: 44px;
  min-height: 44px;
  padding: 6px 10px;
}

.clinical-theme-toggle-btn.clinical-theme-toggle--compact.icon-only {
  width: 44px;
  height: 44px;
  padding: 0;
}

.clinical-theme-toggle-btn.clinical-theme-toggle--touch {
  min-width: var(--semantic-touch-target-min, 48px);
  min-height: var(--semantic-touch-target-min, 48px);
  padding: 8px 12px;
  font-size: var(--semantic-font-size-sm, 14px);
}

.clinical-theme-toggle-btn.clinical-theme-toggle--touch.icon-only {
  width: var(--semantic-touch-target-min, 48px);
  height: var(--semantic-touch-target-min, 48px);
  padding: 0;
}

.clinical-theme-toggle-group {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  padding: 2px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--semantic-radius-md, 8px);
  background-color: var(--semantic-color-surface-muted);
  box-sizing: border-box;
}

.clinical-theme-toggle-segment {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 1px solid transparent;
  border-radius: var(--semantic-radius-sm, 6px);
  background-color: transparent;
  color: var(--semantic-color-text-muted);
  font-family: var(--semantic-font-family);
  font-size: var(--semantic-font-size-xs, 13px);
  font-weight: 600;
  line-height: 1;
  cursor: pointer;
  white-space: nowrap;
  box-sizing: border-box;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.clinical-theme-toggle--compact .clinical-theme-toggle-segment {
  min-height: 44px;
  min-width: 44px;
  padding: 6px 10px;
}

.clinical-theme-toggle--touch .clinical-theme-toggle-segment {
  min-height: var(--semantic-touch-target-min, 48px);
  min-width: var(--semantic-touch-target-min, 48px);
  padding: 8px 12px;
  font-size: var(--semantic-font-size-sm, 14px);
}

.clinical-theme-toggle-segment:not(:disabled):hover {
  color: var(--semantic-color-text);
  background-color: var(--semantic-color-surface);
}

.clinical-theme-toggle-segment.is-active {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-color: var(--semantic-color-border-control);
  box-shadow: var(--semantic-shadow-sm);
}

.clinical-theme-toggle-icon {
  flex-shrink: 0;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
