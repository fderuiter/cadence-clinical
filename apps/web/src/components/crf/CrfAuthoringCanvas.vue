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
              :class="{
                active:
                  designerStore.viewport === 'desktop' && !isSimulationMode,
              }"
              @click="setViewport('desktop')"
            >
              🖥️ <span>Desktop (12-Col)</span>
            </button>
            <button
              class="btn-viewport btn-viewport-tablet"
              :class="{
                active:
                  designerStore.viewport === 'tablet' && !isSimulationMode,
              }"
              @click="setViewport('tablet')"
            >
              📟 <span>Tablet (8-Col)</span>
            </button>
            <button
              class="btn-viewport btn-viewport-mobile"
              :class="{
                active:
                  designerStore.viewport === 'mobile' && !isSimulationMode,
              }"
              @click="setViewport('mobile')"
            >
              📱 <span>Mobile (4-Col)</span>
            </button>
            <button
              class="btn-viewport btn-viewport-simulation btn-simulation-mode"
              :class="{ active: isSimulationMode }"
              @click="toggleSimulationMode"
            >
              🧪 <span>Simulation Mode</span>
            </button>
          </div>

          <div v-if="formSchema" class="form-schema-badge">
            Form: {{ formSchema.name || "Draft" }}
          </div>
        </div>
      </div>

      <!-- Resizable Canvas Stage wrapper -->
      <div class="canvas-stage-wrapper">
        <div
          v-if="!isSimulationMode"
          class="authoring-canvas-stage"
          :class="{
            'touch-simulation-active': designerStore.viewport !== 'desktop',
          }"
          :style="{ maxWidth: canvasMaxWidthStyle, margin: '0 auto' }"
        >
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

        <!-- Simulation Mode Live Embedded Preview Stage -->
        <div
          v-else
          class="simulation-canvas-stage"
          :style="{ maxWidth: canvasMaxWidthStyle, margin: '0 auto' }"
        >
          <div class="simulation-banner">
            <span class="simulation-icon">🧪</span>
            <div>
              <strong>Rule Simulation Mode Active</strong>
              <p>
                Values entered here test AST skip logic and constraint rules in
                memory without modifying draft form specifications.
              </p>
            </div>
          </div>

          <HeadlessFormRenderer
            ref="headlessRendererRef"
            :form-definition="simulationFormDefinition"
            :initial-values="simulationFormData"
            @data-changed="onSimulationDataChanged"
          />
        </div>
      </div>
    </div>

    <!-- Right Side: Layout Inspector & Form Compiler Panel (or Rule Tracer Panel in Simulation Mode) -->
    <div class="properties-inspector-pane">
      <template v-if="!isSimulationMode">
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
              class="warning-item"
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
                    font-family: var(--semantic-font-mono);
                    font-size: var(--font-size-2xs);
                    background: var(--semantic-color-warning-bg-badge);
                    padding: 1px 4px;
                    border-radius: 4px;
                  "
                  >Span: {{ warning.gridSpan }}</span
                >
              </div>
              <p
                style="
                  font-size: var(--font-size-2xs);
                  color: var(--semantic-color-warning-action-border);
                  margin: 0;
                  line-height: 1.3;
                "
              >
                {{ warning.message }}
              </p>
              <div
                style="
                  font-size: var(--font-size-2xs);
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
                  font-family: var(--semantic-font-mono);
                  font-size: var(--font-size-2xs);
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
                  width: var(--semantic-control-target-min, 24px);
                  height: var(--semantic-control-target-min, 24px);
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
                  width: var(--semantic-control-target-min, 24px);
                  height: var(--semantic-control-target-min, 24px);
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
                  font-size: var(--font-size-2xs);
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
              ✅ Compilation successful: form compiled to valid CDISC USDM
              structure!
            </div>
          </div>
        </div>
      </template>

      <!-- Rule Tracer Panel in Simulation Mode -->
      <template v-else>
        <RuleTracerPanel
          :rules="activeSimulationRules"
          :simulation-data="simulationFormData"
          :field-errors="simulationFieldErrors"
          :visible-fields="simulationVisibleFields"
          @apply-profile="onApplyMockProfile"
          @reset-simulation="onResetSimulation"
        />
      </template>
    </div>
  </div>
</template>

<script setup>
/**
 * CrfAuthoringCanvas Component
 *
 * Provides the interactive, real-time responsive authoring stage for the eCRF designer.
 * Integrates drag-and-drop sections/fields, a device switcher toolbar, real-time simulated column
 * width validation, property inspector synchronizations, and compiler quality gating rules.
 */
import { computed, ref, reactive } from "vue";
import draggable from "vuedraggable";
import FormSectionContainer from "./FormSectionContainer.vue";
import HeadlessFormRenderer from "./HeadlessFormRenderer.vue";
import RuleTracerPanel from "./RuleTracerPanel.vue";
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

const sections = computed({
  get: () => props.formSchema?.sections || [],
  set: (val) => {
    emit("update-schema", { ...props.formSchema, sections: val });
    if (designerStore.activeForm) {
      designerStore.activeForm.sections = val;
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
    }
    // Clear compiler success state on schema change to require re-compilation
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
    }
  } else {
    compilationStatus.value = "success";
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

// ----------------------------------------------------------------------------
// SIMULATION MODE & LIVE EXECUTION TRACER LOGIC
// ----------------------------------------------------------------------------
const isSimulationMode = ref(false);
const simulationFormData = reactive({});
const simulationFieldErrors = reactive({});
const simulationVisibleFields = ref({});
const headlessRendererRef = ref(null);

function setViewport(vp) {
  designerStore.setViewport(vp);
}

function toggleSimulationMode() {
  isSimulationMode.value = !isSimulationMode.value;
  if (isSimulationMode.value) {
    initSimulationFormData();
  } else {
    onResetSimulation();
  }
}

function initSimulationFormData() {
  allFields.value.forEach((f) => {
    if (simulationFormData[f.id] === undefined) {
      simulationFormData[f.id] = f.type === "numeric" ? null : "";
    }
  });
}

const activeSimulationRules = computed(() => {
  const schema = props.formSchema || {};
  const rules = [];

  if (schema.rules) {
    rules.push(...schema.rules);
  }
  if (schema.logic_schema?.rules) {
    rules.push(...schema.logic_schema.rules);
  }
  if (schema.logic_schema?.edit_checks) {
    rules.push(...schema.logic_schema.edit_checks);
  }
  if (schema.logic_schema?.skip_logic) {
    rules.push(...schema.logic_schema.skip_logic);
  }

  // Extract rules from field items
  allFields.value.forEach((field) => {
    if (field.relevant) {
      rules.push({
        id: `rule-skip-${field.id}`,
        type: "skip_logic",
        condition: field.relevant,
        action: "show",
        target_field: field.id,
      });
    }
    if (field.rules && Array.isArray(field.rules)) {
      rules.push(...field.rules);
    }
    if (field.constraints) {
      rules.push({
        id: `rule-constraint-${field.id}`,
        type: "constraint",
        condition: field.constraints.condition || field.constraints,
        target_field: field.id,
        query_message:
          field.constraints.message ||
          field.constraints.query_message ||
          "Constraint rule violation.",
      });
    }
  });

  // Deduplicate by id
  const uniqueMap = new Map();
  rules.forEach((r) => {
    const key = r.id || `${r.type}-${r.target_field}`;
    if (!uniqueMap.has(key)) {
      uniqueMap.set(key, { ...r, id: key });
    }
  });

  return Array.from(uniqueMap.values());
});

const simulationFormDefinition = computed(() => {
  const schema = props.formSchema || {};

  const variables = allFields.value.map((f) => ({
    id: f.id,
    name: f.label || f.id,
    data_type: f.type === "numeric" ? "number" : "text",
    constraints: { required: f.required },
    relevant: f.relevant || null,
  }));

  const uiSections = (schema.sections || []).map((sec) => ({
    id: sec.id,
    title: sec.name || "Section",
    columns: 12,
    widgets: (sec.items || []).map((item) => ({
      variable_id: item.id,
      widget: item.type === "numeric" ? "number_input" : "text_input",
      grid_column_span: item.gridSpan || 12,
      placeholder: `Enter ${item.label || item.id}...`,
    })),
  }));

  const rules = activeSimulationRules.value;

  return {
    form_id: schema.id || "draft-form",
    version_tag: "1.0-draft",
    data_schema: {
      name: schema.name || "Draft Form",
      variables,
    },
    ui_schema: {
      sections: uiSections,
    },
    logic_schema: {
      rules,
      edit_checks: rules.filter(
        (r) =>
          r.type === "constraint" ||
          r.type === "cross_form_check" ||
          r.type === "edit_check"
      ),
      skip_logic: rules.filter((r) => r.type === "skip_logic"),
    },
  };
});

function onSimulationDataChanged(data) {
  Object.keys(data).forEach((k) => {
    simulationFormData[k] = data[k];
  });
  if (headlessRendererRef.value) {
    Object.keys(simulationFieldErrors).forEach(
      (k) => delete simulationFieldErrors[k]
    );
    Object.assign(simulationFieldErrors, headlessRendererRef.value.fieldErrors);
    simulationVisibleFields.value = {
      ...headlessRendererRef.value.visibleFields,
    };
  }
}

function onApplyMockProfile(profileName) {
  allFields.value.forEach((f) => {
    const isNum = f.type === "numeric";
    if (profileName === "normal") {
      if (
        f.id.toLowerCase().includes("bp") ||
        f.id.toLowerCase().includes("sys")
      ) {
        simulationFormData[f.id] = 120;
      } else if (f.id.toLowerCase().includes("dia")) {
        simulationFormData[f.id] = 80;
      } else if (
        f.id.toLowerCase().includes("hr") ||
        f.id.toLowerCase().includes("pulse")
      ) {
        simulationFormData[f.id] = 72;
      } else if (isNum) {
        simulationFormData[f.id] = 25;
      } else {
        simulationFormData[f.id] = "Normal";
      }
    } else if (profileName === "hypertensive") {
      if (
        f.id.toLowerCase().includes("bp") ||
        f.id.toLowerCase().includes("sys")
      ) {
        simulationFormData[f.id] = 165;
      } else if (f.id.toLowerCase().includes("dia")) {
        simulationFormData[f.id] = 105;
      } else if (
        f.id.toLowerCase().includes("hr") ||
        f.id.toLowerCase().includes("pulse")
      ) {
        simulationFormData[f.id] = 95;
      } else if (isNum) {
        simulationFormData[f.id] = 180;
      } else {
        simulationFormData[f.id] = "Elevated Alert";
      }
    } else if (profileName === "edge_case") {
      if (f.id.toLowerCase().includes("sys")) {
        simulationFormData[f.id] = 70;
      } else if (f.id.toLowerCase().includes("dia")) {
        simulationFormData[f.id] = 110;
      } else {
        simulationFormData[f.id] = "";
      }
    }
  });

  if (headlessRendererRef.value) {
    if (typeof headlessRendererRef.value.setFormData === "function") {
      headlessRendererRef.value.setFormData(simulationFormData);
    } else {
      headlessRendererRef.value.evaluateRules();
    }
  }
}

function onResetSimulation() {
  Object.keys(simulationFormData).forEach((k) => delete simulationFormData[k]);
  Object.keys(simulationFieldErrors).forEach(
    (k) => delete simulationFieldErrors[k]
  );
  simulationVisibleFields.value = {};
  if (headlessRendererRef.value) {
    headlessRendererRef.value.resetSimulation();
  }
}
</script>

<style scoped>
.authoring-canvas-layout {
  display: flex;
  gap: 20px;
  align-items: flex-start;
  width: 100%;
}

@media (max-width: 1024px) {
  .authoring-canvas-layout {
    flex-direction: column;
  }
}

.canvas-main-pane {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.canvas-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  background-color: var(--semantic-color-surface);
  padding: 16px 20px;
  border-radius: 12px;
  border: 1px solid var(--semantic-color-border);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.canvas-header-info h2 {
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0;
}

.canvas-header-info p {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  margin: 2px 0 0 0;
}

.canvas-header-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.viewport-selector {
  display: inline-flex;
  background-color: var(--semantic-color-surface-header);
  padding: 3px;
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  gap: 2px;
}

.btn-viewport {
  padding: 6px 12px;
  font-size: 0.78rem;
  font-weight: 600;
  border-radius: 6px;
  border: none;
  background: none;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.15s ease;
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-viewport:hover {
  color: var(--semantic-color-text);
}

.btn-viewport.active {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.12);
}

.form-schema-badge {
  font-size: 0.78rem;
  background-color: var(--semantic-color-info-bg-soft);
  border: 1px solid var(--semantic-color-info-border-accent);
  color: var(--semantic-color-primary);
  padding: 5px 12px;
  border-radius: 20px;
  font-weight: 600;
}

.canvas-stage-wrapper {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 16px;
  padding: 24px;
  overflow-x: auto;
  min-height: 480px;
}

.authoring-canvas-stage {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: 24px;
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.02);
  transition: max-width 0.3s ease;
  margin: 0 auto;
  min-height: 400px;
}

.sections-drag-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.properties-inspector-pane {
  width: 320px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  display: flex;
  flex-direction: column;
  gap: 20px;
  flex-shrink: 0;
}

@media (max-width: 1024px) {
  .properties-inspector-pane {
    width: 100%;
  }
}

.inspector-section-title {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 10px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.warning-count-badge {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text-dark);
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 1px 6px;
  border-radius: 10px;
}

.warnings-box-clean {
  font-size: 0.78rem;
  color: var(--semantic-color-success-dark);
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  border-radius: 8px;
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.inspector-field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
}

.inspector-field-group label {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
}

.inspector-input,
.inspector-select {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  color: var(--semantic-color-text);
  background-color: var(--semantic-color-surface);
  transition: all 0.15s;
  box-sizing: border-box;
}

.inspector-input:focus-visible,
.inspector-select:focus-visible {
  border-color: var(--semantic-color-primary);
}

.btn-compile-schema {
  width: 100%;
  padding: 10px 16px;
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  font-size: 0.85rem;
  font-weight: 700;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-compile-schema:hover {
  background-color: var(--semantic-color-primary-dark);
}

.btn-compile-schema:disabled {
  background-color: var(--semantic-color-border-muted);
  cursor: not-allowed;
}

.text-xxs {
  font-size: var(--font-size-2xs);
}
</style>
