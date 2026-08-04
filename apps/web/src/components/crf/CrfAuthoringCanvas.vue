<template>
  <div
    id="primary-design-canvas"
    tabindex="-1"
    class="authoring-canvas-layout flex flex-col lg:flex-row gap-6 focus:outline-none"
  >
    <!-- Left/Center Side: Resizable Canvas Area -->
    <div class="flex-1 flex flex-col gap-4">
      <div
        class="canvas-header flex flex-col sm:flex-row sm:justify-between sm:items-center gap-4 bg-white p-4 rounded-xl border border-slate-200"
      >
        <div>
          <h2 class="text-xl font-bold text-slate-800">
            eCRF Authoring Canvas
          </h2>
          <p class="text-xs text-slate-500">
            Drag sections to reorder, drag fields to layout & configure
            responsive columns.
          </p>
        </div>

        <div class="flex items-center gap-4 flex-wrap">
          <!-- Viewport Selector Buttons -->
          <div
            role="tablist"
            aria-label="Viewport Switcher"
            class="viewport-selector flex bg-slate-100 p-1 rounded-lg border border-slate-200"
          >
            <button
              role="tab"
              :aria-selected="
                designerStore.viewport === 'desktop' ? 'true' : 'false'
              "
              :tabindex="designerStore.viewport === 'desktop' ? 0 : -1"
              class="btn-viewport-desktop px-3 py-1.5 text-xs font-semibold rounded-md transition-all flex items-center gap-1"
              :class="
                designerStore.viewport === 'desktop'
                  ? 'bg-indigo-600 text-white shadow-sm'
                  : 'text-slate-600 hover:text-slate-800'
              "
              @click="designerStore.setViewport('desktop')"
              @keydown="(e) => handleViewportTabKeydown(e, 'desktop')"
            >
              🖥️ <span>Desktop</span>
            </button>
            <button
              role="tab"
              :aria-selected="
                designerStore.viewport === 'tablet' ? 'true' : 'false'
              "
              :tabindex="designerStore.viewport === 'tablet' ? 0 : -1"
              class="btn-viewport-tablet px-3 py-1.5 text-xs font-semibold rounded-md transition-all flex items-center gap-1"
              :class="
                designerStore.viewport === 'tablet'
                  ? 'bg-indigo-600 text-white shadow-sm'
                  : 'text-slate-600 hover:text-slate-800'
              "
              @click="designerStore.setViewport('tablet')"
              @keydown="(e) => handleViewportTabKeydown(e, 'tablet')"
            >
              📟 <span>Tablet</span>
            </button>
            <button
              role="tab"
              :aria-selected="
                designerStore.viewport === 'mobile' ? 'true' : 'false'
              "
              :tabindex="designerStore.viewport === 'mobile' ? 0 : -1"
              class="btn-viewport-mobile px-3 py-1.5 text-xs font-semibold rounded-md transition-all flex items-center gap-1"
              :class="
                designerStore.viewport === 'mobile'
                  ? 'bg-indigo-600 text-white shadow-sm'
                  : 'text-slate-600 hover:text-slate-800'
              "
              @click="designerStore.setViewport('mobile')"
              @keydown="(e) => handleViewportTabKeydown(e, 'mobile')"
            >
              📱 <span>Mobile</span>
            </button>
          </div>

          <div
            v-if="formSchema"
            class="text-xs bg-indigo-50 border border-indigo-100 px-3 py-1.5 rounded-full text-indigo-700 font-medium"
          >
            Form: {{ formSchema.name || "Draft" }}
          </div>
        </div>
      </div>

      <!-- Resizable Canvas Stage wrapper -->
      <div
        class="canvas-stage-wrapper bg-slate-50 p-4 rounded-2xl border border-slate-200 overflow-x-auto"
      >
        <div
          class="authoring-canvas-stage p-6 bg-white rounded-xl border border-slate-200 min-h-[400px] transition-all duration-300"
          :class="{
            'touch-simulation-active': designerStore.viewport !== 'desktop',
          }"
          :style="{ maxWidth: canvasMaxWidthStyle, margin: '0 auto' }"
        >
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
      </div>
    </div>

    <!-- Right Side: Layout Inspector & Form Compiler Panel -->
    <div
      class="w-full lg:w-80 bg-white border border-slate-200 rounded-2xl p-5 shadow-sm flex flex-col gap-6 flex-shrink-0"
    >
      <!-- 1. Viewport Warnings Section -->
      <div class="warnings-section">
        <h3
          class="text-xs font-bold uppercase tracking-wider text-slate-400 mb-3 flex justify-between items-center"
        >
          <span>Viewport Warnings</span>
          <span
            class="warning-count bg-amber-100 text-amber-800 text-xxs font-bold px-2 py-0.5 rounded-full"
          >
            {{ layoutWarnings.length }}
          </span>
        </h3>

        <div
          v-if="layoutWarnings.length === 0"
          class="text-xs text-emerald-600 bg-emerald-50 border border-emerald-100 rounded-lg p-3 flex items-center gap-2"
        >
          <span>✅</span> No layout warnings for this viewport.
        </div>

        <div v-else class="space-y-2 max-h-[220px] overflow-y-auto pr-1">
          <div
            v-for="warning in layoutWarnings"
            :key="warning.fieldId"
            class="warning-item border border-amber-200 bg-amber-50/50 hover:bg-amber-50 rounded-lg p-2.5 transition-colors cursor-pointer"
            @click="onSelectField(warning.fieldId)"
          >
            <div
              class="flex items-center justify-between font-semibold text-xs text-amber-800 mb-0.5"
            >
              <span class="truncate max-w-[150px]">{{ warning.label }}</span>
              <span
                class="text-[10px] bg-amber-100 px-1 py-0.2 rounded font-mono"
                >Span: {{ warning.gridSpan }}</span
              >
            </div>
            <p class="text-[11px] text-amber-700 leading-snug">
              {{ warning.message }}
            </p>
            <div class="text-[10px] text-slate-400 mt-1 italic">
              In: {{ warning.sectionName }}
            </div>
          </div>
        </div>
      </div>

      <!-- 2. Properties Inspector Section -->
      <div class="selected-field-section border-t border-slate-100 pt-5">
        <h3
          class="text-xs font-bold uppercase tracking-wider text-slate-400 mb-3"
        >
          Properties Inspector
        </h3>

        <div
          v-if="!selectedField"
          class="text-xs text-slate-500 italic bg-slate-50 border border-slate-100 rounded-lg p-3"
        >
          Select a field widget on the canvas to inspect and edit its layout
          attributes.
        </div>

        <div
          v-else
          class="properties-inspector-container space-y-4 bg-slate-50 border border-slate-100 rounded-xl p-4"
          @keydown="handleInspectorKeydown"
        >
          <div
            class="flex items-center justify-between text-xs font-semibold text-slate-700 border-b border-slate-200 pb-2"
          >
            <span class="truncate max-w-[120px]">{{
              selectedField.label
            }}</span>
            <span
              class="font-mono text-xxs bg-slate-200 px-1.5 py-0.5 rounded text-slate-600"
              >{{ selectedField.id }}</span
            >
          </div>

          <div class="form-group flex flex-col gap-1.5">
            <label class="text-xxs font-bold text-slate-500 uppercase"
              >Field Label</label
            >
            <input
              id="inspect-field-label"
              v-model="selectedFieldLabel"
              type="text"
              class="inspect-label-input w-full border border-slate-200 rounded-md px-2.5 py-1.5 text-xs focus:ring-1 focus:ring-indigo-500 bg-white"
            />
          </div>

          <div class="form-group flex flex-col gap-1.5">
            <label class="text-xxs font-bold text-slate-500 uppercase"
              >Grid Span (1-12 Columns)</label
            >
            <select
              id="inspect-field-span"
              v-model="selectedFieldGridSpan"
              class="inspect-span-select w-full border border-slate-200 rounded-md px-2.5 py-1.5 text-xs focus:ring-1 focus:ring-indigo-500 bg-white font-medium"
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

          <div class="form-group flex flex-col gap-1.5">
            <label class="text-xxs font-bold text-slate-500 uppercase"
              >Field Type</label
            >
            <select
              v-model="selectedFieldType"
              class="w-full border border-slate-200 rounded-md px-2.5 py-1.5 text-xs focus:ring-1 focus:ring-indigo-500 bg-white"
            >
              <option value="text">Text Input</option>
              <option value="numeric">Numeric Input</option>
              <option value="date">Date Picker</option>
              <option value="select">Dropdown Select</option>
              <option value="radio">Radio Buttons</option>
              <option value="grid">Grid Layout</option>
              <option value="file">File Upload</option>
            </select>
          </div>

          <div class="flex items-center gap-2">
            <input
              id="inspect-field-required"
              v-model="selectedFieldRequired"
              type="checkbox"
              class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
            />
            <label
              for="inspect-field-required"
              class="text-xs font-medium text-slate-700"
              >Required Field</label
            >
          </div>
        </div>
      </div>

      <!-- 3. Form Compiler Section -->
      <div class="compiler-section border-t border-slate-100 pt-5">
        <h3
          class="text-xs font-bold uppercase tracking-wider text-slate-400 mb-3"
        >
          Form Compiler
        </h3>

        <div
          class="space-y-3 bg-slate-50 border border-slate-100 rounded-xl p-4"
        >
          <div class="flex items-center gap-2">
            <input
              id="dismiss-warnings-checkbox"
              v-model="dismissedWarnings"
              type="checkbox"
              class="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
            />
            <label
              for="dismiss-warnings-checkbox"
              class="text-xs font-medium text-slate-700"
            >
              Dismiss layout warnings
            </label>
          </div>

          <button
            class="w-full btn-compile bg-indigo-600 hover:bg-indigo-700 text-white text-xs font-semibold py-2 px-3 rounded-md transition shadow-sm"
            @click="compileForm"
          >
            Compile Form Schema
          </button>

          <!-- Compilation Feedback messages -->
          <div
            v-if="compilationStatus === 'blocked'"
            class="compilation-error text-xs text-red-700 bg-red-50 border border-red-100 rounded-lg p-2.5 flex items-start gap-1.5 font-medium leading-snug"
          >
            <span class="flex-shrink-0">❌</span>
            <span
              >Compilation blocked: active layout warnings must be resolved or
              explicitly dismissed!</span
            >
          </div>
          <div
            v-if="compilationStatus === 'success'"
            class="compilation-success text-xs text-emerald-700 bg-emerald-50 border border-emerald-100 rounded-lg p-2.5 flex items-start gap-1.5 font-medium leading-snug"
          >
            <span class="flex-shrink-0">🎉</span>
            <span
              >Compilation successful! Form schema successfully
              translated.</span
            >
          </div>
        </div>
      </div>
    </div>

    <!-- Polite Live Region for accessibility announcements (Requirement 4) -->
    <div
      style="
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        border: 0;
      "
      aria-live="polite"
    >
      {{ designerStore.announcement }}
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
import { computed, ref, watch, nextTick } from "vue";
import draggable from "vuedraggable";
import FormSectionContainer from "./FormSectionContainer.vue";
import { useDesignerStore } from "../../stores/designer.js";

const designerStore = useDesignerStore();

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

function handleViewportTabKeydown(e, currentMode) {
  const modes = ["desktop", "tablet", "mobile"];
  const currentIndex = modes.indexOf(currentMode);
  if (currentIndex === -1) return;

  let nextIndex;
  if (e.key === "ArrowRight" || e.key === "ArrowDown") {
    e.preventDefault();
    nextIndex = (currentIndex + 1) % modes.length;
  } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
    e.preventDefault();
    nextIndex = (currentIndex - 1 + modes.length) % modes.length;
  } else {
    return;
  }

  const nextMode = modes[nextIndex];
  designerStore.setViewport(nextMode);

  nextTick(() => {
    const nextBtn = document.querySelector(`.btn-viewport-${nextMode}`);
    if (nextBtn) {
      nextBtn.focus();
    }
  });
}

function handleInspectorKeydown(e) {
  if (e.key === "Escape") {
    e.preventDefault();
    if (props.selectedFieldId) {
      const widget = document.getElementById(`field-${props.selectedFieldId}`);
      if (widget) {
        widget.focus();
        designerStore.setFocusedItemId(props.selectedFieldId);
      }
    }
    return;
  }
  if (e.key === "Tab") {
    const container = e.currentTarget;
    const focusables = Array.from(
      container.querySelectorAll(
        'input, select, button, textarea, [tabindex="0"]'
      )
    ).filter(
      (item) => !item.disabled && item.getAttribute("tabindex") !== "-1"
    );
    if (focusables.length === 0) return;
    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    if (e.shiftKey) {
      if (document.activeElement === first) {
        e.preventDefault();
        last.focus();
      }
    } else {
      if (document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  }
}

watch(
  () => props.selectedFieldId,
  (newId) => {
    if (newId) {
      nextTick(() => {
        const container = document.querySelector(
          ".properties-inspector-container"
        );
        if (container) {
          const focusables = Array.from(
            container.querySelectorAll(
              'input, select, button, textarea, [tabindex="0"]'
            )
          ).filter(
            (item) => !item.disabled && item.getAttribute("tabindex") !== "-1"
          );
          if (focusables.length > 0) {
            focusables[0].focus();
          }
        }
      });
    }
  }
);

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
    if (compilationStatus.value === "blocked" && val) {
      compilationStatus.value = null;
    }
  },
});

function compileForm() {
  if (layoutWarnings.value.length > 0 && !dismissedWarnings.value) {
    compilationStatus.value = "blocked";
  } else {
    compilationStatus.value = "success";
  }
}

const handleSectionReorder = (evt) => {
  console.log("Section reordered:", evt);
  compilationStatus.value = null;
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
</style>
