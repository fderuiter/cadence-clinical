<template>
  <div class="headless-form-renderer" :class="[`viewport-${currentViewport}`]">
    <!-- Omni-Channel Viewport Controls -->
    <div class="viewport-toolbar">
      <div class="form-title-badge">
        <h3>{{ formDefinition.data_schema.name }}</h3>
        <span class="version-tag">v{{ formDefinition.version_tag }}</span>
      </div>

      <div class="viewport-switcher">
        <button
          type="button"
          class="btn-viewport"
          :class="{ active: currentViewport === 'desktop_edc' }"
          @click="currentViewport = 'desktop_edc'"
        >
          🖥️ Desktop EDC
        </button>
        <button
          type="button"
          class="btn-viewport"
          :class="{ active: currentViewport === 'mobile_epro' }"
          @click="currentViewport = 'mobile_epro'"
        >
          📱 Mobile ePRO
        </button>
        <button
          type="button"
          class="btn-viewport"
          :class="{ active: currentViewport === 'tablet_esource' }"
          @click="currentViewport = 'tablet_esource'"
        >
          📋 Tablet eSource
        </button>
      </div>
    </div>

    <!-- Active Form Viewport Frame -->
    <div class="form-canvas-frame">
      <form class="rendered-form" @submit.prevent="handleSubmit">
        <!-- Render UI Schema Sections -->
        <div
          v-for="section in formDefinition.ui_schema.sections"
          :key="section.id"
          class="form-section-card"
        >
          <h4 class="section-title">
            {{ section.title }}
          </h4>

          <div
            class="section-grid"
            :style="{
              gridTemplateColumns: `repeat(${currentViewport === 'mobile_epro' ? 1 : section.columns}, 1fr)`,
            }"
          >
            <div
              v-for="widget in section.widgets"
              :key="widget.variable_id"
              class="grid-field-cell"
              :style="{
                gridColumn:
                  currentViewport === 'mobile_epro'
                    ? 'span 1'
                    : `span ${widget.grid_column_span || 12}`,
              }"
            >
              <!-- Field Label & Constraints -->
              <label class="field-label" :for="'widget-' + widget.variable_id">
                {{ getVariable(widget.variable_id)?.name }}
                <span
                  v-if="getVariable(widget.variable_id)?.constraints?.required"
                  class="required-star"
                  >*</span
                >
              </label>

              <!-- Widget Rendering -->
              <template v-if="widget.widget === 'number_input'">
                <input
                  :id="'widget-' + widget.variable_id"
                  v-model.number="formData[widget.variable_id]"
                  type="number"
                  class="form-input"
                  :placeholder="widget.placeholder || ''"
                  @input="evaluateRules"
                />
              </template>

              <template v-else-if="widget.widget === 'medical_autocode'">
                <MedicalAutocodeWidget
                  v-model="formData[widget.variable_id]"
                  :label="getVariable(widget.variable_id)?.name"
                  @code-selected="evaluateRules"
                />
              </template>

              <template v-else-if="widget.widget === 'body_map_coordinate'">
                <BodyMapWidget
                  v-model="formData[widget.variable_id]"
                  :label="getVariable(widget.variable_id)?.name"
                />
              </template>

              <template v-else-if="widget.widget === 'repeating_log_grid'">
                <RepeatingLogGridWidget
                  v-model="formData[widget.variable_id]"
                  :label="getVariable(widget.variable_id)?.name"
                />
              </template>

              <template v-else>
                <input
                  :id="'widget-' + widget.variable_id"
                  v-model="formData[widget.variable_id]"
                  type="text"
                  class="form-input"
                  :placeholder="widget.placeholder || ''"
                  @input="evaluateRules"
                />
              </template>

              <!-- Inline AST Validation Queries / Errors -->
              <div
                v-if="fieldErrors[widget.variable_id]"
                class="field-error-message"
              >
                ⚠️ {{ fieldErrors[widget.variable_id] }}
              </div>
            </div>
          </div>
        </div>

        <!-- Form Submission Actions -->
        <div class="form-actions-bar">
          <button type="submit" class="btn-submit" :disabled="hasHardErrors">
            Save & Submit eCRF
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, reactive } from "vue";
import { ClientRulesEvaluator } from "../../engine/rulesEvaluator.js";
import MedicalAutocodeWidget from "./widgets/MedicalAutocodeWidget.vue";
import BodyMapWidget from "./widgets/BodyMapWidget.vue";
import RepeatingLogGridWidget from "./widgets/RepeatingLogGridWidget.vue";

const props = defineProps({
  formDefinition: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(["form-submitted"]);

const currentViewport = ref(
  props.formDefinition.ui_schema?.default_viewport || "desktop_edc"
);
const formData = reactive({});
const fieldErrors = reactive({});

// Initialize form data model
props.formDefinition.data_schema?.variables?.forEach((v) => {
  formData[v.id] = v.data_type === "repeat_log" ? [] : "";
});

const getVariable = (varId) => {
  return props.formDefinition.data_schema?.variables?.find(
    (v) => v.id === varId
  );
};

const evaluateRules = () => {
  // Clear previous errors
  Object.keys(fieldErrors).forEach((key) => delete fieldErrors[key]);

  // 1. Evaluate AST Edit Checks
  props.formDefinition.logic_schema?.edit_checks?.forEach((check) => {
    const passed = ClientRulesEvaluator.evaluate(
      check.ast_expression,
      formData
    );
    if (!passed) {
      check.target_variable_ids.forEach((varId) => {
        fieldErrors[varId] = check.query_message;
      });
    }
  });
};

const hasHardErrors = computed(() => {
  return Object.keys(fieldErrors).length > 0;
});

const handleSubmit = () => {
  evaluateRules();
  if (!hasHardErrors.value) {
    emit("form-submitted", {
      form_id: props.formDefinition.form_id,
      version_tag: props.formDefinition.version_tag,
      data: { ...formData },
    });
  }
};
</script>

<style scoped>
.headless-form-renderer {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
  width: 100%;
}

.viewport-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-lg, 8px);
}

.form-title-badge {
  display: flex;
  align-items: center;
  gap: 8px;
}

.form-title-badge h3 {
  margin: 0;
  font-size: 1.125rem;
  color: var(--text-primary, #1e293b);
}

.version-tag {
  padding: 2px 6px;
  background-color: var(--surface-subtle, #f1f5f9);
  border-radius: var(--radius-sm, 4px);
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-secondary, #475569);
}

.viewport-switcher {
  display: flex;
  gap: 6px;
}

.btn-viewport {
  padding: 6px 12px;
  border: 1px solid var(--border, #cbd5e1);
  background-color: var(--surface, #ffffff);
  border-radius: var(--radius-md, 6px);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.btn-viewport.active {
  background-color: var(--primary, #2563eb);
  color: #ffffff;
  border-color: var(--primary, #2563eb);
}

.form-canvas-frame {
  display: flex;
  justify-content: center;
  width: 100%;
}

.viewport-mobile_epro .form-canvas-frame {
  max-width: 420px;
  margin: 0 auto;
}

.viewport-tablet_esource .form-canvas-frame {
  max-width: 768px;
  margin: 0 auto;
}

.rendered-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
  width: 100%;
}

.form-section-card {
  padding: 16px;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-lg, 8px);
}

.section-title {
  margin: 0 0 12px 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary, #1e293b);
  border-bottom: 1px solid var(--border-subtle, #e2e8f0);
  padding-bottom: 6px;
}

.section-grid {
  display: grid;
  gap: 12px;
}

.grid-field-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--text-primary, #1e293b);
}

.required-star {
  color: var(--error, #ef4444);
}

.form-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-md, 6px);
  font-size: 0.9375rem;
  background-color: var(--surface, #ffffff);
  color: var(--text-primary, #1e293b);
}

.form-input:focus {
  outline: none;
  border-color: var(--primary, #2563eb);
}

.field-error-message {
  font-size: 0.75rem;
  color: var(--error, #ef4444);
  font-weight: 500;
}

.form-actions-bar {
  display: flex;
  justify-content: flex-end;
  padding: 12px 0;
}

.btn-submit {
  padding: 10px 20px;
  background-color: var(--primary, #2563eb);
  color: #ffffff;
  border: none;
  border-radius: var(--radius-md, 6px);
  font-size: 0.9375rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-submit:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
