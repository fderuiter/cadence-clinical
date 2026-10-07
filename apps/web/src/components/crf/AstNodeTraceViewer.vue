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
  color: #38bdf8;
  font-weight: bold;
}

.node-function {
  color: #c084fc;
  font-weight: bold;
}

.node-field {
  color: #4ade80;
  font-weight: bold;
}

.node-constant {
  color: #fbbf24;
}

.node-desc {
  color: #cbd5e1;
  font-size: 0.7rem;
}

.value-pill {
  font-size: 0.65rem;
  font-weight: bold;
  padding: 1px 5px;
  border-radius: 3px;
  margin-left: auto;
}

.val-true {
  background-color: #15803d;
  color: #ffffff;
}

.val-false {
  background-color: #b91c1c;
  color: #ffffff;
}

.val-null {
  background-color: #475569;
  color: #cbd5e1;
}

.val-value {
  background-color: #0369a1;
  color: #ffffff;
}

.node-children {
  margin-left: 14px;
  padding-left: 8px;
  border-left: 1px dashed #475569;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
</style>
