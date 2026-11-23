<script setup>
import { computed, ref, useAttrs, useId, useSlots, watch } from "vue";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  modelValue: {
    type: [String, Number],
    required: true,
  },
  tabs: {
    type: Array,
    required: true,
  },
  ariaLabel: {
    type: String,
    default: undefined,
  },
  ariaLabelledby: {
    type: String,
    default: undefined,
  },
  orientation: {
    type: String,
    default: "horizontal",
    validator: (val) => val === "horizontal" || val === "vertical",
  },
  id: {
    type: String,
    default: undefined,
  },
  idPrefix: {
    type: String,
    default: "",
  },
  listClass: {
    type: [String, Array, Object],
    default: "",
  },
  tabClass: {
    type: [String, Array, Object],
    default: "",
  },
  activeTabClass: {
    type: String,
    default: "active",
  },
  manual: {
    type: Boolean,
    default: false,
  },
  keepAlivePanels: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["update:modelValue", "change"]);

const attrs = useAttrs();
const slots = useSlots();
const instanceId = useId();
const tabButtonRefs = ref([]);
const focusedTabId = ref(props.modelValue);

watch(
  () => props.modelValue,
  (nextVal) => {
    focusedTabId.value = nextVal;
  }
);

const basePrefix = computed(
  () => props.idPrefix || props.id || `clinical-tabs-${instanceId}`
);

const resolveTabId = (tab) =>
  tab.buttonId || `${basePrefix.value}-tab-${tab.id}`;

const resolvePanelId = (tab) =>
  tab.panelId || `${basePrefix.value}-panel-${tab.id}`;

const isSelected = (tab) => tab.id === props.modelValue;

const enabledTabs = computed(() => props.tabs.filter((tab) => !tab.disabled));

const activeRovingId = computed(() => {
  const candidateId = props.manual ? focusedTabId.value : props.modelValue;
  const matched = enabledTabs.value.find((tab) => tab.id === candidateId);
  if (matched) return matched.id;
  return enabledTabs.value[0]?.id ?? null;
});

const isRovingStop = (tab) => !tab.disabled && tab.id === activeRovingId.value;

const hasPanelSlots = computed(() =>
  props.tabs.some((tab) => Boolean(slots[`panel-${tab.id}`]))
);

const setTabRef = (el, index) => {
  if (el) {
    tabButtonRefs.value[index] = el;
  }
};

const activateTab = (tab) => {
  if (!tab || tab.disabled) return;
  focusedTabId.value = tab.id;
  if (tab.id !== props.modelValue) {
    emit("update:modelValue", tab.id);
    emit("change", tab.id);
  }
};

const focusTabAtIndex = (targetIndex) => {
  const targetTab = props.tabs[targetIndex];
  if (!targetTab || targetTab.disabled) return;
  focusedTabId.value = targetTab.id;
  const btn = tabButtonRefs.value[targetIndex];
  if (btn && typeof btn.focus === "function") {
    btn.focus();
  }
  if (!props.manual) {
    activateTab(targetTab);
  }
};

const onTabKeyDown = (event, currentIndex) => {
  const enabledIndices = props.tabs
    .map((tab, idx) => (tab.disabled ? -1 : idx))
    .filter((idx) => idx !== -1);

  if (enabledIndices.length === 0) return;

  const currentPos = enabledIndices.indexOf(currentIndex);
  const safePos = currentPos === -1 ? 0 : currentPos;
  const isVertical = props.orientation === "vertical";

  const nextKey = isVertical ? "ArrowDown" : "ArrowRight";
  const prevKey = isVertical ? "ArrowUp" : "ArrowLeft";

  if (event.key === nextKey) {
    event.preventDefault();
    const nextIdx = enabledIndices[(safePos + 1) % enabledIndices.length];
    focusTabAtIndex(nextIdx);
    return;
  }

  if (event.key === prevKey) {
    event.preventDefault();
    const prevIdx =
      enabledIndices[
        (safePos - 1 + enabledIndices.length) % enabledIndices.length
      ];
    focusTabAtIndex(prevIdx);
    return;
  }

  if (event.key === "Home") {
    event.preventDefault();
    focusTabAtIndex(enabledIndices[0]);
    return;
  }

  if (event.key === "End") {
    event.preventDefault();
    focusTabAtIndex(enabledIndices[enabledIndices.length - 1]);
    return;
  }

  if (props.manual && (event.key === "Enter" || event.key === " ")) {
    event.preventDefault();
    activateTab(props.tabs[currentIndex]);
  }
};

const getPanelAttrs = (tabId) => {
  const tab = props.tabs.find((item) => item.id === tabId) || { id: tabId };
  return {
    id: resolvePanelId(tab),
    role: "tabpanel",
    "aria-labelledby": resolveTabId(tab),
  };
};

defineExpose({
  resolveTabId,
  resolvePanelId,
  getPanelAttrs,
});
</script>

<template>
  <div
    :id="id"
    v-bind="attrs"
    role="tablist"
    :aria-label="ariaLabel"
    :aria-labelledby="ariaLabelledby"
    :aria-orientation="orientation"
    :class="['clinical-tabs-list', `orientation-${orientation}`, listClass]"
  >
    <button
      v-for="(tab, index) in tabs"
      :id="resolveTabId(tab)"
      :key="tab.id"
      :ref="(el) => setTabRef(el, index)"
      type="button"
      role="tab"
      :aria-selected="isSelected(tab) ? 'true' : 'false'"
      :aria-controls="resolvePanelId(tab)"
      :tabindex="isRovingStop(tab) ? 0 : -1"
      :disabled="Boolean(tab.disabled)"
      :title="tab.title || undefined"
      :data-testid="tab.dataTestid || undefined"
      :class="[
        'clinical-tabs-trigger',
        tabClass,
        tab.buttonClass,
        isSelected(tab) ? activeTabClass : '',
      ]"
      @click="activateTab(tab)"
      @keydown="onTabKeyDown($event, index)"
    >
      <slot name="tab" :tab="tab" :active="isSelected(tab)" :index="index">
        <span class="clinical-tabs-label">{{ tab.label }}</span>
        <span
          v-if="
            tab.badge !== undefined && tab.badge !== null && tab.badge !== ''
          "
          class="clinical-tabs-badge"
        >
          {{ tab.badge }}
        </span>
      </slot>
    </button>
  </div>

  <template v-if="hasPanelSlots">
    <div
      v-for="tab in tabs"
      v-show="isSelected(tab)"
      :id="resolvePanelId(tab)"
      :key="`panel-${tab.id}`"
      role="tabpanel"
      :aria-labelledby="resolveTabId(tab)"
      :hidden="!isSelected(tab) ? true : undefined"
      class="clinical-tabs-panel"
    >
      <slot
        v-if="isSelected(tab) || keepAlivePanels"
        :name="`panel-${tab.id}`"
        :tab="tab"
      />
    </div>
  </template>
</template>

<style scoped>
.clinical-tabs-list {
  display: flex;
  align-items: center;
  gap: 4px;
}

.clinical-tabs-list.orientation-vertical {
  flex-direction: column;
  align-items: stretch;
}

.clinical-tabs-trigger {
  font-family: inherit;
  cursor: pointer;
}

.clinical-tabs-trigger:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.clinical-tabs-trigger:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: -2px;
}

.clinical-tabs-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-left: 6px;
  padding: 1px 6px;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
}

@media (prefers-reduced-motion: reduce) {
  .clinical-tabs-trigger {
    transition: none !important;
  }
}
</style>
