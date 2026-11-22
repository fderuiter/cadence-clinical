<template>
  <div class="rule-tracer-panel">
    <div class="tracer-header">
      <div class="tracer-title">
        <span class="tracer-icon">🔍</span>
        <h3>Live Rule Execution Tracer</h3>
      </div>
      <span
        class="rule-count-badge"
        :class="{ 'has-violations': violationCount > 0 }"
      >
        {{ activeRules.length }} Rules ({{ violationCount }} Queries)
      </span>
    </div>

    <!-- Simulation Mock Controls Section -->
    <div class="simulation-controls-card">
      <div class="control-row">
        <label for="mock-profile-select" class="control-label"
          >Mock Subject Profile</label
        >
        <div class="control-inputs">
          <select
            id="mock-profile-select"
            v-model="selectedProfile"
            class="profile-select"
            @change="applyMockProfile"
          >
            <option value="">-- Select Mock Profile --</option>
            <option value="normal">Normal Adult Baseline</option>
            <option value="hypertensive">Hypertensive Alert Profile</option>
            <option value="edge_case">Incomplete / Edge Case Profile</option>
          </select>
          <button
            type="button"
            class="btn-reset-simulation"
            title="Reset Simulation State"
            @click="resetSimulation"
          >
            🔄 Reset
          </button>
        </div>
      </div>
    </div>

    <!-- Rules Execution List -->
    <div class="rules-list-container">
      <div v-if="activeRules.length === 0" class="empty-rules-box">
        <span>ℹ️</span> No active skip-logic or constraint edit check rules on
        this form.
      </div>

      <div
        v-for="rule in activeRules"
        :key="rule.id"
        class="rule-card"
        :class="getRuleCardClass(rule)"
      >
        <div
          class="rule-card-header"
          role="button"
          tabindex="0"
          @click="toggleRuleExpand(rule.id)"
          @keydown.enter.prevent="toggleRuleExpand(rule.id)"
          @keydown.space.prevent="toggleRuleExpand(rule.id)"
        >
          <div class="rule-title-group">
            <span class="rule-type-badge" :class="rule.type">
              {{ formatRuleType(rule.type) }}
            </span>
            <span class="rule-target-name">
              {{ rule.target_field ? `Target: ${rule.target_field}` : rule.id }}
            </span>
          </div>

          <div class="rule-status-group">
            <span class="status-pill" :class="getRuleStatus(rule).style">
              {{ getRuleStatus(rule).label }}
            </span>
            <span class="expand-chevron">{{
              expandedRules[rule.id] ? "▲" : "▼"
            }}</span>
          </div>
        </div>

        <p v-if="rule.query_message" class="rule-message">
          <strong>Message:</strong> {{ rule.query_message }}
        </p>

        <!-- Collapsible AST Trace Details -->
        <div v-if="expandedRules[rule.id]" class="rule-trace-body">
          <div class="trace-section-header">AST Evaluation Breakdown</div>

          <div class="ast-tree-container">
            <AstNodeTraceViewer :trace="getRuleTrace(rule)" />
          </div>

          <!-- Field Dependencies Section -->
          <div class="trace-section-header">Active Field Dependencies</div>
          <div class="field-dependencies-list">
            <div
              v-for="fieldId in getRuleDependencies(rule)"
              :key="fieldId"
              class="dependency-item"
              :class="{ 'has-value': isFieldPopulated(fieldId) }"
            >
              <span class="dep-field-id">{{ fieldId }}</span>
              <span class="dep-field-val">
                {{ formatFieldValue(fieldId) }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, reactive } from "vue";
import { traceASTEvaluation } from "../../evaluator.js";
import AstNodeTraceViewer from "./AstNodeTraceViewer.vue";

const props = defineProps({
  rules: {
    type: Array,
    default: () => [],
  },
  simulationData: {
    type: Object,
    default: () => ({}),
  },
  fieldErrors: {
    type: Object,
    default: () => ({}),
  },
  visibleFields: {
    type: Object,
    default: () => ({}),
  },
});

const emit = defineEmits(["apply-profile", "reset-simulation"]);

const selectedProfile = ref("");
const expandedRules = reactive({});

const activeRules = computed(() => {
  return props.rules.filter((r) => !r.is_deleted);
});

// Expand all rules by default
activeRules.value.forEach((r) => {
  if (expandedRules[r.id] === undefined) {
    expandedRules[r.id] = true;
  }
});

function toggleRuleExpand(ruleId) {
  expandedRules[ruleId] = !expandedRules[ruleId];
}

function formatRuleType(type) {
  if (type === "skip_logic") return "Skip Logic";
  if (type === "constraint") return "Constraint Check";
  if (type === "cross_form_check" || type === "edit_check") return "Edit Check";
  return type || "Rule";
}

function getRuleTrace(rule) {
  const condNode = rule.condition || rule.ast_expression;
  return traceASTEvaluation(condNode, props.simulationData);
}

function getRuleDependencies(rule) {
  const trace = getRuleTrace(rule);
  return trace.referencedFields || [];
}

function getRuleStatus(rule) {
  const trace = getRuleTrace(rule);
  const resultVal = trace.val;

  if (rule.type === "skip_logic") {
    if (Object.is(resultVal, true)) {
      return {
        label: rule.action === "hide" ? "HIDDEN" : "VISIBLE",
        style: "status-active",
      };
    }
    return {
      label: rule.action === "hide" ? "VISIBLE" : "HIDDEN",
      style: "status-inactive",
    };
  }

  // Constraint or edit check rules
  if (rule.target_field && props.fieldErrors[rule.target_field]) {
    return { label: "QUERY RAISED", style: "status-error" };
  }

  if (Object.is(resultVal, true)) {
    // If AST condition evaluates to true, check if query error exists on target
    const isError =
      props.fieldErrors[rule.target_field || ""] || props.fieldErrors[rule.id];
    if (isError) {
      return { label: "QUERY RAISED", style: "status-error" };
    }
    return { label: "MET / PASS", style: "status-success" };
  }

  if (Object.is(resultVal, false)) {
    return { label: "PASS", style: "status-success" };
  }

  return { label: "PENDING", style: "status-neutral" };
}

const violationCount = computed(() => {
  return Object.keys(props.fieldErrors || {}).length;
});

function getRuleCardClass(rule) {
  const status = getRuleStatus(rule);
  if (status.style === "status-error") return "card-error";
  if (status.style === "status-active") return "card-active";
  return "";
}

function isFieldPopulated(fieldId) {
  const val = props.simulationData[fieldId];
  return val !== undefined && val !== null && val !== "";
}

function formatFieldValue(fieldId) {
  const val = props.simulationData[fieldId];
  if (val === undefined || val === null || val === "") return "(empty)";
  return typeof val === "object" ? JSON.stringify(val) : String(val);
}

function applyMockProfile() {
  if (!selectedProfile.value) return;
  emit("apply-profile", selectedProfile.value);
}

function resetSimulation() {
  selectedProfile.value = "";
  emit("reset-simulation");
}
</script>

<style scoped>
.rule-tracer-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px;
}

.tracer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 8px;
}

.tracer-title {
  display: flex;
  align-items: center;
  gap: 8px;
}

.tracer-title h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.rule-count-badge {
  font-size: 0.72rem;
  font-weight: 700;
  background-color: var(--semantic-color-border);
  color: var(--semantic-color-text-dark);
  padding: 2px 8px;
  border-radius: 12px;
}

.rule-count-badge.has-violations {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
  border: 1px solid var(--semantic-color-warning-border);
}

.simulation-controls-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 10px;
}

.control-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.control-label {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.control-inputs {
  display: flex;
  gap: 8px;
}

.profile-select {
  flex: 1;
  padding: 6px 10px;
  font-size: 0.8rem;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  background-color: var(--semantic-color-surface);
}

.btn-reset-simulation {
  padding: 6px 12px;
  font-size: 0.8rem;
  font-weight: 600;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  cursor: pointer;
}

.btn-reset-simulation:hover {
  background-color: var(--semantic-color-border);
}

.rules-list-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-height: 480px;
  overflow-y: auto;
}

.empty-rules-box {
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted);
  padding: 12px;
  text-align: center;
  background: var(--semantic-color-surface);
  border: 1px dashed var(--semantic-color-border);
  border-radius: 6px;
}

.rule-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  overflow: hidden;
  transition: border-color 0.15s ease-in-out;
}

.rule-card.card-error {
  border-color: var(--semantic-color-error-border);
}

.rule-card.card-active {
  border-color: var(--semantic-color-interactive-brand);
}

.rule-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  background-color: var(--semantic-color-surface-subtle);
  cursor: pointer;
  user-select: none;
}

.rule-card-header:hover {
  background-color: var(--semantic-color-surface-muted);
}

.rule-title-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.rule-type-badge {
  font-size: 0.68rem;
  font-weight: 700;
  text-transform: uppercase;
  padding: 2px 6px;
  border-radius: 4px;
}

.rule-type-badge.skip_logic {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}

.rule-type-badge.constraint,
.rule-type-badge.cross_form_check,
.rule-type-badge.edit_check {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}

.rule-target-name {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.rule-status-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.status-pill {
  font-size: 0.68rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.status-pill.status-success {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.status-pill.status-error {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.status-pill.status-active {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-interactive-primary);
}

.status-pill.status-inactive,
.status-pill.status-neutral {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.expand-chevron {
  font-size: 0.65rem;
  color: var(--semantic-color-text-secondary);
}

.rule-message {
  margin: 0;
  padding: 6px 12px;
  font-size: 0.75rem;
  color: var(--semantic-color-text-body);
  background-color: var(--semantic-color-error-bg-soft);
  border-bottom: 1px solid var(--semantic-color-border-muted);
}

.rule-trace-body {
  padding: 10px 12px;
  border-top: 1px solid var(--semantic-color-border-muted);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.trace-section-header {
  font-size: 0.68rem;
  font-weight: 700;
  text-transform: uppercase;
  color: var(--semantic-color-text-subtle);
  letter-spacing: 0.03em;
}

.ast-tree-container {
  background-color: var(--semantic-color-surface-elevated);
  border-radius: 6px;
  padding: 8px;
  color: var(--semantic-color-text-inverse);
  font-family: monospace;
  font-size: 0.75rem;
  overflow-x: auto;
}

.field-dependencies-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.dependency-item {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 3px 8px;
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.72rem;
  font-family: monospace;
}

.dependency-item.has-value {
  background-color: var(--semantic-color-primary-light);
  border-color: var(--semantic-color-info-border-accent);
}

.dep-field-id {
  font-weight: 700;
  color: var(--semantic-color-primary);
}

.dep-field-val {
  color: var(--semantic-color-text-body);
}
</style>
