<template>
  <div
    class="canvas-field-widget p-3 rounded-lg border-2 transition-all relative group bg-white shadow-sm"
    :class="[
      isSelected
        ? 'border-indigo-600 ring-2 ring-indigo-100'
        : 'border-gray-200 hover:border-gray-300'
    ]"
    @click="selectField"
  >
    <!-- Field Header / Metadata -->
    <div class="flex justify-between items-start mb-2">
      <div class="flex items-center gap-2">
        <!-- Drag Handle -->
        <span
          class="field-drag-handle cursor-move text-gray-400 hover:text-gray-600 text-lg select-none"
          @click.stop
        >
          ⋮⋮
        </span>
        <span class="font-medium text-gray-800 text-sm">
          {{ field.label || 'Untitled Field' }}
          <span
            v-if="field.required"
            class="text-red-500 font-bold"
          >*</span>
        </span>
      </div>

      <!-- SDTM Tag Badge -->
      <span
        v-if="field.cdash || field.sdtm"
        class="sdtm-tag-badge bg-blue-50 text-blue-700 text-xs font-semibold px-2 py-0.5 rounded border border-blue-100"
      >
        [{{ field.cdash || field.sdtm }}]
      </span>
    </div>

    <!-- Interactive Field Preview -->
    <div
      class="field-preview mt-2"
      @click.stop="selectField"
    >
      <template v-if="field.type === 'text'">
        <input
          type="text"
          disabled
          class="w-full border border-gray-300 rounded-md px-3 py-1.5 text-sm bg-gray-50 text-gray-400 cursor-not-allowed"
          placeholder="Text input preview"
        >
      </template>

      <template v-else-if="field.type === 'numeric'">
        <input
          type="number"
          disabled
          class="w-full border border-gray-300 rounded-md px-3 py-1.5 text-sm bg-gray-50 text-gray-400 cursor-not-allowed"
          placeholder="0.00"
        >
      </template>

      <template v-else-if="field.type === 'date'">
        <input
          type="date"
          disabled
          class="w-full border border-gray-300 rounded-md px-3 py-1.5 text-sm bg-gray-50 text-gray-400 cursor-not-allowed"
        >
      </template>

      <template v-else-if="field.type === 'select'">
        <select
          disabled
          class="w-full border border-gray-300 rounded-md px-3 py-1.5 text-sm bg-gray-50 text-gray-400 cursor-not-allowed"
        >
          <option
            value=""
            disabled
            selected
          >
            -- Select Option --
          </option>
          <option
            v-for="opt in field.options || []"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </option>
        </select>
      </template>

      <template v-else-if="field.type === 'radio'">
        <div class="flex flex-wrap gap-4 items-center">
          <label
            v-for="opt in field.options || [
              { value: 'Y', label: 'Yes' },
              { value: 'N', label: 'No' }
            ]"
            :key="opt.value"
            class="flex items-center gap-1.5 text-sm text-gray-500 cursor-not-allowed"
          >
            <input
              type="radio"
              disabled
              class="text-indigo-600 focus:ring-indigo-500 h-4 w-4 border-gray-300"
            >
            <span>{{ opt.label }}</span>
          </label>
        </div>
      </template>

      <template v-else-if="field.type === 'grid'">
        <div class="border border-gray-200 rounded-md overflow-hidden text-xs">
          <table class="w-full bg-gray-50 text-gray-400 cursor-not-allowed">
            <thead>
              <tr class="bg-gray-100 border-b border-gray-200">
                <th class="p-1.5 text-left border-r border-gray-200">
                  Col 1
                </th>
                <th class="p-1.5 text-left">
                  Col 2
                </th>
              </tr>
            </thead>
            <tbody>
              <tr class="border-b border-gray-200">
                <td class="p-1.5 border-r border-gray-200">
                  -
                </td>
                <td class="p-1.5">
                  -
                </td>
              </tr>
              <tr>
                <td class="p-1.5 border-r border-gray-200">
                  -
                </td>
                <td class="p-1.5">
                  -
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <template v-else-if="field.type === 'file' || field.type === 'file upload'">
        <div
          class="border-2 border-dashed border-gray-300 rounded-md p-4 text-center bg-gray-50 text-xs text-gray-400 cursor-not-allowed flex flex-col items-center justify-center gap-1"
        >
          <span class="text-lg">📁</span>
          <span>Click or drag file to upload</span>
        </div>
      </template>

      <template v-else>
        <div class="text-xs text-gray-400 italic">
          Custom field type preview
        </div>
      </template>
    </div>

    <!-- Selected Actions Toolbar -->
    <div
      v-if="isSelected"
      class="widget-actions absolute -top-4 right-2 flex items-center gap-1 bg-indigo-600 text-white rounded-md shadow px-1.5 py-0.5 z-10 text-xs"
    >
      <button
        class="hover:bg-indigo-700 px-1.5 py-0.5 rounded transition font-medium"
        title="Duplicate Field"
        @click.stop="$emit('duplicate-field', field.id)"
      >
        Duplicate
      </button>
      <span class="text-indigo-400 select-none">|</span>
      <button
        class="hover:bg-indigo-700 px-1.5 py-0.5 rounded transition font-medium"
        title="Inspect Properties"
        @click.stop="$emit('select-field', field.id)"
      >
        Inspect
      </button>
      <span class="text-indigo-400 select-none">|</span>
      <button
        class="hover:bg-red-700 hover:text-white text-red-200 px-1.5 py-0.5 rounded transition font-medium"
        title="Delete Field"
        @click.stop="$emit('delete-field', field.id)"
      >
        Delete
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  field: {
    type: Object,
    required: true
  },
  selectedFieldId: {
    type: String,
    default: null
  }
});

const emit = defineEmits(["select-field", "duplicate-field", "delete-field"]);

const isSelected = computed(() => {
  return props.selectedFieldId === props.field.id;
});

function selectField() {
  emit("select-field", props.field.id);
}
</script>

<style scoped>
.canvas-field-widget {
  min-height: 100px;
}
</style>
