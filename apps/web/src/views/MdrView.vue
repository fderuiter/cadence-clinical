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
    visits: [
      "Screening",
      "Baseline (Day 1)",
      "Week 2",
      "Week 4",
      "End of Study",
    ],
    forms: [
      {
        name: "Demographics",
        statuses: ["Complete", "N/A", "N/A", "N/A", "N/A"],
      },
      {
        name: "Vital Signs (BP & Pulse)",
        statuses: ["Complete", "Pending", "Pending", "Pending", "Pending"],
      },
      {
        name: "Adverse Events Check",
        statuses: ["N/A", "Complete", "Pending", "Pending", "Complete"],
      },
      {
        name: "Study Medication Log",
        statuses: ["N/A", "Complete", "Complete", "Complete", "Complete"],
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
    if (!parsed.visits || !parsed.forms) {
      alert("Invalid USDM Structure! Must contain 'visits' and 'forms'.");
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
