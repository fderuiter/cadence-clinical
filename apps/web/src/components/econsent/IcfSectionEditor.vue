<template>
  <div class="icf-section-editor-container card">
    <div class="editor-header">
      <h3>Edit Section: {{ section.title }}</h3>
      <span class="section-id-badge">ID: {{ section.id }}</span>
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
        title="Heading 2"
        @click="formatHeading('h2')"
      >
        H2
      </button>
      <button
        type="button"
        class="toolbar-btn"
        title="Heading 3"
        @click="formatHeading('h3')"
      >
        H3
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
      <button
        type="button"
        class="toolbar-btn"
        title="Insert Table"
        @click="insertTable"
      >
        📊 Table
      </button>
      <button
        type="button"
        class="toolbar-btn"
        title="Insert Image Embed"
        @click="insertImage"
      >
        🖼️ Image
      </button>
      <button
        type="button"
        class="toolbar-btn glossary-btn"
        title="Annotate Glossary Term"
        @click="promptGlossary"
      >
        🏷️ Glossary Term
      </button>
    </div>

    <!-- Main Contenteditable Editor -->
    <div
      ref="editorRef"
      class="editor-canvas"
      contenteditable="true"
      role="textbox"
      tabindex="0"
      @input="handleInput"
      @blur="handleInput"
      @click="handleCanvasClick"
      @mouseover="handleCanvasMouseOver"
      @mouseout="hidePopover"
      @focusin="handleCanvasMouseOver"
      @focusout="hidePopover"
      v-html="localHtml"
    />

    <!-- Glossary Term Annotation Dialog/Modal -->
    <div
      v-if="showGlossaryModal"
      class="glossary-modal-overlay"
    >
      <div class="glossary-modal card">
        <h4>Annotate Selected Text as Glossary Term</h4>
        <p class="selected-text-preview">
          Selected text: <strong>"{{ selectedText }}"</strong>
        </p>
        <div class="form-group">
          <label for="glossary-definition">Term Definition/Explanation:</label>
          <textarea
            id="glossary-definition"
            v-model="glossaryDefinition"
            placeholder="e.g. An examination of tissue removed from a living body to discover the presence or cause of a disease."
            rows="3"
            class="form-control"
          />
        </div>
        <div class="modal-actions">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showGlossaryModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="applyGlossaryAnnotation"
          >
            Apply Annotation
          </button>
        </div>
      </div>
    </div>

    <!-- Glossary Popover definition -->
    <div
      v-if="hoveredGlossaryTerm && popoverX !== null && popoverY !== null"
      class="glossary-popover"
      :style="{ top: popoverY + 'px', left: popoverX + 'px' }"
    >
      <div class="popover-title">
        Glossary Definition
      </div>
      <div class="popover-body">
        <strong>{{ hoveredGlossaryTerm }}</strong>: {{ hoveredGlossaryDefinition }}
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted } from 'vue';

const props = defineProps({
  section: {
    type: Object,
    required: true,
  }
});

const emit = defineEmits(['update']);

const editorRef = ref(null);
const localHtml = ref('');
const showGlossaryModal = ref(false);
const selectedText = ref('');
const glossaryDefinition = ref('');
let savedSelection = null;

// Popover State
const hoveredGlossaryTerm = ref(null);
const hoveredGlossaryDefinition = ref(null);
const popoverX = ref(null);
const popoverY = ref(null);

// Synchronize with prop changes
watch(() => props.section.id, () => {
  localHtml.value = props.section.html;
  if (editorRef.value) {
    editorRef.value.innerHTML = props.section.html;
  }
}, { immediate: true });

onMounted(() => {
  localHtml.value = props.section.html;
});

// Format actions via standard execCommand (or manual DOM fallback)
const formatDoc = (command, value = '') => {
  document.execCommand(command, false, value);
  handleInput();
};

const formatHeading = (tag) => {
  formatDoc('formatBlock', `<${tag}>`);
};

const insertTable = () => {
  const tableHtml = `
    <table class="editor-table" style="width: 100%; border-collapse: collapse; margin: 12px 0;">
      <thead>
        <tr style="background-color: #f1f5f9;">
          <th style="border: 1px solid #cbd5e1; padding: 8px;">Header 1</th>
          <th style="border: 1px solid #cbd5e1; padding: 8px;">Header 2</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td style="border: 1px solid #cbd5e1; padding: 8px;">Cell 1</td>
          <td style="border: 1px solid #cbd5e1; padding: 8px;">Cell 2</td>
        </tr>
      </tbody>
    </table>
  `;
  formatDoc('insertHTML', tableHtml);
};

const insertImage = () => {
  const imageUrl = prompt('Enter Image URL:', 'https://images.unsplash.com/photo-1576091160550-2173dba999ef?auto=format&fit=crop&w=500&q=80');
  if (imageUrl) {
    const imgHtml = `<img src="${imageUrl}" alt="Consent diagram" style="max-width: 100%; border-radius: 6px; margin: 12px 0; display: block;" />`;
    formatDoc('insertHTML', imgHtml);
  }
};

const promptGlossary = () => {
  const selection = window.getSelection();
  if (selection && selection.rangeCount > 0) {
    const range = selection.getRangeAt(0);
    const selected = range.toString().trim();
    if (selected) {
      selectedText.value = selected;
      savedSelection = range.cloneRange();
      glossaryDefinition.value = getPredefinedDefinition(selected);
      showGlossaryModal.value = true;
    } else {
      alert('Please highlight/select a term in the editor text first to annotate it.');
    }
  } else {
    alert('Please select a term in the editor text first.');
  }
};

const getPredefinedDefinition = (term) => {
  const t = term.toLowerCase();
  if (t.includes('biopsy')) {
    return 'An examination of tissue removed from a living body to discover the presence, cause, or extent of a disease.';
  }
  if (t.includes('hypertension')) {
    return 'Abnormally high blood pressure.';
  }
  if (t.includes('confidentiality')) {
    return 'The state of keeping or being kept secret or private.';
  }
  if (t.includes('voluntary')) {
    return 'Done, given, or acting of one\'s own free will; without coercion.';
  }
  return '';
};

const applyGlossaryAnnotation = () => {
  if (savedSelection && glossaryDefinition.value) {
    const span = document.createElement('span');
    span.className = 'glossary-term';
    span.setAttribute('data-definition', glossaryDefinition.value);
    span.style.borderBottom = '2px dashed #3b82f6';
    span.style.cursor = 'help';
    span.style.backgroundColor = '#eff6ff';
    span.style.padding = '0 2px';
    span.innerText = selectedText.value;

    savedSelection.deleteContents();
    savedSelection.insertNode(span);

    showGlossaryModal.value = false;
    handleInput();

    // Clear selection
    const selection = window.getSelection();
    if (selection) {
      selection.removeAllRanges();
    }
  }
};

const handleInput = () => {
  if (editorRef.value) {
    const html = editorRef.value.innerHTML;
    localHtml.value = html;
    emit('update', { id: props.section.id, html });
  }
};

// Canvas Interaction (Hover popover logic)
const handleCanvasClick = (e) => {
  const target = e.target;
  if (target && target.classList.contains('glossary-term')) {
    const definition = target.getAttribute('data-definition');
    const term = target.innerText;
    if (definition) {
      alert(`Glossary Definition:\n\n${term}: ${definition}`);
    }
  }
};

const handleCanvasMouseOver = (e) => {
  const target = e.target;
  if (target && target.classList.contains('glossary-term')) {
    const definition = target.getAttribute('data-definition');
    const term = target.innerText;
    if (definition) {
      hoveredGlossaryTerm.value = term;
      hoveredGlossaryDefinition.value = definition;

      const rect = target.getBoundingClientRect();
      // Calculate popover positioning absolute coordinates
      popoverX.value = rect.left + window.scrollX;
      popoverY.value = rect.bottom + window.scrollY + 8;
    }
  }
};

const hidePopover = () => {
  hoveredGlossaryTerm.value = null;
  hoveredGlossaryDefinition.value = null;
  popoverX.value = null;
  popoverY.value = null;
};
</script>

<style scoped>
.icf-section-editor-container {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--border);
  border-radius: 8px;
  background-color: white;
  padding: 16px;
}

.editor-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.editor-header h3 {
  margin: 0;
  font-size: 1.15rem;
  color: var(--neutral-dark);
}

.section-id-badge {
  font-family: monospace;
  font-size: 0.8rem;
  background-color: var(--neutral-light);
  padding: 4px 8px;
  border-radius: 4px;
  border: 1px solid var(--border);
}

.editor-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px;
  background-color: var(--neutral-light);
  border: 1px solid var(--border);
  border-bottom: none;
  border-radius: 6px 6px 0 0;
}

.toolbar-btn {
  padding: 6px 12px;
  font-size: 0.85rem;
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  gap: 4px;
}

.toolbar-btn:hover {
  background-color: #f1f5f9;
  border-color: var(--accent);
}

.glossary-btn {
  background-color: #eff6ff;
  border-color: #bfdbfe;
  color: #1e40af;
}

.glossary-btn:hover {
  background-color: #dbeafe;
}

.editor-canvas {
  min-height: 250px;
  max-height: 500px;
  overflow-y: auto;
  border: 1px solid var(--border);
  border-radius: 0 0 6px 6px;
  padding: 16px;
  outline: none;
  font-family: var(--font-sans, system-ui, sans-serif);
  line-height: 1.6;
  font-size: 0.95rem;
}

.editor-canvas:focus {
  border-color: var(--accent);
  box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
}

/* Modal styles */
.glossary-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(0, 0, 0, 0.4);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.glossary-modal {
  width: 90%;
  max-width: 450px;
  background-color: white;
  padding: 20px;
  border-radius: 8px;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
}

.selected-text-preview {
  background-color: #f8fafc;
  padding: 8px 12px;
  border-radius: 4px;
  border-left: 3px solid #3b82f6;
  margin: 12px 0;
}

.form-group {
  margin-bottom: 16px;
}

.form-group label {
  display: block;
  margin-bottom: 6px;
  font-weight: 600;
  font-size: 0.85rem;
}

.form-control {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* Popover styles */
.glossary-popover {
  position: absolute;
  background-color: #1e293b;
  color: white;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  max-width: 280px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
  z-index: 1050;
  pointer-events: none;
}

.popover-title {
  font-weight: bold;
  border-bottom: 1px solid #475569;
  padding-bottom: 4px;
  margin-bottom: 4px;
  color: #93c5fd;
  text-transform: uppercase;
  font-size: 0.7rem;
  letter-spacing: 0.05em;
}
</style>
