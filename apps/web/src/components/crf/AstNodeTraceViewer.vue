<template>
  <div class="ast-node-trace">
    <div class="node-line">
      <span
        v-if="trace.type === 'logical' || trace.type === 'comparison'"
        class="node-operator"
      >
        [{{ trace.operator.toUpperCase() }}]
      </span>
      <span v-else-if="trace.type === 'function'" class="node-function">
        fn:{{ trace.operator }}()
      </span>
      <span v-else-if="trace.type === 'field_ref'" class="node-field">
        {{ trace.fieldId || "Field" }}
      </span>
      <span v-else-if="trace.type === 'constant'" class="node-constant">
        {{ JSON.stringify(trace.val) }}
      </span>

      <span class="node-desc">{{ trace.description }}</span>

      <span class="value-pill" :class="getValueClass(trace.val)">
        {{ formatValue(trace.val) }}
      </span>
    </div>

    <!-- Render Children AST Nodes recursively -->
    <div
      v-if="trace.children && trace.children.length > 0"
      class="node-children"
    >
      <AstNodeTraceViewer
        v-for="(childTrace, idx) in trace.children"
        :key="idx"
        :trace="childTrace"
      />
    </div>
  </div>
</template>

<script setup>
defineProps({
  trace: {
    type: Object,
    required: true,
  },
});

function formatValue(val) {
  if (val === true) return "TRUE";
  if (val === false) return "FALSE";
  if (val === null || val === undefined) return "NULL";
  return typeof val === "object" ? JSON.stringify(val) : String(val);
}

function getValueClass(val) {
  if (val === true) return "val-true";
  if (val === false) return "val-false";
  if (val === null || val === undefined) return "val-null";
  return "val-value";
}
</script>

<style scoped>
.ast-node-trace {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.node-line {
  display: flex;
  align-items: center;
  gap: 6px;
  line-height: 1.4;
}

.node-operator {
  color: var(--semantic-color-accent-inverse);
  font-weight: bold;
}

.node-function {
  color: var(--semantic-color-purple-inverse-text);
  font-weight: bold;
}

.node-field {
  color: var(--semantic-color-emerald-inverse-text);
  font-weight: bold;
}

.node-constant {
  color: var(--semantic-color-warning-border);
}

.node-desc {
  color: var(--semantic-color-text-inverse-muted);
  font-size: var(--font-size-2xs);
}

.value-pill {
  font-size: var(--font-size-2xs);
  font-weight: bold;
  padding: 1px 5px;
  border-radius: 3px;
  margin-left: auto;
}

.val-true {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.val-false {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.val-null {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

.val-value {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.node-children {
  margin-left: 14px;
  padding-left: 8px;
  border-left: 1px dashed var(--semantic-color-border-control);
  display: flex;
  flex-direction: column;
  gap: 2px;
}
</style>
