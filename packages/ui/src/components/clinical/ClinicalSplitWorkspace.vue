<script setup>
import { computed } from "vue";
import { useWorkspaceTier } from "../../composables/useWorkspaceTier.js";
import ClinicalDrawer from "./ClinicalDrawer.vue";

const props = defineProps({
  sidebarOpen: {
    type: Boolean,
    default: false,
  },
  inspectorOpen: {
    type: Boolean,
    default: false,
  },
  sidebarTitle: {
    type: String,
    default: "Workspace navigation",
  },
  sidebarAriaLabel: {
    type: String,
    default: undefined,
  },
  sidebarId: {
    type: String,
    default: undefined,
  },
  sidebarClass: {
    type: [String, Array, Object],
    default: "",
  },
  sidebarDrawerSize: {
    type: String,
    default: "sm",
  },
  inspectorTitle: {
    type: String,
    default: "Inspector",
  },
  inspectorAriaLabel: {
    type: String,
    default: undefined,
  },
  inspectorId: {
    type: String,
    default: undefined,
  },
  inspectorClass: {
    type: [String, Array, Object],
    default: "",
  },
  inspectorDrawerSize: {
    type: String,
    default: "sm",
  },
  inspectorCollapsed: {
    type: Boolean,
    default: false,
  },
  showInspectorDrawerHeader: {
    type: Boolean,
    default: true,
  },
  embedded: {
    type: Boolean,
    default: false,
  },
  forceOverlaySidebar: {
    type: Boolean,
    default: false,
  },
  mainTag: {
    type: String,
    default: "div",
  },
  mainId: {
    type: String,
    default: undefined,
  },
  mainClass: {
    type: [String, Array, Object],
    default: "",
  },
  mainAriaLabel: {
    type: String,
    default: undefined,
  },
  mainRole: {
    type: String,
    default: undefined,
  },
  mainTabindex: {
    type: [Number, String],
    default: undefined,
  },
  teleport: {
    type: [Boolean, String],
    default: true,
  },
});

const emit = defineEmits(["update:sidebarOpen", "update:inspectorOpen"]);

const { tier, isDesktop, isTablet, isMobile } = useWorkspaceTier();

const dockSidebarInline = computed(
  () => isDesktop.value && !props.forceOverlaySidebar
);

const dockInspectorInline = computed(() => isDesktop.value);

const sidebarDrawerPosition = computed(() =>
  isMobile.value ? "bottom" : "left"
);

const inspectorDrawerPosition = computed(() =>
  isMobile.value ? "bottom" : "right"
);

function setSidebarOpen(val) {
  emit("update:sidebarOpen", Boolean(val));
}

function setInspectorOpen(val) {
  emit("update:inspectorOpen", Boolean(val));
}
</script>

<template>
  <div
    :class="[
      'clinical-split-workspace',
      `tier-${tier}`,
      { 'is-embedded': embedded },
    ]"
  >
    <slot
      name="rail"
      :tier="tier"
      :is-desktop="isDesktop"
      :is-tablet="isTablet"
      :is-mobile="isMobile"
    />

    <aside
      v-if="$slots.sidebar && dockSidebarInline"
      :id="sidebarId"
      :aria-label="sidebarAriaLabel || sidebarTitle"
      :class="['clinical-split-sidebar', sidebarClass]"
    >
      <slot
        name="sidebar"
        :tier="tier"
        :is-desktop="isDesktop"
        :is-tablet="isTablet"
        :is-mobile="isMobile"
        :is-overlay="false"
        :close="() => setSidebarOpen(false)"
      />
    </aside>

    <component
      :is="mainTag"
      :id="mainId"
      :role="mainRole"
      :aria-label="mainAriaLabel"
      :tabindex="mainTabindex"
      :class="['clinical-split-main', mainClass]"
    >
      <slot
        :tier="tier"
        :is-desktop="isDesktop"
        :is-tablet="isTablet"
        :is-mobile="isMobile"
        :dock-sidebar-inline="dockSidebarInline"
        :dock-inspector-inline="dockInspectorInline"
        :open-sidebar="() => setSidebarOpen(true)"
        :open-inspector="() => setInspectorOpen(true)"
      />
    </component>

    <div
      v-if="$slots.inspector && dockInspectorInline"
      :class="[
        'clinical-split-inspector',
        { 'is-collapsed': inspectorCollapsed },
        inspectorClass,
      ]"
    >
      <slot
        name="inspector"
        :tier="tier"
        :is-desktop="isDesktop"
        :is-tablet="isTablet"
        :is-mobile="isMobile"
        :is-overlay="false"
        :close="() => setInspectorOpen(false)"
      />
    </div>

    <ClinicalDrawer
      v-if="$slots.sidebar && !dockSidebarInline"
      :id="sidebarId"
      :model-value="sidebarOpen"
      :position="sidebarDrawerPosition"
      :size="sidebarDrawerSize"
      :title="sidebarTitle"
      :aria-label="sidebarAriaLabel"
      :panel-class="sidebarClass"
      :teleport="teleport"
      body-class="clinical-split-drawer-body"
      @update:model-value="setSidebarOpen"
    >
      <template #default="{ close }">
        <slot
          name="sidebar"
          :tier="tier"
          :is-desktop="isDesktop"
          :is-tablet="isTablet"
          :is-mobile="isMobile"
          :is-overlay="true"
          :close="close"
        />
      </template>
    </ClinicalDrawer>

    <ClinicalDrawer
      v-if="$slots.inspector && !dockInspectorInline"
      :id="inspectorId"
      :model-value="inspectorOpen"
      :position="inspectorDrawerPosition"
      :size="inspectorDrawerSize"
      :title="inspectorTitle"
      :aria-label="inspectorAriaLabel"
      :panel-class="inspectorClass"
      :show-header="showInspectorDrawerHeader"
      :teleport="teleport"
      body-class="clinical-split-drawer-body"
      @update:model-value="setInspectorOpen"
    >
      <template #default="{ close }">
        <slot
          name="inspector"
          :tier="tier"
          :is-desktop="isDesktop"
          :is-tablet="isTablet"
          :is-mobile="isMobile"
          :is-overlay="true"
          :close="close"
        />
      </template>
    </ClinicalDrawer>
  </div>
</template>

<style scoped>
.clinical-split-workspace {
  display: flex;
  flex-direction: row;
  align-items: stretch;
  width: 100%;
  min-width: 0;
  min-height: 0;
  flex: 1 1 auto;
  box-sizing: border-box;
}

.clinical-split-sidebar {
  width: var(--shell-sidebar-width, 280px);
  min-width: var(--shell-sidebar-width, 280px);
  flex-shrink: 0;
  background: var(--semantic-color-surface);
  border-right: 1px solid var(--semantic-color-border);
  min-height: 0;
  overflow-y: auto;
  box-sizing: border-box;
}

.clinical-split-main {
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  box-sizing: border-box;
}

@media (min-width: 768px) and (max-width: 1279px) {
  .clinical-split-workspace:not(.is-embedded) > .clinical-split-main {
    min-width: var(--shell-workspace-min-width, 480px);
  }
}

.clinical-split-inspector {
  width: var(--shell-inspector-width, 320px);
  min-width: var(--shell-inspector-width, 320px);
  flex-shrink: 0;
  min-height: 0;
  display: flex;
  flex-direction: column;
  box-sizing: border-box;
}

.clinical-split-inspector.is-collapsed {
  width: var(--shell-inspector-collapsed-width, 44px);
  min-width: var(--shell-inspector-collapsed-width, 44px);
}

:deep(.clinical-split-drawer-body) {
  padding: 0;
  display: flex;
  flex-direction: column;
}
</style>
