<template>
  <div ref="drawerRootRef">
    <!-- Contextual Help Slide-Out Drawer -->
    <ClinicalDrawer
      :is-open="knowledgeStore.isOpen"
      :teleport="false"
      id="contextual-help-drawer"
      overlay-id="contextual-help-drawer-overlay"
      overlay-class="drawer-overlay"
      panel-class="drawer-panel"
      body-class="drawer-body"
      aria-labelledby="help-drawer-title"
      @close="knowledgeStore.closeDrawer()"
    >
      <template #header>
        <!-- Header -->
        <div class="drawer-header">
          <div class="header-main-row">
            <div class="header-title-group">
              <span class="header-icon" aria-hidden="true">📖</span>
              <div>
                <h3 id="help-drawer-title" class="drawer-title">
                  Clinical Guidance &amp; SOPs
                </h3>
                <div class="context-badges">
                  <span class="context-badge route-badge" title="Active Route">
                    📍 {{ knowledgeStore.currentRoute }}
                  </span>
                  <span
                    class="context-badge persona-badge"
                    title="Active Persona"
                  >
                    {{ activePersonaLabel }}
                  </span>
                </div>
              </div>
            </div>

            <div class="header-actions">
              <ClinicalIconButton
                class="btn-icon-toggle"
                variant="ghost"
                size="sm"
                :class="{ active: isSearchOpen }"
                :aria-pressed="isSearchOpen ? 'true' : 'false'"
                label="Toggle Search SOPs"
                title="Toggle Search SOPs"
                @click="toggleSearch"
              >
                🔍
              </ClinicalIconButton>
              <ClinicalIconButton
                class="btn-close"
                variant="ghost"
                size="sm"
                label="Close Help Drawer"
                @click="knowledgeStore.closeDrawer()"
              >
                ✕
              </ClinicalIconButton>
            </div>
          </div>

          <!-- Inline Search Bar -->
          <div v-if="isSearchOpen" class="search-bar-row">
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
              ✕
            </ClinicalIconButton>
          </div>
        </div>
      </template>

      <!-- Body Content -->
      <!-- Loading State -->
      <div v-if="knowledgeStore.loading" class="loading-state">
        <div class="spinner" />
        <p>Resolving contextual SOP guidance...</p>
      </div>

      <!-- Error State -->
      <div
        v-else-if="knowledgeStore.error"
        id="contextual-help-error-alert"
        class="help-error-alert"
        role="alert"
        aria-live="assertive"
      >
        <div class="error-alert-header">
          <span class="error-alert-icon" aria-hidden="true">⚠️</span>
          <h4 class="error-alert-title">
            {{
              knowledgeStore.lastFailedAction?.type === "search"
                ? "Unable to Search SOP Articles"
                : "Unable to Load Contextual SOP Guidance"
            }}
          </h4>
        </div>
        <p class="error-alert-message">{{ knowledgeStore.error }}</p>
        <div class="error-alert-actions">
          <button
            id="btn-contextual-help-retry"
            type="button"
            class="btn btn-secondary btn-retry"
            @click="handleRetry"
          >
            ↻ Retry
          </button>
        </div>
      </div>

      <!-- Search Results View -->
      <div
        v-else-if="isSearchOpen && searchInput.trim()"
        class="search-results-section"
      >
        <div class="section-subhead">
          <h4>Search Results ({{ knowledgeStore.searchResults.length }})</h4>
          <button
            v-if="knowledgeStore.selectedArticle"
            type="button"
            class="btn-link"
            @click="backFromSearchSelection"
          >
            ← Back to Matched SOP
          </button>
        </div>

        <div
          v-if="knowledgeStore.searchResults.length === 0"
          class="empty-search"
        >
          <p>
            No SOPs matched your query "<em>{{ searchInput }}</em
            >".
          </p>
        </div>

        <div v-else class="search-items-list">
          <button
            v-for="item in knowledgeStore.searchResults"
            :key="item.id"
            type="button"
            class="search-result-card"
            :class="{
              active: knowledgeStore.displayedArticle?.id === item.id,
            }"
            :aria-pressed="
              knowledgeStore.displayedArticle?.id === item.id ? 'true' : 'false'
            "
            @click="selectSearchResult(item)"
            @keydown.enter.prevent="selectSearchResult(item)"
            @keydown.space.prevent="selectSearchResult(item)"
          >
            <span class="result-card-header">
              <span class="result-title">
                {{ item.title }}
              </span>
              <span v-if="item.version_label" class="badge badge-version">
                v{{ item.version_label }}
              </span>
            </span>
            <span v-if="item.tags" class="tags-row">
              <span
                v-for="t in formatTags(item.tags)"
                :key="t"
                class="tag-pill"
              >
                #{{ t }}
              </span>
            </span>
          </button>
        </div>
      </div>

      <!-- Matched / Selected SOP View -->
      <div v-else-if="knowledgeStore.displayedArticle" class="guidance-content">
        <!-- Back to Matched Spotlight SOP Bar if inspecting related -->
        <div v-if="knowledgeStore.selectedArticle" class="back-bar">
          <button
            ref="backToPrimaryBtnRef"
            type="button"
            class="btn-back"
            @click="returnToPrimarySop"
          >
            ← Return to Route Spotlight SOP
          </button>
        </div>

        <!-- Spotlight SOP Card -->
        <div class="sop-card">
          <div class="sop-meta-header">
            <span class="category-badge">
              {{
                knowledgeStore.displayedArticle.category_name ||
                "Clinical Guidance"
              }}
            </span>
            <span
              v-if="knowledgeStore.displayedArticle.version_label"
              class="badge badge-version"
            >
              v{{ knowledgeStore.displayedArticle.version_label }}
            </span>
            <span class="badge badge-status">PUBLISHED</span>
          </div>

          <h4 class="sop-title">
            {{ knowledgeStore.displayedArticle.title }}
          </h4>

          <!-- Tags -->
          <div v-if="knowledgeStore.displayedArticle.tags" class="tags-row">
            <span
              v-for="tag in formatTags(knowledgeStore.displayedArticle.tags)"
              :key="tag"
              class="tag-pill"
            >
              #{{ tag }}
            </span>
          </div>

          <!-- Section Anchor Callout -->
          <div v-if="knowledgeStore.activeSectionAnchor" class="anchor-callout">
            <span class="anchor-icon" aria-hidden="true">🎯</span>
            <div class="anchor-text">
              <strong>Spotlight Section:</strong>
              <code>{{ knowledgeStore.activeSectionAnchor }}</code>
            </div>
          </div>

          <!-- Article Body Header & Toggle -->
          <div class="body-toggle-bar">
            <span class="body-toggle-label"
              >Standard Operating Procedure Content</span
            >
            <button
              ref="toggleBodyBtnRef"
              type="button"
              class="btn-toggle-body"
              :aria-expanded="
                knowledgeStore.isArticleBodyExpanded ? 'true' : 'false'
              "
              aria-controls="contextual-help-sop-body"
              @click="knowledgeStore.toggleArticleBody()"
            >
              {{
                knowledgeStore.isArticleBodyExpanded
                  ? "▲ Collapse Body"
                  : "▼ Expand Body"
              }}
            </button>
          </div>

          <!-- Collapsible Article Body -->
          <div
            v-if="knowledgeStore.isArticleBodyExpanded"
            id="contextual-help-sop-body"
            class="sop-body prose-content"
          >
            <div
              v-if="knowledgeStore.displayedArticle.body_html"
              v-html="knowledgeStore.displayedArticle.body_html"
            />
            <div
              v-else-if="knowledgeStore.displayedArticle.body_markdown"
              class="markdown-pre"
            >
              {{ knowledgeStore.displayedArticle.body_markdown }}
            </div>
          </div>
        </div>

        <!-- Related Articles / Secondary Guides -->
        <div
          v-if="
            !knowledgeStore.selectedArticle &&
            knowledgeStore.relatedArticles.length > 0
          "
          class="related-section"
        >
          <h5 class="related-title">
            📚 Related Clinical SOPs ({{
              knowledgeStore.relatedArticles.length
            }})
          </h5>
          <div class="related-list">
            <button
              v-for="rel in knowledgeStore.relatedArticles"
              :key="rel.id"
              type="button"
              class="related-card"
              @click="selectRelatedArticle(rel)"
              @keydown.enter.prevent="selectRelatedArticle(rel)"
              @keydown.space.prevent="selectRelatedArticle(rel)"
            >
              <span class="related-card-top">
                <span class="related-card-title">{{ rel.title }}</span>
                <span v-if="rel.version_label" class="badge badge-version-sm">
                  v{{ rel.version_label }}
                </span>
              </span>
              <span v-if="rel.tags" class="tags-row">
                <span
                  v-for="t in formatTags(rel.tags).slice(0, 2)"
                  :key="t"
                  class="tag-pill tag-pill-sm"
                >
                  #{{ t }}
                </span>
              </span>
            </button>
          </div>
        </div>
      </div>

      <!-- Empty Fallback State -->
      <div v-else class="empty-state">
        <div class="empty-icon" aria-hidden="true">🔍</div>
        <h4>No Specific SOP Mapped for This Route</h4>
        <p>
          There is currently no published SOP mapped specifically to
          <code>{{ knowledgeStore.currentRoute }}</code> for persona
          <strong>{{ activePersonaLabel }}</strong>.
        </p>
        <div class="empty-actions">
          <button
            type="button"
            class="btn btn-secondary"
            @click="openSearchWithFallback"
          >
            🔍 Search Knowledge Hub
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="openHubRoute('/knowledge')"
          >
            📚 Open Full SOP Hub
          </button>
        </div>
      </div>

      <!-- Footer -->
      <template #footer>
        <div class="drawer-footer">
          <div class="drawer-footer-primary">
            <button
              type="button"
              class="btn btn-escalate"
              title="Log Support Ticket pre-populated with current route context"
              @click="handleEscalate"
            >
              <span class="btn-icon" aria-hidden="true">🎫</span>
              Escalate to Support Ticket
            </button>
            <div class="drawer-hub-links">
              <button
                id="btn-open-knowledge-hub"
                type="button"
                class="btn-hub-link"
                @click="openHubRoute('/knowledge')"
              >
                📚 Browse SOP Hub
              </button>
              <span class="hub-link-sep" aria-hidden="true">·</span>
              <button
                id="btn-open-tickets-hub"
                type="button"
                class="btn-hub-link"
                @click="openHubRoute('/tickets')"
              >
                🎫 Support Queue
              </button>
            </div>
          </div>
          <button
            type="button"
            class="btn btn-secondary"
            @click="knowledgeStore.closeDrawer()"
          >
            Close
          </button>
        </div>
      </template>
    </ClinicalDrawer>

    <!-- Ticket Escalation Modal -->
    <TicketCreateModal
      v-if="knowledgeStore.isEscalating"
      :is-open="knowledgeStore.isEscalating"
      :initial-data="knowledgeStore.escalationPayload || {}"
      @close="knowledgeStore.cancelEscalation()"
      @created="handleTicketCreated"
    />
  </div>
</template>

<script setup>
import {
  ref,
  computed,
  watch,
  nextTick,
  onMounted,
  onUnmounted,
  getCurrentInstance,
} from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import {
  ClinicalDrawer,
  ClinicalIconButton,
  hasActiveModalFocusTrap,
  useEscapeClose,
} from "ui";
import {
  buildScopedWorkflowPath,
  resolveWorkspaceNavigationScope,
} from "../canvas/palette";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import {
  canonicalizeContextualRoute,
  useKnowledgeStore,
} from "../stores/knowledge.js";
import { useAuthStore, PERSONA_PRESETS } from "../stores/auth.js";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "../utils/demoMode.js";
import TicketCreateModal from "./tickets/TicketCreateModal.vue";

const knowledgeStore = useKnowledgeStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const route = useRoute();
const instance = getCurrentInstance();
const { activeStudyCode } = useActiveStudyScope();
const activeCanvasStore = computed(() => {
  const pinia = getActivePinia();
  return pinia ? useStudyCanvasStore(pinia) : null;
});
let injectedRouter = null;
try {
  injectedRouter = useRouter();
} catch {
  injectedRouter = null;
}

const drawerRootRef = ref(null);
const searchInputRef = ref(null);
const backToPrimaryBtnRef = ref(null);
const toggleBodyBtnRef = ref(null);

function openHubRoute(path) {
  const scope = resolveWorkspaceNavigationScope(
    route,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  );
  const scopedPath = buildScopedWorkflowPath(path, {
    studyCode: scope.studyCode,
    siteId: scope.siteId,
    subjectCode: scope.subjectCode,
  });
  knowledgeStore.closeDrawer();
  const router =
    injectedRouter || instance?.appContext?.config?.globalProperties?.$router;
  if (router && typeof router.push === "function") {
    router.push(scopedPath);
  }
}

useEscapeClose(() => knowledgeStore.closeDrawer(), {
  isOpen: () => knowledgeStore.isOpen && !knowledgeStore.isEscalating,
});

const isSearchOpen = ref(false);
const searchInput = ref("");
let searchDebounceTimer = null;

const canonicalRoute = computed(() =>
  canonicalizeContextualRoute({
    path: route?.path || "/",
    query: route?.query || {},
  })
);

const activePersonaLabel = computed(() => {
  const pKey =
    authStore.currentPersona || knowledgeStore.activePersona || "super_admin";
  const p = PERSONA_PRESETS.find((preset) => preset.key === pKey);
  return p ? p.label.replace(/^.+?\s/, "") : "Super Admin";
});

function formatTags(tags) {
  if (!tags) return [];
  if (Array.isArray(tags)) return tags;
  if (typeof tags === "string") {
    try {
      const parsed = JSON.parse(tags);
      if (Array.isArray(parsed)) return parsed;
    } catch {
      return tags
        .split(",")
        .map((t) => t.trim())
        .filter(Boolean);
    }
  }
  return [];
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

function selectSearchResult(item) {
  knowledgeStore.selectArticle(item);
}

async function selectRelatedArticle(item) {
  knowledgeStore.selectArticle(item);
  await nextTick();
  backToPrimaryBtnRef.value?.focus?.();
}

async function returnToPrimarySop() {
  knowledgeStore.backToPrimary();
  await nextTick();
  const firstRelated = drawerRootRef.value?.querySelector?.(".related-card");
  if (firstRelated && typeof firstRelated.focus === "function") {
    firstRelated.focus();
  } else {
    toggleBodyBtnRef.value?.focus?.();
  }
}

async function backFromSearchSelection() {
  knowledgeStore.backToPrimary();
  await nextTick();
  searchInputRef.value?.focus?.();
}

function handleEscalate() {
  const hasRouteScope =
    route?.path &&
    canonicalRoute.value === knowledgeStore.currentRoute &&
    route.query &&
    Object.keys(route.query).length > 0;
  knowledgeStore.prepareEscalation(
    hasRouteScope ? { query: route.query } : undefined
  );
}

async function handleRetry() {
  await knowledgeStore.retryLastAction();
}

function handleTicketCreated(createdTicket) {
  if (createdTicket) {
    knowledgeStore.recordEscalatedTicket(createdTicket);
    const refId = createdTicket.reference || createdTicket.id;
    const queueSuffix = createdTicket.assigned_queue
      ? ` (${createdTicket.assigned_queue})`
      : "";
    const simulated = isDemoBuild() || String(refId || "").startsWith("SIM-");
    toastStore.push({
      tone: "success",
      title: simulated
        ? `Simulated ticket ${refId} saved in demo session`
        : `Support ticket ${refId} submitted`,
      message: `Reference ID: ${refId}${queueSuffix}`,
      action: refId
        ? {
            label: `Open ${refId}`,
            to: `/tickets?ref=${encodeURIComponent(refId)}`,
          }
        : null,
    });
  }
  knowledgeStore.cancelEscalation();
  knowledgeStore.closeDrawer();
}

// Watch canonical route path + Clinical Canvas stage/section queries
watch(canonicalRoute, (newRoute) => {
  if (newRoute && newRoute !== knowledgeStore.currentRoute) {
    searchInput.value = "";
    isSearchOpen.value = false;
    knowledgeStore.fetchContextualHelp(newRoute, authStore.currentPersona);
  }
});

// Watch persona changes to automatically refresh contextual help
watch(
  () => authStore.currentPersona,
  (newPersona) => {
    if (newPersona && newPersona !== knowledgeStore.activePersona) {
      searchInput.value = "";
      isSearchOpen.value = false;
      knowledgeStore.fetchContextualHelp(
        canonicalRoute.value || knowledgeStore.currentRoute,
        newPersona
      );
    }
  }
);

function isEditableOrFormControl(node) {
  const el =
    node instanceof HTMLElement
      ? node
      : typeof document !== "undefined" &&
          document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
  if (!el) return false;
  if (el.isContentEditable) return true;
  const tag = el.tagName?.toUpperCase?.() || "";
  if (["INPUT", "TEXTAREA", "SELECT", "OPTION"].includes(tag)) return true;
  if (
    typeof el.closest === "function" &&
    el.closest(
      '[contenteditable="true"], [contenteditable=""], [contenteditable="plaintext-only"]'
    )
  ) {
    return true;
  }
  const role = (el.getAttribute?.("role") || "").toLowerCase();
  return ["textbox", "searchbox", "combobox", "listbox", "spinbutton"].includes(
    role
  );
}

function hasActiveExternalModalTrap() {
  if (knowledgeStore.isEscalating) return true;
  if (hasActiveModalFocusTrap({ excludeId: "contextual-help-drawer" })) {
    return true;
  }
  let ancestor = drawerRootRef.value;
  while (ancestor && ancestor !== document.body) {
    if (
      ancestor.inert ||
      ancestor.hasAttribute?.("inert") ||
      ancestor.getAttribute?.("aria-hidden") === "true"
    ) {
      return true;
    }
    ancestor = ancestor.parentElement;
  }
  if (typeof document === "undefined") return false;
  const activeDialog = document.activeElement?.closest?.(
    '[aria-modal="true"], [role="dialog"], [role="alertdialog"]'
  );
  if (
    activeDialog &&
    activeDialog.id !== "contextual-help-drawer" &&
    !activeDialog.closest?.("#contextual-help-drawer")
  ) {
    return true;
  }
  const modalDialogs = document.querySelectorAll(
    '[aria-modal="true"], [role="alertdialog"]'
  );
  for (const dialog of modalDialogs) {
    if (dialog.id === "contextual-help-drawer") continue;
    if (dialog.closest?.("#contextual-help-drawer")) continue;
    return true;
  }
  return false;
}

// Keyboard accessibility: F1 / '?' to toggle contextual help; guarded against form controls and modal traps
function handleKeydown(e) {
  if (e.defaultPrevented) return;
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  if (e.key !== "F1" && e.key !== "?") return;
  if (hasActiveExternalModalTrap()) return;
  if (isEditableOrFormControl(e.target)) return;

  e.preventDefault();
  knowledgeStore.toggleDrawer();
}

onMounted(() => {
  window.addEventListener("keydown", handleKeydown);
  if (
    route?.path &&
    !knowledgeStore.hasLoadedContextualHelp &&
    !knowledgeStore.primaryArticle &&
    !knowledgeStore.loading
  ) {
    knowledgeStore.fetchContextualHelp(
      canonicalRoute.value,
      authStore.currentPersona
    );
  }
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeydown);
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
});
</script>

<style scoped>
:deep(.drawer-overlay) {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.45);
  display: flex;
  justify-content: flex-end;
  z-index: 1050;
  backdrop-filter: blur(2px);
}

:deep(.drawer-panel) {
  background-color: white;
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
  max-width: 100vw;
  height: 100vh;
  box-shadow: -6px 0 28px rgba(0, 0, 0, 0.18);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.22s cubic-bezier(0.16, 1, 0.3, 1);
  font-family: var(
    --font,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif
  );
  color: var(--primary);
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.header-main-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.header-title-group {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}

.header-icon {
  font-size: 1.5rem;
  line-height: 1;
}

.drawer-title {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--primary);
}

.context-badges {
  display: flex;
  gap: 6px;
  margin-top: 4px;
  flex-wrap: wrap;
}

.context-badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
}

.route-badge {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  font-family: monospace;
}

.persona-badge {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-icon-toggle {
  font-size: 0.95rem;
}

.btn-icon-toggle.active {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.btn-close {
  font-size: 1rem;
  line-height: 1;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.search-bar-row {
  position: relative;
  display: flex;
  align-items: center;
}

.search-input {
  width: 100%;
  min-height: 36px;
  padding: 8px 38px 8px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.85rem;
  background-color: white;
  color: var(--primary);
  box-sizing: border-box;
}

.search-input:focus-visible {
  border-color: var(--accent);
}

.btn-clear-search {
  position: absolute;
  right: 4px;
  font-size: 0.85rem;
}

:deep(.drawer-body) {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  background-color: var(--semantic-color-surface);
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 36px 16px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  gap: 10px;
}

.empty-icon {
  font-size: 2.5rem;
}

.empty-state h4 {
  margin: 0;
  color: var(--primary);
  font-size: 0.98rem;
}

.empty-state p {
  margin: 0;
  font-size: 0.82rem;
  line-height: 1.4;
}

.empty-actions {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
}

.spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--semantic-color-border);
  border-top-color: var(--accent);
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.guidance-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.back-bar {
  margin-bottom: -6px;
}

.btn-back {
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  padding: 0;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.btn-back:hover {
  text-decoration: underline;
}

.sop-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  background-color: var(--semantic-color-surface);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.sop-meta-header {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.category-badge {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
}

.badge-version {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-status {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.sop-title {
  margin: 0;
  font-size: 1.08rem;
  font-weight: 700;
  color: var(--primary);
  line-height: 1.35;
}

.tags-row {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.tag-pill {
  font-size: 0.7rem;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
  padding: 1px 5px;
  border-radius: 3px;
}

.tag-pill-sm {
  font-size: 0.65rem;
  padding: 1px 4px;
}

.anchor-callout {
  background-color: var(--semantic-color-warning-bg-badge);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.78rem;
  color: var(--semantic-color-warning-dark);
}

.anchor-icon {
  font-size: 1rem;
}

.anchor-text code {
  background-color: rgba(255, 255, 255, 0.6);
  padding: 2px 4px;
  border-radius: 3px;
  font-family: monospace;
}

.body-toggle-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 6px;
  border-top: 1px solid var(--semantic-color-surface-header);
}

.body-toggle-label {
  font-size: 0.76rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.btn-toggle-body {
  background: transparent;
  border: none;
  color: var(--accent);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  padding: 2px 4px;
}

.btn-toggle-body:hover {
  text-decoration: underline;
}

.sop-body {
  font-size: 0.86rem;
  line-height: 1.55;
  color: var(--semantic-color-text-body);
  border-top: 1px dashed var(--semantic-color-border);
  padding-top: 10px;
}

.sop-body :deep(h2),
.sop-body :deep(h3),
.sop-body :deep(h4) {
  color: var(--primary);
  margin: 12px 0 6px 0;
  font-weight: 700;
}

.sop-body :deep(h2) {
  font-size: 0.98rem;
}

.sop-body :deep(h3) {
  font-size: 0.9rem;
}

.sop-body :deep(p) {
  margin: 0 0 8px 0;
}

.sop-body :deep(ul),
.sop-body :deep(ol) {
  padding-left: 18px;
  margin: 0 0 8px 0;
}

.sop-body :deep(li) {
  margin-bottom: 4px;
}

.sop-body :deep(code) {
  background-color: var(--semantic-color-surface-header);
  padding: 2px 4px;
  border-radius: 3px;
  font-family: monospace;
  font-size: 0.8rem;
}

.markdown-pre {
  white-space: pre-wrap;
  font-family: inherit;
}

.related-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.related-title {
  margin: 0;
  font-size: 0.86rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.related-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.related-card {
  width: 100%;
  text-align: left;
  font-family: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
  background-color: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.related-card:hover,
.related-card:focus-visible {
  background-color: var(--semantic-color-surface);
  border-color: var(--accent);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.05);
}

.related-card:focus-visible,
.search-result-card:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.related-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.related-card-title {
  font-size: 0.84rem;
  font-weight: 600;
  color: var(--primary);
}

.badge-version-sm {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  font-size: 0.68rem;
  font-weight: 700;
  padding: 1px 5px;
  border-radius: 3px;
}

.search-results-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-subhead {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.section-subhead h4 {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
}

.btn-link {
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.78rem;
  cursor: pointer;
  padding: 0;
}

.search-items-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.search-result-card {
  width: 100%;
  text-align: left;
  font-family: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
  background-color: white;
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.search-result-card:hover {
  border-color: var(--accent);
  background-color: var(--semantic-color-surface-muted);
}

.search-result-card.active {
  border-color: var(--accent);
  background-color: var(--semantic-color-indigo-bg);
}

.result-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 6px;
}

.result-title {
  margin: 0;
  font-size: 0.86rem;
  font-weight: 600;
  color: var(--primary);
}

.empty-search {
  padding: 20px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  font-size: 0.84rem;
}

.help-error-alert {
  padding: 16px;
  border: 1px solid var(--semantic-color-error);
  border-radius: 8px;
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-deep);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.error-alert-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.error-alert-title {
  margin: 0;
  font-size: 0.92rem;
  font-weight: 700;
  color: var(--semantic-color-error-text-deep);
}

.error-alert-message {
  margin: 0;
  font-size: 0.84rem;
  line-height: 1.45;
  color: var(--semantic-color-error-text-dark);
}

.error-alert-actions {
  display: flex;
  justify-content: flex-start;
}

.btn-retry {
  border-color: var(--semantic-color-error-dark);
  color: var(--semantic-color-error-text-deep);
}

:deep(.clinical-drawer-footer) {
  padding: 0;
  border-top: none;
}

.drawer-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
}

.drawer-footer-primary {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
}

.drawer-hub-links {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.btn-hub-link {
  background: none;
  border: none;
  padding: 0;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-hub-link:hover {
  text-decoration: underline;
}

.hub-link-sep {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}

.btn {
  padding: 8px 14px;
  font-size: 0.84rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-header);
}

.btn-escalate {
  background-color: var(--accent);
  color: var(--semantic-color-surface);
  border: 1px solid transparent;
}

.btn-escalate:hover {
  background-color: var(--semantic-color-accent);
}

.btn-icon {
  font-size: 0.95rem;
}
</style>
