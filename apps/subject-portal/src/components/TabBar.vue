<template>
  <ul ref="tablistRef" class="nav-tabs" role="tablist">
    <li
      v-for="tab in tabs"
      :id="tab.itemId"
      :key="tab.id"
      class="nav-item"
      :class="{ active: activeTab?.id === tab.id }"
      role="none"
    >
      <button
        :id="tab.buttonId"
        :ref="
          (el) => {
            if (el) tabRefs[tab.id] = el;
          }
        "
        type="button"
        role="tab"
        :aria-selected="activeTab?.id === tab.id ? 'true' : 'false'"
        :aria-controls="tab.viewId"
        :tabindex="activeTab?.id === tab.id ? 0 : -1"
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
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";

import { showView } from "../features/navigation.js";
import { activeTabForView, tabs } from "../router/routes.js";
import { useInboxStore } from "../stores/inbox.js";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
const inbox = useInboxStore();

const tablistRef = ref(null);
const tabRefs = {};
const activeTab = computed(() => activeTabForView(ui.currentView));

onMounted(() => {
  tablistRef.value?.addEventListener("keydown", handleTabKeyDown);
});

onBeforeUnmount(() => {
  tablistRef.value?.removeEventListener("keydown", handleTabKeyDown);
});

function handleTabKeyDown(event) {
  const activeElement =
    typeof document !== "undefined" ? document.activeElement : null;
  let currentIndex = tabs.findIndex((t) => tabRefs[t.id] === activeElement);
  if (currentIndex === -1) {
    currentIndex = tabs.findIndex((t) => t.id === activeTab.value?.id);
  }
  if (currentIndex === -1) return;

  let nextIndex;
  if (event.key === "ArrowRight" || event.key === "ArrowDown") {
    nextIndex = (currentIndex + 1) % tabs.length;
    event.preventDefault();
  } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
    nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    event.preventDefault();
  } else if (event.key === "Home") {
    nextIndex = 0;
    event.preventDefault();
  } else if (event.key === "End") {
    nextIndex = tabs.length - 1;
    event.preventDefault();
  } else {
    return;
  }

  const nextTab = tabs[nextIndex];
  selectView(nextTab.viewId);

  const btn =
    tabRefs[nextTab.id] ||
    (typeof document !== "undefined"
      ? document.getElementById(nextTab.buttonId)
      : null);
  if (btn) {
    btn.focus();
  } else {
    nextTick(() => {
      const asyncBtn = tabRefs[nextTab.id];
      if (asyncBtn) asyncBtn.focus();
    });
  }
}

function selectView(viewId) {
  showView(viewId);
}
</script>
