<template>
  <div>
    <!-- Contextual Help Slide-Out Drawer -->
    <div
      v-if="knowledgeStore.isOpen"
      id="contextual-help-drawer-overlay"
      class="drawer-overlay"
      @click.self="knowledgeStore.closeDrawer()"
    >
      <div
        id="contextual-help-drawer"
        class="drawer-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="help-drawer-title"
        tabindex="-1"
      >
        <!-- Header -->
        <div class="drawer-header">
          <div class="header-main-row">
            <div class="header-title-group">
              <span class="header-icon">📖</span>
              <div>
                <h3
                  id="help-drawer-title"
                  class="drawer-title"
                >
                  Clinical Guidance &amp; SOPs
                </h3>
                <div class="context-badges">
                  <span
                    class="context-badge route-badge"
                    title="Active Route"
                  >
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
              <button
                type="button"
                class="btn-icon-toggle"
                :class="{ active: isSearchOpen }"
                title="Toggle Search SOPs"
                @click="toggleSearch"
              >
                🔍
              </button>
              <button
                type="button"
                class="btn-close"
                aria-label="Close Help Drawer"
                @click="knowledgeStore.closeDrawer()"
              >
                ✕
              </button>
            </div>
          </div>

          <!-- Inline Search Bar -->
          <div
            v-if="isSearchOpen"
            class="search-bar-row"
          >
            <input
              id="help-search-input"
              v-model="searchInput"
              type="text"
              class="search-input"
              placeholder="Search SOPs, guidelines, or topics..."
              @input="onSearchInput"
            >
            <button
              v-if="searchInput"
              type="button"
              class="btn-clear-search"
              @click="clearSearch"
            >
              ✕
            </button>
          </div>
        </div>

        <!-- Body Content -->
        <div class="drawer-body">
          <!-- Loading State -->
          <div
            v-if="knowledgeStore.loading"
            class="loading-state"
          >
            <div class="spinner" />
            <p>Resolving contextual SOP guidance...</p>
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
                class="btn-link"
                @click="knowledgeStore.backToPrimary()"
              >
                ← Back to Matched SOP
              </button>
            </div>

            <div
              v-if="knowledgeStore.searchResults.length === 0"
              class="empty-search"
            >
              <p>No SOPs matched your query "<em>{{ searchInput }}</em>".</p>
            </div>

            <div
              v-else
              class="search-items-list"
            >
              <div
                v-for="item in knowledgeStore.searchResults"
                :key="item.id"
                class="search-result-card"
                :class="{
                  active: knowledgeStore.displayedArticle?.id === item.id,
                }"
                @click="selectSearchResult(item)"
              >
                <div class="result-card-header">
                  <h5 class="result-title">
                    {{ item.title }}
                  </h5>
                  <span
                    v-if="item.version_label"
                    class="badge badge-version"
                  >
                    v{{ item.version_label }}
                  </span>
                </div>
                <div
                  v-if="item.tags"
                  class="tags-row"
                >
                  <span
                    v-for="t in formatTags(item.tags)"
                    :key="t"
                    class="tag-pill"
                  >
                    #{{ t }}
                  </span>
                </div>
              </div>
            </div>
          </div>

          <!-- Matched / Selected SOP View -->
          <div
            v-else-if="knowledgeStore.displayedArticle"
            class="guidance-content"
          >
            <!-- Back to Matched Spotlight SOP Bar if inspecting related -->
            <div
              v-if="knowledgeStore.selectedArticle"
              class="back-bar"
            >
              <button
                class="btn-back"
                @click="knowledgeStore.backToPrimary()"
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
              <div
                v-if="knowledgeStore.displayedArticle.tags"
                class="tags-row"
              >
                <span
                  v-for="tag in formatTags(
                    knowledgeStore.displayedArticle.tags
                  )"
                  :key="tag"
                  class="tag-pill"
                >
                  #{{ tag }}
                </span>
              </div>

              <!-- Section Anchor Callout -->
              <div
                v-if="knowledgeStore.activeSectionAnchor"
                class="anchor-callout"
              >
                <span class="anchor-icon">🎯</span>
                <div class="anchor-text">
                  <strong>Spotlight Section:</strong>
                  <code>{{ knowledgeStore.activeSectionAnchor }}</code>
                </div>
              </div>

              <!-- Article Body Header & Toggle -->
              <div class="body-toggle-bar">
                <span class="body-toggle-label">Standard Operating Procedure Content</span>
                <button
                  type="button"
                  class="btn-toggle-body"
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
                <div
                  v-for="rel in knowledgeStore.relatedArticles"
                  :key="rel.id"
                  class="related-card"
                  @click="knowledgeStore.selectArticle(rel)"
                >
                  <div class="related-card-top">
                    <span class="related-card-title">{{ rel.title }}</span>
                    <span
                      v-if="rel.version_label"
                      class="badge badge-version-sm"
                    >
                      v{{ rel.version_label }}
                    </span>
                  </div>
                  <div
                    v-if="rel.tags"
                    class="tags-row"
                  >
                    <span
                      v-for="t in formatTags(rel.tags).slice(0, 2)"
                      :key="t"
                      class="tag-pill tag-pill-sm"
                    >
                      #{{ t }}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Empty Fallback State -->
          <div
            v-else
            class="empty-state"
          >
            <div class="empty-icon">
              🔍
            </div>
            <h4>No Specific SOP Mapped for This Route</h4>
            <p>
              There is currently no published SOP mapped specifically to
              <code>{{ knowledgeStore.currentRoute }}</code> for persona
              <strong>{{ activePersonaLabel }}</strong>.
            </p>
            <div class="empty-actions">
              <button
                class="btn btn-secondary"
                @click="openSearchWithFallback"
              >
                🔍 Search Knowledge Hub
              </button>
            </div>
          </div>
        </div>

        <!-- Footer -->
        <div class="drawer-footer">
          <button
            type="button"
            class="btn btn-escalate"
            title="Log Support Ticket pre-populated with current route context"
            @click="handleEscalate"
          >
            <span class="btn-icon">🎫</span>
            Escalate to Support Ticket
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="knowledgeStore.closeDrawer()"
          >
            Close
          </button>
        </div>
      </div>
    </div>

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
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useRoute } from "vue-router";
import { useKnowledgeStore } from "../stores/knowledge.js";
import { useAuthStore, PERSONA_PRESETS } from "../stores/auth.js";
import TicketCreateModal from "./tickets/TicketCreateModal.vue";

const knowledgeStore = useKnowledgeStore();
const authStore = useAuthStore();
const route = useRoute();

const isSearchOpen = ref(false);
const searchInput = ref("");
let searchDebounceTimer = null;

const activePersonaLabel = computed(() => {
  const pKey = authStore.currentPersona || knowledgeStore.activePersona || "super_admin";
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
      return tags.split(",").map((t) => t.trim()).filter(Boolean);
    }
  }
  return [];
}

function toggleSearch() {
  isSearchOpen.value = !isSearchOpen.value;
  if (isSearchOpen.value) {
    searchInput.value = "";
  }
}

function openSearchWithFallback() {
  isSearchOpen.value = true;
  searchInput.value = "";
}

function clearSearch() {
  searchInput.value = "";
  knowledgeStore.search("");
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

function handleEscalate() {
  knowledgeStore.prepareEscalation();
}

function handleTicketCreated(payload) {
  knowledgeStore.cancelEscalation();
  knowledgeStore.closeDrawer();
}

// Watch route path changes to automatically update contextual help
watch(
  () => route?.path,
  (newPath) => {
    if (newPath && newPath !== knowledgeStore.currentRoute) {
      knowledgeStore.fetchContextualHelp(newPath, authStore.currentPersona);
    }
  }
);

// Watch persona changes to automatically refresh contextual help
watch(
  () => authStore.currentPersona,
  (newPersona) => {
    if (newPersona && newPersona !== knowledgeStore.activePersona) {
      knowledgeStore.fetchContextualHelp(
        route?.path || knowledgeStore.currentRoute,
        newPersona
      );
    }
  }
);

// Keyboard accessibility: Escape to close drawer, F1 / '?' to toggle
function handleKeydown(e) {
  if (e.key === "Escape" && knowledgeStore.isOpen) {
    knowledgeStore.closeDrawer();
  } else if (
    e.key === "F1" ||
    (e.key === "?" && !["INPUT", "TEXTAREA"].includes(e.target?.tagName))
  ) {
    e.preventDefault();
    knowledgeStore.toggleDrawer();
  }
}

onMounted(() => {
  window.addEventListener("keydown", handleKeydown);
  if (route?.path && !knowledgeStore.primaryArticle && knowledgeStore.currentRoute !== route.path) {
    knowledgeStore.fetchContextualHelp(route.path, authStore.currentPersona);
  }
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeydown);
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
});
</script>

<style scoped>
.drawer-overlay {
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

.drawer-panel {
  background-color: white;
  width: 100%;
  max-width: 420px;
  height: 100vh;
  box-shadow: -6px 0 28px rgba(0, 0, 0, 0.18);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.22s cubic-bezier(0.16, 1, 0.3, 1);
  font-family: var(--font, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif);
  color: var(--primary, #0f172a);
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
  border-bottom: 1px solid var(--border, #e2e8f0);
  background-color: #f8fafc;
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
  color: var(--primary, #0f172a);
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
  background-color: #e0e7ff;
  color: #3730a3;
  font-family: monospace;
}

.persona-badge {
  background-color: #f1f5f9;
  color: #475569;
  border: 1px solid #cbd5e1;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-icon-toggle {
  background: transparent;
  border: 1px solid transparent;
  border-radius: 4px;
  padding: 4px 6px;
  cursor: pointer;
  font-size: 1rem;
  transition: all 0.15s;
}

.btn-icon-toggle:hover,
.btn-icon-toggle.active {
  background-color: #e2e8f0;
  border-color: #cbd5e1;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.25rem;
  cursor: pointer;
  color: #64748b;
  padding: 4px 6px;
  line-height: 1;
}

.btn-close:hover {
  color: #0f172a;
}

.search-bar-row {
  position: relative;
  display: flex;
  align-items: center;
}

.search-input {
  width: 100%;
  padding: 8px 32px 8px 10px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 6px;
  font-size: 0.85rem;
  background-color: white;
  color: var(--primary, #0f172a);
  outline: none;
}

.search-input:focus {
  border-color: var(--accent, #4f46e5);
  box-shadow: 0 0 0 2px rgba(79, 70, 229, 0.15);
}

.btn-clear-search {
  position: absolute;
  right: 8px;
  background: transparent;
  border: none;
  color: #94a3b8;
  cursor: pointer;
  font-size: 0.85rem;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  background-color: #ffffff;
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 36px 16px;
  text-align: center;
  color: #64748b;
  gap: 10px;
}

.empty-icon {
  font-size: 2.5rem;
}

.empty-state h4 {
  margin: 0;
  color: var(--primary, #0f172a);
  font-size: 0.98rem;
}

.empty-state p {
  margin: 0;
  font-size: 0.82rem;
  line-height: 1.4;
}

.empty-actions {
  margin-top: 8px;
}

.spinner {
  width: 24px;
  height: 24px;
  border: 3px solid #e2e8f0;
  border-top-color: var(--accent, #4f46e5);
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
  color: var(--accent, #4f46e5);
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
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  background-color: #ffffff;
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
  background-color: #f1f5f9;
  color: #334155;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid #e2e8f0;
}

.badge-version {
  background-color: #dbeafe;
  color: #1e40af;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-status {
  background-color: #dcfce7;
  color: #15803d;
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.sop-title {
  margin: 0;
  font-size: 1.08rem;
  font-weight: 700;
  color: var(--primary, #0f172a);
  line-height: 1.35;
}

.tags-row {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.tag-pill {
  font-size: 0.7rem;
  background-color: #f8fafc;
  border: 1px solid #e2e8f0;
  color: #64748b;
  padding: 1px 5px;
  border-radius: 3px;
}

.tag-pill-sm {
  font-size: 0.65rem;
  padding: 1px 4px;
}

.anchor-callout {
  background-color: #fef3c7;
  border: 1px solid #fde68a;
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.78rem;
  color: #92400e;
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
  border-top: 1px solid #f1f5f9;
}

.body-toggle-label {
  font-size: 0.76rem;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.btn-toggle-body {
  background: transparent;
  border: none;
  color: var(--accent, #4f46e5);
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
  color: #334155;
  border-top: 1px dashed #e2e8f0;
  padding-top: 10px;
}

.sop-body :deep(h2),
.sop-body :deep(h3),
.sop-body :deep(h4) {
  color: var(--primary, #0f172a);
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
  background-color: #f1f5f9;
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
  color: #475569;
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.related-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.related-card {
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 10px 12px;
  background-color: #f8fafc;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.related-card:hover {
  background-color: #ffffff;
  border-color: var(--accent, #4f46e5);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.05);
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
  color: var(--primary, #0f172a);
}

.badge-version-sm {
  background-color: #e0e7ff;
  color: #3730a3;
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
  color: #475569;
}

.btn-link {
  background: none;
  border: none;
  color: var(--accent, #4f46e5);
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
  border: 1px solid var(--border, #e2e8f0);
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
  border-color: var(--accent, #4f46e5);
  background-color: #f8fafc;
}

.search-result-card.active {
  border-color: var(--accent, #4f46e5);
  background-color: #eef2ff;
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
  color: var(--primary, #0f172a);
}

.empty-search {
  padding: 20px;
  text-align: center;
  color: #64748b;
  font-size: 0.84rem;
}

.drawer-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--border, #e2e8f0);
  background-color: #f8fafc;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
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
  background-color: #ffffff;
  color: #334155;
  border: 1px solid #cbd5e1;
}

.btn-secondary:hover {
  background-color: #f1f5f9;
}

.btn-escalate {
  background-color: var(--accent, #4f46e5);
  color: #ffffff;
  border: 1px solid transparent;
}

.btn-escalate:hover {
  background-color: #4338ca;
}

.btn-icon {
  font-size: 0.95rem;
}
</style>
