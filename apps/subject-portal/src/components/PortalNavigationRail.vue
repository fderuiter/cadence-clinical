<template>
  <nav
    class="portal-navigation"
    :class="{
      'is-tablet-rail': isTabletRailViewport,
      'is-rail-expanded': isTabletRailViewport && ui.railOverlayExpanded,
    }"
    aria-label="Participant navigation"
  >
    <div
      v-if="isTabletRailViewport && ui.railOverlayExpanded"
      class="portal-rail-scrim"
      aria-hidden="true"
      @click="ui.railOverlayExpanded = false"
    />

    <div class="portal-rail-surface">
      <button
        v-if="isTabletRailViewport"
        id="btn-portal-rail-toggle"
        type="button"
        class="btn btn-secondary portal-rail-toggle-btn"
        :aria-expanded="ui.railOverlayExpanded ? 'true' : 'false'"
        :aria-label="
          ui.railOverlayExpanded
            ? 'Collapse navigation rail'
            : 'Expand navigation rail'
        "
        :title="
          ui.railOverlayExpanded
            ? 'Collapse navigation rail'
            : 'Expand navigation rail'
        "
        @click="ui.railOverlayExpanded = !ui.railOverlayExpanded"
      >
        <component
          :is="ui.railOverlayExpanded ? ChevronsLeft : ChevronsRight"
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <span v-if="ui.railOverlayExpanded" class="rail-toggle-label">
          Collapse
        </span>
      </button>

      <TabBar
        :rail-collapsed="isTabletRailViewport && !ui.railOverlayExpanded"
        @select-tab="ui.railOverlayExpanded = false"
      />

      <!-- Inline Participant Card on wide desktop (>= 1280px) -->
      <ParticipantCard v-if="isWideDesktopViewport" mobile-expanded />
    </div>
  </nav>
</template>

<script setup>
import { ChevronsLeft, ChevronsRight } from "lucide-vue-next";
import { useEscapeClose } from "ui";
import { watch } from "vue";

import { usePortalViewport } from "../composables/usePortalViewport.js";
import { useUiStore } from "../stores/ui.js";
import ParticipantCard from "./ParticipantCard.vue";
import TabBar from "./TabBar.vue";

const ui = useUiStore();
const { isTabletRailViewport, isWideDesktopViewport } = usePortalViewport();

useEscapeClose(
  () => {
    ui.railOverlayExpanded = false;
  },
  {
    isOpen: () => isTabletRailViewport.value && ui.railOverlayExpanded,
  }
);

watch(isTabletRailViewport, (isRail) => {
  if (!isRail && ui.railOverlayExpanded) {
    ui.railOverlayExpanded = false;
  }
});
</script>

<style scoped>
.portal-rail-surface {
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
}

@media (min-width: 768px) and (max-width: 1279px) {
  .portal-navigation,
  .portal-navigation.is-tablet-rail {
    position: relative;
    width: 60px;
    min-width: 60px;
    max-width: 60px;
    flex: 0 0 60px;
    gap: 10px;
  }

  .portal-navigation.is-tablet-rail .portal-rail-surface {
    width: 60px;
    gap: 10px;
  }

  .portal-navigation.is-tablet-rail:not(.is-rail-expanded)
    :deep(.nav-item button) {
    justify-content: center;
    align-items: center;
    padding: 0;
    min-height: var(--touch-target-min, 48px);
    height: var(--touch-target-min, 48px);
    position: relative;
  }

  .portal-navigation.is-tablet-rail:not(.is-rail-expanded)
    :deep(.nav-item .icon) {
    width: 18px;
    height: 18px;
  }

  .portal-navigation.is-tablet-rail:not(.is-rail-expanded)
    :deep(.nav-item.active button) {
    border-left: 3px solid var(--primary);
    padding-left: 0;
    padding-right: 3px;
  }

  .portal-navigation.is-tablet-rail:not(.is-rail-expanded)
    :deep(.nav-item .badge-unread) {
    position: absolute;
    top: 4px;
    right: 4px;
    margin-left: 0;
    padding: 1px 5px;
    font-size: 0.75rem;
    line-height: 1.2;
  }

  .btn.portal-rail-toggle-btn {
    width: 100%;
    min-height: 44px;
    padding: 0 8px;
    gap: 6px;
    font-size: 12px;
    border-radius: 10px;
  }

  .portal-rail-scrim {
    position: fixed;
    inset: 0;
    background: var(--semantic-color-backdrop-soft);
    z-index: 940;
  }

  .portal-navigation.is-tablet-rail.is-rail-expanded .portal-rail-surface {
    position: absolute;
    top: 0;
    left: 0;
    width: 260px;
    z-index: 950;
    padding: 8px;
    background-color: var(--bg-card);
    border: 1px solid var(--border);
    border-radius: 12px;
    box-shadow: var(--shadow-xl);
  }

  .portal-navigation.is-tablet-rail.is-rail-expanded :deep(.nav-tabs) {
    border: none;
    box-shadow: none;
  }
}

@media (max-width: 767px) {
  .portal-rail-surface {
    gap: 0;
  }
}
</style>
