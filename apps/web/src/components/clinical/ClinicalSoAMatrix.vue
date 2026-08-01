<template>
  <div
    v-if="hasError"
    class="clinical-visit-matrix-error"
  >
    Invalid SoA matrix data.
  </div>
  <div
    v-else-if="cols.length === 0"
    class="clinical-visit-matrix-error"
  >
    No encounters defined for SoA matrix.
  </div>
  <table
    v-else
    class="clinical-visit-matrix clinical-soa-matrix"
  >
    <thead>
      <!-- Arm Row (only if arms are defined) -->
      <tr v-if="hasArms">
        <th
          scope="col"
          :rowspan="totalHeaderRows"
          class="corner-header"
        >
          Form / Procedure
        </th>
        <th
          v-for="g in armGroups"
          :key="g.id"
          scope="col"
          :colspan="g.colspan"
          class="grouped-header arm-header"
        >
          {{ g.name }}
        </th>
      </tr>

      <!-- Epoch Row -->
      <tr v-if="hasArms">
        <th
          v-for="g in epochGroups"
          :key="g.key"
          scope="col"
          :colspan="g.colspan"
          class="grouped-header epoch-header"
        >
          {{ g.name }}
        </th>
      </tr>
      <tr v-else>
        <th
          scope="col"
          :rowspan="totalHeaderRows"
          class="corner-header"
        >
          Form / Procedure
        </th>
        <th
          v-for="g in epochGroups"
          :key="g.key"
          scope="col"
          :colspan="g.colspan"
          class="grouped-header epoch-header"
        >
          {{ g.name }}
        </th>
      </tr>

      <!-- Encounter/Visit Row -->
      <tr>
        <th
          v-if="!hasArms"
          style="display: none"
        />
        <th
          v-for="c in cols"
          :key="c.encounter.encounter_id"
          scope="col"
          class="encounter-header"
        >
          {{ c.encounter.encounter_name }}
        </th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="row in rows"
        :key="row.activity_id"
      >
        <th scope="row">
          {{ row.activity_name }}
        </th>
        <td
          v-for="col in cols"
          :key="col.encounter.encounter_id"
          :class="getCellClass(row, col.encounter.encounter_id)"
        >
          <template v-if="isApplicable(row, col.encounter.encounter_id)">
            ✓
            <span
              v-if="getCellDetails(row, col.encounter.encounter_id)"
              class="cell-details"
            >
              {{ getCellDetails(row, col.encounter.encounter_id) }}
            </span>
          </template>
          <template v-else>
            -
          </template>
        </td>
      </tr>
    </tbody>
  </table>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  soaData: {
    type: Object,
    default: null,
  },
});

const hasError = computed(() => {
  return !props.soaData || !props.soaData.rows;
});

const epochs = computed(() => props.soaData?.epochs || []);
const encounters = computed(() => props.soaData?.encounters || []);
const rows = computed(() => props.soaData?.rows || []);
const arms = computed(() => props.soaData?.arms || []);

const epochMap = computed(() => {
  const map = {};
  epochs.value.forEach((ep) => {
    map[ep.epoch_id] = ep;
  });
  return map;
});

const armMap = computed(() => {
  const map = {};
  arms.value.forEach((arm) => {
    map[arm.arm_id] = arm;
  });
  return map;
});

const cols = computed(() => {
  return encounters.value.map((enc) => {
    const epoch = epochMap.value[enc.epoch_id] || {
      epoch_id: enc.epoch_id,
      epoch_name: enc.epoch_id,
    };
    const armId = enc.arm_id || epoch.arm_id;
    const arm = armId
      ? armMap.value[armId] || { arm_id: armId, arm_name: armId }
      : null;

    return {
      arm,
      epoch,
      encounter: enc,
    };
  });
});

const hasArms = computed(() => {
  return cols.value.some((c) => c.arm !== null);
});

const totalHeaderRows = computed(() => {
  return hasArms.value ? 3 : 2;
});

const armGroups = computed(() => {
  const groups = [];
  let currentGroup = null;

  cols.value.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const armName = col.arm ? col.arm.arm_name : "Common / Shared";

    if (!currentGroup || currentGroup.id !== armId) {
      if (currentGroup) {
        groups.push(currentGroup);
      }
      currentGroup = {
        id: armId,
        name: armName,
        colspan: 0,
      };
    }
    currentGroup.colspan++;
  });

  if (currentGroup) {
    groups.push(currentGroup);
  }
  return groups;
});

const epochGroups = computed(() => {
  const groups = [];
  let currentGroup = null;

  cols.value.forEach((col) => {
    const armId = col.arm ? col.arm.arm_id : "shared";
    const epochId = col.epoch.epoch_id;
    const epochName = col.epoch.epoch_name;
    const groupKey = `${armId}_${epochId}`;

    if (!currentGroup || currentGroup.key !== groupKey) {
      if (currentGroup) {
        groups.push(currentGroup);
      }
      currentGroup = {
        key: groupKey,
        name: epochName,
        colspan: 0,
      };
    }
    currentGroup.colspan++;
  });

  if (currentGroup) {
    groups.push(currentGroup);
  }
  return groups;
});

function getCell(row, encounterId) {
  return (
    row.cells?.find((c) => c.encounter_id === encounterId) || {
      is_applicable: false,
    }
  );
}

function isApplicable(row, encounterId) {
  return getCell(row, encounterId).is_applicable;
}

function getCellDetails(row, encounterId) {
  return getCell(row, encounterId).details || "";
}

function getCellClass(row, encounterId) {
  const cell = getCell(row, encounterId);
  if (!cell.is_applicable) {
    return "status-n-a";
  }

  let cellClass = "status-applicable";
  if (cell.details) {
    const detailsLower = cell.details.toLowerCase();
    if (detailsLower.includes("conditional")) {
      cellClass = "status-conditional";
    } else if (detailsLower.includes("optional")) {
      cellClass = "status-optional";
    }
  }
  return cellClass;
}
</script>
