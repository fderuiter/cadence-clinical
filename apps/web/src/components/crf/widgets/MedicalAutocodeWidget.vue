<template>
  <div class="medical-autocode-container">
    <label class="widget-label" :for="inputId"
      >{{ label }} <span v-if="required" class="required-star">*</span></label
    >
    <div class="input-wrapper">
      <input
        :id="inputId"
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
        role="option"
        :aria-selected="selectedCoding?.code === term.code"
        tabindex="0"
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
import { demoRandomToken } from "../../../utils/demoMode.js";
import { unbackedActionsEnabled } from "../../../utils/unbackedActions.js";

const props = defineProps({
  modelValue: { type: String, default: "" },
  label: { type: String, default: "Medical Term" },
  required: { type: Boolean, default: false },
  placeholder: { type: String, default: "" },
  dictionary: { type: String, default: "MedDRA" },
});

const emit = defineEmits(["update:modelValue", "code-selected"]);

const inputId = `autocode-input-${demoRandomToken(7, 36, "medical-autocode")}`;
const isOpen = ref(false);
const loading = ref(false);
const suggestions = ref([]);
const selectedCoding = ref(null);

let debounceTimer = null;

const onInput = (e) => {
  const query = e.target.value;
  emit("update:modelValue", query);

  clearTimeout(debounceTimer);
  // #4827: no coding service backs these suggestions, and the codes below are made up. Outside demo builds
  // the verbatim term is still captured, but nothing suggests or assigns a code.
  if (!query || query.length < 2 || !unbackedActionsEnabled()) {
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
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--semantic-color-text);
}

.required-star {
  color: var(--error);
}

.input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.autocode-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
  font-size: var(--font-size-md);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.autocode-input:focus-visible {
  border-color: var(--primary);
}

.autocode-spinner {
  position: absolute;
  right: 12px;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.suggestions-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 50;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
  box-shadow: var(--shadow-sm);
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
  background-color: var(--semantic-color-surface-header);
}

.term-name {
  font-weight: 500;
  color: var(--semantic-color-text);
}

.term-badge {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.selected-coding-badge {
  margin-top: 4px;
  padding: 4px 8px;
  background-color: var(--semantic-color-info-bg);
  border-left: 3px solid var(--primary);
  font-size: var(--font-size-xs);
  color: var(--semantic-color-primary-dark);
}
</style>
