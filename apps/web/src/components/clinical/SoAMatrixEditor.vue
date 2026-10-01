<template>
  <div class="soa-matrix-editor">
    <div class="matrix-toolbar">
      <div class="toolbar-stats">
        <span class="stat-badge"
          ><strong>{{ localActivities.length }}</strong> Procedures</span
        >
        <span class="stat-badge"
          ><strong>{{ visits.length }}</strong> Visits</span
        >
        <span class="stat-badge highlight"
          ><strong>{{ totalAssignedCells }}</strong> Scheduled Activities</span
        >
        <span v-if="selectedRangeCellCount > 0" class="stat-badge range-badge">
          ⚡ <strong>{{ selectedRangeCellCount }}</strong> Cells Selected
        </span>
      </div>
      <div class="toolbar-actions">
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="
            showAddProcedure = !showAddProcedure;
            showCloneGroup = false;
          "
        >
          + Add Procedure
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="
            showAddVisit = !showAddVisit;
            showCloneGroup = false;
          "
        >
          + Add Visit
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm btn-clone-group"
          @click="
            showCloneGroup = !showCloneGroup;
            showAddProcedure = false;
            showAddVisit = false;
          "
        >
          📋 Clone Procedure Group
        </button>
      </div>
    </div>

    <!-- Range Selection Bulk Actions Bar -->
    <div v-if="selectedRangeCellCount > 0" class="range-action-banner">
      <div class="range-banner-title">
        Selected Multi-Cell Range ({{ selectedRangeCellCount }} cells)
      </div>
      <div class="range-banner-buttons">
        <button
          type="button"
          class="btn btn-primary btn-sm btn-range-assign"
          @click="applyRangeState(true)"
        >
          ✓ Assign Range
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm btn-range-unassign"
          @click="applyRangeState(false)"
        >
          ✗ Unassign Range
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm btn-range-clear"
          @click="clearRangeSelection"
        >
          Clear Selection
        </button>
      </div>
    </div>

    <!-- Inline Add Procedure Form -->
    <div v-if="showAddProcedure" class="inline-add-box">
      <div class="inline-form-group">
        <label for="new-proc-name">Procedure Name</label>
        <input
          id="new-proc-name"
          v-model="newProcedureName"
          placeholder="e.g. Pharmacokinetic Blood Sampling"
          class="input-text"
        />
      </div>
      <div class="inline-form-group">
        <label for="new-proc-domain">CDASH Domain</label>
        <select
          id="new-proc-domain"
          v-model="newProcedureDomain"
          class="input-select"
        >
          <option value="VS">VS - Vital Signs</option>
          <option value="EG">EG - ECG</option>
          <option value="LB">LB - Laboratory</option>
          <option value="QS">QS - Questionnaire / VAS</option>
          <option value="PE">PE - Physical Exam / Body Map</option>
          <option value="AE">AE - Adverse Events</option>
          <option value="CM">CM - Concomitant Medications</option>
          <option value="DM">DM - Demographics</option>
          <option value="EX">EX - Exposure</option>
        </select>
      </div>
      <button
        type="button"
        class="btn btn-primary btn-sm"
        :disabled="!newProcedureName.trim()"
        @click="addCustomProcedure"
      >
        Save Procedure
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-sm"
        @click="showAddProcedure = false"
      >
        Cancel
      </button>
    </div>

    <!-- Inline Add Visit Form -->
    <div v-if="showAddVisit" class="inline-add-box">
      <div class="inline-form-group">
        <label for="new-visit-name">Visit Name</label>
        <input
          id="new-visit-name"
          v-model="newVisitName"
          placeholder="e.g. Visit 4 / Week 8"
          class="input-text"
        />
      </div>
      <div class="inline-form-group">
        <label for="new-visit-day">Target Day</label>
        <input
          id="new-visit-day"
          v-model.number="newVisitDay"
          type="number"
          class="input-text"
          style="width: 90px"
        />
      </div>
      <button
        type="button"
        class="btn btn-primary btn-sm"
        :disabled="!newVisitName.trim()"
        @click="addCustomVisit"
      >
        Save Visit
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-sm"
        @click="showAddVisit = false"
      >
        Cancel
      </button>
    </div>

    <!-- Inline Clone Procedure Group Form -->
    <div v-if="showCloneGroup" class="inline-add-box clone-group-box">
      <div class="inline-form-group">
        <label for="clone-domain-select">Source Procedure Group (Domain)</label>
        <select
          id="clone-domain-select"
          v-model="cloneSourceDomain"
          class="input-select"
        >
          <option v-for="d in availableDomains" :key="d" :value="d">
            {{ d }} Domain
          </option>
        </select>
      </div>
      <div class="inline-form-group">
        <label for="clone-suffix-input"
          >Cloned Group Name Prefix / Suffix</label
        >
        <input
          id="clone-suffix-input"
          v-model="cloneNameSuffix"
          placeholder="e.g. (Cohort B)"
          class="input-text"
        />
      </div>
      <button
        type="button"
        class="btn btn-primary btn-sm btn-execute-clone"
        @click="executeProcedureGroupCloning"
      >
        ⚡ Clone Group to Target Arm
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-sm"
        @click="showCloneGroup = false"
      >
        Cancel
      </button>
    </div>

    <!-- High Density Matrix Table -->
    <!-- eslint-disable vuejs-accessibility/mouse-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div class="matrix-table-container">
      <table class="soa-interactive-table">
        <thead>
          <tr>
            <th class="col-procedure-header" scope="col">
              <div class="header-with-master">
                <input
                  type="checkbox"
                  class="soa-master-checkbox master-grand-checkbox"
                  :checked="isAllMatrixAssigned"
                  :indeterminate="isMatrixPartialAssigned"
                  aria-label="Master select all matrix cells"
                  @change="toggleGrandMaster"
                  @click.stop="toggleGrandMaster"
                />
                <span>Clinical Procedure / Assessment</span>
              </div>
            </th>
            <th class="col-domain-header" scope="col">CDASH</th>
            <th
              v-for="visit in visits"
              :key="visit.visit_name"
              class="col-visit-header"
              scope="col"
            >
              <div class="visit-header-top">
                <input
                  type="checkbox"
                  class="soa-master-checkbox master-col-checkbox"
                  :checked="isColumnAllAssigned(visit.visit_name)"
                  :indeterminate="isColumnPartialAssigned(visit.visit_name)"
                  :aria-label="`Toggle all procedures for ${visit.visit_name}`"
                  @change="toggleColumnMaster(visit.visit_name)"
                  @click.stop="toggleColumnMaster(visit.visit_name)"
                />
                <div class="visit-header-title">
                  {{ visit.visit_name }}
                </div>
              </div>
              <div class="visit-header-meta">Day {{ visit.target_day }}</div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(act, rIdx) in localActivities"
            :key="act.activity_name"
            class="soa-row"
          >
            <td class="cell-procedure-name">
              <div class="row-procedure-flex">
                <input
                  type="checkbox"
                  class="soa-master-checkbox master-row-checkbox"
                  :checked="isRowAllAssigned(act)"
                  :indeterminate="isRowPartialAssigned(act)"
                  :aria-label="`Toggle all visits for ${act.activity_name}`"
                  @change="toggleRowMaster(act)"
                  @click.stop="toggleRowMaster(act)"
                />
                <div>
                  <span class="procedure-label">{{ act.activity_name }}</span>
                  <span
                    v-if="act.biomedical_concept_code"
                    class="concept-code-tag"
                  >
                    {{ act.biomedical_concept_code }}
                  </span>
                </div>
              </div>
            </td>
            <td class="cell-domain-tag">
              <span
                class="cdash-badge"
                :class="`cdash-${act.cdash_domain.toLowerCase()}`"
              >
                {{ act.cdash_domain }}
              </span>
            </td>
            <td
              v-for="(visit, cIdx) in visits"
              :key="visit.visit_name"
              class="cell-toggle"
              :class="{
                active: isAssigned(act, visit.visit_name),
                'in-range': isCellInSelectedRange(rIdx, cIdx),
              }"
              :data-r-idx="rIdx"
              :data-c-idx="cIdx"
              @pointerdown.prevent="handleCellPointerDown(rIdx, cIdx, $event)"
              @pointermove="handleCellPointerMove($event)"
              @pointerup="handleCellPointerUp(rIdx, cIdx, $event)"
              @pointercancel="handleCellPointerCancel($event)"
              @mousedown="handleCellPointerDown(rIdx, cIdx, $event)"
              @mouseenter="handleCellMouseEnter(rIdx, cIdx)"
              @mouseup="handleCellPointerUp(rIdx, cIdx, $event)"
              @click="
                handleCellClick(act, visit.visit_name, rIdx, cIdx, $event)
              "
            >
              <input
                type="checkbox"
                class="soa-checkbox"
                :checked="isAssigned(act, visit.visit_name)"
                :aria-label="`Schedule ${act.activity_name} at ${visit.visit_name}`"
                @click.stop="
                  handleCellClick(act, visit.visit_name, rIdx, cIdx, $event)
                "
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch, onUnmounted } from "vue";

const props = defineProps({
  activities: {
    type: Array,
    default: () => [],
  },
  visits: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["update:activities", "add-visit"]);

const localActivities = ref([]);
const showAddProcedure = ref(false);
const showAddVisit = ref(false);
const showCloneGroup = ref(false);
const newProcedureName = ref("");
const newProcedureDomain = ref("VS");
const newVisitName = ref("");
const newVisitDay = ref(1);

const cloneSourceDomain = ref("VS");
const cloneNameSuffix = ref("(Cohort B)");

// Selection & Drag State
const lastClickedCell = ref(null); // { rIdx, cIdx }
const dragStartCell = ref(null); // { rIdx, cIdx }
const dragCurrentCell = ref(null); // { rIdx, cIdx }
const isDragging = ref(false);
const selectedRangeBounds = ref(null); // { minRow, maxRow, minCol, maxCol }

watch(
  () => props.activities,
  (newVal) => {
    localActivities.value = JSON.parse(JSON.stringify(newVal || []));
  },
  { immediate: true, deep: true }
);

const isAssigned = (act, visitName) => {
  return (
    Array.isArray(act.assigned_visit_names) &&
    act.assigned_visit_names.includes(visitName)
  );
};

const toggleAssignment = (act, visitName) => {
  if (!act.assigned_visit_names) {
    act.assigned_visit_names = [];
  }
  const idx = act.assigned_visit_names.indexOf(visitName);
  if (idx >= 0) {
    act.assigned_visit_names.splice(idx, 1);
  } else {
    act.assigned_visit_names.push(visitName);
  }
  emit("update:activities", localActivities.value);
};

// Master Row / Column Checkbox Logic
const isRowAllAssigned = (act) => {
  if (!props.visits || props.visits.length === 0) return false;
  return props.visits.every((v) => isAssigned(act, v.visit_name));
};

const isRowPartialAssigned = (act) => {
  if (!props.visits || props.visits.length === 0) return false;
  const count = props.visits.filter((v) =>
    isAssigned(act, v.visit_name)
  ).length;
  return count > 0 && count < props.visits.length;
};

const toggleRowMaster = (act) => {
  const allAssigned = isRowAllAssigned(act);
  if (allAssigned) {
    act.assigned_visit_names = [];
  } else {
    act.assigned_visit_names = props.visits.map((v) => v.visit_name);
  }
  emit("update:activities", localActivities.value);
};

const isColumnAllAssigned = (visitName) => {
  if (!localActivities.value || localActivities.value.length === 0)
    return false;
  return localActivities.value.every((act) => isAssigned(act, visitName));
};

const isColumnPartialAssigned = (visitName) => {
  if (!localActivities.value || localActivities.value.length === 0)
    return false;
  const count = localActivities.value.filter((act) =>
    isAssigned(act, visitName)
  ).length;
  return count > 0 && count < localActivities.value.length;
};

const toggleColumnMaster = (visitName) => {
  const allAssigned = isColumnAllAssigned(visitName);
  for (const act of localActivities.value) {
    if (!act.assigned_visit_names) {
      act.assigned_visit_names = [];
    }
    const idx = act.assigned_visit_names.indexOf(visitName);
    if (allAssigned) {
      if (idx >= 0) act.assigned_visit_names.splice(idx, 1);
    } else {
      if (idx < 0) act.assigned_visit_names.push(visitName);
    }
  }
  emit("update:activities", localActivities.value);
};

const isAllMatrixAssigned = computed(() => {
  if (localActivities.value.length === 0 || props.visits.length === 0)
    return false;
  return localActivities.value.every((act) => isRowAllAssigned(act));
});

const isMatrixPartialAssigned = computed(() => {
  if (localActivities.value.length === 0 || props.visits.length === 0)
    return false;
  const totalCells = localActivities.value.length * props.visits.length;
  const assigned = totalAssignedCells.value;
  return assigned > 0 && assigned < totalCells;
});

const toggleGrandMaster = () => {
  const allAssigned = isAllMatrixAssigned.value;
  const visitNames = props.visits.map((v) => v.visit_name);
  for (const act of localActivities.value) {
    act.assigned_visit_names = allAssigned ? [] : [...visitNames];
  }
  emit("update:activities", localActivities.value);
};

// Range Selection Handlers
const computedRangeBounds = computed(() => {
  if (selectedRangeBounds.value) return selectedRangeBounds.value;
  if (isDragging.value && dragStartCell.value && dragCurrentCell.value) {
    return {
      minRow: Math.min(dragStartCell.value.rIdx, dragCurrentCell.value.rIdx),
      maxRow: Math.max(dragStartCell.value.rIdx, dragCurrentCell.value.rIdx),
      minCol: Math.min(dragStartCell.value.cIdx, dragCurrentCell.value.cIdx),
      maxCol: Math.max(dragStartCell.value.cIdx, dragCurrentCell.value.cIdx),
    };
  }
  return null;
});

const isCellInSelectedRange = (rIdx, cIdx) => {
  const bounds = computedRangeBounds.value;
  if (!bounds) return false;
  return (
    rIdx >= bounds.minRow &&
    rIdx <= bounds.maxRow &&
    cIdx >= bounds.minCol &&
    cIdx <= bounds.maxCol
  );
};

const selectedRangeCellCount = computed(() => {
  const bounds = computedRangeBounds.value;
  if (!bounds) return 0;
  return (
    (bounds.maxRow - bounds.minRow + 1) * (bounds.maxCol - bounds.minCol + 1)
  );
});

const handleCellPointerDown = (rIdx, cIdx, event) => {
  if (event.shiftKey && lastClickedCell.value) {
    // Shift+Click range selection
    selectedRangeBounds.value = {
      minRow: Math.min(lastClickedCell.value.rIdx, rIdx),
      maxRow: Math.max(lastClickedCell.value.rIdx, rIdx),
      minCol: Math.min(lastClickedCell.value.cIdx, cIdx),
      maxCol: Math.max(lastClickedCell.value.cIdx, cIdx),
    };
  } else {
    isDragging.value = true;
    dragStartCell.value = { rIdx, cIdx };
    dragCurrentCell.value = { rIdx, cIdx };
    selectedRangeBounds.value = null;
    if (event.target && typeof event.target.setPointerCapture === "function") {
      try {
        event.target.setPointerCapture(event.pointerId);
      } catch {
        // ignore
      }
    }
    window.addEventListener("pointerup", handleGlobalPointerUpOnce);
    window.addEventListener("pointercancel", handleGlobalPointerCancelOnce);
  }
};

const handleCellPointerMove = (event) => {
  if (isDragging.value) {
    const el = document.elementFromPoint(event.clientX, event.clientY);
    const cellEl = el?.closest(".cell-toggle");
    if (
      cellEl &&
      cellEl.dataset.rIdx !== undefined &&
      cellEl.dataset.cIdx !== undefined
    ) {
      const r = parseInt(cellEl.dataset.rIdx, 10);
      const c = parseInt(cellEl.dataset.cIdx, 10);
      if (!isNaN(r) && !isNaN(c)) {
        dragCurrentCell.value = { rIdx: r, cIdx: c };
      }
    }
  }
};

const handleCellMouseEnter = (rIdx, cIdx) => {
  if (isDragging.value) {
    dragCurrentCell.value = { rIdx, cIdx };
  }
};

const finishPointerDrag = (event) => {
  if (isDragging.value) {
    if (
      dragStartCell.value &&
      dragCurrentCell.value &&
      (dragStartCell.value.rIdx !== dragCurrentCell.value.rIdx ||
        dragStartCell.value.cIdx !== dragCurrentCell.value.cIdx)
    ) {
      selectedRangeBounds.value = {
        minRow: Math.min(dragStartCell.value.rIdx, dragCurrentCell.value.rIdx),
        maxRow: Math.max(dragStartCell.value.rIdx, dragCurrentCell.value.rIdx),
        minCol: Math.min(dragStartCell.value.cIdx, dragCurrentCell.value.cIdx),
        maxCol: Math.max(dragStartCell.value.cIdx, dragCurrentCell.value.cIdx),
      };
    }
    isDragging.value = false;
  }
  if (
    event &&
    event.target &&
    typeof event.target.releasePointerCapture === "function"
  ) {
    try {
      if (
        event.target.hasPointerCapture &&
        event.target.hasPointerCapture(event.pointerId)
      ) {
        event.target.releasePointerCapture(event.pointerId);
      }
    } catch {
      // ignore
    }
  }
};

const handleGlobalPointerUpOnce = (event) => {
  finishPointerDrag(event);
  window.removeEventListener("pointerup", handleGlobalPointerUpOnce);
  window.removeEventListener("pointercancel", handleGlobalPointerCancelOnce);
};

const handleGlobalPointerCancelOnce = (event) => {
  finishPointerDrag(event);
  window.removeEventListener("pointerup", handleGlobalPointerUpOnce);
  window.removeEventListener("pointercancel", handleGlobalPointerCancelOnce);
};

const handleCellPointerUp = (rIdx, cIdx, event) => {
  if (isDragging.value) {
    if (
      dragStartCell.value &&
      (dragStartCell.value.rIdx !== rIdx || dragStartCell.value.cIdx !== cIdx)
    ) {
      selectedRangeBounds.value = {
        minRow: Math.min(dragStartCell.value.rIdx, rIdx),
        maxRow: Math.max(dragStartCell.value.rIdx, rIdx),
        minCol: Math.min(dragStartCell.value.cIdx, cIdx),
        maxCol: Math.max(dragStartCell.value.cIdx, cIdx),
      };
    }
    isDragging.value = false;
  }
  finishPointerDrag(event);
};

const handleCellPointerCancel = (event) => {
  finishPointerDrag(event);
};

const handleCellClick = (act, visitName, rIdx, cIdx, event) => {
  if (event.shiftKey && lastClickedCell.value) {
    selectedRangeBounds.value = {
      minRow: Math.min(lastClickedCell.value.rIdx, rIdx),
      maxRow: Math.max(lastClickedCell.value.rIdx, rIdx),
      minCol: Math.min(lastClickedCell.value.cIdx, cIdx),
      maxCol: Math.max(lastClickedCell.value.cIdx, cIdx),
    };
  } else if (
    !selectedRangeBounds.value ||
    (selectedRangeBounds.value && selectedRangeCellCount.value <= 1)
  ) {
    toggleAssignment(act, visitName);
    lastClickedCell.value = { rIdx, cIdx };
  }
};

const applyRangeState = (targetState) => {
  const bounds = computedRangeBounds.value;
  if (!bounds) return;

  for (let r = bounds.minRow; r <= bounds.maxRow; r++) {
    const act = localActivities.value[r];
    if (!act) continue;
    if (!act.assigned_visit_names) act.assigned_visit_names = [];

    for (let c = bounds.minCol; c <= bounds.maxCol; c++) {
      const visit = props.visits[c];
      if (!visit) continue;

      const idx = act.assigned_visit_names.indexOf(visit.visit_name);
      if (targetState && idx < 0) {
        act.assigned_visit_names.push(visit.visit_name);
      } else if (!targetState && idx >= 0) {
        act.assigned_visit_names.splice(idx, 1);
      }
    }
  }

  emit("update:activities", localActivities.value);
};

const clearRangeSelection = () => {
  selectedRangeBounds.value = null;
  dragStartCell.value = null;
  dragCurrentCell.value = null;
  isDragging.value = false;
};

// Procedure Group Batch Cloning
const availableDomains = computed(() => {
  const set = new Set(localActivities.value.map((a) => a.cdash_domain));
  return Array.from(set);
});

const executeProcedureGroupCloning = () => {
  const sourceGroup = localActivities.value.filter(
    (a) => a.cdash_domain === cloneSourceDomain.value
  );
  if (sourceGroup.length === 0) return;

  const newClonedActivities = sourceGroup.map((act) => {
    return {
      activity_name: `${act.activity_name} ${cloneNameSuffix.value.trim()}`,
      cdash_domain: act.cdash_domain,
      biomedical_concept_code: act.biomedical_concept_code,
      assigned_visit_names: [...(act.assigned_visit_names || [])],
    };
  });

  localActivities.value.push(...newClonedActivities);
  showCloneGroup.value = false;
  emit("update:activities", localActivities.value);
};

const totalAssignedCells = computed(() => {
  return localActivities.value.reduce(
    (acc, curr) => acc + (curr.assigned_visit_names?.length || 0),
    0
  );
});

const addCustomProcedure = () => {
  if (!newProcedureName.value.trim()) return;
  localActivities.value.push({
    activity_name: newProcedureName.value.trim(),
    cdash_domain: newProcedureDomain.value,
    biomedical_concept_code: null,
    assigned_visit_names: props.visits.map((v) => v.visit_name),
  });
  newProcedureName.value = "";
  showAddProcedure.value = false;
  emit("update:activities", localActivities.value);
};

const addCustomVisit = () => {
  if (!newVisitName.value.trim()) return;
  emit("add-visit", {
    visit_name: newVisitName.value.trim(),
    epoch_name: "Treatment Epoch",
    target_day: newVisitDay.value || 1,
    window_lower_days: 2,
    window_upper_days: 2,
    is_mandatory: true,
  });
  newVisitName.value = "";
  showAddVisit.value = false;
};

onUnmounted(() => {
  window.removeEventListener("pointerup", handleGlobalPointerUpOnce);
  window.removeEventListener("pointercancel", handleGlobalPointerCancelOnce);
});
</script>

<style scoped>
.soa-matrix-editor {
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 16px;
  width: 100%;
  user-select: none;
}

.matrix-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.toolbar-stats {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: center;
}

.stat-badge {
  background-color: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 0.85rem;
  color: #334155;
}

.stat-badge.highlight {
  background-color: #ecfdf5;
  border-color: #a7f3d0;
  color: #065f46;
}

.stat-badge.range-badge {
  background-color: #eff6ff;
  border-color: #bfdbfe;
  color: #1e40af;
}

.toolbar-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.range-action-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: #eff6ff;
  border: 1px solid #93c5fd;
  border-radius: 6px;
  padding: 8px 14px;
  margin-bottom: 14px;
  flex-wrap: wrap;
  gap: 10px;
}

.range-banner-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #1e40af;
}

.range-banner-buttons {
  display: flex;
  gap: 8px;
}

.inline-add-box {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  padding: 12px;
  border-radius: 6px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.clone-group-box {
  background-color: #f0fdf4;
  border-color: #86efac;
}

.inline-form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.inline-form-group label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #475569;
}

.input-text,
.input-select {
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.85rem;
}

.matrix-table-container {
  overflow-x: auto;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  touch-action: none;
}

.soa-interactive-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  text-align: left;
  touch-action: none;
}

.soa-interactive-table th,
.soa-interactive-table td {
  padding: 10px 12px;
  border: 1px solid var(--border, #e2e8f0);
}

.col-procedure-header {
  background-color: #f8fafc;
  color: var(--primary, #0f172a);
  font-weight: 700;
  min-width: 260px;
}

.header-with-master {
  display: flex;
  align-items: center;
  gap: 10px;
}

.row-procedure-flex {
  display: flex;
  align-items: center;
  gap: 10px;
}

.col-domain-header {
  background-color: #f8fafc;
  width: 70px;
  text-align: center;
  font-weight: 700;
}

.col-visit-header {
  background-color: #f8fafc;
  text-align: center;
  min-width: 130px;
}

.visit-header-top {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.visit-header-title {
  font-weight: 600;
  color: #1e293b;
}

.visit-header-meta {
  font-size: 0.75rem;
  color: #64748b;
  margin-top: 2px;
}

.soa-row:hover {
  background-color: #f8fafc;
}

.cell-procedure-name {
  font-weight: 500;
}

.procedure-label {
  display: block;
}

.concept-code-tag {
  display: inline-block;
  font-size: 0.7rem;
  color: #64748b;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  margin-top: 2px;
}

.cell-domain-tag {
  text-align: center;
}

.cdash-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
  background-color: #e2e8f0;
  color: #334155;
}

.cdash-vs {
  background-color: #e0f2fe;
  color: #0369a1;
}
.cdash-eg {
  background-color: #fef3c7;
  color: #b45309;
}
.cdash-lb {
  background-color: #f3e8ff;
  color: #7e22ce;
}
.cdash-qs {
  background-color: #dcfce7;
  color: #15803d;
}
.cdash-pe {
  background-color: #fee2e2;
  color: #b91c1c;
}
.cdash-ae {
  background-color: #ffedd5;
  color: #c2410c;
}
.cdash-dm {
  background-color: #f1f5f9;
  color: #475569;
}

.cell-toggle {
  text-align: center;
  cursor: pointer;
  transition: background-color 0.12s ease;
  position: relative;
  touch-action: none;
  min-width: 44px;
  min-height: 44px;
}

.cell-toggle.active {
  background-color: #ecfdf5;
}

.cell-toggle.in-range {
  background-color: #dbeafe !important;
  outline: 2px solid #3b82f6;
  outline-offset: -2px;
}

.soa-checkbox {
  width: 24px;
  height: 24px;
  min-width: 24px;
  min-height: 24px;
  cursor: pointer;
  accent-color: #059669;
}

.soa-master-checkbox {
  width: 24px;
  height: 24px;
  min-width: 24px;
  min-height: 24px;
  cursor: pointer;
  accent-color: #2563eb;
}

.soa-matrix-editor .btn {
  min-height: 44px;
  min-width: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
}
</style>
