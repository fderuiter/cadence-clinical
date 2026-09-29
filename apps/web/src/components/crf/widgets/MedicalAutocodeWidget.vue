<template>
  <div class="medical-autocode-container">
    <label for="autocode-input-field" class="widget-label"
      >{{ label }} <span v-if="required" class="required-star">*</span></label
    >
    <div class="input-wrapper">
      <input
        id="autocode-input-field"
        type="text"
        class="autocode-input"
        :value="modelValue"
        :placeholder="
          placeholder || 'Type verbatim medical term (e.g. Headache)...'
        "
        @input="onInput"
        @focus="isOpen = true"
      />
      <div v-if="loading" class="autocode-spinner">Searching...</div>
    </div>

    <!-- Suggested Terms Dropdown -->
    <ul v-if="isOpen && suggestions.length > 0" class="suggestions-dropdown">
      <li
        v-for="term in suggestions"
        :key="term.code"
        class="suggestion-item"
        tabindex="0"
        role="option"
        :aria-selected="selectedCoding?.code === term.code"
        @click="selectTerm(term)"
        @keydown.enter="selectTerm(term)"
      >
        <span class="term-name">{{ term.name }}</span>
        <span class="term-badge"
          >{{ term.dictionary }}: {{ term.code }} (PT: {{ term.pt }})</span
        >
      </li>
    </ul>

    <div v-if="selectedCoding" class="selected-coding-badge">
      <span
        >Auto-Coded: <strong>{{ selectedCoding.name }}</strong> [{{
          selectedCoding.dictionary
        }}
        PT: {{ selectedCoding.pt }}, SOC: {{ selectedCoding.soc }}]</span
      >
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";

const props = defineProps({
  modelValue: { type: String, default: "" },
  label: { type: String, default: "Medical Term" },
  required: { type: Boolean, default: false },
  placeholder: { type: String, default: "" },
  dictionary: { type: String, default: "MedDRA" },
});

const emit = defineEmits(["update:modelValue", "code-selected"]);

const isOpen = ref(false);
const loading = ref(false);
const suggestions = ref([]);
const selectedCoding = ref(null);

let debounceTimer = null;

const onInput = (e) => {
  const query = e.target.value;
  emit("update:modelValue", query);

  clearTimeout(debounceTimer);
  if (!query || query.length < 2) {
    suggestions.value = [];
    return;
  }

  loading.value = true;
  debounceTimer = setTimeout(() => {
    // Simulated MedDRA / WHODrug Auto-Coding suggestion engine
    suggestions.value = [
      {
        name: query,
        code: "10019211",
        pt: query.toUpperCase(),
        soc: "Nervous system disorders",
        dictionary: props.dictionary,
      },
      {
        name: `${query} (acute)`,
        code: "10019212",
        pt: `${query.toUpperCase()} ACUTE`,
        soc: "General disorders",
        dictionary: props.dictionary,
      },
    ];
    loading.value = false;
    isOpen.value = true;
  }, 250);
};

const selectTerm = (term) => {
  selectedCoding.value = term;
  emit("update:modelValue", term.name);
  emit("code-selected", term);
  isOpen.value = false;
};
</script>

<style scoped>
.medical-autocode-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs, 4px);
  position: relative;
  width: 100%;
}

.widget-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-primary, #1e293b);
}

.required-star {
  color: var(--error, #ef4444);
}

.input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.autocode-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-md, 6px);
  font-size: 0.9375rem;
  background-color: var(--surface, #ffffff);
  color: var(--text-primary, #1e293b);
}

.autocode-input:focus {
  outline: none;
  border-color: var(--primary, #2563eb);
  box-shadow: 0 0 0 2px rgba(37, 99, 235, 0.2);
}

.autocode-spinner {
  position: absolute;
  right: 12px;
  font-size: 0.75rem;
  color: var(--text-muted, #64748b);
}

.suggestions-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 50;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-md, 6px);
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
  max-height: 200px;
  overflow-y: auto;
  list-style: none;
  padding: 4px 0;
  margin: 4px 0 0 0;
}

.suggestion-item {
  display: flex;
  flex-direction: column;
  padding: 8px 12px;
  cursor: pointer;
}

.suggestion-item:hover {
  background-color: var(--surface-hover, #f1f5f9);
}

.term-name {
  font-weight: 500;
  color: var(--text-primary, #1e293b);
}

.term-badge {
  font-size: 0.75rem;
  color: var(--text-muted, #64748b);
}

.selected-coding-badge {
  margin-top: 4px;
  padding: 4px 8px;
  background-color: var(--primary-subtle, #eff6ff);
  border-left: 3px solid var(--primary, #2563eb);
  font-size: 0.8125rem;
  color: var(--primary-dark, #1d4ed8);
}
</style>
