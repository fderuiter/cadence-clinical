<template>
  <div class="repeating-log-grid-container">
    <div class="grid-header">
      <label class="widget-label">{{ label }}</label>
      <button type="button" class="btn-add-row" @click="addRow">+ Add Row</button>
    </div>

    <div class="table-wrapper">
      <table class="log-table">
        <thead>
          <tr>
            <th style="width: 40px;">#</th>
            <th v-for="col in columns" :key="col.id">{{ col.label }}</th>
            <th style="width: 50px;">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="rows.length === 0">
            <td :colspan="columns.length + 2" class="empty-cell">No log records captured. Click "+ Add Row" to begin.</td>
          </tr>
          <tr v-for="(row, rIdx) in rows" :key="rIdx">
            <td class="row-num">{{ rIdx + 1 }}</td>
            <td v-for="col in columns" :key="col.id">
              <input
                v-if="col.type === 'number'"
                type="number"
                class="cell-input"
                :value="row[col.id]"
                @input="updateCell(rIdx, col.id, $event.target.value)"
              />
              <input
                v-else
                type="text"
                class="cell-input"
                :value="row[col.id]"
                @input="updateCell(rIdx, col.id, $event.target.value)"
              />
            </td>
            <td class="action-cell">
              <button type="button" class="btn-delete-row" @click="deleteRow(rIdx)" title="Delete Row">×</button>
            </td>
          </tr>
        </tbody>
        <tfoot v-if="columns.some(c => c.aggregate === 'sum') && rows.length > 0">
          <tr class="summary-row">
            <td colspan="1">Total:</td>
            <td v-for="col in columns" :key="col.id">
              <span v-if="col.aggregate === 'sum'" class="aggregate-value">
                {{ calculateSum(col.id) }}
              </span>
            </td>
            <td></td>
          </tr>
        </tfoot>
      </table>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  modelValue: { type: Array, default: () => [] },
  label: { type: String, default: 'Repeating Log Grid' },
  columns: {
    type: Array,
    default: () => [
      { id: 'medication', label: 'Medication Name', type: 'text' },
      { id: 'dose_mg', label: 'Dose (mg)', type: 'number', aggregate: 'sum' },
      { id: 'frequency', label: 'Frequency', type: 'text' }
    ]
  }
})

const emit = defineEmits(['update:modelValue'])

const rows = ref([...props.modelValue])

watch(() => props.modelValue, (newVal) => {
  rows.value = [...newVal]
})

const addRow = () => {
  const newRow = {}
  props.columns.forEach(col => {
    newRow[col.id] = col.type === 'number' ? 0 : ''
  })
  rows.value.push(newRow)
  emit('update:modelValue', rows.value)
}

const deleteRow = (index) => {
  rows.value.splice(index, 1)
  emit('update:modelValue', rows.value)
}

const updateCell = (rowIndex, columnId, value) => {
  rows.value[rowIndex][columnId] = value
  emit('update:modelValue', rows.value)
}

const calculateSum = (columnId) => {
  return rows.value.reduce((acc, row) => acc + (Number(row[columnId]) || 0), 0)
}
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
  color: var(--text-primary, #1e293b);
}

.btn-add-row {
  padding: 4px 10px;
  background-color: var(--primary, #2563eb);
  color: #ffffff;
  border: none;
  border-radius: var(--radius-md, 6px);
  font-size: 0.8125rem;
  font-weight: 500;
  cursor: pointer;
}

.table-wrapper {
  overflow-x: auto;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-md, 6px);
}

.log-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.log-table th {
  background-color: var(--surface-subtle, #f8fafc);
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid var(--border, #cbd5e1);
  font-weight: 600;
  color: var(--text-secondary, #475569);
}

.log-table td {
  padding: 6px 10px;
  border-bottom: 1px solid var(--border-subtle, #e2e8f0);
}

.cell-input {
  width: 100%;
  padding: 4px 8px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-sm, 4px);
  font-size: 0.875rem;
  background-color: var(--surface, #ffffff);
}

.cell-input:focus {
  outline: none;
  border-color: var(--primary, #2563eb);
}

.row-num {
  text-align: center;
  color: var(--text-muted, #64748b);
  font-size: 0.75rem;
}

.btn-delete-row {
  background: none;
  border: none;
  color: var(--error, #ef4444);
  font-size: 1.125rem;
  font-weight: 700;
  cursor: pointer;
}

.empty-cell {
  text-align: center;
  color: var(--text-muted, #64748b);
  padding: 16px;
}

.summary-row {
  background-color: var(--surface-subtle, #f8fafc);
  font-weight: 600;
}

.aggregate-value {
  color: var(--primary-dark, #1d4ed8);
}
</style>
