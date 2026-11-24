<script setup>
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  useAttrs,
  useId,
  useSlots,
  watch,
} from "vue";

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
  variant: {
    type: String,
    default: "underline",
    validator: (val) =>
      val === "underline" || val === "pills" || val === "segmented",
  },
  overflow: {
    type: String,
    default: "scroll",
    validator: (val) => val === "scroll" || val === "wrap",
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
const tablistRef = ref(null);
const tabButtonRefs = ref([]);
const focusedTabId = ref(props.modelValue);
const canScrollStart = ref(false);
const canScrollEnd = ref(false);
let resizeObserver = null;
let lastFocusScrolledId = null;

const prefersReducedMotion = () =>
  typeof window !== "undefined" &&
  typeof window.matchMedia === "function" &&
  window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const updateScrollState = () => {
  const listEl = tablistRef.value;
  if (
    !listEl ||
    props.orientation !== "horizontal" ||
    props.overflow !== "scroll"
  ) {
    canScrollStart.value = false;
    canScrollEnd.value = false;
    return;
  }
  const maxScroll = listEl.scrollWidth - listEl.clientWidth;
  if (maxScroll <= 1) {
    canScrollStart.value = false;
    canScrollEnd.value = false;
    return;
  }
  canScrollStart.value = listEl.scrollLeft > 1;
  canScrollEnd.value = listEl.scrollLeft < maxScroll - 1;
};

const scrollTabIntoView = (btn, targetIndex) => {
  const listEl = tablistRef.value;
  if (!listEl || !btn || props.overflow !== "scroll") return;

  const behavior = prefersReducedMotion() ? "instant" : "smooth";

  if (props.orientation === "vertical") {
    const maxTop = listEl.scrollHeight - listEl.clientHeight;
    if (maxTop <= 1) return;
    const listRect =
      typeof listEl.getBoundingClientRect === "function"
        ? listEl.getBoundingClientRect()
        : { top: 0, height: 0 };
    const btnRect =
      typeof btn.getBoundingClientRect === "function"
        ? btn.getBoundingClientRect()
        : { top: 0, height: 0 };
    const useRect = listRect.height > 0 && btnRect.height > 0;
    const btnHeight = useRect ? btnRect.height : btn.offsetHeight || 0;
    const btnTopInContent = useRect
      ? listEl.scrollTop +
        (btnRect.top - listRect.top - (listEl.clientTop || 0))
      : btn.offsetTop || 0;
    const btnBottomInContent = btnTopInContent + btnHeight;
    const visibleTop = listEl.scrollTop;
    const visibleBottom = visibleTop + listEl.clientHeight;
    const inset =
      btnHeight >= listEl.clientHeight
        ? 0
        : Math.min(
            12,
            Math.max(4, Math.floor((listEl.clientHeight - btnHeight) / 2))
          );

    let nextTop = null;
    if (btnTopInContent < visibleTop + (targetIndex === 0 ? 4 : inset)) {
      nextTop =
        targetIndex === 0
          ? 0
          : Math.max(0, Math.floor(btnTopInContent - inset));
    } else if (
      btnBottomInContent >
      visibleBottom - (targetIndex === props.tabs.length - 1 ? 4 : inset)
    ) {
      nextTop =
        targetIndex === props.tabs.length - 1
          ? maxTop
          : Math.min(
              maxTop,
              Math.ceil(btnBottomInContent - listEl.clientHeight + inset)
            );
    }

    if (nextTop !== null && nextTop !== listEl.scrollTop) {
      if (typeof listEl.scrollTo === "function") {
        listEl.scrollTo({ top: nextTop, behavior });
      }
      if (behavior === "instant" || typeof listEl.scrollTo !== "function") {
        listEl.scrollTop = nextTop;
      }
    }
    return;
  }

  const maxLeft = listEl.scrollWidth - listEl.clientWidth;
  if (maxLeft <= 1) {
    updateScrollState();
    return;
  }

  const listRect =
    typeof listEl.getBoundingClientRect === "function"
      ? listEl.getBoundingClientRect()
      : { left: 0, width: 0 };
  const btnRect =
    typeof btn.getBoundingClientRect === "function"
      ? btn.getBoundingClientRect()
      : { left: 0, width: 0 };
  const useRect = listRect.width > 0 && btnRect.width > 0;
  const btnWidth = useRect ? btnRect.width : btn.offsetWidth || 0;
  const btnLeftInContent = useRect
    ? listEl.scrollLeft +
      (btnRect.left - listRect.left - (listEl.clientLeft || 0))
    : btn.offsetLeft || 0;
  const btnRightInContent = btnLeftInContent + btnWidth;
  const visibleLeft = listEl.scrollLeft;
  const visibleRight = visibleLeft + listEl.clientWidth;

  const rawInset = Math.floor((listEl.clientWidth - btnWidth) / 2);
  const inset =
    btnWidth >= listEl.clientWidth ? 0 : Math.min(28, Math.max(6, rawInset));

  let nextLeft = null;
  if (btnWidth >= listEl.clientWidth) {
    nextLeft = Math.max(0, Math.min(maxLeft, Math.floor(btnLeftInContent)));
  } else if (btnLeftInContent < visibleLeft + (targetIndex === 0 ? 6 : inset)) {
    nextLeft =
      targetIndex === 0 ? 0 : Math.max(0, Math.floor(btnLeftInContent - inset));
  } else if (
    btnRightInContent >
    visibleRight - (targetIndex === props.tabs.length - 1 ? 6 : inset)
  ) {
    nextLeft =
      targetIndex === props.tabs.length - 1
        ? maxLeft
        : Math.min(
            maxLeft,
            Math.ceil(btnRightInContent - listEl.clientWidth + inset)
          );
  }

  if (nextLeft !== null && nextLeft !== listEl.scrollLeft) {
    if (typeof listEl.scrollTo === "function") {
      listEl.scrollTo({ left: nextLeft, behavior });
    }
    if (behavior === "instant" || typeof listEl.scrollTo !== "function") {
      listEl.scrollLeft = nextLeft;
    }
  }
  updateScrollState();
};

watch(
  () => props.modelValue,
  (nextVal) => {
    focusedTabId.value = nextVal;
    if (lastFocusScrolledId === nextVal) {
      lastFocusScrolledId = null;
      return;
    }
    const idx = props.tabs.findIndex((tab) => tab.id === nextVal);
    if (idx !== -1 && tabButtonRefs.value[idx]) {
      scrollTabIntoView(tabButtonRefs.value[idx], idx);
    }
  },
  { flush: "post" }
);

watch(
  () => [props.tabs.length, props.overflow, props.orientation],
  () => {
    updateScrollState();
  },
  { flush: "post" }
);

onMounted(async () => {
  await nextTick();
  updateScrollState();
  const activeIdx = props.tabs.findIndex((tab) => tab.id === props.modelValue);
  if (activeIdx > 0 && tabButtonRefs.value[activeIdx]) {
    scrollTabIntoView(tabButtonRefs.value[activeIdx], activeIdx);
  }
  if (typeof ResizeObserver !== "undefined" && tablistRef.value) {
    resizeObserver = new ResizeObserver(() => {
      updateScrollState();
    });
    resizeObserver.observe(tablistRef.value);
  }
});

onBeforeUnmount(() => {
  if (resizeObserver) {
    resizeObserver.disconnect();
    resizeObserver = null;
  }
});

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
    try {
      btn.focus({ preventScroll: true });
    } catch {
      btn.focus();
    }
  }
  lastFocusScrolledId =
    !props.manual && targetTab.id !== props.modelValue ? targetTab.id : null;
  if (btn) {
    scrollTabIntoView(btn, targetIndex);
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
    ref="tablistRef"
    v-bind="attrs"
    role="tablist"
    :aria-label="ariaLabel"
    :aria-labelledby="ariaLabelledby"
    :aria-orientation="orientation"
    :class="[
      'clinical-tabs-list',
      `variant-${variant}`,
      `orientation-${orientation}`,
      `overflow-${overflow}`,
      {
        'can-scroll-start': canScrollStart,
        'can-scroll-end': canScrollEnd,
      },
      listClass,
    ]"
    @scroll.passive="updateScrollState"
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
  position: relative;
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
  flex-shrink: 0;
  box-sizing: border-box;
  padding: 6px;
}

.clinical-tabs-list.orientation-horizontal.overflow-scroll {
  flex-wrap: nowrap;
  overflow-x: auto;
  overflow-y: hidden;
  overscroll-behavior-x: contain;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: thin;
  scroll-padding-inline: 24px;
}

.clinical-tabs-list.orientation-horizontal.overflow-scroll.can-scroll-end:not(
    .can-scroll-start
  ) {
  -webkit-mask-image: linear-gradient(
    to right,
    rgb(0 0 0 / 1) calc(100% - 24px),
    rgb(0 0 0 / 0) 100%
  );
  mask-image: linear-gradient(
    to right,
    rgb(0 0 0 / 1) calc(100% - 24px),
    rgb(0 0 0 / 0) 100%
  );
}

.clinical-tabs-list.orientation-horizontal.overflow-scroll.can-scroll-start:not(
    .can-scroll-end
  ) {
  -webkit-mask-image: linear-gradient(
    to right,
    rgb(0 0 0 / 0) 0%,
    rgb(0 0 0 / 1) 24px
  );
  mask-image: linear-gradient(to right, rgb(0 0 0 / 0) 0%, rgb(0 0 0 / 1) 24px);
}

.clinical-tabs-list.orientation-horizontal.overflow-scroll.can-scroll-start.can-scroll-end {
  -webkit-mask-image: linear-gradient(
    to right,
    rgb(0 0 0 / 0) 0%,
    rgb(0 0 0 / 1) 24px,
    rgb(0 0 0 / 1) calc(100% - 24px),
    rgb(0 0 0 / 0) 100%
  );
  mask-image: linear-gradient(
    to right,
    rgb(0 0 0 / 0) 0%,
    rgb(0 0 0 / 1) 24px,
    rgb(0 0 0 / 1) calc(100% - 24px),
    rgb(0 0 0 / 0) 100%
  );
}

.clinical-tabs-list.orientation-horizontal.overflow-wrap {
  flex-wrap: wrap;
  overflow-x: visible;
  overflow-y: visible;
}

.clinical-tabs-list.orientation-vertical {
  flex-direction: column;
  align-items: stretch;
}

.clinical-tabs-trigger {
  appearance: none;
  -webkit-appearance: none;
  margin: 0;
  border-style: solid;
  border-width: 0;
  border-color: transparent;
  background-color: transparent;
  box-shadow: none;
  font-family: var(--semantic-font-family);
  font-size: var(--font-size-sm, 0.875rem);
  font-weight: 600;
  line-height: 1.25;
  font-variant-numeric: tabular-nums;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 44px;
  min-width: 44px;
  padding: 10px 16px;
  box-sizing: border-box;
  cursor: pointer;
  text-decoration: none;
  transition:
    background-color var(--transition-fast, 0.15s ease),
    color var(--transition-fast, 0.15s ease),
    border-color var(--transition-fast, 0.15s ease),
    box-shadow var(--transition-fast, 0.15s ease);
}

.clinical-tabs-list.orientation-horizontal.overflow-scroll
  .clinical-tabs-trigger {
  flex-shrink: 0;
  white-space: nowrap;
}

.clinical-tabs-list.orientation-horizontal.overflow-wrap
  .clinical-tabs-trigger {
  flex-shrink: 0;
  max-width: 100%;
  white-space: normal;
  text-align: center;
}

.clinical-tabs-trigger:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.clinical-tabs-trigger:focus-visible {
  position: relative;
  z-index: 2;
}

/* Variant: underline */
.clinical-tabs-list.variant-underline {
  width: 100%;
  border-bottom: 1px solid var(--semantic-color-border);
}

.clinical-tabs-list.variant-underline .clinical-tabs-trigger {
  background-color: transparent;
  color: var(--semantic-color-text-muted);
  border-bottom-width: 3px;
  border-bottom-style: solid;
  border-bottom-color: transparent;
  border-radius: var(--semantic-radius-sm, 6px) var(--semantic-radius-sm, 6px) 0
    0;
}

.clinical-tabs-list.variant-underline
  .clinical-tabs-trigger:hover:not(:disabled):not([aria-selected="true"]) {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  border-bottom-color: var(--semantic-color-border-control);
}

.clinical-tabs-list.variant-underline
  .clinical-tabs-trigger[aria-selected="true"] {
  background-color: transparent;
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
}

/* Variant: pills */
.clinical-tabs-list.variant-pills {
  gap: 8px;
}

.clinical-tabs-list.variant-pills .clinical-tabs-trigger {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  border-width: 1px;
  border-style: solid;
  border-color: var(--semantic-color-border-control);
  border-radius: var(--radius-full, 9999px);
}

.clinical-tabs-list.variant-pills
  .clinical-tabs-trigger:hover:not(:disabled):not([aria-selected="true"]) {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  border-color: var(--semantic-color-primary);
}

.clinical-tabs-list.variant-pills .clinical-tabs-trigger[aria-selected="true"] {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--semantic-color-primary);
}

/* Variant: segmented */
.clinical-tabs-list.variant-segmented {
  width: fit-content;
  gap: 4px;
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--semantic-radius-md, 8px);
}

.clinical-tabs-list.variant-segmented .clinical-tabs-trigger {
  background-color: transparent;
  color: var(--semantic-color-text-body);
  border-width: 1px;
  border-style: solid;
  border-color: transparent;
  border-radius: var(--semantic-radius-sm, 6px);
}

.clinical-tabs-list.variant-segmented
  .clinical-tabs-trigger:hover:not(:disabled):not([aria-selected="true"]) {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.clinical-tabs-list.variant-segmented
  .clinical-tabs-trigger[aria-selected="true"] {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--semantic-color-primary);
  box-shadow: var(--semantic-shadow-sm);
}

.clinical-tabs-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-left: 6px;
  padding: 1px 6px;
  border-radius: var(--radius-full, 9999px);
  font-size: 0.75rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
}

.clinical-tabs-list.variant-underline
  .clinical-tabs-trigger[aria-selected="true"]
  .clinical-tabs-badge {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.clinical-tabs-list.variant-pills
  .clinical-tabs-trigger[aria-selected="true"]
  .clinical-tabs-badge,
.clinical-tabs-list.variant-segmented
  .clinical-tabs-trigger[aria-selected="true"]
  .clinical-tabs-badge {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-primary);
}

@media (max-width: 480px) {
  .clinical-tabs-list {
    padding: 4px;
    gap: 4px;
  }

  .clinical-tabs-trigger {
    padding: 10px 10px;
    font-size: 0.8125rem;
  }
}

@media (prefers-reduced-motion: reduce) {
  .clinical-tabs-trigger {
    transition: none !important;
  }
}
</style>
