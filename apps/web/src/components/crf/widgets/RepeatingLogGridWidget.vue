<template>
  <div class="repeating-log-grid-container">
    <div class="grid-header">
      <span class="widget-label">{{ label }}</span>
      <button type="button" class="btn-add-row" @click="addRow">
        + Add Row
      </button>
    </div>

    <div class="table-wrapper">
      <table class="log-table">
        <thead>
          <tr>
            <th style="width: 40px">#</th>
            <th v-for="col in columns" :key="col.id">
              {{ col.label }}
            </th>
            <th style="width: 50px">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="rows.length === 0">
            <td :colspan="columns.length + 2" class="empty-cell">
              No log records captured. Click "+ Add Row" to begin.
            </td>
          </tr>
          <tr v-for="(row, rIdx) in rows" :key="rIdx">
            <td class="row-num">
              {{ rIdx + 1 }}
            </td>
            <td v-for="col in columns" :key="col.id">
              <input
                v-if="col.type === 'number'"
                type="number"
                class="cell-input"
                :aria-label="col.label"
                :value="row[col.id]"
                @input="updateCell(rIdx, col.id, $event.target.value)"
              />
              <input
                v-else
                type="text"
                class="cell-input"
                :aria-label="col.label"
                :value="row[col.id]"
                @input="updateCell(rIdx, col.id, $event.target.value)"
              />
            </td>
            <td class="action-cell">
              <ClinicalIconButton
                class="btn-delete-row"
                title="Delete Row"
                :label="`Delete row ${rIdx + 1}`"
                @click="deleteRow(rIdx)"
              >
                ×
              </ClinicalIconButton>
            </td>
          </tr>
        </tbody>
        <tfoot
          v-if="columns.some((c) => c.aggregate === 'sum') && rows.length > 0"
        >
          <tr class="summary-row">
            <td colspan="1">Total:</td>
            <td v-for="col in columns" :key="col.id">
              <span v-if="col.aggregate === 'sum'" class="aggregate-value">
                {{ calculateSum(col.id) }}
              </span>
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";
import { ClinicalIconButton } from "ui";

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: "Repeating Log Grid" },
  columns: {
    type: Array,
    default: () => [
      { id: "medication", label: "Medication Name", type: "text" },
      { id: "dose_mg", label: "Dose (mg)", type: "number", aggregate: "sum" },
      { id: "frequency", label: "Frequency", type: "text" },
    ],
  },
});

const emit = defineEmits(["update:modelValue"]);

const rows = ref([...props.modelValue]);

watch(
  () => props.modelValue,
  (newVal) => {
    rows.value = [...newVal];
  }
);

const addRow = () => {
  const newRow = {};
  props.columns.forEach((col) => {
    newRow[col.id] = col.type === "number" ? 0 : "";
  });
  rows.value.push(newRow);
  emit("update:modelValue", rows.value);
};

const deleteRow = (index) => {
  rows.value.splice(index, 1);
  emit("update:modelValue", rows.value);
};

const updateCell = (rowIndex, columnId, value) => {
  rows.value[rowIndex][columnId] = value;
  emit("update:modelValue", rows.value);
};

const calculateSum = (columnId) => {
  return rows.value.reduce((acc, row) => acc + (Number(row[columnId]) || 0), 0);
};
</script>

<style scoped>
.repeating-log-grid-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs, 4px);
  width: 100%;
}

.grid-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.widget-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.btn-add-row {
  padding: 4px 10px;
  background-color: var(--primary);
  color: var(--semantic-color-surface);
  border: none;
  border-radius: var(--radius-md, 6px);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.table-wrapper {
  overflow-x: auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
}

.log-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.log-table th {
  background-color: var(--semantic-color-surface-muted);
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border);
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.log-table td {
  padding: 6px 10px;
  border-bottom: 1px solid var(--semantic-color-border);
}

.cell-input {
  width: 100%;
  padding: 4px 8px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 4px);
  font-size: 0.875rem;
  background-color: var(--semantic-color-surface);
}

.cell-input:focus {
  outline: none;
  border-color: var(--primary);
}

.row-num {
  text-align: center;
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}

.btn-delete-row {
  background: none;
  border: none;
  color: var(--error);
  font-size: 1.125rem;
  font-weight: 700;
  cursor: pointer;
}

.empty-cell {
  text-align: center;
  color: var(--semantic-color-text-secondary);
  padding: 16px;
}

.summary-row {
  background-color: var(--semantic-color-surface-muted);
  font-weight: 600;
}

.aggregate-value {
  color: var(--semantic-color-primary-dark);
}
</style>
