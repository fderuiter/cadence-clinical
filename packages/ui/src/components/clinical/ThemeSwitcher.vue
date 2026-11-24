<template>
  <div
    :id="id || undefined"
    class="clinical-theme-switcher"
    :class="[`clinical-theme-switcher--${size}`]"
    role="group"
    :aria-label="groupLabel"
  >
    <button
      v-for="option in themeOptions"
      :id="id ? `${id}-${option.value}` : undefined"
      :key="option.value"
      type="button"
      class="clinical-theme-switcher__btn"
      :class="{ 'is-active': preference === option.value }"
      :data-theme-option="option.value"
      :aria-pressed="preference === option.value ? 'true' : 'false'"
      :aria-label="showLabels ? undefined : option.ariaLabel"
      :title="option.title"
      @click="handleSelect(option.value)"
    >
      <svg
        class="clinical-icon lucide"
        :width="iconSize"
        :height="iconSize"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.75"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
        focusable="false"
      >
        <template v-if="option.value === 'light'">
          <circle cx="12" cy="12" r="4" />
          <path
            d="M12 2v2m0 16v2M4.93 4.93l1.41 1.41m11.32 11.32 1.41 1.41M2 12h2m16 0h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"
          />
        </template>
        <path
          v-else-if="option.value === 'dark'"
          d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"
        />
        <template v-else>
          <rect width="20" height="14" x="2" y="3" rx="2" />
          <path d="M8 21h8m-4-4v4" />
        </template>
      </svg>
      <span v-if="showLabels" class="clinical-theme-switcher__label"
        >{{ option.shortLabel }}<span class="sr-only"> theme</span></span
      >
    </button>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useTheme } from "../../composables/useTheme.js";

const props = defineProps({
  id: {
    type: String,
    default: "",
  },
  size: {
    type: String,
    default: "comfortable",
    validator: (val) => ["compact", "comfortable"].includes(val),
  },
  showLabels: {
    type: Boolean,
    default: false,
  },
  groupLabel: {
    type: String,
    default: "Appearance theme",
  },
});

const emit = defineEmits(["change"]);

const { preference, resolvedTheme, setTheme } = useTheme();

const iconSize = computed(() => (props.size === "compact" ? 16 : 18));

const themeOptions = [
  {
    value: "light",
    shortLabel: "Light",
    ariaLabel: "Light theme",
    title: "Use light theme",
  },
  {
    value: "dark",
    shortLabel: "Dark",
    ariaLabel: "Dark theme",
    title: "Use dark theme",
  },
  {
    value: "auto",
    shortLabel: "Auto",
    ariaLabel: "System theme (Auto)",
    title: "Follow system color scheme (Auto)",
  },
];

function handleSelect(mode) {
  const result = setTheme(mode);
  emit("change", {
    preference: result.preference,
    resolvedTheme: result.resolvedTheme || resolvedTheme.value,
  });
}
</script>

<style scoped>
.clinical-theme-switcher {
  display: inline-flex;
  align-items: center;
  gap: 0;
  padding: 0;
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface-muted);
  box-sizing: border-box;
}

.clinical-theme-switcher__btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 0;
  border-radius: 0;
  background-color: transparent;
  color: var(--semantic-color-text-secondary);
  font-family: var(--semantic-font-family, sans-serif);
  font-size: var(--font-size-xs, 13px);
  font-weight: 600;
  line-height: 1;
  cursor: pointer;
  transition:
    background-color var(--transition-fast, 0.15s ease),
    color var(--transition-fast, 0.15s ease),
    box-shadow var(--transition-fast, 0.15s ease);
  box-sizing: border-box;
}

.clinical-theme-switcher__btn:first-child {
  border-top-left-radius: calc(var(--radius-md, 8px) - 1px);
  border-bottom-left-radius: calc(var(--radius-md, 8px) - 1px);
}

.clinical-theme-switcher__btn:last-child {
  border-top-right-radius: calc(var(--radius-md, 8px) - 1px);
  border-bottom-right-radius: calc(var(--radius-md, 8px) - 1px);
}

.clinical-theme-switcher__btn:focus-visible {
  position: relative;
  z-index: 1;
}

.clinical-theme-switcher--comfortable .clinical-theme-switcher__btn {
  min-width: var(--semantic-touch-target-min, 48px);
  min-height: var(--semantic-touch-target-min, 48px);
  padding: 0 10px;
}

.clinical-theme-switcher--compact .clinical-theme-switcher__btn {
  min-width: 44px;
  min-height: 44px;
  padding: 0 8px;
}

.clinical-theme-switcher__btn:hover {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.clinical-theme-switcher__btn.is-active {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  box-shadow: inset 0 -2px 0 var(--semantic-color-primary);
}

.clinical-theme-switcher__label {
  white-space: nowrap;
}
</style>
