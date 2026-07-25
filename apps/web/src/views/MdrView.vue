<template>
  <div id="section-mdr" class="dashboard-section active">
    <div class="section-header">
      <h2>MDR / Protocol Visualizer</h2>
      <p>
        Unify upstream clinical study definitions (USDM metadata) directly into
        a visual Schedule of Activities matrix.
      </p>
    </div>

    <div class="grid-2">
      <!-- USDM study JSON -->
      <div class="card json-editor-container">
        <div class="card-title">
          <span>CDISC USDM Study Protocol JSON</span>
          <button
            id="btn-reset-usdm"
            class="badge"
            style="
              cursor: pointer;
              background-color: var(--accent);
              color: white;
              border: none;
            "
            @click="resetUsdm"
          >
            Reset Mock JSON
          </button>
        </div>
        <textarea
          id="usdm-json"
          v-model="usdmText"
          class="json-editor"
          spellcheck="false"
        />
        <p style="font-size: 0.8rem; color: #64748b; margin-top: 8px">
          Edit this mock USDM JSON definition and click "Update Visualizer"
          below to dynamically re-render the Schedule of Activities Matrix.
        </p>
        <div style="margin-top: 12px; display: flex; justify-content: flex-end">
          <button
            id="btn-update-soa"
            class="btn btn-primary"
            @click="updateSoa"
          >
            Update Visualizer
          </button>
        </div>
      </div>

      <!-- Schedule of Activities table -->
      <div class="card">
        <div class="card-title">Schedule of Activities (SoA) Matrix</div>
        <div id="soa-matrix-container" v-html="matrixHtml" />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { createClinicalVisitMatrix } from "ui";

const store = useClinicalStore();

const usdmText = ref(JSON.stringify(store.currentUsdm, null, 2));

watch(
  () => store.currentUsdm,
  (newVal) => {
    usdmText.value = JSON.stringify(newVal, null, 2);
  },
  { deep: true }
);

const matrixHtml = computed(() => {
  try {
    const parsed = JSON.parse(usdmText.value);
    if (parsed.rows || parsed.encounters || parsed.epochs) {
      return createClinicalVisitMatrix(parsed);
    }
    return createClinicalVisitMatrix({
      visits: parsed.visits || [],
      forms: parsed.forms || [],
    });
  } catch {
    return `<div class="clinical-visit-matrix-error">Invalid JSON format</div>`;
  }
});

function resetUsdm() {
  const defaultUsdm = {
    studyId: "STUDY-USDM-001",
    studyTitle: "Phase II Trial of Cadence-001 in Essential Hypertension",
    objectives: [
      {
        id: "OBJ-001",
        type: "Primary",
        description:
          "Evaluate the reduction of mean sitting Systolic Blood Pressure (SBP) from baseline.",
      },
      {
        id: "OBJ-002",
        type: "Secondary",
        description:
          "Evaluate safety and tolerability of daily oral administration of Cadence-001.",
      },
    ],
    arms: [
      { arm_id: "ARM-A", arm_name: "Arm A: Active 10mg daily" },
      { arm_id: "ARM-B", arm_name: "Arm B: Placebo Control" },
    ],
    epochs: [
      { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
      {
        epoch_id: "EP-TRT-A",
        epoch_name: "Treatment Phase",
        sequence: 2,
        arm_id: "ARM-A",
      },
      {
        epoch_id: "EP-TRT-B",
        epoch_name: "Treatment Phase",
        sequence: 2,
        arm_id: "ARM-B",
      },
    ],
    encounters: [
      {
        encounter_id: "V-SCR",
        encounter_name: "Day -7 to -1",
        epoch_id: "EP-SCR",
        sequence: 1,
      },
      {
        encounter_id: "V-TRT-A1",
        encounter_name: "Week 2",
        epoch_id: "EP-TRT-A",
        sequence: 2,
      },
      {
        encounter_id: "V-TRT-A2",
        encounter_name: "Week 4",
        epoch_id: "EP-TRT-A",
        sequence: 3,
      },
      {
        encounter_id: "V-TRT-B1",
        encounter_name: "Week 2",
        epoch_id: "EP-TRT-B",
        sequence: 4,
      },
    ],
    rows: [
      {
        activity_id: "ACT-DEM",
        activity_name: "Informed Consent & Demographics",
        cells: [
          { encounter_id: "V-SCR", is_applicable: true, details: "Mandatory" },
          { encounter_id: "V-TRT-A1", is_applicable: false },
          { encounter_id: "V-TRT-A2", is_applicable: false },
          { encounter_id: "V-TRT-B1", is_applicable: false },
        ],
      },
      {
        activity_id: "ACT-VS",
        activity_name: "Vital Signs (BP & Pulse)",
        cells: [
          { encounter_id: "V-SCR", is_applicable: true, details: "Day -7" },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Within 10 mins",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Conditional",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Within 10 mins",
          },
        ],
      },
      {
        activity_id: "ACT-AE",
        activity_name: "Adverse Events Check",
        cells: [
          { encounter_id: "V-SCR", is_applicable: false },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Continuous",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Continuous",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Optional",
          },
        ],
      },
      {
        activity_id: "ACT-MED",
        activity_name: "Study Medication Log",
        cells: [
          { encounter_id: "V-SCR", is_applicable: false },
          {
            encounter_id: "V-TRT-A1",
            is_applicable: true,
            details: "Daily entry",
          },
          {
            encounter_id: "V-TRT-A2",
            is_applicable: true,
            details: "Daily entry",
          },
          {
            encounter_id: "V-TRT-B1",
            is_applicable: true,
            details: "Daily entry",
          },
        ],
      },
    ],
  };
  store.currentUsdm = JSON.parse(JSON.stringify(defaultUsdm));
  store.addLedgerBlock(
    "USDM_RESET",
    { studyId: store.currentUsdm.studyId },
    "User reset study protocol schema back to default USDM v3.0 specs."
  );
}

function updateSoa() {
  try {
    const parsed = JSON.parse(usdmText.value);
    if (!parsed.visits && !parsed.encounters) {
      alert(
        "Invalid USDM Structure! Must contain either 'visits' or 'encounters'."
      );
      return;
    }
    store.currentUsdm = parsed;
    store.addLedgerBlock(
      "USDM_UPDATE",
      { studyId: store.currentUsdm.studyId },
      "User modified and compiled a custom USDM study protocol."
    );
  } catch (err) {
    alert("Parsing Error: " + err.message);
  }
}
</script>
