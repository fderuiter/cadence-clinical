<template>
  <div class="econsent-editor-container card">
    <div class="editor-header">
      <div class="header-titles">
        <h3>{{ title || 'Informed Consent Clause Editor' }}</h3>
        <span
          v-if="clauseId"
          class="clause-badge"
        >Clause ID: {{ clauseId }}</span>
      </div>
      <div class="header-badges">
        <span
          v-if="currentMetrics"
          class="grade-badge"
          :class="currentMetrics.is_target_grade_level ? 'target' : 'warning'"
        >
          FKGL: {{ currentMetrics.flesch_kincaid_grade_level }} ({{ currentMetrics.is_target_grade_level ? '6th-8th Grade' : 'Complex' }})
        </span>
        <button
          type="button"
          class="btn-toggle-drawer"
          :class="{ active: isDrawerOpen }"
          title="Toggle Readability Harmonizer"
          @click="isDrawerOpen = !isDrawerOpen"
        >
          📖 Readability Assistant
        </button>
      </div>
    </div>

    <!-- Formatting Toolbar -->
    <div class="editor-toolbar">
      <button
        type="button"
        class="toolbar-btn"
        title="Bold"
        @click="formatDoc('bold')"
      >
        <strong>B</strong>
      </button>
      <button
        type="button"
        class="toolbar-btn"
        title="Italic"
        @click="formatDoc('italic')"
      >
        <em>I</em>
      </button>
      <button
        type="button"
        class="toolbar-btn"
        title="Bullet List"
        @click="formatDoc('insertUnorderedList')"
      >
        • List
      </button>
      <button
        type="button"
        class="toolbar-btn"
        title="Numbered List"
        @click="formatDoc('insertOrderedList')"
      >
        1. List
      </button>
      <span class="toolbar-separator" />
      <button
        type="button"
        class="toolbar-btn btn-harmonize-quick"
        title="Scan for Jargon"
        @click="isDrawerOpen = true"
      >
        ✨ Harmonize Jargon
      </button>
    </div>

    <!-- Main Content Editable Canvas -->
    <div
      ref="editorRef"
      class="editor-canvas"
      contenteditable="true"
      role="textbox"
      tabindex="0"
      @input="handleInput"
      @blur="handleInput"
      v-html="localHtml"
    />

    <!-- Readability Assistant Split-Screen Drawer -->
    <ReadabilityDrawer
      :is-open="isDrawerOpen"
      :text="rawText"
      :clause-id="clauseId"
      :study-id="studyId"
      @close="isDrawerOpen = false"
      @apply-substitution="onApplySubstitution"
      @apply-all="onApplyAll"
      @update-metrics="onUpdateMetrics"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted } from "vue";
import ReadabilityDrawer from "./ReadabilityDrawer.vue";

const props = defineProps({
  modelValue: {
    type: String,
    default: "",
  },
  title: {
    type: String,
    default: "",
  },
  clauseId: {
    type: String,
    default: "",
  },
  studyId: {
    type: String,
    default: "CADENCE-101",
  },
});

const emit = defineEmits(["update:modelValue", "save-draft", "harmonize"]);

const editorRef = ref(null);
const localHtml = ref(props.modelValue || "<p></p>");
const isDrawerOpen = ref(false);
const currentMetrics = ref(null);

const rawText = computed(() => {
  const div = document.createElement("div");
  div.innerHTML = localHtml.value;
  return div.textContent || div.innerText || "";
});

function handleInput() {
  if (editorRef.value) {
    localHtml.value = editorRef.value.innerHTML;
    emit("update:modelValue", localHtml.value);
  }
}

function formatDoc(cmd, value = null) {
  document.execCommand(cmd, false, value);
  handleInput();
}

function replaceTermInHtml(html, originalTerm, suggestedTerm) {
  const container = document.createElement("div");
  container.innerHTML = html;
  const regex = new RegExp(`\\b${escapeRegExp(originalTerm)}\\b`, "gi");

  const walk = (node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      if (regex.test(node.nodeValue)) {
        node.nodeValue = node.nodeValue.replace(regex, suggestedTerm);
      }
    } else if (node.nodeType === Node.ELEMENT_NODE && node.nodeName !== "SCRIPT" && node.nodeName !== "STYLE") {
      for (let child = node.firstChild; child; child = child.nextSibling) {
        walk(child);
      }
    }
  };
  walk(container);
  return container.innerHTML;
}

function onApplySubstitution({ substitution }) {
  if (!substitution) return;
  localHtml.value = replaceTermInHtml(localHtml.value, substitution.original_term, substitution.suggested_term);
  if (editorRef.value) {
    editorRef.value.innerHTML = localHtml.value;
  }
  emit("update:modelValue", localHtml.value);
}

function onApplyAll({ substitutions }) {
  if (!substitutions || !substitutions.length) return;
  let updated = localHtml.value;
  substitutions.forEach((s) => {
    updated = replaceTermInHtml(updated, s.original_term, s.suggested_term);
  });
  localHtml.value = updated;
  if (editorRef.value) {
    editorRef.value.innerHTML = localHtml.value;
  }
  emit("update:modelValue", localHtml.value);
}

function onUpdateMetrics(metrics) {

  currentMetrics.value = metrics;
}

function escapeRegExp(string) {
  return string.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

watch(
  () => props.modelValue,
  (newVal) => {
    if (newVal !== localHtml.value) {
      localHtml.value = newVal || "<p></p>";
    }
  }
);

onMounted(() => {
  if (editorRef.value && !editorRef.value.innerHTML) {
    editorRef.value.innerHTML = localHtml.value;
  }
});
</script>

<style scoped>
.econsent-editor-container {
  display: flex;
  flex-direction: column;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  position: relative;
  overflow: hidden;
}

.editor-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background-color: var(--surface-subtle, #f8fafc);
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.header-titles {
  display: flex;
  align-items: center;
  gap: 10px;
}

.header-titles h3 {
  margin: 0;
  font-size: 1rem;
  color: var(--text-primary, #0f172a);
}

.clause-badge {
  font-size: 0.75rem;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #cbd5e1);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--text-muted, #64748b);
}

.header-badges {
  display: flex;
  align-items: center;
  gap: 10px;
}

.grade-badge {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 12px;
}

.grade-badge.target {
  background-color: #dcfce7;
  color: #166534;
}

.grade-badge.warning {
  background-color: #fef9c3;
  color: #854d0e;
}

.btn-toggle-drawer {
  background-color: var(--primary-subtle, #eff6ff);
  color: var(--primary, #2563eb);
  border: 1px solid var(--primary, #bfdbfe);
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-toggle-drawer:hover,
.btn-toggle-drawer.active {
  background-color: var(--primary, #2563eb);
  color: #ffffff;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  background-color: var(--surface, #ffffff);
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.toolbar-btn {
  background: transparent;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 4px;
  padding: 4px 8px;
  font-size: 0.85rem;
  cursor: pointer;
  color: var(--text-primary, #334155);
}

.toolbar-btn:hover {
  background-color: var(--surface-hover, #f1f5f9);
}

.toolbar-separator {
  width: 1px;
  height: 20px;
  background-color: var(--border, #e2e8f0);
  margin: 0 4px;
}

.btn-harmonize-quick {
  background-color: #f0fdf4;
  border-color: #bbf7d0;
  color: #15803d;
  font-weight: 600;
}

.btn-harmonize-quick:hover {
  background-color: #dcfce7;
}

.editor-canvas {
  min-height: 260px;
  padding: 16px;
  outline: none;
  font-size: 0.95rem;
  line-height: 1.6;
  color: var(--text-primary, #1e293b);
}

.editor-canvas:focus {
  background-color: #fafbfc;
}
</style>
