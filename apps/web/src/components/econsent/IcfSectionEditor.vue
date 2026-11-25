<template>
  <div class="icf-section-editor-container card">
    <div class="editor-header">
      <h3>{{ $t("econsent.editor.editSection", { title: section.title }) }}</h3>
      <span class="section-id-badge">{{
        $t("econsent.editor.sectionId", { id: section.id })
      }}</span>
    </div>

    <!-- Formatting Toolbar -->
    <div class="editor-toolbar">
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.bold')"
        @click="formatDoc('bold')"
      >
        <strong>B</strong>
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.italic')"
        @click="formatDoc('italic')"
      >
        <em>I</em>
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.heading2')"
        @click="formatHeading('h2')"
      >
        H2
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.heading3')"
        @click="formatHeading('h3')"
      >
        H3
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.bulletList')"
        @click="formatDoc('insertUnorderedList')"
      >
        • {{ $t("econsent.editor.list") }}
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.numberedList')"
        @click="formatDoc('insertOrderedList')"
      >
        1. {{ $t("econsent.editor.list") }}
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.insertTable')"
        @click="insertTable"
      >
        📊 {{ $t("econsent.editor.table") }}
      </button>
      <button
        type="button"
        class="toolbar-btn"
        :title="$t('econsent.editor.insertImage')"
        @click="insertImage"
      >
        🖼️ {{ $t("econsent.editor.image") }}
      </button>
      <button
        type="button"
        class="toolbar-btn glossary-btn"
        :title="$t('econsent.editor.annotateGlossary')"
        @click="promptGlossary"
      >
        🏷️ {{ $t("econsent.editor.glossaryTerm") }}
      </button>
      <button
        type="button"
        class="toolbar-btn readability-btn"
        :title="$t('econsent.editor.readabilityHarmonizerTitle')"
        @click="showReadabilityDrawer = true"
      >
        📖 {{ $t("econsent.editor.readabilityHarmonizer") }}
      </button>
    </div>

    <!-- Main Contenteditable Editor -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
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
      @mouseout="handleCanvasMouseOut"
      @focusin="handleCanvasFocusIn"
      @focusout="handleCanvasFocusOut"
      v-html="localHtml"
    />

    <!-- Glossary Term Annotation Dialog/Modal -->
    <ClinicalModal
      v-model="showGlossaryModal"
      :teleport="false"
      size="sm"
      backdrop-class="glossary-modal-overlay"
      dialog-class="glossary-modal card"
      :title="$t('econsent.editor.annotateSelectedText')"
    >
      <p class="selected-text-preview">
        {{ $t("econsent.editor.selectedText", { text: selectedText }) }}
      </p>
      <div class="form-group">
        <label for="glossary-definition">{{
          $t("econsent.editor.termDefinitionLabel")
        }}</label>
        <textarea
          id="glossary-definition"
          v-model="glossaryDefinition"
          :placeholder="$t('econsent.editor.termDefinitionPlaceholder')"
          rows="3"
          class="form-control"
        />
      </div>

      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          @click="showGlossaryModal = false"
        >
          {{ $t("econsent.editor.cancel") }}
        </button>
        <button
          type="button"
          class="btn btn-primary"
          @click="applyGlossaryAnnotation"
        >
          {{ $t("econsent.editor.applyAnnotation") }}
        </button>
      </template>
    </ClinicalModal>

    <!-- Glossary Popover definition -->
    <ClinicalPopover
      :model-value="Boolean(hoveredGlossaryTerm && activeGlossaryAnchor)"
      :anchor="activeGlossaryAnchor"
      :teleport="false"
      :viewport-margin="12"
      :preferred-width="280"
      hover-bridge
      role="tooltip"
      popover-class="glossary-popover"
      @update:model-value="handlePopoverModelUpdate"
      @mouseleave="handlePopoverMouseLeave"
      @focusout="handleCanvasFocusOut"
    >
      <div class="popover-title">
        {{ $t("econsent.editor.glossaryDefinition") }}
      </div>
      <div class="popover-body">
        <strong>{{ hoveredGlossaryTerm }}</strong>:
        {{ hoveredGlossaryDefinition }}
      </div>
    </ClinicalPopover>

    <!-- Readability Assistant Split-Screen Drawer -->
    <ReadabilityDrawer
      :is-open="showReadabilityDrawer"
      :text="rawSectionText"
      :clause-id="section.id"
      @close="showReadabilityDrawer = false"
      @apply-substitution="onApplyReadabilitySubstitution"
      @apply-all="onApplyAllReadability"
    />
  </div>
</template>

<script setup>
import { ClinicalModal, ClinicalPopover } from "ui";
import { ref, computed, watch, nextTick, onMounted } from "vue";
import { useConfirmDialog } from "../../composables/useConfirmDialog.js";
import { useToastStore } from "../../stores/toasts.js";
import ReadabilityDrawer from "./ReadabilityDrawer.vue";

const toastStore = useToastStore();
const { promptDialog } = useConfirmDialog();

const props = defineProps({
  section: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(["update"]);

const editorRef = ref(null);
const localHtml = ref("");
const showGlossaryModal = ref(false);
const showReadabilityDrawer = ref(false);
const selectedText = ref("");

const rawSectionText = computed(() => {
  const div = document.createElement("div");
  div.innerHTML = localHtml.value;
  return div.textContent || div.innerText || "";
});

const replaceTermInHtml = (html, originalTerm, suggestedTerm) => {
  const container = document.createElement("div");
  container.innerHTML = html;
  const regex = new RegExp(
    `\\b${originalTerm.replace(/[.*+?^${}()|[\\]\\]/g, "\\$&")}\\b`,
    "gi"
  );

  const walk = (node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      if (regex.test(node.nodeValue)) {
        node.nodeValue = node.nodeValue.replace(regex, suggestedTerm);
      }
    } else if (
      node.nodeType === Node.ELEMENT_NODE &&
      node.nodeName !== "SCRIPT" &&
      node.nodeName !== "STYLE"
    ) {
      for (let child = node.firstChild; child; child = child.nextSibling) {
        walk(child);
      }
    }
  };
  walk(container);
  return container.innerHTML;
};

const onApplyReadabilitySubstitution = ({ substitution }) => {
  if (!substitution) return;
  localHtml.value = replaceTermInHtml(
    localHtml.value,
    substitution.original_term,
    substitution.suggested_term
  );
  if (editorRef.value) {
    editorRef.value.innerHTML = localHtml.value;
  }
  handleInput();
};

const onApplyAllReadability = ({ substitutions }) => {
  if (!substitutions || !substitutions.length) return;
  let updated = localHtml.value;
  substitutions.forEach((s) => {
    updated = replaceTermInHtml(updated, s.original_term, s.suggested_term);
  });
  localHtml.value = updated;
  if (editorRef.value) {
    editorRef.value.innerHTML = localHtml.value;
  }
  handleInput();
};

const glossaryDefinition = ref("");
let savedSelection = null;

// Popover State
const hoveredGlossaryTerm = ref(null);
const hoveredGlossaryDefinition = ref(null);
const activeGlossaryAnchor = ref(null);
const isHoverSupported = ref(true);

const syncGlossaryTermFocusable = () => {
  if (!editorRef.value) return;
  const terms = editorRef.value.querySelectorAll(".glossary-term");
  terms.forEach((node) => {
    if (!node.hasAttribute("tabindex")) {
      node.setAttribute("tabindex", "0");
    }
  });
};

// Synchronize with prop changes
watch(
  () => props.section.id,
  () => {
    localHtml.value = props.section.html;
    if (editorRef.value) {
      editorRef.value.innerHTML = props.section.html;
    }
    nextTick(syncGlossaryTermFocusable);
  },
  { immediate: true }
);

onMounted(() => {
  localHtml.value = props.section.html;
  isHoverSupported.value = window.matchMedia("(hover: hover)").matches;
  syncGlossaryTermFocusable();
});

// Format actions via standard execCommand (or manual DOM fallback)
const formatDoc = (command, value = "") => {
  document.execCommand(command, false, value);
  handleInput();
};

const formatHeading = (tag) => {
  formatDoc("formatBlock", `<${tag}>`);
};

const insertTable = () => {
  const tableHtml = `
    <table class="editor-table" style="width: 100%; border-collapse: collapse; margin: 12px 0;">
      <thead>
        <tr style="background-color: var(--semantic-color-surface-header); color: var(--semantic-color-text);">
          <th style="border: 1px solid var(--semantic-color-border-muted); padding: 8px;">Header 1</th>
          <th style="border: 1px solid var(--semantic-color-border-muted); padding: 8px;">Header 2</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td style="border: 1px solid var(--semantic-color-border-muted); padding: 8px;">Cell 1</td>
          <td style="border: 1px solid var(--semantic-color-border-muted); padding: 8px;">Cell 2</td>
        </tr>
      </tbody>
    </table>
  `;
  formatDoc("insertHTML", tableHtml);
};

const insertImage = async () => {
  const defaultDiagramUrl = `${import.meta.env.BASE_URL || "/"}assets/consent-diagram.svg`;
  const selection = window.getSelection();
  const savedImgRange =
    selection &&
    selection.rangeCount > 0 &&
    editorRef.value?.contains(selection.anchorNode)
      ? selection.getRangeAt(0).cloneRange()
      : null;

  const imageUrl = await promptDialog({
    title: "Insert Consent Diagram",
    message: "Specify the image URL to embed in this consent section.",
    inputLabel: "Enter Image URL",
    defaultValue: defaultDiagramUrl,
    confirmLabel: "Insert Image",
    cancelLabel: "Cancel",
    variant: "info",
  });
  if (imageUrl) {
    if (savedImgRange && selection) {
      selection.removeAllRanges();
      selection.addRange(savedImgRange);
    } else if (editorRef.value) {
      editorRef.value.focus();
    }
    const imgHtml = `<img src="${imageUrl}" alt="Consent diagram" style="max-width: 100%; border-radius: 6px; margin: 12px 0; display: block;" />`;
    formatDoc("insertHTML", imgHtml);
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
      toastStore.push({
        tone: "warning",
        title: "Select Text First",
        message:
          "Please highlight/select a term in the editor text first to annotate it.",
      });
    }
  } else {
    toastStore.push({
      tone: "warning",
      title: "Select Text First",
      message: "Please select a term in the editor text first.",
    });
  }
};

const getPredefinedDefinition = (term) => {
  const t = term.toLowerCase();
  if (t.includes("biopsy")) {
    return "An examination of tissue removed from a living body to discover the presence, cause, or extent of a disease.";
  }
  if (t.includes("hypertension")) {
    return "Abnormally high blood pressure.";
  }
  if (t.includes("confidentiality")) {
    return "The state of keeping or being kept secret or private.";
  }
  if (t.includes("voluntary")) {
    return "Done, given, or acting of one's own free will; without coercion.";
  }
  return "";
};

const applyGlossaryAnnotation = () => {
  if (savedSelection && glossaryDefinition.value) {
    const span = document.createElement("span");
    span.className = "glossary-term";
    span.setAttribute("tabindex", "0");
    span.setAttribute("data-definition", glossaryDefinition.value);
    span.style.borderBottom =
      "2px dashed var(--semantic-color-interactive-primary)";
    span.style.cursor = "help";
    span.style.backgroundColor = "var(--semantic-color-info-bg)";
    span.style.padding = "0 2px";
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
    emit("update", { id: props.section.id, html });
  }
};

// Canvas Interaction (Hover, focus, and tap popover logic)
const resolveGlossaryTarget = (target) => {
  if (!target || typeof target !== "object") return null;
  if (typeof target.closest === "function") {
    return target.closest(".glossary-term");
  }
  return target.classList?.contains("glossary-term") ? target : null;
};

const showPopoverForElement = (target) => {
  if (!target) return false;
  const definition = target.getAttribute("data-definition");
  const term = target.innerText || target.textContent;
  if (!definition) return false;
  hoveredGlossaryTerm.value = term;
  hoveredGlossaryDefinition.value = definition;
  activeGlossaryAnchor.value = target;
  return true;
};

const isWithinActivePopoverOrAnchor = (node) => {
  if (!node) return false;
  if (activeGlossaryAnchor.value?.contains?.(node)) {
    return true;
  }
  const popoverEl =
    typeof document !== "undefined"
      ? document.querySelector(".glossary-popover")
      : null;
  return Boolean(popoverEl?.contains?.(node));
};

const handleCanvasClick = (e) => {
  const target = resolveGlossaryTarget(e.target);
  if (target) {
    const definition = target.getAttribute("data-definition");
    const term = target.innerText || target.textContent;
    if (definition) {
      if (!isHoverSupported.value) {
        e.preventDefault();
        e.stopPropagation();

        if (
          hoveredGlossaryTerm.value === term &&
          activeGlossaryAnchor.value === target
        ) {
          hidePopover();
        } else {
          showPopoverForElement(target);
        }
      } else {
        toastStore.push({
          tone: "info",
          title: `Glossary Definition: ${term}`,
          message: `${term}: ${definition}`,
        });
      }
    }
  }
};

const handleCanvasMouseOver = (e) => {
  if (!isHoverSupported.value) return;
  const target = resolveGlossaryTarget(e.target);
  if (target) {
    showPopoverForElement(target);
  }
};

const handleCanvasMouseOut = (e) => {
  if (!isHoverSupported.value) return;
  if (isWithinActivePopoverOrAnchor(e?.relatedTarget)) {
    return;
  }
  hidePopover();
};

const handlePopoverMouseLeave = (e) => {
  if (!isHoverSupported.value) return;
  if (
    e?.relatedTarget &&
    activeGlossaryAnchor.value?.contains?.(e.relatedTarget)
  ) {
    return;
  }
  hidePopover();
};

const handleCanvasFocusIn = (e) => {
  const target = resolveGlossaryTarget(e.target);
  if (target) {
    showPopoverForElement(target);
  }
};

const handleCanvasFocusOut = (e) => {
  if (isWithinActivePopoverOrAnchor(e?.relatedTarget)) {
    return;
  }
  hidePopover();
};

const handlePopoverModelUpdate = (nextOpen) => {
  if (!nextOpen) {
    hidePopover();
  }
};

const hidePopover = () => {
  hoveredGlossaryTerm.value = null;
  hoveredGlossaryDefinition.value = null;
  activeGlossaryAnchor.value = null;
};
</script>

<style scoped>
.icf-section-editor-container {
  display: flex;
  flex-direction: column;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  border: 1px solid var(--border);
  border-radius: 8px;
  background-color: var(--semantic-color-surface);
  padding: 16px;
}

.editor-header {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
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
  font-family: var(--semantic-font-mono);
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
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--border);
  border-radius: 4px;
  cursor: pointer;
  transition: all 0.2s;
  display: flex;
  align-items: center;
  gap: 4px;
}

.toolbar-btn:hover {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--accent);
}

.glossary-btn {
  background-color: var(--semantic-color-info-bg);
  border-color: var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.glossary-btn:hover {
  background-color: var(--semantic-color-info-light);
}

.editor-canvas {
  min-height: 250px;
  max-height: 500px;
  overflow-y: auto;
  border: 1px solid var(--border);
  border-radius: 0 0 6px 6px;
  padding: 16px;
  outline: 2px solid transparent;
  font-family: var(--semantic-font-family);
  line-height: 1.6;
  font-size: 0.95rem;
}

.editor-canvas:focus {
  border-color: var(--accent);
  box-shadow: var(--shadow-sm);
}

/* Modal styles */
.glossary-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-color: var(--semantic-color-backdrop-soft);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.glossary-modal {
  width: 90%;
  max-width: 450px;
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  box-shadow: var(--shadow-lg);
}

.selected-text-preview {
  background-color: var(--semantic-color-surface-muted);
  padding: 8px 12px;
  border-radius: 4px;
  border-left: 3px solid var(--semantic-color-interactive-primary);
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
:deep(.glossary-popover) {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border-color: var(--semantic-color-border-control);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  max-width: 280px;
  box-shadow: var(--shadow-md);
  z-index: 1050;
}

.popover-title {
  font-weight: bold;
  border-bottom: 1px solid var(--semantic-color-border-control);
  padding-bottom: 4px;
  margin-bottom: 4px;
  color: var(--semantic-color-divider-accent);
  text-transform: uppercase;
  font-size: var(--font-size-2xs);
  letter-spacing: 0.05em;
}
</style>
