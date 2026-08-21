<template>
  <div class="soa-matrix-editor">
    <div class="matrix-toolbar">
      <div class="toolbar-stats">
        <span class="stat-badge"><strong>{{ localActivities.length }}</strong> Procedures</span>
        <span class="stat-badge"><strong>{{ visits.length }}</strong> Visits</span>
        <span class="stat-badge highlight"><strong>{{ totalAssignedCells }}</strong> Scheduled Activities</span>
      </div>
      <div class="toolbar-actions">
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="showAddProcedure = !showAddProcedure"
        >
          + Add Procedure
        </button>
        <button
          type="button"
          class="btn btn-secondary btn-sm"
          @click="showAddVisit = !showAddVisit"
        >
          + Add Visit
        </button>
      </div>
    </div>

    <!-- Inline Add Procedure Form -->
    <div
      v-if="showAddProcedure"
      class="inline-add-box"
    >
      <div class="inline-form-group">
        <label for="new-proc-name">Procedure Name</label>
        <input
          id="new-proc-name"
          v-model="newProcedureName"
          placeholder="e.g. Pharmacokinetic Blood Sampling"
          class="input-text"
        >
      </div>
      <div class="inline-form-group">
        <label for="new-proc-domain">CDASH Domain</label>
        <select
          id="new-proc-domain"
          v-model="newProcedureDomain"
          class="input-select"
        >
          <option value="VS">
            VS - Vital Signs
          </option>
          <option value="EG">
            EG - ECG
          </option>
          <option value="LB">
            LB - Laboratory
          </option>
          <option value="QS">
            QS - Questionnaire / VAS
          </option>
          <option value="PE">
            PE - Physical Exam / Body Map
          </option>
          <option value="AE">
            AE - Adverse Events
          </option>
          <option value="CM">
            CM - Concomitant Medications
          </option>
          <option value="DM">
            DM - Demographics
          </option>
          <option value="EX">
            EX - Exposure
          </option>
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
    <div
      v-if="showAddVisit"
      class="inline-add-box"
    >
      <div class="inline-form-group">
        <label for="new-visit-name">Visit Name</label>
        <input
          id="new-visit-name"
          v-model="newVisitName"
          placeholder="e.g. Visit 4 / Week 8"
          class="input-text"
        >
      </div>
      <div class="inline-form-group">
        <label for="new-visit-day">Target Day</label>
        <input
          id="new-visit-day"
          v-model.number="newVisitDay"
          type="number"
          class="input-text"
          style="width: 90px"
        >
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

    <!-- High Density Matrix Table -->
    <div class="matrix-table-container">
      <table class="soa-interactive-table">
        <thead>
          <tr>
            <th
              class="col-procedure-header"
              scope="col"
            >
              Clinical Procedure / Assessment
            </th>
            <th
              class="col-domain-header"
              scope="col"
            >
              CDASH
            </th>
            <th
              v-for="visit in visits"
              :key="visit.visit_name"
              class="col-visit-header"
              scope="col"
            >
              <div class="visit-header-title">
                {{ visit.visit_name }}
              </div>
              <div class="visit-header-meta">
                Day {{ visit.target_day }}
              </div>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="act in localActivities"
            :key="act.activity_name"
            class="soa-row"
          >
            <td class="cell-procedure-name">
              <span class="procedure-label">{{ act.activity_name }}</span>
              <span
                v-if="act.biomedical_concept_code"
                class="concept-code-tag"
              >
                {{ act.biomedical_concept_code }}
              </span>
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
              v-for="visit in visits"
              :key="visit.visit_name"
              class="cell-toggle"
              :class="{ active: isAssigned(act, visit.visit_name) }"
              @click="toggleAssignment(act, visit.visit_name)"
            >
              <input
                type="checkbox"
                class="soa-checkbox"
                :checked="isAssigned(act, visit.visit_name)"
                :aria-label="`Schedule ${act.activity_name} at ${visit.visit_name}`"
                @click.stop="toggleAssignment(act, visit.visit_name)"
              >
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
const newProcedureName = ref("");
const newProcedureDomain = ref("VS");
const newVisitName = ref("");
const newVisitDay = ref(1);

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
</script>

<style scoped>
.soa-matrix-editor {
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 16px;
  width: 100%;
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

.toolbar-actions {
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
}

.soa-interactive-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  text-align: left;
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
  min-width: 240px;
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
  transition: background-color 0.15s ease;
}

.cell-toggle.active {
  background-color: #ecfdf5;
}

.soa-checkbox {
  width: 18px;
  height: 18px;
  cursor: pointer;
  accent-color: #059669;
}
</style>
