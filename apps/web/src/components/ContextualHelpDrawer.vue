<template>
  <div ref="drawerRootRef">
    <ClinicalDrawer
      id="contextual-help-drawer"
      :is-open="knowledgeStore.isOpen && !knowledgeStore.isEscalating"
      :restore-focus="() => !knowledgeStore.isEscalating"
      :teleport="false"
      size="sm"
      overlay-id="contextual-help-drawer-overlay"
      overlay-class="drawer-overlay"
      panel-class="drawer-panel"
      body-class="drawer-body"
      aria-labelledby="help-drawer-title"
      @close="knowledgeStore.closeDrawer()"
    >
      <template #header>
        <header class="drawer-header">
          <div class="header-title-group">
            <span class="header-icon" aria-hidden="true">
              <BookOpen
                :size="20"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
            </span>
            <h3 id="help-drawer-title" class="drawer-title">
              Clinical Guidance &amp; SOPs
            </h3>
          </div>

          <div class="header-actions">
            <ClinicalIconButton
              v-if="knowledgeStore.activeDrawerTab === 'guidance'"
              class="btn-icon-toggle"
              variant="ghost"
              size="sm"
              :class="{ active: isSearchOpen }"
              :aria-pressed="isSearchOpen ? 'true' : 'false'"
              label="Toggle Search SOPs"
              title="Toggle Search SOPs"
              @click="toggleSearch"
            >
              <Search
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
            </ClinicalIconButton>
            <ClinicalIconButton
              class="btn-close"
              variant="ghost"
              size="sm"
              label="Close Help Drawer"
              @click="knowledgeStore.closeDrawer()"
            >
              <X
                :size="20"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
            </ClinicalIconButton>
          </div>
        </header>
      </template>

      <div class="context-badges">
        <span class="context-badge route-badge" title="Active Route">
          <MapPin
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>{{ knowledgeStore.currentRoute }}</span>
        </span>
        <span class="context-badge persona-badge" title="Active Persona">
          {{ activePersonaLabel }}
        </span>
      </div>

      <div
        class="drawer-tabs"
        role="tablist"
        aria-label="Contextual assistance views"
      >
        <button
          id="tab-contextual-guidance"
          type="button"
          role="tab"
          class="drawer-tab-btn"
          :class="{ active: knowledgeStore.activeDrawerTab === 'guidance' }"
          :aria-selected="
            knowledgeStore.activeDrawerTab === 'guidance' ? 'true' : 'false'
          "
          aria-controls="panel-contextual-guidance"
          @click="switchTab('guidance')"
        >
          <BookOpen
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>SOP Guidance</span>
        </button>
        <button
          id="tab-contextual-tickets"
          type="button"
          role="tab"
          class="drawer-tab-btn"
          :class="{ active: knowledgeStore.activeDrawerTab === 'tickets' }"
          :aria-selected="
            knowledgeStore.activeDrawerTab === 'tickets' ? 'true' : 'false'
          "
          aria-controls="panel-contextual-tickets"
          @click="switchTab('tickets')"
        >
          <Ticket
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Support Tickets</span>
          <span
            v-if="knowledgeStore.hasLoadedDrawerTickets"
            class="tab-count-pill tabular-nums"
          >
            {{ knowledgeStore.drawerTickets.length }}
          </span>
        </button>
      </div>

      <div
        v-if="knowledgeStore.activeDrawerTab === 'guidance' && isSearchOpen"
        class="search-bar-row"
      >
        <label for="help-search-input" class="sr-only">
          Search clinical SOPs, guidelines, or topics
        </label>
        <input
          id="help-search-input"
          ref="searchInputRef"
          v-model="searchInput"
          type="text"
          class="search-input"
          placeholder="Search SOPs, guidelines, or topics..."
          @input="onSearchInput"
        />
        <ClinicalIconButton
          v-if="searchInput"
          class="btn-clear-search"
          variant="ghost"
          size="sm"
          label="Clear SOP search"
          @click="clearSearch"
        >
          <X
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
        </ClinicalIconButton>
      </div>

      <ContextualHelpGuidanceTab
        v-if="knowledgeStore.activeDrawerTab === 'guidance'"
        :is-search-open="isSearchOpen"
        :search-query="searchInput"
        :active-persona-label="activePersonaLabel"
        :get-drawer-container="resolveDrawerDialogContainer"
        :get-search-input-el="() => searchInputRef"
        @back-from-search="backFromSearchSelection"
        @open-search-fallback="openSearchWithFallback"
        @open-hub="openHubRoute"
      />
      <ContextualHelpTicketsTab
        v-else
        :get-drawer-container="resolveDrawerDialogContainer"
      />

      <template #footer>
        <ContextualHelpFooter
          :is-read-only-auditor="isReadOnlyAuditor"
          @escalate="handleEscalate"
          @open-hub="openHubRoute"
          @close="knowledgeStore.closeDrawer()"
        />
      </template>
    </ClinicalDrawer>

    <TicketCreateModal
      v-if="knowledgeStore.isEscalating && !isReadOnlyAuditor"
      :is-open="knowledgeStore.isEscalating"
      :initial-data="knowledgeStore.escalationPayload || {}"
      @close="knowledgeStore.cancelEscalation()"
      @created="handleTicketCreated"
    />
  </div>
</template>

<script>
import { defineLazyOverlay } from "../utils/lazyOverlay.js";

const TicketCreateModal = defineLazyOverlay(
  () => import("./tickets/TicketCreateModal.vue")
);
</script>

<script setup>
import { nextTick, ref } from "vue";
import { useRoute } from "vue-router";
import { BookOpen, MapPin, Search, Ticket, X } from "lucide-vue-next";
import { ClinicalDrawer, ClinicalIconButton } from "ui";
import { useKnowledgeStore } from "../stores/knowledge.js";
import { useAuthStore } from "../stores/auth.js";
import ContextualHelpFooter from "./help/ContextualHelpFooter.vue";
import ContextualHelpGuidanceTab from "./help/ContextualHelpGuidanceTab.vue";
import ContextualHelpTicketsTab from "./help/ContextualHelpTicketsTab.vue";
import { useContextualHelpSync } from "./help/useContextualHelpSync.js";

const knowledgeStore = useKnowledgeStore();
const authStore = useAuthStore();
const route = useRoute();

const drawerRootRef = ref(null);
const searchInputRef = ref(null);
const isSearchOpen = ref(false);
const searchInput = ref("");
let searchDebounceTimer = null;

function resolveDrawerDialogContainer() {
  return (
    drawerRootRef.value?.querySelector?.("#contextual-help-drawer") ||
    drawerRootRef.value
  );
}

const {
  isReadOnlyAuditor,
  activePersonaLabel,
  openHubRoute,
  handleEscalate,
  handleTicketCreated,
} = useContextualHelpSync({
  route,
  authStore,
  knowledgeStore,
  drawerRootRef,
  isSearchOpen,
  searchInput,
  clearSearchTimer: () => {
    if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  },
});

function switchTab(tab) {
  knowledgeStore.setActiveDrawerTab(tab);
  if (tab === "tickets" && !knowledgeStore.selectedTicket) {
    knowledgeStore.fetchDrawerTickets();
  }
}

async function toggleSearch() {
  isSearchOpen.value = !isSearchOpen.value;
  if (isSearchOpen.value) {
    searchInput.value = "";
    await nextTick();
    searchInputRef.value?.focus?.();
  }
}

async function openSearchWithFallback() {
  isSearchOpen.value = true;
  searchInput.value = "";
  await nextTick();
  searchInputRef.value?.focus?.();
}

async function clearSearch() {
  searchInput.value = "";
  knowledgeStore.search("");
  await nextTick();
  searchInputRef.value?.focus?.();
}

function onSearchInput() {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  searchDebounceTimer = setTimeout(() => {
    knowledgeStore.search(searchInput.value);
  }, 250);
}

async function backFromSearchSelection() {
  knowledgeStore.backToPrimary();
  await nextTick();
  searchInputRef.value?.focus?.();
}
</script>

<style scoped src="./help/contextualHelpPanel.css"></style>
<style scoped>
:deep(.drawer-overlay) {
  z-index: 1050;
}

:deep(.drawer-panel) {
  font-family: var(--semantic-font-family);
  color: var(--text-main);
}

:deep(.drawer-body) {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  background-color: var(--semantic-color-surface);
}

:deep(.clinical-drawer-footer) {
  padding: 0;
  border-top: none;
}
</style>
