<template>
  <ClinicalDrawer
    :model-value="isOpen"
    :teleport="false"
    size="lg"
    overlay-class="drawer-backdrop"
    panel-class="quiz-template-drawer"
    aria-label="Regulatory Quiz Template Library"
    @close="$emit('close')"
  >
    <template #header>
      <div class="drawer-header">
        <div class="drawer-title-group">
          <BookOpen :size="18" class="drawer-icon" aria-hidden="true" />
          <h3 class="drawer-title">Regulatory Quiz Template Library</h3>
        </div>
        <button
          type="button"
          class="btn-close-drawer"
          title="Close Template Library Drawer"
          aria-label="Close Template Library Drawer"
          @click="$emit('close')"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <p class="drawer-subtitle">
        Browse, filter, and batch-import pre-approved comprehension check
        questions
      </p>

      <!-- Controls: Domain Filter & Search Bar -->
      <div class="drawer-controls">
        <div class="filter-group">
          <label for="domain-filter-select" class="control-label"
            >Domain Category:</label
          >
          <select
            id="domain-filter-select"
            v-model="selectedDomain"
            class="form-control domain-select"
          >
            <option value="All">All Domains ({{ catalog.length }})</option>
            <option v-for="domain in domains" :key="domain" :value="domain">
              {{ domain }} ({{ getDomainCount(domain) }})
            </option>
          </select>
        </div>

        <div class="search-group">
          <label for="template-search-input" class="control-label"
            >Search Keywords:</label
          >
          <div class="search-input-wrapper">
            <Search :size="16" class="search-icon" aria-hidden="true" />
            <input
              id="template-search-input"
              v-model="searchQuery"
              type="text"
              placeholder="Search by keyword (e.g. HIPAA, voluntary, risks)..."
              class="form-control input-template-search"
            />
            <ClinicalIconButton
              v-if="searchQuery"
              class="btn-clear-search"
              title="Clear search query"
              label="Clear search query"
              @click="searchQuery = ''"
            >
              <X :size="16" aria-hidden="true" />
            </ClinicalIconButton>
          </div>
        </div>
      </div>

      <!-- Domain Tab Pills -->
      <div class="domain-pills" role="tablist" aria-label="Regulatory Domains">
        <button
          type="button"
          role="tab"
          :aria-selected="selectedDomain === 'All'"
          :class="['domain-pill', { active: selectedDomain === 'All' }]"
          @click="selectedDomain = 'All'"
        >
          All Domains
        </button>
        <button
          v-for="domain in domains"
          :key="domain"
          type="button"
          role="tab"
          :aria-selected="selectedDomain === domain"
          :class="['domain-pill', { active: selectedDomain === domain }]"
          @click="selectedDomain = domain"
        >
          {{ domain }}
        </button>
      </div>

      <div class="selection-summary-bar">
        <label class="select-all-label">
          <input
            type="checkbox"
            class="checkbox-select-all"
            :checked="isAllFilteredSelected && filteredTemplates.length > 0"
            :disabled="filteredTemplates.length === 0"
            @change="toggleSelectAll"
          />
          <span> Select All Filtered ({{ filteredTemplates.length }}) </span>
        </label>
        <span class="selected-count-badge tabular-nums">
          {{ selectedTemplateIds.length }} Question{{
            selectedTemplateIds.length === 1 ? "" : "s"
          }}
          Selected
        </span>
      </div>

      <div v-if="filteredTemplates.length === 0" class="no-templates-state">
        <p class="no-templates-text">
          No matching quiz templates found for "{{ searchQuery }}"
          <span v-if="selectedDomain !== 'All'"> in {{ selectedDomain }}</span
          >.
        </p>
        <button
          type="button"
          class="btn btn-secondary btn-small"
          @click="resetFilters"
        >
          Clear Filters &amp; Search
        </button>
      </div>

      <div v-else class="templates-list">
        <div
          v-for="tmpl in filteredTemplates"
          :key="tmpl.id"
          :class="['template-card card', { selected: isSelected(tmpl.id) }]"
        >
          <div class="template-card-header">
            <label class="checkbox-wrapper">
              <input
                type="checkbox"
                class="checkbox-template-select"
                :checked="isSelected(tmpl.id)"
                @change="toggleSelect(tmpl.id)"
              />
              <span class="sr-only">Select question: {{ tmpl.text }}</span>
            </label>

            <span class="domain-badge">{{ tmpl.domain }}</span>
          </div>

          <div class="template-card-body">
            <h4 class="template-question-text">
              {{ tmpl.text }}
            </h4>

            <div class="template-options-preview">
              <div
                v-for="(opt, optIdx) in tmpl.options"
                :key="optIdx"
                :class="[
                  'preview-option-item',
                  { 'correct-option': optIdx === tmpl.correctAnswerIndex },
                ]"
              >
                <span class="opt-bullet" aria-hidden="true">
                  <Check v-if="optIdx === tmpl.correctAnswerIndex" :size="14" />
                  <template v-else>&bull;</template>
                </span>
                <span class="opt-text">{{ opt }}</span>
                <span
                  v-if="optIdx === tmpl.correctAnswerIndex"
                  class="correct-tag"
                >
                  (Correct Answer)
                </span>
              </div>
            </div>

            <div v-if="tmpl.hint" class="template-hint-preview">
              <Lightbulb :size="15" class="hint-icon" aria-hidden="true" />
              <span class="hint-text"
                ><strong>Feedback Hint:</strong> {{ tmpl.hint }}</span
              >
            </div>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <button type="button" class="btn btn-secondary" @click="$emit('close')">
          Cancel
        </button>

        <button
          type="button"
          class="btn btn-primary btn-batch-import"
          :disabled="selectedTemplateIds.length === 0"
          @click="confirmBatchImport"
        >
          <Download :size="16" aria-hidden="true" />
          <span
            >Import Selected Questions ({{ selectedTemplateIds.length }})</span
          >
        </button>
      </div>
    </template>
  </ClinicalDrawer>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { ClinicalDrawer, ClinicalIconButton } from "ui";
import {
  BookOpen,
  Check,
  Download,
  Lightbulb,
  Search,
  X,
} from "lucide-vue-next";
import {
  REGULATORY_DOMAINS,
  QUIZ_TEMPLATE_CATALOG,
} from "../../data/quizTemplates.js";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["close", "import"]);

const domains = REGULATORY_DOMAINS;
const catalog = QUIZ_TEMPLATE_CATALOG;

const selectedDomain = ref("All");
const searchQuery = ref("");
const selectedTemplateIds = ref([]);

const getDomainCount = (domain) => {
  return catalog.filter((q) => q.domain === domain).length;
};

const filteredTemplates = computed(() => {
  let list = catalog;

  if (selectedDomain.value && selectedDomain.value !== "All") {
    list = list.filter((q) => q.domain === selectedDomain.value);
  }

  const query = searchQuery.value.trim().toLowerCase();
  if (query) {
    list = list.filter((q) => {
      const inText = q.text.toLowerCase().includes(query);
      const inDomain = q.domain.toLowerCase().includes(query);
      const inHint = q.hint ? q.hint.toLowerCase().includes(query) : false;
      const inOptions = q.options.some((opt) =>
        opt.toLowerCase().includes(query)
      );
      return inText || inDomain || inHint || inOptions;
    });
  }

  return list;
});

const isAllFilteredSelected = computed(() => {
  if (filteredTemplates.value.length === 0) return false;
  return filteredTemplates.value.every((tmpl) =>
    selectedTemplateIds.value.includes(tmpl.id)
  );
});

const isSelected = (id) => {
  return selectedTemplateIds.value.includes(id);
};

const toggleSelect = (id) => {
  const index = selectedTemplateIds.value.indexOf(id);
  if (index === -1) {
    selectedTemplateIds.value = [...selectedTemplateIds.value, id];
  } else {
    selectedTemplateIds.value = selectedTemplateIds.value.filter(
      (item) => item !== id
    );
  }
};

const toggleSelectAll = () => {
  if (isAllFilteredSelected.value) {
    const visibleIds = filteredTemplates.value.map((t) => t.id);
    selectedTemplateIds.value = selectedTemplateIds.value.filter(
      (id) => !visibleIds.includes(id)
    );
  } else {
    filteredTemplates.value.forEach((t) => {
      if (!selectedTemplateIds.value.includes(t.id)) {
        selectedTemplateIds.value.push(t.id);
      }
    });
  }
};

const resetFilters = () => {
  selectedDomain.value = "All";
  searchQuery.value = "";
};

const confirmBatchImport = () => {
  const selectedObjects = catalog.filter((tmpl) =>
    selectedTemplateIds.value.includes(tmpl.id)
  );

  emit("import", selectedObjects);
  selectedTemplateIds.value = [];
  emit("close");
};

watch(
  () => props.isOpen,
  (val) => {
    if (!val) {
      selectedTemplateIds.value = [];
      searchQuery.value = "";
      selectedDomain.value = "All";
    }
  }
);

defineExpose({
  selectedDomain,
  searchQuery,
  selectedTemplateIds,
  filteredTemplates,
  confirmBatchImport,
  toggleSelect,
  toggleSelectAll,
  resetFilters,
});
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
.drawer-icon {
  color: var(--semantic-color-primary);
}

.drawer-subtitle {
  margin: 0;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

.drawer-controls {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.filter-group,
.search-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.control-label {
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--semantic-color-text);
}

.domain-select,
.input-template-search {
  width: 100%;
  min-height: 44px;
  font-size: var(--font-size-md);
  box-sizing: border-box;
}

.search-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.search-icon {
  position: absolute;
  left: 10px;
  pointer-events: none;
  color: var(--semantic-color-text-secondary);
}

.input-template-search {
  padding-left: 34px;
  padding-right: 44px;
}

.btn-clear-search {
  position: absolute;
  right: 2px;
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
}

.domain-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border);
}

.domain-pill {
  min-height: 44px;
  padding: 6px 12px;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  border-radius: 999px;
  border: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  cursor: pointer;
  transition: all 0.15s;
}

.domain-pill.active {
  background-color: var(--primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--primary);
}

.selection-summary-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 8px 12px;
  background-color: var(--semantic-color-surface-muted);
  border-radius: 6px;
  border: 1px solid var(--border);
  font-size: var(--font-size-sm);
}

.select-all-label {
  display: flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  font-weight: 600;
  cursor: pointer;
}

.checkbox-select-all {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
}

.selected-count-badge {
  font-weight: 700;
  color: var(--text-main);
}

.no-templates-state {
  text-align: center;
  padding: 24px 10px;
  color: var(--semantic-color-text-secondary);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.no-templates-text {
  margin: 0;
  font-style: italic;
}

.templates-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.template-card {
  position: relative;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 14px;
  background-color: var(--semantic-color-surface);
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
}

.template-card:hover {
  border-color: var(--primary);
  box-shadow: var(--shadow-sm);
}

.template-card.selected {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-info-bg-soft);
}

.template-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.checkbox-wrapper {
  display: flex;
  align-items: center;
  min-width: 44px;
  min-height: 44px;
  cursor: pointer;
}

.checkbox-wrapper::after {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: 8px;
  cursor: pointer;
}

.checkbox-template-select {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  cursor: pointer;
}

.domain-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  padding: 3px 8px;
  border-radius: 4px;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.template-question-text {
  margin: 0;
  font-size: var(--font-size-base);
  color: var(--semantic-color-text);
  line-height: 1.4;
}

.template-options-preview {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding-left: 4px;
}

.preview-option-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  flex-wrap: wrap;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
}

.preview-option-item.correct-option {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}

.opt-bullet {
  font-weight: 700;
  display: inline-flex;
  align-items: center;
}

.correct-tag {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-success-dark);
  font-style: italic;
}

.template-hint-preview {
  margin-top: 4px;
  padding: 8px 10px;
  background-color: var(--semantic-color-warning-bg-soft);
  border-left: 3px solid var(--semantic-color-warning-border-strong);
  border-radius: 4px;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-warning-text);
  display: flex;
  align-items: flex-start;
  gap: 6px;
}

.hint-icon {
  flex-shrink: 0;
  margin-top: 2px;
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

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
}

.drawer-footer .btn,
.no-templates-state .btn {
  min-height: 44px;
  padding: 8px 14px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

@media (max-width: 640px) {
  .drawer-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .drawer-footer .btn {
    width: 100%;
  }
}
</style>
