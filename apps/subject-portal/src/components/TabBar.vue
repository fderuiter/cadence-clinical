<template>
  <ul class="nav-tabs" role="tablist" tabindex="0" @keydown="handleTabKeyDown">
    <li
      v-for="tab in tabs"
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
import { nextTick } from "vue";

import { showView } from "../features/navigation.js";
import { tabs } from "../router/routes.js";
import { useInboxStore } from "../stores/inbox.js";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
const inbox = useInboxStore();

const tabRefs = {};

function handleTabKeyDown(event) {
  const currentTabId = tabs.find((t) => t.viewId === ui.currentView)?.id;
  if (!currentTabId) return;

  const currentIndex = tabs.findIndex((t) => t.id === currentTabId);
  let nextIndex;

  if (event.key === "ArrowRight" || event.key === "ArrowDown") {
    nextIndex = (currentIndex + 1) % tabs.length;
    event.preventDefault();
  } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
    nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    event.preventDefault();
  } else {
    return;
  }

  const nextTab = tabs[nextIndex];
  selectView(nextTab.viewId);

  // Focus the newly active tab button
  nextTick(() => {
    const btn = tabRefs[nextTab.id];
    if (btn) btn.focus();
  });
}

function selectView(viewId) {
  showView(viewId);
}
</script>
