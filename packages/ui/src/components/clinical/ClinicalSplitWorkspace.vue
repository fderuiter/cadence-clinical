<script setup>
import { computed, isRef } from "vue";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import {
  clinicalDrawerHostProps,
  clinicalOverlayProps,
} from "./sharedProps.js";
import { useResponsiveSplitWorkspace } from "../../composables/useResponsiveSplitWorkspace.js";

const props = defineProps({
  ...clinicalOverlayProps,
  modelValue: {
    type: Boolean,
    default: undefined,
  },
  isOpen: {
    type: Boolean,
    default: undefined,
  },
  hasSelection: {
    type: Boolean,
    default: true,
  },
  inlineOpen: {
    type: Boolean,
    default: undefined,
  },
  keepMounted: {
    type: Boolean,
    default: false,
  },
  layoutVariant: {
    type: String,
    default: "master-detail",
    validator: (val) => ["master-detail", "main-inspector"].includes(val),
  },
  masterWidth: {
    type: String,
    default: "340px",
  },
  detailWidth: {
    type: String,
    default: "360px",
  },
  compactBreakpoint: {
    type: Number,
    default: 1280,
  },
  sheetBreakpoint: {
    type: Number,
    default: 640,
  },
  ...clinicalDrawerHostProps,
  drawerSize: {
    type: String,
    default: "lg",
  },
  drawerPosition: {
    type: String,
    default: "right",
  },
  showDrawerHeader: {
    type: Boolean,
    default: true,
  },
  closeLabel: {
    type: String,
    default: "Close detail panel",
  },
  ariaLabel: {
    type: String,
    default: undefined,
  },
  ariaLabelledby: {
    type: String,
    default: undefined,
  },
  masterClass: {
    type: [String, Array, Object],
    default: "",
  },
  detailClass: {
    type: [String, Array, Object],
    default: "",
  },
  overlayClass: {
    type: [String, Array, Object],
    default: "",
  },
  panelClass: {
    type: [String, Array, Object],
    default: "",
  },
  bodyClass: {
    type: [String, Array, Object],
    default: "",
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:isOpen",
  "close",
  "mode-change",
]);

const workspace = useResponsiveSplitWorkspace({
  compactBreakpoint: props.compactBreakpoint,
  sheetBreakpoint: props.sheetBreakpoint,
  onModeChange: (mode) => emit("mode-change", mode),
});

const isControlled = computed(
  () => props.modelValue !== undefined || props.isOpen !== undefined
);

const rawOpenState = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue;
  if (props.isOpen !== undefined) return props.isOpen;
  return workspace.isDetailOpen.value;
});

const resolvedDrawerOpen = computed(
  () =>
    workspace.isCompact.value &&
    Boolean(props.hasSelection) &&
    Boolean(rawOpenState.value)
);

const resolvedInlineOpen = computed(() => {
  if (props.inlineOpen !== undefined) return props.inlineOpen;
  return true;
});

const splitStyle = computed(() => ({
  "--split-master-width": props.masterWidth,
  "--split-detail-width": props.detailWidth,
}));

const resolveExplicitRestoreTarget = () => {
  const source = props.restoreFocus;
  if (!source || source === true) return null;
  if (typeof source === "function") return source();
  if (isRef(source)) return source.value;
  return source;
};

const resolvedRestoreFocus = computed(() => {
  if (props.restoreFocus === false) return false;
  return () =>
    resolveExplicitRestoreTarget() || workspace.activeTriggerEl.value;
});

const openDetail = (eventOrEl) => {
  workspace.openDetail(eventOrEl);
  if (isControlled.value) {
    emit("update:modelValue", true);
    emit("update:isOpen", true);
  }
};

const closeDetail = () => {
  if (!props.dismissible) return;
  workspace.closeDetail();
  emit("update:modelValue", false);
  emit("update:isOpen", false);
  emit("close");
};

const handleDrawerModelUpdate = (nextVal) => {
  if (!nextVal) {
    closeDetail();
  } else {
    openDetail();
  }
};

defineExpose({
  isCompact: workspace.isCompact,
  isMobileSheet: workspace.isMobileSheet,
  presentationMode: workspace.presentationMode,
  isDetailOpen: resolvedDrawerOpen,
  activeTriggerEl: workspace.activeTriggerEl,
  captureTrigger: workspace.captureTrigger,
  openDetail,
  closeDetail,
  syncViewport: workspace.syncViewport,
});
</script>

<template>
  <div
    :class="[
      'clinical-split-workspace',
      `variant-${layoutVariant}`,
      {
        'is-compact': workspace.isCompact.value,
        'is-inline-single': !workspace.isCompact.value && !resolvedInlineOpen,
      },
    ]"
    :style="splitStyle"
  >
    <div :class="['clinical-split-master', masterClass]">
      <slot
        name="master"
        :is-compact="workspace.isCompact.value"
        :is-mobile-sheet="workspace.isMobileSheet.value"
        :presentation-mode="workspace.presentationMode.value"
        :open-detail="openDetail"
        :close-detail="closeDetail"
        :capture-trigger="workspace.captureTrigger"
      />
    </div>

    <div
      v-if="$slots['inline-content']"
      :class="['clinical-split-detail', detailClass]"
    >
      <slot
        name="inline-content"
        :is-compact="workspace.isCompact.value"
        :is-mobile-sheet="workspace.isMobileSheet.value"
        :is-detail-open="resolvedDrawerOpen"
        :open-detail="openDetail"
        :close-detail="closeDetail"
      />
    </div>

    <slot
      v-else-if="workspace.isCompact.value && $slots['compact-after']"
      name="compact-after"
      :is-detail-open="resolvedDrawerOpen"
      :open-detail="openDetail"
      :close-detail="closeDetail"
    />

    <div
      v-else-if="!workspace.isCompact.value && resolvedInlineOpen"
      :class="['clinical-split-detail', detailClass]"
    >
      <slot
        name="detail"
        :is-compact="false"
        :is-mobile-sheet="false"
        presentation-mode="inline"
        :close="closeDetail"
      />
    </div>

    <ClinicalDrawer
      v-if="workspace.isCompact.value"
      :id="drawerId"
      :overlay-id="drawerOverlayId"
      :model-value="resolvedDrawerOpen"
      :keep-mounted="keepMounted"
      :title="drawerTitle"
      :subtitle="drawerSubtitle"
      :size="drawerSize"
      :position="drawerPosition"
      :mobile-bottom-sheet="mobileBottomSheet"
      :dismissible="dismissible"
      :close-on-backdrop="closeOnBackdrop"
      :show-header="showDrawerHeader"
      :close-label="closeLabel"
      :teleport="teleport"
      :initial-focus="initialFocus"
      :restore-focus="resolvedRestoreFocus"
      :fallback-focus="fallbackFocus"
      :aria-label="ariaLabel"
      :aria-labelledby="ariaLabelledby"
      :overlay-class="['clinical-split-drawer-overlay', overlayClass]"
      :panel-class="['clinical-split-drawer-panel', panelClass]"
      :body-class="['clinical-split-drawer-body', bodyClass]"
      @update:model-value="handleDrawerModelUpdate"
    >
      <template v-if="$slots['drawer-header']" #header="headerSlotProps">
        <slot name="drawer-header" v-bind="headerSlotProps" />
      </template>

      <slot
        name="detail"
        :is-compact="true"
        :is-mobile-sheet="workspace.isMobileSheet.value"
        :presentation-mode="workspace.presentationMode.value"
        :close="closeDetail"
      />

      <template v-if="$slots['drawer-footer']" #footer="footerSlotProps">
        <slot name="drawer-footer" v-bind="footerSlotProps" />
      </template>
    </ClinicalDrawer>
  </div>
</template>

<style scoped>
.clinical-split-workspace {
  --split-master-width: 340px;
  --split-detail-width: 360px;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  align-items: stretch;
  gap: var(--spacing-md, 16px);
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}

.clinical-split-master,
.clinical-split-detail {
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

@media (min-width: 1280px) {
  .clinical-split-workspace.variant-master-detail:not(.is-inline-single) {
    grid-template-columns: var(--split-master-width, 340px) minmax(0, 1fr);
  }

  .clinical-split-workspace.variant-main-inspector:not(.is-inline-single) {
    grid-template-columns: minmax(0, 1fr) var(--split-detail-width, 360px);
  }
}
</style>
