<template>
  <!-- eslint-disable vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" class="drawer-backdrop" @click.self="$emit('close')">
    <aside
      class="quiz-template-drawer"
      :class="{ open: isOpen }"
      role="dialog"
      aria-modal="true"
      aria-label="Regulatory Quiz Template Library"
    >
      <!-- Header -->
      <div class="drawer-header">
        <div class="drawer-title-group">
          <span class="drawer-icon" aria-hidden="true">📚</span>
          <div>
            <h3 class="drawer-title">Regulatory Quiz Template Library</h3>
            <p class="drawer-subtitle">
              Browse, filter, and batch-import pre-approved comprehension check
              questions
            </p>
          </div>
        </div>
        <button
          type="button"
          class="btn-close-drawer"
          title="Close Template Library Drawer"
          aria-label="Close Template Library Drawer"
          @click="$emit('close')"
        >
          ✕
        </button>
      </div>

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
            <span class="search-icon" aria-hidden="true">🔍</span>
            <input
              id="template-search-input"
              v-model="searchQuery"
              type="text"
              placeholder="Search by keyword (e.g. HIPAA, voluntary, risks)..."
              class="form-control input-template-search"
            />
            <button
              v-if="searchQuery"
              type="button"
              class="btn-clear-search"
              title="Clear search query"
              @click="searchQuery = ''"
            >
              ✕
            </button>
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

      <!-- Drawer Body: Questions List & Selection -->
      <div class="drawer-body">
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
          <span class="selected-count-badge">
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
            Clear Filters & Search
          </button>
        </div>

        <div v-else class="templates-list">
          <div
            v-for="tmpl in filteredTemplates"
            :key="tmpl.id"
            :class="['template-card card', { selected: isSelected(tmpl.id) }]"
            role="button"
            tabindex="0"
            @click="toggleSelect(tmpl.id)"
            @keydown.enter="toggleSelect(tmpl.id)"
            @keydown.space.prevent="toggleSelect(tmpl.id)"
          >
            <div class="template-card-header">
              <label class="checkbox-wrapper" @click.stop>
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
              <h5 class="template-question-text">
                {{ tmpl.text }}
              </h5>

              <div class="template-options-preview">
                <div
                  v-for="(opt, optIdx) in tmpl.options"
                  :key="optIdx"
                  :class="[
                    'preview-option-item',
                    { 'correct-option': optIdx === tmpl.correctAnswerIndex },
                  ]"
                >
                  <span class="opt-bullet">
                    {{ optIdx === tmpl.correctAnswerIndex ? "✓" : "•" }}
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
                <span class="hint-icon" aria-hidden="true">💡</span>
                <span class="hint-text"
                  ><strong>Feedback Hint:</strong> {{ tmpl.hint }}</span
                >
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Drawer Footer -->
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
          📥 Import Selected Questions ({{ selectedTemplateIds.length }})
        </button>
      </div>
    </aside>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
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

<style scoped>
.drawer-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background-color: rgba(15, 23, 42, 0.4);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
}

.quiz-template-drawer {
  width: 580px;
  max-width: 90vw;
  height: 100%;
  background-color: white;
  display: flex;
  flex-direction: column;
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
  animation: slideIn 0.25s ease-out;
}

@keyframes slideIn {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  background-color: var(--neutral-light);
}

.drawer-title-group {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}

.drawer-icon {
  font-size: 1.5rem;
}

.drawer-title {
  margin: 0 0 4px 0;
  font-size: 1.15rem;
  color: var(--neutral-dark);
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.btn-close-drawer {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-close-drawer:hover {
  background-color: var(--border);
  color: var(--neutral-dark);
}

.drawer-controls {
  padding: 16px 20px 10px 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  background-color: white;
}

.filter-group,
.search-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.control-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--neutral-dark);
}

.domain-select {
  width: 100%;
}

.search-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.search-icon {
  position: absolute;
  left: 10px;
  font-size: 0.85rem;
  pointer-events: none;
  color: var(--semantic-color-text-secondary);
}

.input-template-search {
  padding-left: 32px;
  padding-right: 28px;
  width: 100%;
}

.btn-clear-search {
  position: absolute;
  right: 8px;
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  font-size: 0.85rem;
}

.domain-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 0 20px 12px 20px;
  border-bottom: 1px solid var(--border);
}

.domain-pill {
  padding: 4px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  border-radius: 12px;
  border: 1px solid var(--border);
  background-color: var(--neutral-light);
  color: var(--neutral-dark);
  cursor: pointer;
  transition: all 0.15s;
}

.domain-pill.active {
  background-color: var(--primary);
  color: white;
  border-color: var(--primary);
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.selection-summary-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background-color: var(--neutral-light);
  border-radius: 6px;
  border: 1px solid var(--border);
  font-size: 0.85rem;
}

.select-all-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  cursor: pointer;
}

.selected-count-badge {
  font-weight: bold;
  color: var(--primary);
}

.no-templates-state {
  text-align: center;
  padding: 30px 10px;
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
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 14px;
  background-color: white;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.template-card:hover {
  border-color: var(--primary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
}

.template-card.selected {
  border-color: var(--primary);
  background-color: var(--semantic-color-info-bg-soft);
}

.template-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.checkbox-wrapper {
  display: flex;
  align-items: center;
  cursor: pointer;
}

.checkbox-template-select {
  width: 18px;
  height: 18px;
  cursor: pointer;
}

.domain-badge {
  font-size: 0.72rem;
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
  font-size: 0.95rem;
  color: var(--neutral-dark);
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
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.preview-option-item.correct-option {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}

.opt-bullet {
  font-weight: bold;
}

.correct-tag {
  font-size: 0.75rem;
  color: var(--semantic-color-success-dark);
  font-style: italic;
  margin-left: 4px;
}

.template-hint-preview {
  margin-top: 4px;
  padding: 6px 10px;
  background-color: var(--semantic-color-warning-bg-soft);
  border-left: 3px solid var(--semantic-color-warning-border-strong);
  border-radius: 4px;
  font-size: 0.8rem;
  color: var(--semantic-color-warning-dark);
  display: flex;
  align-items: center;
  gap: 6px;
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
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--neutral-light);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
