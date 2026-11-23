<script setup>
import { computed, ref } from "vue";
import { CircleDot, PanelLeftClose, PanelLeftOpen } from "lucide-vue-next";
import { useWorkspaceTier } from "../../composables/useWorkspaceTier.js";
import ClinicalDrawer from "./ClinicalDrawer.vue";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  id: {
    type: String,
    default: undefined,
  },
  ariaLabel: {
    type: String,
    default: "Workspace navigation",
  },
  title: {
    type: String,
    default: "Navigation",
  },
  items: {
    type: Array,
    default: () => [],
  },
  groups: {
    type: Array,
    default: () => [],
  },
  collapsed: {
    type: Boolean,
    default: undefined,
  },
  collapsible: {
    type: Boolean,
    default: true,
  },
  drawerOpen: {
    type: Boolean,
    default: false,
  },
  expandLabel: {
    type: String,
    default: "Expand navigation drawer",
  },
  collapseLabel: {
    type: String,
    default: "Collapse navigation rail",
  },
  teleport: {
    type: [Boolean, String],
    default: true,
  },
});

const emit = defineEmits(["select", "update:collapsed", "update:drawerOpen"]);

const { tier, isDesktop, isTablet, isMobile } = useWorkspaceTier();
const internalDesktopCollapsed = ref(false);

const isDesktopCollapsed = computed(() =>
  props.collapsed !== undefined
    ? props.collapsed
    : internalDesktopCollapsed.value
);

const isRailCompact = computed(
  () => isTablet.value || (isDesktop.value && isDesktopCollapsed.value)
);

const normalizedGroups = computed(() => {
  if (Array.isArray(props.groups) && props.groups.length > 0) {
    return props.groups;
  }
  if (Array.isArray(props.items) && props.items.length > 0) {
    return [{ id: "default", label: "", items: props.items }];
  }
  return [];
});

const flatItems = computed(() =>
  normalizedGroups.value.flatMap((group) => group.items || [])
);

function toggleDesktopRail() {
  if (isTablet.value) {
    emit("update:drawerOpen", !props.drawerOpen);
    return;
  }
  const next = !isDesktopCollapsed.value;
  internalDesktopCollapsed.value = next;
  emit("update:collapsed", next);
}

function openFlyoutDrawer() {
  emit("update:drawerOpen", true);
}

function setDrawerOpen(val) {
  emit("update:drawerOpen", Boolean(val));
}

function handleSelectItem(item, closeFn) {
  if (!item || item.disabled) return;
  emit("select", item);
  if (typeof closeFn === "function") {
    closeFn();
  }
}
</script>

<template>
  <nav
    v-if="!isMobile"
    v-bind="$attrs"
    :id="id"
    :aria-label="ariaLabel"
    :class="[
      'clinical-icon-rail',
      isRailCompact ? 'is-compact' : 'is-expanded',
      `tier-${tier}`,
    ]"
  >
    <div v-if="collapsible || isTablet" class="rail-header-bar">
      <button
        v-if="isTablet"
        type="button"
        class="rail-toggle-btn"
        :aria-label="expandLabel"
        :title="expandLabel"
        :aria-expanded="drawerOpen ? 'true' : 'false'"
        @click="openFlyoutDrawer"
      >
        <PanelLeftOpen :size="18" :stroke-width="1.75" aria-hidden="true" />
      </button>
      <button
        v-else-if="collapsible"
        type="button"
        class="rail-toggle-btn"
        :aria-label="isDesktopCollapsed ? expandLabel : collapseLabel"
        :title="isDesktopCollapsed ? expandLabel : collapseLabel"
        :aria-expanded="!isDesktopCollapsed ? 'true' : 'false'"
        @click="toggleDesktopRail"
      >
        <PanelLeftOpen
          v-if="isDesktopCollapsed"
          :size="18"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <PanelLeftClose
          v-else
          :size="18"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <span v-if="!isDesktopCollapsed" class="rail-header-title">{{
          title
        }}</span>
      </button>
    </div>

    <div v-if="isRailCompact" class="rail-compact-list">
      <button
        v-for="item in flatItems"
        :id="item.domId || undefined"
        :key="item.id"
        type="button"
        class="rail-compact-btn"
        :class="{ 'is-active': item.active }"
        :disabled="item.disabled"
        :aria-label="item.ariaLabel || item.label"
        :aria-current="item.active ? 'page' : undefined"
        :title="item.label"
        @click="handleSelectItem(item)"
      >
        <component
          :is="item.icon || CircleDot"
          :size="18"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <span
          v-if="
            item.badge !== undefined && item.badge !== null && item.badge !== ''
          "
          class="rail-compact-badge"
          aria-hidden="true"
        >
          {{ item.badge }}
        </span>
      </button>
    </div>

    <div v-else class="rail-expanded-body">
      <slot :tier="tier" :is-compact="false">
        <div
          v-for="group in normalizedGroups"
          :key="group.id"
          class="rail-group"
        >
          <h2 v-if="group.label" class="rail-group-heading">
            {{ group.label }}
          </h2>
          <ul class="rail-group-items">
            <li v-for="item in group.items" :key="item.id">
              <button
                :id="item.domId || undefined"
                type="button"
                class="rail-expanded-btn"
                :class="{ 'is-active': item.active }"
                :disabled="item.disabled"
                :aria-current="item.active ? 'page' : undefined"
                @click="handleSelectItem(item)"
              >
                <span class="rail-expanded-btn-label">
                  <component
                    :is="item.icon || CircleDot"
                    :size="16"
                    :stroke-width="1.75"
                    aria-hidden="true"
                  />
                  <span>{{ item.label }}</span>
                </span>
                <span
                  v-if="
                    item.badge !== undefined &&
                    item.badge !== null &&
                    item.badge !== ''
                  "
                  class="rail-expanded-badge"
                >
                  {{ item.badge }}
                </span>
              </button>
            </li>
          </ul>
        </div>
      </slot>
    </div>
  </nav>

  <ClinicalDrawer
    v-if="isTablet || isMobile"
    :id="isMobile ? id : undefined"
    :model-value="drawerOpen"
    :position="isMobile ? 'bottom' : 'left'"
    size="sm"
    :title="title"
    :aria-label="ariaLabel"
    :teleport="teleport"
    body-class="clinical-icon-rail-drawer-body"
    @update:model-value="setDrawerOpen"
  >
    <template #default="{ close }">
      <slot name="drawer" :close="close" :tier="tier">
        <slot :close="close" :tier="tier" :is-compact="false">
          <nav :aria-label="ariaLabel" class="rail-drawer-nav">
            <div
              v-for="group in normalizedGroups"
              :key="group.id"
              class="rail-group"
            >
              <h3 v-if="group.label" class="rail-group-heading">
                {{ group.label }}
              </h3>
              <ul class="rail-group-items">
                <li v-for="item in group.items" :key="item.id">
                  <button
                    :id="isMobile ? item.domId || undefined : undefined"
                    type="button"
                    class="rail-expanded-btn"
                    :class="{ 'is-active': item.active }"
                    :disabled="item.disabled"
                    :aria-current="item.active ? 'page' : undefined"
                    @click="handleSelectItem(item, close)"
                  >
                    <span class="rail-expanded-btn-label">
                      <component
                        :is="item.icon || CircleDot"
                        :size="16"
                        :stroke-width="1.75"
                        aria-hidden="true"
                      />
                      <span>{{ item.label }}</span>
                    </span>
                    <span
                      v-if="
                        item.badge !== undefined &&
                        item.badge !== null &&
                        item.badge !== ''
                      "
                      class="rail-expanded-badge"
                    >
                      {{ item.badge }}
                    </span>
                  </button>
                </li>
              </ul>
            </div>
          </nav>
        </slot>
      </slot>
    </template>
  </ClinicalDrawer>
</template>

<style scoped>
.clinical-icon-rail {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border-right: 1px solid var(--semantic-color-border);
  flex-shrink: 0;
  min-height: 0;
  box-sizing: border-box;
}

.clinical-icon-rail.is-expanded {
  width: var(--shell-rail-expanded-width, 224px);
  min-width: var(--shell-rail-expanded-width, 224px);
}

.clinical-icon-rail.is-compact {
  width: var(--shell-rail-collapsed-width, 60px);
  min-width: var(--shell-rail-collapsed-width, 60px);
  max-width: var(--shell-rail-collapsed-width, 60px);
  align-items: center;
}

.rail-header-bar {
  display: flex;
  align-items: center;
  width: 100%;
  padding: 6px 8px;
  border-bottom: 1px solid var(--semantic-color-border);
  box-sizing: border-box;
}

.clinical-icon-rail.is-compact .rail-header-bar {
  justify-content: center;
  padding: 6px 0;
}

.rail-toggle-btn {
  min-width: var(--touch-hit-area-min, 44px);
  min-height: var(--touch-hit-area-min, 44px);
  padding: 6px 8px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  font: inherit;
  font-size: var(--font-size-xs, 13px);
  font-weight: 600;
}

.rail-toggle-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.rail-header-title {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.rail-compact-list {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 8px 0;
  width: 100%;
  overflow-y: auto;
}

.rail-compact-btn {
  position: relative;
  width: var(--touch-hit-area-min, 44px);
  height: var(--touch-hit-area-min, 44px);
  min-width: var(--touch-hit-area-min, 44px);
  min-height: var(--touch-hit-area-min, 44px);
  border: 1px solid transparent;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}

.rail-compact-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.rail-compact-btn.is-active {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  border-color: var(--semantic-color-primary);
}

.rail-compact-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.rail-compact-badge {
  position: absolute;
  top: 3px;
  right: 3px;
  min-width: 16px;
  height: 16px;
  padding: 0 3px;
  border-radius: var(--radius-full);
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  font-size: 10px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.rail-expanded-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 10px;
}

.rail-drawer-nav {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.rail-group + .rail-group {
  margin-top: 12px;
}

.rail-group-heading {
  margin: 0 0 4px;
  padding: 0 8px;
  font-size: var(--font-size-2xs, 11px);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--semantic-color-text-muted);
}

.rail-group-items {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.rail-expanded-btn {
  width: 100%;
  min-height: var(--touch-hit-area-min, 44px);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--semantic-color-text);
  font: inherit;
  font-size: var(--font-size-sm, 14px);
  text-align: left;
  cursor: pointer;
}

.rail-expanded-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.rail-expanded-btn.is-active {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  border-color: var(--semantic-color-primary);
  font-weight: 600;
}

.rail-expanded-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.rail-expanded-btn-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.rail-expanded-badge {
  font-size: var(--font-size-xs, 13px);
  color: var(--semantic-color-text-muted);
  font-variant-numeric: tabular-nums;
}
</style>
