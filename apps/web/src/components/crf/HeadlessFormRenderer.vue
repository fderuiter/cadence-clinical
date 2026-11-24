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
              v-show="!Object.is(visibleFields[widget.variable_id], false)"
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
import { computed, ref, reactive, watch, onMounted } from "vue";
import { evaluateAST, renderFormFromJSON } from "../../evaluator.js";
import MedicalAutocodeWidget from "./widgets/MedicalAutocodeWidget.vue";
import BodyMapWidget from "./widgets/BodyMapWidget.vue";
import RepeatingLogGridWidget from "./widgets/RepeatingLogGridWidget.vue";

const props = defineProps({
  formDefinition: {
    type: Object,
    required: true,
  },
  initialValues: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits(["form-submitted", "data-changed"]);

const currentViewport = ref(
  props.formDefinition.ui_schema?.default_viewport || "desktop_edc"
);
const formData = reactive({});
const fieldErrors = reactive({});
const visibleFields = ref({});

// Initialize form data model
function initFormData() {
  props.formDefinition.data_schema?.variables?.forEach((v) => {
    if (props.initialValues && props.initialValues[v.id] !== undefined) {
      formData[v.id] = props.initialValues[v.id];
    } else {
      formData[v.id] = v.data_type === "repeat_log" ? [] : "";
    }
  });
}
initFormData();

const getVariable = (varId) => {
  return props.formDefinition.data_schema?.variables?.find(
    (v) => v.id === varId
  );
};

const evaluateRules = () => {
  // Clear previous errors
  Object.keys(fieldErrors).forEach((key) => delete fieldErrors[key]);

  // 1. Prepare fields metadata for renderFormFromJSON
  const variables = props.formDefinition.data_schema?.variables || [];
  const rulesList = [
    ...(props.formDefinition.logic_schema?.rules || []),
    ...(props.formDefinition.logic_schema?.skip_logic || []),
  ];

  const fieldsMeta = variables.map((v) => {
    let relevantExpr = v.relevant || null;
    if (!relevantExpr) {
      const skipRule = rulesList.find(
        (r) => (r.type === "skip_logic" || r.action) && r.target_field === v.id
      );
      if (skipRule) {
        const cond = skipRule.condition || skipRule.ast_expression;
        if (cond) {
          const action = (skipRule.action || "").toLowerCase();
          if (action === "hide" || action === "skip") {
            relevantExpr = {
              type: "logical",
              operator: "not",
              operands: [cond],
            };
          } else {
            relevantExpr = cond;
          }
        }
      }
    }
    return {
      id: v.id,
      relevant: relevantExpr,
    };
  });

  // Evaluate visibility & cascading nullification
  const { visibleFields: vf, updatedValues } = renderFormFromJSON(
    fieldsMeta,
    formData
  );
  visibleFields.value = vf;

  // Sync updated values back to formData if purged
  Object.keys(updatedValues).forEach((key) => {
    if (formData[key] !== updatedValues[key]) {
      formData[key] = updatedValues[key];
    }
  });

  // 2. Evaluate Edit Checks & Constraint Rules
  const editChecks = [
    ...(props.formDefinition.logic_schema?.edit_checks || []),
    ...(props.formDefinition.logic_schema?.constraints || []),
  ];

  // Also include general rules array if specified
  rulesList.forEach((r) => {
    if (
      r.type === "constraint" ||
      r.type === "cross_form_check" ||
      (r.query_message && r.type !== "skip_logic")
    ) {
      editChecks.push(r);
    }
  });

  editChecks.forEach((check) => {
    const expr = check.ast_expression || check.condition;
    if (!expr) return;

    const res = evaluateAST(expr, formData);
    let isViolated;
    if (check.is_violation_when === "true") {
      isViolated = Object.is(res, true);
    } else if (check.is_violation_when === "false") {
      isViolated = Object.is(res, false);
    } else if (
      check.type === "constraint" ||
      check.rule_type === "constraint"
    ) {
      isViolated = Object.is(res, false);
    } else {
      isViolated = Object.is(res, true);
    }

    if (isViolated) {
      const targetIds =
        check.target_variable_ids ||
        (check.target_field ? [check.target_field] : []);
      targetIds.forEach((varId) => {
        fieldErrors[varId] =
          check.query_message || "Constraint rule violation.";
      });
    }
  });

  emit("data-changed", { ...formData });
};

watch(
  () => (props.initialValues ? JSON.stringify(props.initialValues) : ""),
  () => {
    if (props.initialValues) {
      Object.keys(props.initialValues).forEach((k) => {
        formData[k] = props.initialValues[k];
      });
      evaluateRules();
    }
  },
  { immediate: true }
);

// Initial evaluation on mount
onMounted(() => {
  evaluateRules();
});

const hasHardErrors = computed(() => {
  return Object.keys(fieldErrors).length > 0;
});

const resetSimulation = () => {
  props.formDefinition.data_schema?.variables?.forEach((v) => {
    formData[v.id] = v.data_type === "repeat_log" ? [] : "";
  });
  evaluateRules();
};

const setFormData = (data) => {
  if (data) {
    Object.keys(data).forEach((k) => {
      formData[k] = data[k];
    });
  }
  evaluateRules();
};

const handleSubmit = () => {
  evaluateRules();
  if (!hasHardErrors.value) {
    const payloadData = {};
    Object.keys(formData).forEach((key) => {
      if (!Object.is(visibleFields.value[key], false)) {
        payloadData[key] = formData[key];
      }
    });

    emit("form-submitted", {
      form_id: props.formDefinition.form_id,
      version_tag: props.formDefinition.version_tag,
      data: payloadData,
    });
  }
};

defineExpose({
  formData,
  fieldErrors,
  visibleFields,
  evaluateRules,
  resetSimulation,
  setFormData,
});
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
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
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
  color: var(--semantic-color-text);
}

.version-tag {
  padding: 2px 6px;
  background-color: var(--semantic-color-surface-muted);
  border-radius: var(--radius-sm, 4px);
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.viewport-switcher {
  display: flex;
  gap: 6px;
}

.btn-viewport {
  padding: 6px 12px;
  border: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  border-radius: var(--radius-md, 6px);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.btn-viewport.active {
  background-color: var(--primary);
  color: var(--semantic-color-surface);
  border-color: var(--primary);
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
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 8px);
}

.section-title {
  margin: 0 0 12px 0;
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  border-bottom: 1px solid var(--semantic-color-border);
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
  color: var(--semantic-color-text);
}

.required-star {
  color: var(--error);
}

.form-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
  font-size: 0.9375rem;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.form-input:focus-visible {
  border-color: var(--primary);
}

.field-error-message {
  font-size: 0.75rem;
  color: var(--error);
  font-weight: 500;
}

.form-actions-bar {
  display: flex;
  justify-content: flex-end;
  padding: 12px 0;
}

.btn-submit {
  padding: 10px 20px;
  background-color: var(--primary);
  color: var(--semantic-color-surface);
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
