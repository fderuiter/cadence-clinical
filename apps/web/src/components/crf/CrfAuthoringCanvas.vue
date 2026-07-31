<template>
  <div class="authoring-canvas-stage p-6 bg-slate-100 rounded-2xl border border-slate-200 min-h-[400px]">
    <div class="canvas-header flex justify-between items-center mb-6">
      <div>
        <h2 class="text-xl font-bold text-slate-800">
          eCRF Authoring Canvas
        </h2>
        <p class="text-xs text-slate-500">
          Drag sections to reorder, drag fields to layout & configure responsive columns.
        </p>
      </div>
      <div
        v-if="formSchema"
        class="text-xs bg-indigo-50 border border-indigo-100 px-3 py-1 rounded-full text-indigo-700 font-medium"
      >
        Active Form: {{ formSchema.name || 'Draft' }}
      </div>
    </div>

    <draggable
      v-model="sections"
      item-key="id"
      handle=".section-drag-handle"
      class="sections-drag-list space-y-4"
      @change="handleSectionReorder"
    >
      <template #item="{ element: section }">
        <FormSectionContainer
          :section="section"
          :selected-field-id="selectedFieldId"
          @select-field="onSelectField"
          @update-section="onUpdateSection"
        />
      </template>
    </draggable>
  </div>
</template>

<script setup>
import { computed } from "vue";
import draggable from "vuedraggable";
import FormSectionContainer from "./FormSectionContainer.vue";
import { useDesignerStore } from "../../stores/designer.js";

const props = defineProps({
  formSchema: {
    type: Object,
    required: true
  },
  selectedFieldId: {
    type: String,
    default: null
  }
});

const emit = defineEmits(["update-schema", "select-field"]);

const designerStore = useDesignerStore();

const sections = computed({
  get: () => props.formSchema?.sections || [],
  set: (val) => {
    emit("update-schema", { ...props.formSchema, sections: val });
    if (designerStore.activeForm) {
      designerStore.activeForm.sections = val;
    }
  }
});

const handleSectionReorder = (evt) => {
  // GxP / Part 11 Audit Trail Hook for reordering events
  console.log("Section reordered:", evt);
};

function onSelectField(fieldId) {
  emit("select-field", fieldId);
  designerStore.setSelectedFieldId(fieldId);
}

function onUpdateSection(updatedSection) {
  const currentSections = [...sections.value];
  const idx = currentSections.findIndex((s) => s.id === updatedSection.id);
  if (idx !== -1) {
    currentSections[idx] = updatedSection;
    sections.value = currentSections;
  }
}
</script>

<style scoped>
.authoring-canvas-stage {
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.02);
}
</style>
