<template>
  <div
    v-keyboard-click="selectField"
    tabindex="0"
    role="button"
    class="canvas-field-widget"
    :class="{
      'is-selected': isSelected,
      'has-warning': hasWarning,
      'remote-field-active': remoteEditors.length > 0,
    }"
    @click="selectField"
    @keydown.enter.prevent="selectField"
    @keydown.space.prevent="selectField"
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
        <!-- Field Reorder Controls -->
        <div class="field-reorder-controls" @click.stop>
          <button
            class="btn-move-up btn-field-move-up"
            :disabled="isFirst"
            title="Move field up"
            aria-label="Move field up"
            @click.stop="$emit('move-field-up')"
          >
            ▲
          </button>
          <button
            class="btn-move-down btn-field-move-down"
            :disabled="isLast"
            title="Move field down"
            aria-label="Move field down"
            @click.stop="$emit('move-field-down')"
          >
            ▼
          </button>
        </div>
        <span class="field-label-text">
          {{ field.label || "Untitled Field" }}
          <span v-if="field.required" class="field-required-star">*</span>
        </span>
      </div>

      <!-- SDTM Tag Badge -->
      <span v-if="field.cdash || field.sdtm" class="sdtm-tag-badge">
        [{{ field.cdash || field.sdtm }}]
      </span>
    </div>

    <!-- Interactive Field Preview -->
    <div class="field-preview-container">
      <template v-if="field.type === 'text'">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          type="text"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
          placeholder="Text input preview..."
        />
      </template>

      <template v-else-if="field.type === 'numeric'">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          type="number"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
          placeholder="0.00"
        />
      </template>

      <template v-else-if="field.type === 'date'">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          type="date"
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
        />
      </template>

      <template v-else-if="field.type === 'select'">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <select
          disabled
          class="field-preview-input"
          :class="{
            'touch-target-interactive': designerStore.viewport !== 'desktop',
          }"
        >
          <option value="" disabled selected>-- Select Option --</option>
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
              color: var(--semantic-color-text-muted);
              cursor: pointer;
            "
            :class="{ 'touch-target': designerStore.viewport !== 'desktop' }"
          >
            <input
              type="radio"
              disabled
              style="
                cursor: pointer;
                accent-color: var(--semantic-color-interactive-brand);
              "
              :class="{
                'touch-target-interactive':
                  designerStore.viewport !== 'desktop',
              }"
            />
            <span>{{ opt.label }}</span>
          </label>
        </div>
      </template>

      <template v-else-if="field.type === 'grid'">
        <div
          style="
            border: 1px solid var(--semantic-color-border);
            border-radius: 6px;
            overflow: hidden;
            font-size: 0.75rem;
          "
        >
          <table
            style="
              width: 100%;
              background: var(--semantic-color-surface-muted);
              border-collapse: collapse;
            "
          >
            <thead>
              <tr
                style="
                  background: var(--semantic-color-surface-header);
                  border-bottom: 1px solid var(--semantic-color-border);
                "
              >
                <th
                  style="
                    padding: 4px 8px;
                    text-align: left;
                    border-right: 1px solid var(--semantic-color-border);
                  "
                >
                  Column 1
                </th>
                <th style="padding: 4px 8px; text-align: left">Column 2</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td
                  style="
                    padding: 4px 8px;
                    border-right: 1px solid var(--semantic-color-border);
                    color: var(--semantic-color-text-subtle);
                  "
                >
                  Data item A
                </td>
                <td
                  style="
                    padding: 4px 8px;
                    color: var(--semantic-color-text-subtle);
                  "
                >
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
            border: 2px dashed var(--semantic-color-border-muted);
            border-radius: 6px;
            padding: 12px;
            text-align: center;
            background: var(--semantic-color-surface-muted);
            font-size: 0.75rem;
            color: var(--semantic-color-text-secondary);
          "
        >
          <span>📁 Attachment Upload Area</span>
        </div>
      </template>

      <template v-else>
        <div
          style="
            font-size: 0.75rem;
            color: var(--semantic-color-text-subtle);
            font-style: italic;
          "
        >
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
        background-color: var(--semantic-color-interactive-brand);
        color: var(--semantic-color-surface);
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
          color: var(--semantic-color-surface);
          cursor: pointer;
          font-weight: 600;
          padding: 2px 4px;
        "
        title="Duplicate Field"
        @click.stop="$emit('duplicate-field', field.id)"
      >
        Duplicate
      </button>
      <span
        style="color: var(--semantic-color-divider-accent); user-select: none"
        >|</span
      >
      <button
        style="
          background: none;
          border: none;
          color: var(--semantic-color-surface);
          cursor: pointer;
          font-weight: 600;
          padding: 2px 4px;
        "
        title="Inspect Properties"
        @click.stop="$emit('select-field', field.id)"
      >
        Inspect
      </button>
      <span
        style="color: var(--semantic-color-divider-accent); user-select: none"
        >|</span
      >
      <button
        style="
          background: none;
          border: none;
          color: var(--semantic-color-error-border);
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

    <!-- Remote Co-Author Focus Badges -->
    <div v-if="remoteEditors.length > 0" class="remote-editors-bar">
      <span
        v-for="editor in remoteEditors"
        :key="editor.userId || editor.userName"
        class="remote-editor-badge"
        :style="{ backgroundColor: editor.color || '#2563EB' }"
      >
        ✏️ {{ editor.userName }} ({{ editor.userRole }})
      </span>
    </div>

    <!-- Layout Warning Banner -->
    <div
      v-if="hasWarning"
      style="
        background-color: var(--semantic-color-warning-bg-light);
        border: 1px solid var(--semantic-color-warning-border);
        color: var(--semantic-color-warning-text);
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
  fieldIndex: {
    type: Number,
    default: 0,
  },
  totalFields: {
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
  "duplicate-field",
  "delete-field",
  "move-field-up",
  "move-field-down",
]);

const isSelected = computed(() => {
  return props.selectedFieldId === props.field.id;
});

const remoteEditors = computed(() => {
  const users = designerStore.activeUsers || {};
  return Object.values(users).filter(
    (u) => u.selectedFieldId === props.field.id
  );
});

function selectField() {
  emit("select-field", props.field.id);
}
</script>

<style scoped>
.canvas-field-widget {
  min-height: 100px;
}

.remote-field-active {
  outline: 2px dashed #2563eb !important;
  outline-offset: 3px;
  box-shadow: 0 0 10px rgba(37, 99, 235, 0.35) !important;
}

.remote-editors-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
}

.remote-editor-badge {
  font-size: 0.7rem;
  font-weight: 700;
  color: #ffffff;
  padding: 3px 8px;
  border-radius: 4px;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.15);
}
</style>
