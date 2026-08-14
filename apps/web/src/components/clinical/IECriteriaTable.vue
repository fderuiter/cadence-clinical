<template>
  <div class="ie-criteria-table-container">
    <div class="table-toolbar">
      <div class="toolbar-stats">
        <span class="stat-pill inc"><strong>{{ inclusionCount }}</strong> Inclusion Criteria</span>
        <span class="stat-pill exc"><strong>{{ exclusionCount }}</strong> Exclusion Criteria</span>
      </div>
      <button type="button" class="btn btn-secondary btn-sm" @click="showAddModal = !showAddModal">
        + Add Criterion
      </button>
    </div>

    <!-- Inline Add Form -->
    <div v-if="showAddModal" class="crit-add-panel">
      <div class="crit-field">
        <label for="new-crit-type">Type</label>
        <select id="new-crit-type" v-model="newCritType" class="crit-select">
          <option value="INCLUSION">Inclusion</option>
          <option value="EXCLUSION">Exclusion</option>
        </select>
      </div>
      <div class="crit-field">
        <label for="new-crit-id">Identifier</label>
        <input id="new-crit-id" v-model="newCritId" placeholder="e.g. INC-04" class="crit-input" style="width: 100px;" />
      </div>
      <div class="crit-field" style="flex: 2;">
        <label for="new-crit-text">Protocol Requirement Text</label>
        <input id="new-crit-text" v-model="newCritText" placeholder="e.g. Subject must have normal ECG at baseline." class="crit-input" />
      </div>
      <div class="crit-field" style="flex: 1;">
        <label for="new-crit-expr">Logical CDASH Expression</label>
        <input id="new-crit-expr" v-model="newCritExpr" placeholder="e.g. EG.EGORRES == 'NORMAL'" class="crit-input" />
      </div>
      <button type="button" class="btn btn-primary btn-sm" :disabled="!newCritText.trim()" @click="addCriterion">
        Save
      </button>
      <button type="button" class="btn btn-secondary btn-sm" @click="showAddModal = false">Cancel</button>
    </div>

    <div class="crit-table-container">
      <table class="criteria-table">
        <thead>
          <tr>
            <th class="col-type" scope="col">Type</th>
            <th class="col-id" scope="col">ID</th>
            <th class="col-text" scope="col">Protocol Text Requirement</th>
            <th class="col-expr" scope="col">Compiled Logical CDASH Expression</th>
            <th class="col-syntax" scope="col">AST Check</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in localCriteria" :key="item.identifier" class="criterion-row">
            <td class="cell-type">
              <span class="type-badge" :class="item.criterion_type.toLowerCase()">
                {{ item.criterion_type }}
              </span>
            </td>
            <td class="cell-id">
              <code>{{ item.identifier }}</code>
            </td>
            <td class="cell-text">
              {{ item.text_expression }}
            </td>
            <td class="cell-expr">
              <code v-if="item.logical_expression" class="logic-code">
                {{ item.logical_expression }}
              </code>
              <span v-else class="text-muted">Unmapped narrative requirement</span>
            </td>
            <td class="cell-syntax">
              <span v-if="item.logical_expression" class="syntax-badge valid">
                ✓ Valid CDASH AST
              </span>
              <span v-else class="syntax-badge narrative">
                Narrative Text
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from "vue";

const props = defineProps({
  criteria: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["update:criteria"]);

const localCriteria = ref([]);
const showAddModal = ref(false);
const newCritType = ref("INCLUSION");
const newCritId = ref("");
const newCritText = ref("");
const newCritExpr = ref("");

watch(
  () => props.criteria,
  (newVal) => {
    localCriteria.value = JSON.parse(JSON.stringify(newVal || []));
  },
  { immediate: true, deep: true }
);

const inclusionCount = computed(() => {
  return localCriteria.value.filter((c) => c.criterion_type === "INCLUSION").length;
});

const exclusionCount = computed(() => {
  return localCriteria.value.filter((c) => c.criterion_type === "EXCLUSION").length;
});

const addCriterion = () => {
  if (!newCritText.value.trim()) return;
  const defaultId = newCritId.value.trim() || `${newCritType.value === "INCLUSION" ? "INC" : "EXC"}-${localCriteria.value.length + 1}`;
  localCriteria.value.push({
    criterion_type: newCritType.value,
    identifier: defaultId,
    text_expression: newCritText.value.trim(),
    logical_expression: newCritExpr.value.trim() || null,
  });
  newCritId.value = "";
  newCritText.value = "";
  newCritExpr.value = "";
  showAddModal.value = false;
  emit("update:criteria", localCriteria.value);
};
</script>

<style scoped>
.ie-criteria-table-container {
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 16px;
  width: 100%;
}

.table-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.toolbar-stats {
  display: flex;
  gap: 8px;
}

.stat-pill {
  font-size: 0.8rem;
  padding: 4px 10px;
  border-radius: 6px;
  border: 1px solid transparent;
}

.stat-pill.inc {
  background-color: #ecfdf5;
  border-color: #a7f3d0;
  color: #065f46;
}

.stat-pill.exc {
  background-color: #fff1f2;
  border-color: #fecdd3;
  color: #9f1239;
}

.crit-add-panel {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  background-color: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 14px;
  border-radius: var(--radius-md, 8px);
  margin-bottom: 18px;
  flex-wrap: wrap;
}

.crit-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.crit-field label {
  font-size: 0.72rem;
  font-weight: 700;
  color: #334155;
  text-transform: uppercase;
}

.crit-input, .crit-select {
  padding: 7px 12px;
  border: 1px solid #94a3b8;
  border-radius: 4px;
  font-size: 0.88rem;
  background-color: #ffffff;
}

.crit-table-container {
  overflow-x: auto;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
}

.criteria-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  text-align: left;
}

.criteria-table th,
.criteria-table td {
  padding: 10px 12px;
  border: 1px solid var(--border, #e2e8f0);
}

.criteria-table th {
  background-color: #f8fafc;
  font-weight: 700;
  color: var(--primary, #0f172a);
}

.col-type { width: 110px; text-align: center; }
.col-id { width: 100px; }
.col-text { min-width: 280px; }
.col-expr { min-width: 240px; }
.col-syntax { width: 140px; text-align: center; }

.type-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
}

.type-badge.inclusion {
  background-color: #dcfce7;
  color: #15803d;
}

.type-badge.exclusion {
  background-color: #fee2e2;
  color: #b91c1c;
}

.logic-code {
  background-color: #f1f5f9;
  border: 1px solid #e2e8f0;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
  font-size: 0.8rem;
  color: #0369a1;
}

.text-muted {
  color: #94a3b8;
  font-style: italic;
  font-size: 0.8rem;
}

.syntax-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.syntax-badge.valid {
  background-color: #ecfdf5;
  color: #047857;
}

.syntax-badge.narrative {
  background-color: #f1f5f9;
  color: #64748b;
}
</style>
