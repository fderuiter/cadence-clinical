<template>
  <ul
    v-show="visibleTabs.length > 0"
    ref="tablistRef"
    class="nav-tabs"
    :role="visibleTabs.length > 0 ? 'tablist' : undefined"
  >
    <li
      v-for="tab in visibleTabs"
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
        :aria-controls="
          tab.id === 'tasks' && ui.currentView === 'view-questionnaire'
            ? 'view-questionnaire'
            : tab.viewId
        "
        :tabindex="activeTab?.id === tab.id ? 0 : -1"
        @click="selectView(tab.viewId)"
      >
        <component
          :is="tab.icon"
          class="icon"
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
        />
        <span class="nav-label">{{ tab.label }}</span>
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
import { flushActiveQuestionnaireDraft } from "../features/questionnaire.js";
import { isScreenAvailable } from "../router/availability.js";
import { activeTabForView, tabs } from "../router/routes.js";
import { useInboxStore } from "../stores/inbox.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const inbox = useInboxStore();
const sessionStore = useSessionStore();

const tablistRef = ref(null);
const tabRefs = {};
const activeTab = computed(() => activeTabForView(ui.currentView));

onMounted(() => {
  tablistRef.value?.addEventListener("keydown", handleTabKeyDown);
});

onBeforeUnmount(() => {
  tablistRef.value?.removeEventListener("keydown", handleTabKeyDown);
});

const visibleTabs = computed(() => {
  void sessionStore.session?.isDemoMode;
  void sessionStore.session?.token;
  void sessionStore.session?.userId;
  void sessionStore.session?.isOfflineMode;
  if (isDemoModeActive()) {
    return tabs;
  }
  return tabs.filter((tab) => isScreenAvailable(tab.id));
});

function handleTabKeyDown(event) {
  const list = visibleTabs.value;
  if (list.length === 0) return;

  const activeElement =
    typeof document !== "undefined" ? document.activeElement : null;
  let currentIndex = list.findIndex((t) => tabRefs[t.id] === activeElement);
  if (currentIndex === -1) {
    currentIndex = list.findIndex((t) => t.id === activeTab.value?.id);
  }
  if (currentIndex === -1) return;

  let nextIndex;
  if (event.key === "ArrowRight" || event.key === "ArrowDown") {
    nextIndex = (currentIndex + 1) % list.length;
    event.preventDefault();
  } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
    nextIndex = (currentIndex - 1 + list.length) % list.length;
    event.preventDefault();
  } else if (event.key === "Home") {
    nextIndex = 0;
    event.preventDefault();
  } else if (event.key === "End") {
    nextIndex = list.length - 1;
    event.preventDefault();
  } else {
    return;
  }

  const nextTab = list[nextIndex];
  showView(nextTab.viewId, { preserveTabFocus: true });

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
  if (
    ui.currentView === "view-questionnaire" &&
    viewId !== "view-questionnaire"
  ) {
    void flushActiveQuestionnaireDraft({ clearActive: true });
  }
  showView(viewId);
}
</script>
