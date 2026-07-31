<template>
  <div
    class="form-section-container bg-gray-50 rounded-xl border border-gray-200 shadow-sm mb-6 overflow-hidden"
    :class="{ 'border-indigo-200 ring-2 ring-indigo-50': !section.isCollapsed && isDragging }"
  >
    <!-- Section Header Card -->
    <div
      class="section-header px-4 py-3 bg-white border-b border-gray-200 flex items-center justify-between cursor-pointer select-none"
      @click="toggleCollapse"
    >
      <div class="flex items-center gap-3">
        <!-- Drag Handle for Section Reordering -->
        <span
          class="section-drag-handle cursor-move text-gray-400 hover:text-gray-600 text-lg"
          @click.stop
        >
          ☰
        </span>
        <h3 class="text-base font-bold text-gray-800">
          {{ section.name || 'Unnamed Section' }}
        </h3>
        <span class="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded-full font-medium font-mono">
          {{ section.items?.length || 0 }} {{ (section.items?.length || 0) === 1 ? 'field' : 'fields' }}
        </span>
      </div>

      <!-- Action Controls -->
      <div class="flex items-center gap-2" @click.stop>
        <button
          class="btn-add-item bg-indigo-50 hover:bg-indigo-100 text-indigo-700 text-xs font-semibold px-2.5 py-1.5 rounded transition flex items-center gap-1"
          @click="addNewItem"
        >
          <span>➕</span> Add Row
        </button>
        <button
          class="bg-gray-100 hover:bg-gray-200 text-gray-700 text-xs font-semibold px-2.5 py-1.5 rounded transition"
          @click="toggleCollapse"
        >
          {{ section.isCollapsed ? 'Expand' : 'Collapse' }}
        </button>
      </div>
    </div>

    <!-- Section Body: Draggable Nested Items List -->
    <div
      v-show="!section.isCollapsed"
      class="section-body p-4 transition-all"
    >
      <!-- Visual Dropzone Indicator when empty -->
      <div
        v-if="(!section.items || section.items.length === 0)"
        class="empty-dropzone-placeholder border-2 border-dashed border-gray-300 rounded-lg p-6 text-center text-sm text-gray-400 bg-white"
      >
        Drag and drop field widgets here
      </div>

      <!-- Nested Draggable with group fields for inter-section field moves -->
      <draggable
        v-model="items"
        item-key="id"
        group="fields"
        handle=".field-drag-handle"
        class="grid grid-cols-12 gap-4"
        :class="{ 'dragging-active': isDragging }"
        @start="isDragging = true"
        @end="isDragging = false"
      >
        <template #item="{ element: field }">
          <div :class="gridSpanClass(field.gridSpan)" class="field-item-wrapper relative">
            <CanvasFieldWidget
              :field="field"
              :selected-field-id="selectedFieldId"
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
import { ref, computed } from "vue";
import draggable from "vuedraggable";
import CanvasFieldWidget from "./CanvasFieldWidget.vue";
import { useDesignerStore } from "../../stores/designer.js";

const props = defineProps({
  section: {
    type: Object,
    required: true
  },
  selectedFieldId: {
    type: String,
    default: null
  }
});

const emit = defineEmits(["select-field", "update-section"]);

const designerStore = useDesignerStore();
const isDragging = ref(false);

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
  }
});

function toggleCollapse() {
  const storeSection = designerStore.activeForm?.sections?.find(
    (s) => s.id === props.section.id
  );
  if (storeSection) {
    storeSection.isCollapsed = !storeSection.isCollapsed;
  } else {
    emit("update-section", { ...props.section, isCollapsed: !props.section.isCollapsed });
  }
}

function addNewItem() {
  const newField = {
    id: `field-${Date.now()}-${Math.random().toString(36).substring(2, 7)}`,
    label: "New Field Entry",
    type: "text",
    gridSpan: 12,
    required: false
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
    const updatedItems = (props.section.items || []).filter((item) => item.id !== fieldId);
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
        label: `${original.label} (Copy)`
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
.field-item-wrapper {
  transition: all 0.2s ease;
}

.col-span-1 { grid-column: span 1 / span 1; }
.col-span-2 { grid-column: span 2 / span 2; }
.col-span-3 { grid-column: span 3 / span 3; }
.col-span-4 { grid-column: span 4 / span 4; }
.col-span-5 { grid-column: span 5 / span 5; }
.col-span-6 { grid-column: span 6 / span 6; }
.col-span-7 { grid-column: span 7 / span 7; }
.col-span-8 { grid-column: span 8 / span 8; }
.col-span-9 { grid-column: span 9 / span 9; }
.col-span-10 { grid-column: span 10 / span 10; }
.col-span-11 { grid-column: span 11 / span 11; }
.col-span-12 { grid-column: span 12 / span 12; }
</style>
