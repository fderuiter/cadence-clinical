<template>
  <div
    v-keyboard-click="selectField"
    class="canvas-field-widget"
    :class="{
      'is-selected': isSelected,
      'has-warning': hasWarning,
    }"
    @click="selectField"
  >
    <!-- Field Header / Metadata -->
    <div class="field-widget-top">
      <div class="field-widget-info">
        <!-- Drag Handle -->
        <span
          class="field-drag-handle"
          title="Drag to reorder field"
          @click.stop
        >
          ⋮⋮
        </span>
        <span class="field-label-text">
          {{ field.label || "Untitled Field" }}
          <span
            v-if="field.required"
            class="field-required-star"
          >*</span>
        </span>
      </div>

      <!-- SDTM Tag Badge -->
      <span
        v-if="field.cdash || field.sdtm"
        class="sdtm-tag-badge"
      >
        [{{ field.cdash || field.sdtm }}]
      </span>
    </div>

    <!-- Interactive Field Preview -->
    <div
      class="field-preview-container"
      @click.stop="selectField"
    >
      <template v-if="field.type === 'text'">
        <input
          type="text"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
          placeholder="Text input preview..."
        >
      </template>

      <template v-else-if="field.type === 'numeric'">
        <input
          type="number"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
          placeholder="0.00"
        >
      </template>

      <template v-else-if="field.type === 'date'">
        <input
          type="date"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
        >
      </template>

      <template v-else-if="field.type === 'select'">
        <select
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
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
        <div
          style="display: flex; gap: 12px; align-items: center; padding: 4px 0"
        >
          <label
            v-for="opt in field.options || [
              { value: 'Y', label: 'Yes' },
              { value: 'N', label: 'No' },
            ]"
            :key="opt.value"
            style="
              display: flex;
              align-items: center;
              gap: 6px;
              font-size: 0.8rem;
              color: #475569;
              cursor: pointer;
            "
            :class="{ 'touch-target': designerStore.viewport !== 'desktop' }"
          >
            <input
              type="radio"
              disabled
              style="cursor: pointer; accent-color: #026597"
              :class="{
                'touch-target-interactive':
                  designerStore.viewport !== 'desktop',
              }"
            >
            <span>{{ opt.label }}</span>
          </label>
        </div>
      </template>

      <template v-else-if="field.type === 'grid'">
        <div
          style="
            border: 1px solid #e2e8f0;
            border-radius: 6px;
            overflow: hidden;
            font-size: 0.75rem;
          "
        >
          <table
            style="width: 100%; background: #f8fafc; border-collapse: collapse"
          >
            <thead>
              <tr style="background: #f1f5f9; border-bottom: 1px solid #e2e8f0">
                <th
                  style="
                    padding: 4px 8px;
                    text-align: left;
                    border-right: 1px solid #e2e8f0;
                  "
                >
                  Column 1
                </th>
                <th style="padding: 4px 8px; text-align: left">
                  Column 2
                </th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td
                  style="
                    padding: 4px 8px;
                    border-right: 1px solid #e2e8f0;
                    color: #94a3b8;
                  "
                >
                  Data item A
                </td>
                <td style="padding: 4px 8px; color: #94a3b8">
                  Data item B
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>

      <template
        v-else-if="field.type === 'file' || field.type === 'file upload'"
      >
        <div
          style="
            border: 2px dashed #cbd5e1;
            border-radius: 6px;
            padding: 12px;
            text-align: center;
            background: #f8fafc;
            font-size: 0.75rem;
            color: #64748b;
          "
        >
          <span>📁 Attachment Upload Area</span>
        </div>
      </template>

      <template v-else>
        <div style="font-size: 0.75rem; color: #94a3b8; font-style: italic">
          Standard clinical field preview
        </div>
      </template>
    </div>

    <!-- Selected Actions Toolbar -->
    <div
      v-if="isSelected"
      class="widget-actions"
      style="
        position: absolute;
        top: -12px;
        right: 8px;
        display: flex;
        gap: 4px;
        background-color: #026597;
        color: white;
        border-radius: 6px;
        padding: 2px 6px;
        z-index: 10;
        font-size: 0.72rem;
        box-shadow: 0 2px 6px rgba(0, 0, 0, 0.15);
      "
    >
      <button
        style="
          background: none;
          border: none;
          color: white;
          cursor: pointer;
          font-weight: 600;
          padding: 2px 4px;
        "
        title="Duplicate Field"
        @click.stop="$emit('duplicate-field', field.id)"
      >
        Duplicate
      </button>
      <span style="color: #93c5fd; user-select: none">|</span>
      <button
        style="
          background: none;
          border: none;
          color: white;
          cursor: pointer;
          font-weight: 600;
          padding: 2px 4px;
        "
        title="Inspect Properties"
        @click.stop="$emit('select-field', field.id)"
      >
        Inspect
      </button>
      <span style="color: #93c5fd; user-select: none">|</span>
      <button
        style="
          background: none;
          border: none;
          color: #fca5a5;
          cursor: pointer;
          font-weight: 600;
          padding: 2px 4px;
        "
        title="Delete Field"
        @click.stop="$emit('delete-field', field.id)"
      >
        Delete
      </button>
    </div>

    <!-- Layout Warning Banner -->
    <div
      v-if="hasWarning"
      style="
        background-color: #fefce8;
        border: 1px solid #fef08a;
        color: #854d0e;
        font-size: 0.72rem;
        border-radius: 6px;
        padding: 6px 8px;
        margin-top: 8px;
        display: flex;
        align-items: flex-start;
        gap: 6px;
      "
    >
      <span>⚠️</span>
      <span style="font-weight: 600">{{ warningMessage }}</span>
    </div>
  </div>
</template>

<script setup>
/**
 * CanvasFieldWidget component
 *
 * Represents an individual interactive field widget on the eCRF designer canvas.
 * Handles selection, duplication, deletion, property inspection, and live responsive width alerts.
 */
import { computed } from "vue";
import { useDesignerStore } from "../../stores/designer.js";
import { vKeyboardClick } from "../../directives/keyboardClick.js";

const designerStore = useDesignerStore();

const props = defineProps({
  field: {
    type: Object,
    required: true,
  },
  selectedFieldId: {
    type: String,
    default: null,
  },
  hasWarning: {
    type: Boolean,
    default: false,
  },
  warningMessage: {
    type: String,
    default: "",
  },
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
