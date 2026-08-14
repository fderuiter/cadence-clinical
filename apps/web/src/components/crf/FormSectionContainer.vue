<template>
  <div
    class="form-section-container"
    :class="{
      'is-active-section': !section.isCollapsed && isDragging,
    }"
  >
    <!-- Section Header Card -->
    <div
      class="section-header-card"
      v-keyboard-click="toggleCollapse"
      :aria-expanded="!section.isCollapsed"
      @click="toggleCollapse"
    >
      <div class="section-header-left">
        <!-- Drag Handle for Section Reordering -->
        <span
          class="section-drag-handle"
          title="Drag to reorder section"
          @click.stop
        >
          ☰
        </span>
        <h3 class="section-title">
          {{ section.name || "Unnamed Section" }}
        </h3>
        <span class="section-field-count">
          {{ section.items?.length || 0 }}
          {{ (section.items?.length || 0) === 1 ? "field" : "fields" }}
        </span>
      </div>

      <!-- Action Controls -->
      <div class="section-header-right" @click.stop>
        <button class="btn-add-field" @click="addNewItem">
          <span>➕</span> Add Field
        </button>
        <button class="btn-section-toggle" @click="toggleCollapse">
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
        class="crf-grid-container"
        :class="{ 'dragging-active': isDragging }"
        @start="isDragging = true"
        @end="isDragging = false"
      >
        <template #item="{ element: field }">
          <div
            :class="gridSpanClass(field.gridSpan)"
            class="field-item-wrapper"
          >
            <CanvasFieldWidget
              :field="field"
              :selected-field-id="selectedFieldId"
              :has-warning="fieldHasWarning(field)"
              :warning-message="getFieldWarningMessage(field)"
              @select-field="onSelectField"
              @delete-field="onDeleteField"
              @duplicate-field="onDuplicateField"
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
import { ref, computed } from "vue";
import draggable from "vuedraggable";
import CanvasFieldWidget from "./CanvasFieldWidget.vue";
import { useDesignerStore } from "../../stores/designer.js";
import { vKeyboardClick } from "../../directives/keyboardClick.js";

const props = defineProps({
  section: {
    type: Object,
    required: true,
  },
  selectedFieldId: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["select-field", "update-section"]);

const designerStore = useDesignerStore();
const isDragging = ref(false);

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

const items = computed({
  get: () => props.section.items || [],
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
  const newField = {
    id: `field-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`,
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
      const newId = `field-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`;
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
</script>

<style scoped>
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
