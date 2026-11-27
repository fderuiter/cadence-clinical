<template>
  <div
    id="panel-contextual-guidance"
    role="tabpanel"
    aria-labelledby="tab-contextual-guidance"
    class="drawer-tabpanel"
  >
    <div v-if="knowledgeStore.loading" class="loading-state">
      <div class="spinner" />
      <p>Resolving contextual SOP guidance...</p>
    </div>

    <div
      v-else-if="knowledgeStore.error"
      id="contextual-help-error-alert"
      class="help-error-alert"
      role="alert"
      aria-live="assertive"
    >
      <div class="error-alert-header">
        <AlertTriangle
          class="error-alert-icon"
          :size="20"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
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
          <RefreshCw
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Retry</span>
        </button>
      </div>
    </div>

    <div
      v-else-if="isSearchOpen && searchQuery.trim()"
      class="search-results-section"
    >
      <div class="section-subhead">
        <h4>Search Results ({{ knowledgeStore.searchResults.length }})</h4>
        <button
          v-if="knowledgeStore.selectedArticle"
          type="button"
          class="btn-link"
          @click="emit('back-from-search')"
        >
          Back to Matched SOP
        </button>
      </div>

      <div
        v-if="knowledgeStore.searchResults.length === 0"
        class="empty-search"
      >
        <p>
          No SOPs matched your query "<em>{{ searchQuery }}</em
          >".
        </p>
      </div>

      <div v-else class="search-items-list">
        <button
          v-for="item in knowledgeStore.searchResults"
          :key="item.id"
          type="button"
          class="search-result-card"
          :class="{ active: knowledgeStore.displayedArticle?.id === item.id }"
          :aria-pressed="
            knowledgeStore.displayedArticle?.id === item.id ? 'true' : 'false'
          "
          @click="knowledgeStore.selectArticle(item)"
          @keydown.enter.prevent="knowledgeStore.selectArticle(item)"
          @keydown.space.prevent="knowledgeStore.selectArticle(item)"
        >
          <span class="result-card-header">
            <span class="result-title">{{ item.title }}</span>
            <span
              v-if="item.version_label"
              class="badge badge-version tabular-nums"
            >
              v{{ item.version_label }}
            </span>
          </span>
          <span v-if="item.tags" class="tags-row">
            <span v-for="t in formatTags(item.tags)" :key="t" class="tag-pill">
              #{{ t }}
            </span>
          </span>
        </button>
      </div>
    </div>

    <div v-else-if="knowledgeStore.displayedArticle" class="guidance-content">
      <div v-if="knowledgeStore.selectedArticle" class="back-bar">
        <button
          ref="backToPrimaryBtnRef"
          type="button"
          class="btn-back"
          @click="returnToPrimarySop"
        >
          <ArrowLeft
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Return to Route Spotlight SOP</span>
        </button>
      </div>

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
            class="badge badge-version tabular-nums"
          >
            v{{ knowledgeStore.displayedArticle.version_label }}
          </span>
          <span class="badge badge-status">PUBLISHED</span>
        </div>

        <h4 class="sop-title">{{ knowledgeStore.displayedArticle.title }}</h4>

        <div v-if="knowledgeStore.displayedArticle.tags" class="tags-row">
          <span
            v-for="tag in formatTags(knowledgeStore.displayedArticle.tags)"
            :key="tag"
            class="tag-pill"
          >
            #{{ tag }}
          </span>
        </div>

        <div
          v-if="knowledgeStore.activeSectionAnchor"
          class="anchor-callout"
          :data-anchor-target="normalizedSectionAnchorId"
        >
          <Target
            class="anchor-icon"
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <div class="anchor-text">
            <strong>Spotlight Section:</strong>
            <button
              id="btn-jump-spotlight-section"
              type="button"
              class="anchor-jump-btn"
              :title="`Scroll to ${formattedSectionAnchor}`"
              @click="handleJumpToSectionAnchor"
            >
              {{ formattedSectionAnchor }}
            </button>
          </div>
        </div>

        <div class="body-toggle-bar">
          <span class="body-toggle-label">
            Standard Operating Procedure Content
          </span>
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
                ? "Collapse Body"
                : "Expand Body"
            }}
          </button>
        </div>

        <div
          v-if="knowledgeStore.isArticleBodyExpanded"
          id="contextual-help-sop-body"
          ref="sopBodyRef"
          class="sop-body prose-content"
        >
          <div v-if="displayedArticleHtml" v-html="displayedArticleHtml" />
          <div v-else-if="displayedArticleMarkdown" class="markdown-pre">
            {{ displayedArticleMarkdown }}
          </div>
        </div>
      </div>

      <div
        v-if="
          !knowledgeStore.selectedArticle &&
          knowledgeStore.relatedArticles.length > 0
        "
        class="related-section"
      >
        <h5 class="related-title">
          <Library
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>
            Related Clinical SOPs ({{ knowledgeStore.relatedArticles.length }})
          </span>
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
              <span
                v-if="rel.version_label"
                class="badge badge-version-sm tabular-nums"
              >
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

    <div v-else class="empty-state">
      <Search
        class="empty-icon"
        :size="20"
        aria-hidden="true"
        :stroke-width="1.75"
        focusable="false"
      />
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
          @click="emit('open-search-fallback')"
        >
          <Search
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Search Knowledge Hub</span>
        </button>
        <button
          type="button"
          class="btn btn-secondary"
          @click="emit('open-hub', '/knowledge')"
        >
          <Library
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Open Full SOP Hub</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, ref, watch } from "vue";
import {
  AlertTriangle,
  ArrowLeft,
  Library,
  RefreshCw,
  Search,
  Target,
} from "lucide-vue-next";
import { handoffRetryFocus } from "ui";
import { compileSopMarkdownToHtml } from "../../api/knowledge.js";
import { useKnowledgeStore } from "../../stores/knowledge.js";
import { formatAnchorSlug, formatTags } from "./contextualHelpUtils.js";

const props = defineProps({
  isSearchOpen: { type: Boolean, default: false },
  searchQuery: { type: String, default: "" },
  activePersonaLabel: { type: String, default: "Super Admin" },
  getDrawerContainer: { type: Function, required: true },
  getSearchInputEl: { type: Function, default: () => null },
});

const emit = defineEmits([
  "back-from-search",
  "open-search-fallback",
  "open-hub",
]);

const knowledgeStore = useKnowledgeStore();
const backToPrimaryBtnRef = ref(null);
const toggleBodyBtnRef = ref(null);
const sopBodyRef = ref(null);

const displayedArticleHtml = computed(() => {
  const article = knowledgeStore.displayedArticle;
  if (!article) return "";
  if (article.body_html) return article.body_html;
  if (
    !knowledgeStore.selectedArticle &&
    knowledgeStore.primaryVersion?.body_html
  ) {
    return knowledgeStore.primaryVersion.body_html;
  }
  const fallbackMarkdown =
    article.body_markdown ||
    (!knowledgeStore.selectedArticle
      ? knowledgeStore.primaryVersion?.body_markdown
      : "") ||
    "";
  if (fallbackMarkdown) {
    return compileSopMarkdownToHtml(
      fallbackMarkdown,
      knowledgeStore.activeSectionAnchor
    );
  }
  return "";
});

const displayedArticleMarkdown = computed(() => {
  const article = knowledgeStore.displayedArticle;
  if (!article) return "";
  if (article.body_markdown) return article.body_markdown;
  if (
    !knowledgeStore.selectedArticle &&
    knowledgeStore.primaryVersion?.body_markdown
  ) {
    return knowledgeStore.primaryVersion.body_markdown;
  }
  return "";
});

const normalizedSectionAnchorId = computed(() =>
  String(knowledgeStore.activeSectionAnchor || "")
    .trim()
    .replace(/^#+/, "")
);

const formattedSectionAnchor = computed(() =>
  formatAnchorSlug(knowledgeStore.activeSectionAnchor)
);

function scrollToSectionAnchorTarget() {
  const anchorId = normalizedSectionAnchorId.value;
  const container = sopBodyRef.value;
  if (!anchorId || !container) return false;
  const elementsWithId = container.querySelectorAll("[id]");
  let targetEl = null;
  for (const el of elementsWithId) {
    el.classList.remove("spotlight-target-heading");
    if (el.id === anchorId) targetEl = el;
  }
  if (!targetEl) return false;
  targetEl.classList.add("spotlight-target-heading");
  if (typeof targetEl.scrollIntoView === "function") {
    targetEl.scrollIntoView({ behavior: "smooth", block: "start" });
  }
  return true;
}

async function handleJumpToSectionAnchor() {
  if (!knowledgeStore.isArticleBodyExpanded) {
    knowledgeStore.isArticleBodyExpanded = true;
    await nextTick();
    return;
  }
  scrollToSectionAnchorTarget();
}

async function selectRelatedArticle(item) {
  knowledgeStore.selectArticle(item);
  await nextTick();
  backToPrimaryBtnRef.value?.focus?.();
}

async function returnToPrimarySop() {
  knowledgeStore.backToPrimary();
  await nextTick();
  const root = props.getDrawerContainer();
  const firstRelated = root?.querySelector?.(".related-card");
  if (firstRelated && typeof firstRelated.focus === "function") {
    firstRelated.focus();
  } else {
    toggleBodyBtnRef.value?.focus?.();
  }
}

async function handleRetry() {
  await handoffRetryFocus(() => knowledgeStore.retryLastAction(), {
    container: props.getDrawerContainer,
    includeDefaultFallbacks: false,
    isError: () => Boolean(knowledgeStore.error),
    errorTargets: ["#btn-contextual-help-retry", "#tab-contextual-guidance"],
    successTargets: [
      () =>
        props.isSearchOpen && props.searchQuery.trim()
          ? [
              ".search-result-card",
              props.getSearchInputEl(),
              "#help-search-input",
            ]
          : null,
      toggleBodyBtnRef,
      "#btn-jump-spotlight-section",
      ".related-card",
      ".empty-actions button:not([disabled])",
      "#tab-contextual-guidance",
    ],
  });
}

watch(
  () => [
    knowledgeStore.isOpen,
    knowledgeStore.activeDrawerTab,
    knowledgeStore.displayedArticle?.id,
    knowledgeStore.activeSectionAnchor,
    knowledgeStore.isArticleBodyExpanded,
  ],
  async ([isOpen, tab, articleId, anchor, expanded]) => {
    if (isOpen && tab === "guidance" && articleId && anchor && expanded) {
      await nextTick();
      scrollToSectionAnchorTarget();
    }
  },
  { immediate: true }
);
</script>

<style scoped src="./contextualHelpPanel.css"></style>
