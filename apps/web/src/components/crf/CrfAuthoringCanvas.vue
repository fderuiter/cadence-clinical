<template>
  <div class="authoring-canvas-layout">
    <!-- Left/Center Side: Resizable Canvas Area -->
    <div class="canvas-main-pane">
      <div class="canvas-header">
        <div class="canvas-header-info">
          <h2>eCRF Authoring Canvas &amp; Responsive Form Builder</h2>
          <p>
            Drag sections to reorder, add field widgets, configure CDASH
            bindings &amp; test responsive viewports.
          </p>
        </div>

        <div class="canvas-header-controls">
          <!-- Viewport Selector Buttons -->
          <div class="viewport-selector">
            <button
              class="btn-viewport btn-viewport-desktop"
              :class="{ active: designerStore.viewport === 'desktop' }"
              @click="designerStore.setViewport('desktop')"
            >
              🖥️ <span>Desktop (12-Col)</span>
            </button>
            <button
              class="btn-viewport btn-viewport-tablet"
              :class="{ active: designerStore.viewport === 'tablet' }"
              @click="designerStore.setViewport('tablet')"
            >
              📟 <span>Tablet (8-Col)</span>
            </button>
            <button
              class="btn-viewport btn-viewport-mobile"
              :class="{ active: designerStore.viewport === 'mobile' }"
              @click="designerStore.setViewport('mobile')"
            >
              📱 <span>Mobile (4-Col)</span>
            </button>
          </div>

          <!-- Connection & Real-Time Sync Status Badge -->
          <div
            class="connection-sync-badge"
            :class="designerStore.connectionState"
            style="
              display: inline-flex;
              align-items: center;
              gap: 6px;
              font-size: 0.75rem;
              font-weight: 700;
              padding: 4px 10px;
              border-radius: 12px;
              background-color: var(--semantic-color-surface-muted);
              border: 1px solid var(--semantic-color-border);
            "
          >
            <span v-if="designerStore.connectionState === 'connected'">🟢 Connected (CRDT Sync Active)</span>
            <span v-else-if="designerStore.connectionState === 'reconnecting'">🟡 Reconnecting...</span>
            <span v-else-if="designerStore.connectionState === 'connecting'">🔵 Connecting...</span>
            <span v-else>🔴 Offline (Local Edits Saved)</span>
          </div>

          <!-- Active Co-Authors Role Badges -->
          <div
            v-if="Object.keys(activeCoAuthors).length > 0"
            class="active-coauthors-list"
            style="display: flex; gap: 6px; align-items: center"
          >
            <span
              v-for="(user, uid) in activeCoAuthors"
              :key="uid"
              class="coauthor-role-badge"
              :style="{ backgroundColor: user.color || '#2563EB' }"
              style="
                color: #ffffff;
                font-size: 0.72rem;
                font-weight: 700;
                padding: 2px 8px;
                border-radius: 10px;
                display: inline-flex;
                align-items: center;
                gap: 4px;
              "
            >
              👤 {{ user.userName }} ({{ user.userRole }})
            </span>
          </div>

          <div v-if="formSchema" class="form-schema-badge">
            Form: {{ formSchema.name || "Draft" }}
          </div>
        </div>
      </div>

      <!-- Resizable Canvas Stage wrapper -->
      <div class="canvas-stage-wrapper">
        <div
          ref="canvasStageRef"
          class="authoring-canvas-stage"
          :class="{
            'touch-simulation-active': designerStore.viewport !== 'desktop',
          }"
          :style="{ maxWidth: canvasMaxWidthStyle, margin: '0 auto', position: 'relative' }"
          @mousemove="handleCanvasMouseMove"
        >
          <!-- Remote Co-Author Cursors Overlay -->
          <div
            v-for="(user, userId) in activeCoAuthorsWithCursor"
            :key="userId"
            class="remote-cursor-badge"
            :style="{
              left: (user.cursorX || 0) + 'px',
              top: (user.cursorY || 0) + 'px',
              borderColor: user.color || '#2563EB',
            }"
          >
            <span class="remote-cursor-pointer" :style="{ color: user.color || '#2563EB' }">↖</span>
            <span class="remote-cursor-label" :style="{ backgroundColor: user.color || '#2563EB' }">
              {{ user.userName }} ({{ user.userRole }})
            </span>
          </div>

          <draggable
            v-model="sections"
            item-key="id"
            handle=".section-drag-handle"
            :force-fallback="true"
            :fallback-tolerance="5"
            scroll-sensitivity="30"
            class="sections-drag-list"
            @change="handleSectionReorder"
          >
            <template #item="{ element: section, index }">
              <FormSectionContainer
                :section="section"
                :selected-field-id="selectedFieldId"
                :section-index="index"
                :total-sections="sections.length"
                :is-first="index === 0"
                :is-last="index === sections.length - 1"
                @select-field="onSelectField"
                @update-section="onUpdateSection"
                @move-section-up="moveSectionUp(index)"
                @move-section-down="moveSectionDown(index)"
              />
            </template>
          </draggable>
        </div>
      </div>
    </div>

    <!-- Right Side: Layout Inspector & Form Compiler Panel -->
    <div class="properties-inspector-pane">
      <!-- 1. Viewport Warnings Section -->
      <div class="warnings-section-card warnings-section">
        <div class="inspector-section-title">
          <span>Viewport Warnings</span>
          <span class="warning-count-badge warning-count">
            {{ layoutWarnings.length }}
          </span>
        </div>

        <div v-if="layoutWarnings.length === 0" class="warnings-box-clean">
          <span>✅</span> No layout warnings for this viewport.
        </div>

        <div
          v-else
          style="
            display: flex;
            flex-direction: column;
            gap: 8px;
            max-height: 200px;
            overflow-y: auto;
          "
        >
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="warning in layoutWarnings"
            :key="warning.fieldId"
            style="
              border: 1px solid var(--semantic-color-warning-border);
              background-color: var(--semantic-color-warning-bg-light);
              border-radius: 6px;
              padding: 8px;
              cursor: pointer;
            "
            @click="onSelectField(warning.fieldId)"
          >
            <div
              style="
                display: flex;
                justify-content: space-between;
                font-weight: 700;
                font-size: 0.75rem;
                color: var(--semantic-color-warning-text);
                margin-bottom: 2px;
              "
            >
              <span>{{ warning.label }}</span>
              <span
                style="
                  font-family: monospace;
                  font-size: 0.7rem;
                  background: var(--semantic-color-warning-bg-badge);
                  padding: 1px 4px;
                  border-radius: 4px;
                "
                >Span: {{ warning.gridSpan }}</span
              >
            </div>
            <p
              style="
                font-size: 0.72rem;
                color: var(--semantic-color-warning-action-border);
                margin: 0;
                line-height: 1.3;
              "
            >
              {{ warning.message }}
            </p>
            <div
              style="
                font-size: 0.68rem;
                color: var(--semantic-color-text-subtle);
                margin-top: 4px;
                font-style: italic;
              "
            >
              In: {{ warning.sectionName }}
            </div>
          </div>
        </div>
      </div>

      <!-- 2. Properties Inspector Section -->
      <div
        style="
          border-top: 1px solid var(--semantic-color-border);
          padding-top: 16px;
        "
      >
        <div class="inspector-section-title">Properties Inspector</div>

        <div
          v-if="!selectedField"
          style="
            font-size: 0.78rem;
            color: var(--semantic-color-text-secondary);
            font-style: italic;
            background-color: var(--semantic-color-surface-muted);
            border: 1px solid var(--semantic-color-border);
            border-radius: 8px;
            padding: 12px;
            text-align: center;
          "
        >
          Select a field widget on the canvas to inspect and edit its
          attributes.
        </div>

        <div
          v-else
          style="
            display: flex;
            flex-direction: column;
            gap: 12px;
            background-color: var(--semantic-color-surface-muted);
            border: 1px solid var(--semantic-color-border);
            border-radius: 8px;
            padding: 14px;
          "
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              border-bottom: 1px solid var(--semantic-color-border);
              padding-bottom: 8px;
            "
          >
            <span
              style="
                font-weight: 700;
                font-size: 0.85rem;
                color: var(--semantic-color-text-dark);
              "
              >{{ selectedField.label }}</span
            >
            <span
              style="
                font-family: monospace;
                font-size: 0.72rem;
                background-color: var(--semantic-color-border);
                padding: 2px 6px;
                border-radius: 4px;
                color: var(--semantic-color-text-muted);
              "
              >{{ selectedField.id }}</span
            >
          </div>

          <div class="inspector-field-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Field Label</label>
            <input
              id="inspect-field-label"
              v-model="selectedFieldLabel"
              type="text"
              class="inspector-input"
            />
          </div>

          <div class="inspector-field-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Grid Span (1-12 Columns)</label>
            <select
              id="inspect-field-span"
              v-model="selectedFieldGridSpan"
              class="inspector-select"
            >
              <option v-for="n in 12" :key="n" :value="n">
                {{ n }}
                {{
                  n === 12
                    ? "Columns (Full Width)"
                    : n === 6
                      ? "Columns (Half Width)"
                      : "Columns"
                }}
              </option>
            </select>
          </div>

          <div class="inspector-field-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Field Type</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select v-model="selectedFieldType" class="inspector-select">
              <option value="text">Text Input</option>
              <option value="numeric">Numeric Input</option>
              <option value="date">Date Picker</option>
              <option value="select">Dropdown Select</option>
              <option value="radio">Radio Buttons</option>
              <option value="grid">Grid Layout</option>
              <option value="file">File Upload</option>
            </select>
          </div>

          <div
            style="
              display: flex;
              align-items: center;
              gap: 8px;
              margin-top: 4px;
            "
          >
            <input
              id="inspect-field-required"
              v-model="selectedFieldRequired"
              type="checkbox"
              style="
                width: 16px;
                height: 16px;
                cursor: pointer;
                accent-color: var(--semantic-color-interactive-brand);
              "
            />
            <label
              for="inspect-field-required"
              style="
                font-size: 0.8rem;
                font-weight: 600;
                color: var(--semantic-color-text-body);
                cursor: pointer;
              "
            >
              Required Field
            </label>
          </div>
        </div>
      </div>

      <!-- 3. Form Compiler Section -->
      <div
        style="
          border-top: 1px solid var(--semantic-color-border);
          padding-top: 16px;
        "
      >
        <div class="inspector-section-title">Form Compiler</div>

        <div
          style="
            display: flex;
            flex-direction: column;
            gap: 12px;
            background-color: var(--semantic-color-surface-muted);
            border: 1px solid var(--semantic-color-border);
            border-radius: 8px;
            padding: 14px;
          "
        >
          <div style="display: flex; align-items: center; gap: 8px">
            <input
              id="dismiss-warnings-checkbox"
              v-model="dismissedWarnings"
              type="checkbox"
              style="
                width: 16px;
                height: 16px;
                cursor: pointer;
                accent-color: var(--semantic-color-interactive-brand);
              "
            />
            <label
              for="dismiss-warnings-checkbox"
              style="
                font-size: 0.8rem;
                font-weight: 600;
                color: var(--semantic-color-text-body);
                cursor: pointer;
              "
            >
              Dismiss layout warnings
            </label>
          </div>

          <div
            v-if="dismissedWarnings && layoutWarnings.length > 0"
            class="layout-justification-box"
          >
            <label
              for="layout-justification-input"
              style="
                font-size: 0.72rem;
                font-weight: 700;
                color: var(--semantic-color-text-secondary);
                text-transform: uppercase;
                display: block;
                margin-bottom: 4px;
              "
            >
              Clinical Justification (Required)
            </label>
            <textarea
              id="layout-justification-input"
              v-model="layoutJustification"
              placeholder="Provide a clinical justification for this layout deviation..."
              style="
                width: 100%;
                font-size: 0.8rem;
                padding: 8px;
                border: 1px solid var(--semantic-color-border-muted);
                border-radius: 6px;
                box-sizing: border-box;
              "
              rows="2"
            />
          </div>

          <button class="btn-compile-schema btn-compile" @click="compileForm">
            Compile Form Schema
          </button>

          <!-- Compilation Feedback messages -->
          <div
            v-if="compilationStatus === 'blocked'"
            class="compilation-error"
            style="
              color: var(--semantic-color-error);
              background-color: var(--semantic-color-error-bg);
              border: 1px solid var(--semantic-color-error-border);
              padding: 10px;
              border-radius: 6px;
              font-size: 0.78rem;
              font-weight: 600;
            "
          >
            ❌ Compilation blocked: active layout warnings must be resolved or
            explicitly dismissed!
          </div>
          <div
            v-if="compilationStatus === 'success'"
            class="compilation-success"
            style="
              color: var(--semantic-color-success);
              background-color: var(--semantic-color-success-bg);
              border: 1px solid var(--semantic-color-success-border);
              padding: 10px;
              border-radius: 6px;
              font-size: 0.78rem;
              font-weight: 600;
            "
          >
            ✅ Form compiled successfully to valid CDISC USDM structure!
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
/**
 * CrfAuthoringCanvas Component
 *
 * Provides the interactive, real-time responsive authoring stage for the eCRF designer.
 * Integrates drag-and-drop sections/fields, a device switcher toolbar, real-time simulated column
 * width validation, property inspector synchronizations, compiler quality gating rules,
 * and decentralized CRDT document synchronization with live co-author presence tracking.
 */
import { computed, ref, onMounted } from "vue";
import draggable from "vuedraggable";
import FormSectionContainer from "./FormSectionContainer.vue";
import { useDesignerStore } from "../../stores/designer.js";

const props = defineProps({
  formSchema: {
    type: Object,
    required: true,
  },
  selectedFieldId: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["update-schema", "select-field"]);

const designerStore = useDesignerStore();
const compilationStatus = ref(null);
const canvasStageRef = ref(null);

onMounted(() => {
  designerStore.initCollaboration(props.formSchema?.id);
});

const activeCoAuthors = computed(() => designerStore.activeUsers || {});

const activeCoAuthorsWithCursor = computed(() => {
  const users = designerStore.activeUsers || {};
  const res = {};
  for (const [id, user] of Object.entries(users)) {
    if (user.cursorX !== null && user.cursorX !== undefined) {
      res[id] = user;
    }
  }
  return res;
});

function handleCanvasMouseMove(evt) {
  if (!canvasStageRef.value) return;
  const rect = canvasStageRef.value.getBoundingClientRect();
  const x = evt.clientX - rect.left;
  const y = evt.clientY - rect.top;
  designerStore.sendCursorPosition(x, y);
}

const sections = computed({
  get: () => props.formSchema?.sections || [],
  set: (val) => {
    emit("update-schema", { ...props.formSchema, sections: val });
    if (designerStore.activeForm) {
      designerStore.activeForm.sections = val;
      designerStore.syncToCrdt();
    }
  },
});

const canvasMaxWidthStyle = computed(() => {
  const vp = designerStore.viewport || "desktop";
  if (vp === "mobile") return "480px";
  if (vp === "tablet") return "768px";
  return "100%";
});

const allFields = computed(() => {
  const fields = [];
  sections.value.forEach((section) => {
    if (section.items) {
      section.items.forEach((item) => {
        fields.push({
          ...item,
          sectionId: section.id,
          sectionName: section.name,
        });
      });
    }
  });
  return fields;
});

const viewportWidth = computed(() => {
  const vp = designerStore.viewport || "desktop";
  if (vp === "mobile") return 480;
  if (vp === "tablet") return 768;
  return 1200;
});

const layoutWarnings = computed(() => {
  const w = viewportWidth.value;
  const warnings = [];
  allFields.value.forEach((field) => {
    const span = parseInt(field.gridSpan) || 12;
    const simWidth = (w / 12) * span;
    if (simWidth < 150) {
      warnings.push({
        fieldId: field.id,
        label: field.label || "Untitled Field",
        gridSpan: span,
        simWidth: Math.round(simWidth),
        message: `Label may clip/overlap: column width is ${Math.round(simWidth)}px (< 150px)`,
        sectionName: field.sectionName,
      });
    }
  });
  return warnings;
});

const selectedField = computed(() => {
  if (!props.selectedFieldId) return null;
  return allFields.value.find((f) => f.id === props.selectedFieldId) || null;
});

function updateSelectedFieldProp(propName, value) {
  if (!props.selectedFieldId || !props.formSchema) return;
  const fieldId = props.selectedFieldId;
  const updatedSections = JSON.parse(JSON.stringify(props.formSchema.sections));

  let found = false;
  for (const s of updatedSections) {
    if (s.items) {
      const idx = s.items.findIndex((item) => item.id === fieldId);
      if (idx !== -1) {
        s.items[idx][propName] = value;
        found = true;
        break;
      }
    }
  }

  if (found) {
    emit("update-schema", { ...props.formSchema, sections: updatedSections });
    if (designerStore.activeForm) {
      designerStore.activeForm.sections = updatedSections;
      designerStore.syncToCrdt();
    }
    compilationStatus.value = null;
  }
}

const selectedFieldLabel = computed({
  get: () => selectedField.value?.label || "",
  set: (val) => updateSelectedFieldProp("label", val),
});

const selectedFieldGridSpan = computed({
  get: () => selectedField.value?.gridSpan || 12,
  set: (val) => updateSelectedFieldProp("gridSpan", parseInt(val)),
});

const selectedFieldType = computed({
  get: () => selectedField.value?.type || "text",
  set: (val) => updateSelectedFieldProp("type", val),
});

const selectedFieldRequired = computed({
  get: () => selectedField.value?.required || false,
  set: (val) => updateSelectedFieldProp("required", val),
});

const dismissedWarnings = computed({
  get: () => designerStore.dismissedWarnings,
  set: (val) => {
    designerStore.setDismissedWarnings(val);
    if (val) {
      if (!layoutJustification.value || !layoutJustification.value.trim()) {
        layoutJustification.value =
          "Clinical layout deviation authorized by form designer.";
      }
    } else {
      layoutJustification.value = "";
    }
    if (
      compilationStatus.value === "blocked" &&
      val &&
      layoutJustification.value.trim()
    ) {
      compilationStatus.value = null;
    }
  },
});

const layoutJustification = computed({
  get: () => designerStore.activeForm?.layoutJustification || "",
  set: (val) => {
    designerStore.setLayoutJustification(val);
    if (compilationStatus.value === "blocked" && val.trim()) {
      compilationStatus.value = null;
    }
  },
});

function compileForm() {
  if (layoutWarnings.value.length > 0) {
    if (
      !dismissedWarnings.value ||
      !layoutJustification.value ||
      !layoutJustification.value.trim()
    ) {
      compilationStatus.value = "blocked";
    } else {
      compilationStatus.value = "success";
      designerStore.saveSnapshotToServer();
    }
  } else {
    compilationStatus.value = "success";
    designerStore.saveSnapshotToServer();
  }
}

const handleSectionReorder = (evt) => {
  console.log("Section reordered:", evt);
  compilationStatus.value = null;
};

function moveSectionUp(index) {
  if (index <= 0) return;
  const updated = [...sections.value];
  const temp = updated[index];
  updated[index] = updated[index - 1];
  updated[index - 1] = temp;
  sections.value = updated;
  compilationStatus.value = null;
}

function moveSectionDown(index) {
  if (index < 0 || index >= sections.value.length - 1) return;
  const updated = [...sections.value];
  const temp = updated[index];
  updated[index] = updated[index + 1];
  updated[index + 1] = temp;
  sections.value = updated;
  compilationStatus.value = null;
}

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
    compilationStatus.value = null;
  }
}
</script>

<style scoped>
.authoring-canvas-stage {
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.02);
}

.text-xxs {
  font-size: 0.65rem;
}

.remote-cursor-badge {
  position: absolute;
  pointer-events: none;
  z-index: 1000;
  display: flex;
  align-items: center;
  gap: 3px;
  transition: left 0.05s ease-out, top 0.05s ease-out;
}

.remote-cursor-pointer {
  font-size: 1.2rem;
  font-weight: bold;
  line-height: 1;
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.4);
}

.remote-cursor-label {
  color: #ffffff;
  font-size: 0.68rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  white-space: nowrap;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}
</style>
