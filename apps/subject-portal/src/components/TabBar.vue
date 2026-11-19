<template>
  <ul class="nav-tabs" role="tablist" tabindex="0" @keydown="handleTabKeyDown">
    <li
      v-for="tab in visibleTabs"
      :id="'tab-btn-' + tab.id"
      :key="tab.id"
      class="nav-item"
      :class="{ active: ui.currentView === tab.viewId }"
      role="none"
    >
      <button
        :ref="
          (el) => {
            if (el) tabRefs[tab.id] = el;
          }
        "
        type="button"
        role="tab"
        :aria-selected="ui.currentView === tab.viewId ? 'true' : 'false'"
        :aria-controls="tab.viewId"
        :tabindex="ui.currentView === tab.viewId ? 0 : -1"
        @click="selectView(tab.viewId)"
      >
        <span class="icon">{{ tab.icon }}</span> {{ tab.label }}
        <span
          v-if="tab.id === 'inbox' && inbox.unreadCount > 0"
          id="unread-count"
          class="badge-unread"
        >
          {{ inbox.unreadCount }}
        </span>
      </button>
    </li>
  </ul>
</template>

<script setup>
import { computed, nextTick } from "vue";

import { showView } from "../features/navigation.js";
import { AVAILABLE_SCREENS } from "../router/availability.js";
import { tabs } from "../router/routes.js";
import { useInboxStore } from "../stores/inbox.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const inbox = useInboxStore();
const sessionStore = useSessionStore();

const tabRefs = {};

const visibleTabs = computed(() => {
  void sessionStore.session?.isDemoMode;
  if (isDemoModeActive()) {
    return tabs;
  }
  return tabs.filter((tab) => AVAILABLE_SCREENS.has(tab.id));
});

function handleTabKeyDown(event) {
  const list = visibleTabs.value;
  if (list.length === 0) return;

  const currentTabId = list.find((t) => t.viewId === ui.currentView)?.id;
  if (!currentTabId) return;

  const currentIndex = list.findIndex((t) => t.id === currentTabId);
  let nextIndex;

  if (event.key === "ArrowRight" || event.key === "ArrowDown") {
    nextIndex = (currentIndex + 1) % list.length;
    event.preventDefault();
  } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
    nextIndex = (currentIndex - 1 + list.length) % list.length;
    event.preventDefault();
  } else {
    return;
  }

  const nextTab = list[nextIndex];
  selectView(nextTab.viewId);

  nextTick(() => {
    const btn = tabRefs[nextTab.id];
    if (btn) btn.focus();
  });
}

function selectView(viewId) {
  showView(viewId);
}
</script>
