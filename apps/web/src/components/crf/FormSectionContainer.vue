<template>
  <div
    class="form-section-container"
    :class="{
      'is-active-section': !section.isCollapsed && isDragging,
    }"
  >
    <!-- Section Header Card -->
    <div ref="headerCardRef" class="section-header-card">
      <div class="section-header-left">
        <!-- Drag Handle for Section Reordering -->
        <span
          class="section-drag-handle"
          title="Drag to reorder section"
          aria-hidden="true"
        >
          ☰
        </span>
        <h3 class="section-title">
          <button
            type="button"
            class="section-header-toggle section-header"
            :aria-expanded="!section.isCollapsed"
            @click.stop="toggleCollapse"
          >
            <span class="section-title-text">
              {{ section.name || "Unnamed Section" }}
            </span>
            <span class="section-field-count">
              {{ section.items?.length || 0 }}
              {{ (section.items?.length || 0) === 1 ? "field" : "fields" }}
            </span>
          </button>
        </h3>
      </div>

      <!-- Action Controls -->
      <div class="section-header-right">
        <div class="section-reorder-controls">
          <button
            type="button"
            class="btn-move-up btn-section-move-up"
            :disabled="isFirst"
            title="Move section up"
            aria-label="Move section up"
            @click.stop="$emit('move-section-up')"
          >
            ▲
          </button>
          <button
            type="button"
            class="btn-move-down btn-section-move-down"
            :disabled="isLast"
            title="Move section down"
            aria-label="Move section down"
            @click.stop="$emit('move-section-down')"
          >
            ▼
          </button>
        </div>
        <button
          type="button"
          class="btn-add-field btn-add-item"
          @click.stop="addNewItem"
        >
          <span aria-hidden="true">➕</span> Add Field
        </button>
        <button
          type="button"
          class="btn-section-toggle"
          :aria-expanded="!section.isCollapsed"
          @click.stop="toggleCollapse"
        >
          {{ section.isCollapsed ? "Expand" : "Collapse" }}
        </button>
      </div>
    </div>

    <!-- Section Body: Draggable Nested Items List -->
    <div v-show="!section.isCollapsed" class="section-body">
      <!-- Visual Dropzone Indicator when empty -->
      <div
        v-if="!section.items || section.items.length === 0"
        class="empty-dropzone-placeholder"
      >
        Drag and drop clinical field widgets here or click "+ Add Field"
      </div>

      <!-- Nested Draggable with group fields for inter-section field moves -->
      <draggable
        v-model="items"
        item-key="id"
        group="fields"
        handle=".field-drag-handle"
        :force-fallback="true"
        :fallback-tolerance="5"
        scroll-sensitivity="30"
        class="crf-grid-container"
        :class="{ 'dragging-active': isDragging }"
        @start="isDragging = true"
        @end="isDragging = false"
      >
        <template #item="{ element: field, index }">
          <div
            :class="gridSpanClass(field.gridSpan)"
            class="field-item-wrapper"
          >
            <CanvasFieldWidget
              :field="field"
              :selected-field-id="selectedFieldId"
              :has-warning="fieldHasWarning(field)"
              :warning-message="getFieldWarningMessage(field)"
              :field-index="index"
              :total-fields="items.length"
              :is-first="!canMoveFieldUp(index)"
              :is-last="!canMoveFieldDown(index)"
              @select-field="onSelectField"
              @delete-field="onDeleteField"
              @duplicate-field="onDuplicateField"
              @move-field-up="moveFieldUp(index)"
              @move-field-down="moveFieldDown(index)"
            />
          </div>
        </template>
      </draggable>
    </div>
  </div>
</template>

<script setup>
/**
 * FormSectionContainer component
 *
 * Provides a collapsible visual container for a group of related clinical fields.
 * Manages intra-section and inter-section dragging operations, row appending, collapsing,
 * and passes viewport alerts downstream to individual CanvasFieldWidgets.
 */
import { ref, computed, toRaw, watch, onBeforeUnmount } from "vue";
import draggable from "vuedraggable";
import CanvasFieldWidget from "./CanvasFieldWidget.vue";
import { useDesignerStore } from "../../stores/designer.js";
import {
  demoNowMs,
  demoRandomToken,
  nextDemoStep,
} from "../../utils/demoMode.js";

const props = defineProps({
  section: {
    type: Object,
    required: true,
  },
  selectedFieldId: {
    type: [String, null],
    default: null,
  },
  sectionIndex: {
    type: Number,
    default: 0,
  },
  totalSections: {
    type: Number,
    default: 1,
  },
  isFirst: {
    type: Boolean,
    default: false,
  },
  isLast: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "select-field",
  "update-section",
  "sections-changed",
  "move-section-up",
  "move-section-down",
]);

const designerStore = useDesignerStore();
const isDragging = ref(false);
const headerCardRef = ref(null);

function onHeaderCardClick(event) {
  if (
    event?.target instanceof Element &&
    event.target.closest(
      "button, input, select, textarea, a, label, .section-drag-handle"
    )
  ) {
    return;
  }
  toggleCollapse();
}

watch(
  headerCardRef,
  (nextEl, prevEl) => {
    if (prevEl) {
      prevEl.removeEventListener("click", onHeaderCardClick);
    }
    if (nextEl) {
      nextEl.addEventListener("click", onHeaderCardClick);
    }
  },
  { immediate: true, flush: "sync" }
);

onBeforeUnmount(() => {
  if (headerCardRef.value) {
    headerCardRef.value.removeEventListener("click", onHeaderCardClick);
  }
});

const viewportWidth = computed(() => {
  const vp = designerStore.viewport || "desktop";
  if (vp === "mobile") return 480;
  if (vp === "tablet") return 768;
  return 1200;
});

function getFieldWarningMessage(field) {
  const w = viewportWidth.value;
  const span = parseInt(field.gridSpan) || 12;
  const simWidth = (w / 12) * span;
  if (simWidth < 150) {
    return `Label may clip or overlap on narrow viewport (${Math.round(simWidth)}px < 150px)`;
  }
  return "";
}

function fieldHasWarning(field) {
  return !!getFieldWarningMessage(field);
}

const itemsRevision = ref(0);
const items = computed({
  get: () => {
    void itemsRevision.value;
    const storeSection = designerStore.activeForm?.sections?.find(
      (s) => s.id === props.section.id
    );
    if (storeSection && toRaw(storeSection) === toRaw(props.section)) {
      return storeSection.items || [];
    }
    return props.section.items || [];
  },
  set: (val) => {
    const updatedSection = { ...props.section, items: val };
    emit("update-section", updatedSection);

    if (designerStore.activeForm?.sections) {
      const idx = designerStore.activeForm.sections.findIndex(
        (s) => s.id === props.section.id
      );
      if (idx !== -1) {
        designerStore.activeForm.sections[idx].items = val;
      }
    }
    itemsRevision.value++;
  },
});

function toggleCollapse() {
  const storeSection = designerStore.activeForm?.sections?.find(
    (s) => s.id === props.section.id
  );
  if (storeSection) {
    storeSection.isCollapsed = !storeSection.isCollapsed;
  } else {
    const isCollapsed = !props.section.isCollapsed;
    emit("update-section", { ...props.section, isCollapsed });
  }
}

function addNewItem() {
  const step = nextDemoStep("form-section-field");
  const newField = {
    id: `field-${demoNowMs(step)}-${demoRandomToken(5, 36, "form-section-field")}`,
    label: "New Field Entry",
    type: "text",
    gridSpan: 12,
    required: false,
  };

  const storeSection = designerStore.activeForm?.sections?.find(
    (s) => s.id === props.section.id
  );

  if (storeSection) {
    designerStore.addFieldToSection(props.section.id, newField);
  } else {
    const updatedItems = [...(props.section.items || []), newField];
    emit("update-section", { ...props.section, items: updatedItems });
  }

  designerStore.setSelectedFieldId(newField.id);
  emit("select-field", newField.id);
}

function gridSpanClass(span) {
  const s = parseInt(span) || 12;
  return `col-span-${s}`;
}

function onSelectField(fieldId) {
  designerStore.setSelectedFieldId(fieldId);
  emit("select-field", fieldId);
}

function onDeleteField(fieldId) {
  const storeSection = designerStore.activeForm?.sections?.find(
    (s) => s.id === props.section.id
  );

  if (storeSection) {
    designerStore.deleteField(fieldId);
  } else {
    const updatedItems = (props.section.items || []).filter(
      (item) => item.id !== fieldId
    );
    emit("update-section", { ...props.section, items: updatedItems });
  }
}

function onDuplicateField(fieldId) {
  const storeSection = designerStore.activeForm?.sections?.find(
    (s) => s.id === props.section.id
  );

  if (storeSection) {
    designerStore.duplicateField(fieldId);
    if (designerStore.selectedFieldId) {
      emit("select-field", designerStore.selectedFieldId);
    }
  } else {
    const currentItems = props.section.items || [];
    const idx = currentItems.findIndex((item) => item.id === fieldId);
    if (idx !== -1) {
      const original = currentItems[idx];
      const step = nextDemoStep("form-section-field");
      const newId = `field-${demoNowMs(step)}-${demoRandomToken(5, 36, "form-section-field")}`;
      const copy = {
        ...original,
        id: newId,
        label: `${original.label} (Copy)`,
      };
      const updatedItems = [...currentItems];
      updatedItems.splice(idx + 1, 0, copy);
      emit("update-section", { ...props.section, items: updatedItems });
      designerStore.setSelectedFieldId(newId);
      emit("select-field", newId);
    }
  }
}

function getStoreSectionIndex() {
  const storeSections = designerStore.activeForm?.sections;
  if (!Array.isArray(storeSections)) return -1;
  return storeSections.findIndex((s) => s.id === props.section.id);
}

function canMoveFieldUp(index) {
  if (index > 0) return true;
  if (index === 0) {
    return getStoreSectionIndex() > 0;
  }
  return false;
}

function canMoveFieldDown(index) {
  if (index >= 0 && index < items.value.length - 1) return true;
  if (index === items.value.length - 1) {
    const storeSections = designerStore.activeForm?.sections || [];
    const sIdx = getStoreSectionIndex();
    return sIdx !== -1 && sIdx < storeSections.length - 1;
  }
  return false;
}

function moveFieldUp(index) {
  if (index < 0 || index >= items.value.length) return;
  if (index > 0) {
    const currentItems = [...items.value];
    const temp = currentItems[index];
    currentItems[index] = currentItems[index - 1];
    currentItems[index - 1] = temp;
    items.value = currentItems;
    return;
  }
  const field = items.value[index];
  if (field && designerStore.moveFieldAcrossSections(field.id, "up")) {
    itemsRevision.value++;
    emit("sections-changed", designerStore.activeForm?.sections);
    emit("select-field", field.id);
  }
}

function moveFieldDown(index) {
  if (index < 0 || index >= items.value.length) return;
  if (index < items.value.length - 1) {
    const currentItems = [...items.value];
    const temp = currentItems[index];
    currentItems[index] = currentItems[index + 1];
    currentItems[index + 1] = temp;
    items.value = currentItems;
    return;
  }
  const field = items.value[index];
  if (field && designerStore.moveFieldAcrossSections(field.id, "down")) {
    itemsRevision.value++;
    emit("sections-changed", designerStore.activeForm?.sections);
    emit("select-field", field.id);
  }
}
</script>

<style scoped>
.form-section-container {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03);
  overflow: hidden;
  transition: all 0.15s;
}

.form-section-container.is-active-section {
  border-color: var(--semantic-color-divider-accent);
  box-shadow: 0 0 0 2px rgba(147, 197, 253, 0.3);
}

.section-header-card {
  padding: 12px 16px;
  background-color: var(--semantic-color-surface);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  user-select: none;
  cursor: pointer;
}

.section-header-left {
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-drag-handle {
  cursor: grab;
  color: var(--semantic-color-text-secondary);
  font-size: 1.1rem;
  padding: 0 4px;
  touch-action: none;
  user-select: none;
}

.section-drag-handle:active {
  cursor: grabbing;
}

.section-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
  margin: 0;
}

.section-header-toggle {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 4px 6px;
  border: none;
  border-radius: 6px;
  background: none;
  font: inherit;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.section-header-toggle:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 2px;
}

.section-field-count {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  background-color: var(--semantic-color-surface-header);
  padding: 2px 8px;
  border-radius: 12px;
  font-weight: 600;
  font-family: monospace;
}

.section-header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-reorder-controls {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}

.btn-add-field {
  padding: 5px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  background-color: var(--semantic-color-primary-light);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-primary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 4px;
  transition: all 0.15s;
}

.btn-add-field:hover {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-primary-dark);
}

.btn-section-toggle {
  padding: 5px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  border-radius: 6px;
  cursor: pointer;
}

.section-body {
  padding: 16px;
}

.empty-dropzone-placeholder {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 24px;
  text-align: center;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  background-color: var(--semantic-color-surface);
}

.crf-grid-container {
  display: grid;
  grid-template-columns: repeat(12, minmax(0, 1fr));
  gap: 16px;
  width: 100%;
}

.field-item-wrapper {
  transition: all 0.2s ease;
  position: relative;
}

.col-span-1 {
  grid-column: span 1 / span 1;
}
.col-span-2 {
  grid-column: span 2 / span 2;
}
.col-span-3 {
  grid-column: span 3 / span 3;
}
.col-span-4 {
  grid-column: span 4 / span 4;
}
.col-span-5 {
  grid-column: span 5 / span 5;
}
.col-span-6 {
  grid-column: span 6 / span 6;
}
.col-span-7 {
  grid-column: span 7 / span 7;
}
.col-span-8 {
  grid-column: span 8 / span 8;
}
.col-span-9 {
  grid-column: span 9 / span 9;
}
.col-span-10 {
  grid-column: span 10 / span 10;
}
.col-span-11 {
  grid-column: span 11 / span 11;
}
.col-span-12 {
  grid-column: span 12 / span 12;
}
</style>

<style scoped src="./crfReorderControls.css"></style>
