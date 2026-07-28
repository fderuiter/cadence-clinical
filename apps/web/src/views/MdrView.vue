<template>
  <div id="section-mdr" class="dashboard-section active">
    <div class="section-header">
      <h2>MDR / Protocol Visualizer & Interactive SoA Builder</h2>
      <p>
        Unify upstream clinical study definitions (USDM metadata) directly into
        a visual Schedule of Activities matrix or author elements directly.
      </p>
    </div>

    <!-- Interactive Builder Controls -->
    <div class="card" style="margin-bottom: 24px; padding: 16px">
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 12px;
        "
      >
        <h3 style="font-weight: bold; margin: 0; color: var(--primary)">
          SoA Authoring & Builder Workspace
        </h3>
        <button
          class="btn"
          :class="builderMode ? 'btn-primary' : 'btn-secondary'"
          @click="builderMode = !builderMode"
        >
          {{
            builderMode
              ? "Close Interactive Builder"
              : "🔧 Open Interactive Builder"
          }}
        </button>
      </div>

      <!-- Creator forms -->
      <div
        v-if="builderMode"
        style="
          margin-top: 16px;
          border-top: 1px solid var(--border);
          padding-top: 16px;
        "
      >
        <!-- Error alert banner -->
        <div
          v-if="store.soaError"
          style="
            background-color: #fef2f2;
            border: 1px solid #fca5a5;
            color: #b91c1c;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 16px;
            font-size: 0.9rem;
          "
        >
          <strong>API Sync Failed (Reverting to Sandbox Local Mode):</strong>
          {{ store.soaError }}
        </div>

        <div
          style="
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 16px;
          "
        >
          <!-- Arm Form -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">
              Add Study Arm
            </legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-arm-id">Arm ID</label>
              <input
                id="new-arm-id"
                v-model="newArm.id"
                type="text"
                placeholder="e.g. ARM-C"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-arm-name">Arm Name</label>
              <input
                id="new-arm-name"
                v-model="newArm.name"
                type="text"
                placeholder="e.g. Arm C: High Dose"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div
              class="form-group"
              style="margin-bottom: 8px; position: relative"
            >
              <label for="new-arm-concept">Arm Type Concept Code</label>
              <input
                id="new-arm-concept"
                v-model="newArm.concept_code"
                type="text"
                placeholder="Search Arm Type CT..."
                style="width: 100%; padding: 6px"
                @input="searchArmTerminology($event.target.value)"
              />
              <!-- Autocomplete Suggestion Dropdown -->
              <div
                v-if="armSuggestions.length > 0"
                class="autocomplete-dropdown"
                style="
                  position: absolute;
                  background: white;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                  width: 100%;
                  z-index: 100;
                  max-height: 150px;
                  overflow-y: auto;
                  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                "
              >
                <div
                  v-for="sug in armSuggestions"
                  :key="sug.concept_code"
                  style="
                    padding: 6px;
                    cursor: pointer;
                    border-bottom: 1px solid #f1f5f9;
                  "
                  @click="selectArmConcept(sug)"
                >
                  <strong>{{ sug.concept_code }}</strong> -
                  {{ sug.preferred_name }}
                </div>
              </div>
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="handleAddArm"
            >
              Add Arm
            </button>
          </fieldset>

          <!-- Epoch Form -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">Add Epoch</legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-epoch-id">Epoch ID</label>
              <input
                id="new-epoch-id"
                v-model="newEpoch.id"
                type="text"
                placeholder="e.g. EP-FLW"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-epoch-name">Epoch Name</label>
              <input
                id="new-epoch-name"
                v-model="newEpoch.name"
                type="text"
                placeholder="e.g. Follow-up"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-epoch-seq">Sequence</label>
              <input
                id="new-epoch-seq"
                v-model.number="newEpoch.sequence"
                type="number"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-epoch-arm">Associated Arm (Optional)</label>
              <select
                id="new-epoch-arm"
                v-model="newEpoch.arm_id"
                style="width: 100%; padding: 6px"
              >
                <option value="">-- None / Shared --</option>
                <option
                  v-for="arm in store.currentUsdm.arms"
                  :key="arm.arm_id"
                  :value="arm.arm_id"
                >
                  {{ arm.arm_name }}
                </option>
              </select>
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="handleAddEpoch"
            >
              Add Epoch
            </button>
          </fieldset>

          <!-- Visit Form -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">
              Add Visit / Encounter
            </legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-enc-id">Encounter ID</label>
              <input
                id="new-enc-id"
                v-model="newEnc.id"
                type="text"
                placeholder="e.g. V-WEEK6"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-enc-name">Encounter Name</label>
              <input
                id="new-enc-name"
                v-model="newEnc.name"
                type="text"
                placeholder="e.g. Week 6"
                style="width: 100%; padding: 6px"
              />
            </div>

            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-enc-seq">Sequence</label>
              <input
                id="new-enc-seq"
                v-model.number="newEnc.sequence"
                type="number"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div
              class="form-group"
              style="margin-bottom: 8px; position: relative"
            >
              <label for="new-enc-concept">Visit Type Concept Code</label>
              <input
                id="new-enc-concept"
                v-model="newEnc.concept_code"
                type="text"
                placeholder="Search Visit Type CT..."
                style="width: 100%; padding: 6px"
                @input="searchEncTerminology($event.target.value)"
              />
              <!-- Autocomplete Suggestion Dropdown -->
              <div
                v-if="encSuggestions.length > 0"
                class="autocomplete-dropdown"
                style="
                  position: absolute;
                  background: white;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                  width: 100%;
                  z-index: 100;
                  max-height: 150px;
                  overflow-y: auto;
                  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
                "
              >
                <div
                  v-for="sug in encSuggestions"
                  :key="sug.concept_code"
                  style="
                    padding: 6px;
                    cursor: pointer;
                    border-bottom: 1px solid #f1f5f9;
                  "
                  @click="selectEncConcept(sug)"
                >
                  <strong>{{ sug.concept_code }}</strong> -
                  {{ sug.preferred_name }}
                </div>
              </div>
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-enc-epoch">Associated Epoch</label>
              <select
                id="new-enc-epoch"
                v-model="newEnc.epoch_id"
                style="width: 100%; padding: 6px"
              >
                <option value="">-- Select Epoch --</option>
                <option
                  v-for="ep in store.currentUsdm.epochs"
                  :key="ep.epoch_id"
                  :value="ep.epoch_id"
                >
                  {{ ep.epoch_name }}
                </option>
              </select>
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="handleAddEncounter"
            >
              Add Visit
            </button>
          </fieldset>

          <!-- Procedure Form -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">
              Add Activity / Procedure
            </legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-proc-id">Activity ID</label>
              <input
                id="new-proc-id"
                v-model="newProc.id"
                type="text"
                placeholder="e.g. ACT-LAB"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-proc-name">Activity Name</label>
              <input
                id="new-proc-name"
                v-model="newProc.name"
                type="text"
                placeholder="e.g. Laboratory Blood Draw"
                style="width: 100%; padding: 6px"
              />
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="handleAddProcedure"
            >
              Add Procedure
            </button>
          </fieldset>
        </div>

        <!-- Custom Interactive Link & Triggering Conditions -->
        <fieldset
          style="
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 12px;
            margin-top: 16px;
          "
        >
          <legend style="font-weight: bold; padding: 0 6px">
            Applicability, Custom Timing, and Arm Filtering
          </legend>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px">
            <div class="form-group">
              <label for="link-procedure">Select Procedure</label>
              <select
                id="link-procedure"
                v-model="linkPayload.procedure_id"
                style="width: 100%; padding: 6px"
              >
                <option value="">-- Select Procedure --</option>
                <option
                  v-for="row in store.currentUsdm.rows"
                  :key="row.activity_id"
                  :value="row.activity_id"
                >
                  {{ row.activity_name }}
                </option>
              </select>
            </div>
            <div class="form-group">
              <label for="link-visit">Select Visit</label>
              <select
                id="link-visit"
                v-model="linkPayload.visit_id"
                style="width: 100%; padding: 6px"
              >
                <option value="">-- Select Visit --</option>
                <option
                  v-for="enc in store.currentUsdm.encounters"
                  :key="enc.encounter_id"
                  :value="enc.encounter_id"
                >
                  {{ enc.encounter_name }}
                </option>
              </select>
            </div>
          </div>
          <div class="form-group" style="margin-top: 8px">
            <label for="link-timing"
              >Custom Timing Window / Details (e.g. "Within 10 mins", "Day
              1")</label
            >
            <input
              id="link-timing"
              v-model="linkPayload.timing"
              type="text"
              placeholder="Leave empty for default applicability"
              style="width: 100%; padding: 6px"
            />
          </div>
          <div
            style="
              margin-top: 12px;
              display: flex;
              gap: 8px;
              justify-content: flex-end;
            "
          >
            <button
              class="btn"
              style="background-color: var(--error); color: white"
              @click="handleToggleApplicability(false)"
            >
              Remove Applicability
            </button>
            <button
              class="btn btn-primary"
              @click="handleToggleApplicability(true)"
            >
              Apply Applicability & Timing
            </button>
          </div>
        </fieldset>
      </div>
    </div>

    <!-- Visual protocol editor layout -->
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
        <div
          class="card-header"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
          "
        >
          <div class="card-title">Schedule of Activities (SoA) Matrix</div>
          <span
            v-if="store.soaLoading"
            style="font-size: 0.8rem; font-weight: normal; color: #64748b"
            >(Syncing...)</span
          >
        </div>
        <div id="soa-matrix-container" v-html="matrixHtml" />
      </div>
    </div>

    <!-- Part 11 Change Reason Modal -->
    <div v-if="showReasonModal" class="modal-overlay" style="display: flex">
      <div class="modal">
        <div class="modal-header">Reason for Change Required</div>
        <div class="modal-body">
          <p>
            To comply with <strong>21 CFR Part 11 / EU Annex 11</strong>, you
            must document a reason for changing this clinical study design.
          </p>
          <div class="form-group" style="margin-bottom: 12px">
            <label for="change-reason-select">Select Standard Reason</label>
            <select id="change-reason-select" v-model="changeReason">
              <option value="Initial Entry">Initial Study Configuration</option>
              <option value="Protocol Amendment">
                Protocol Amendment / Fork
              </option>
              <option value="Correction of Error">
                Correction of study layout error
              </option>
              <option value="Other">Other (specify below)</option>
            </select>
          </div>
          <div class="form-group">
            <label for="change-reason-text"
              >Custom Explanation (Optional)</label
            >
            <textarea
              id="change-reason-text"
              v-model="customChangeReason"
              placeholder="Explain the clinical reason for this modification..."
            />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn" @click="cancelMutation">Cancel Change</button>
          <button class="btn btn-primary" @click="confirmMutation">
            Sign & Save
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, reactive } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { createClinicalVisitMatrix } from "ui";
import { terminologyClient } from "../api/terminologyClient.js";
import { useAuthStore } from "../stores/auth.js";
import { debounce } from "ui";

const store = useClinicalStore();
const authStore = useAuthStore();

const armSuggestions = ref([]);
const encSuggestions = ref([]);

const debouncedSearchArm = debounce(async (term) => {
  if (!term || !term.trim()) {
    armSuggestions.value = [];
    return;
  }
  try {
    const res = await terminologyClient.searchTerminology(term, {
      userId: authStore.identity?.username || "fderuiter",
      roles: authStore.identity?.roles?.[0] || "investigator",
      changeReason: "Arm concept search",
    });
    armSuggestions.value = res.results || [];
  } catch (err) {
    console.warn("Failed to search arm terminology:", err);
  }
}, 300);

const debouncedSearchEnc = debounce(async (term) => {
  if (!term || !term.trim()) {
    encSuggestions.value = [];
    return;
  }
  try {
    const res = await terminologyClient.searchTerminology(term, {
      userId: authStore.identity?.username || "fderuiter",
      roles: authStore.identity?.roles?.[0] || "investigator",
      changeReason: "Encounter concept search",
    });
    encSuggestions.value = res.results || [];
  } catch (err) {
    console.warn("Failed to search encounter/visit terminology:", err);
  }
}, 300);

function searchArmTerminology(term) {
  debouncedSearchArm(term);
}

function searchEncTerminology(term) {
  debouncedSearchEnc(term);
}

function selectArmConcept(sug) {
  newArm.concept_code = sug.concept_code;
  armSuggestions.value = [];
}

function selectEncConcept(sug) {
  newEnc.concept_code = sug.concept_code;
  encSuggestions.value = [];
}

const builderMode = ref(false);
const usdmText = ref(JSON.stringify(store.currentUsdm, null, 2));

// Creation Forms States
const newArm = reactive({ id: "", name: "", concept_code: "" });
const newEpoch = reactive({ id: "", name: "", sequence: 1, arm_id: "" });
const newEnc = reactive({
  id: "",
  name: "",
  sequence: 1,
  epoch_id: "",
  concept_code: "",
});
const newProc = reactive({ id: "", name: "" });

// Link Applicability States
const linkPayload = reactive({ procedure_id: "", visit_id: "", timing: "" });

// Part 11 Reason Modal States
const showReasonModal = ref(false);
const changeReason = ref("Initial Entry");
const customChangeReason = ref("");
const pendingMutation = ref(null);

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

// Interactive Creator Handlers
function queueMutation(mutation) {
  pendingMutation.value = mutation;
  changeReason.value = "Initial Entry";
  customChangeReason.value = "";
  showReasonModal.value = true;
}

function cancelMutation() {
  showReasonModal.value = false;
  pendingMutation.value = null;
}

async function confirmMutation() {
  if (!pendingMutation.value) return;

  const sel = changeReason.value;
  const cust = customChangeReason.value.trim();
  const finalReason =
    sel === "Other" && cust ? cust : `${sel}${cust ? ": " + cust : ""}`;

  showReasonModal.value = false;
  const mutation = pendingMutation.value;
  pendingMutation.value = null;

  try {
    if (mutation.type === "link") {
      await store.pushSoALink(mutation.linkType, mutation.payload, finalReason);
    } else {
      await store.pushSoAMutation(
        mutation.type,
        mutation.id,
        mutation.properties,
        finalReason
      );
    }
    // Local fallback replication in sandbox mode
    applyMutationLocally(mutation);
  } catch (err) {
    console.warn(
      "API save failed, mutation preserved locally inside browser:",
      err
    );
    applyMutationLocally(mutation);
  }
}

function applyMutationLocally(mutation) {
  if (mutation.type === "arms") {
    if (!store.currentUsdm.arms) store.currentUsdm.arms = [];
    store.currentUsdm.arms.push({
      arm_id: mutation.id,
      arm_name: mutation.properties.name,
    });
  } else if (mutation.type === "epochs") {
    if (!store.currentUsdm.epochs) store.currentUsdm.epochs = [];
    store.currentUsdm.epochs.push({
      epoch_id: mutation.id,
      epoch_name: mutation.properties.name,
      sequence: mutation.properties.sequence,
      arm_id: mutation.properties.arm_id || undefined,
    });
  } else if (mutation.type === "visits") {
    if (!store.currentUsdm.encounters) store.currentUsdm.encounters = [];
    store.currentUsdm.encounters.push({
      encounter_id: mutation.id,
      encounter_name: mutation.properties.name,
      epoch_id: mutation.properties.epoch_id,
      sequence: mutation.properties.sequence,
    });
  } else if (mutation.type === "procedures") {
    if (!store.currentUsdm.rows) store.currentUsdm.rows = [];
    store.currentUsdm.rows.push({
      activity_id: mutation.id,
      activity_name: mutation.properties.name,
      cells: store.currentUsdm.encounters.map((e) => ({
        encounter_id: e.encounter_id,
        is_applicable: false,
      })),
    });
  } else if (mutation.type === "link") {
    const { procedure_id, visit_id, is_applicable, timing } = mutation.payload;
    const row = store.currentUsdm.rows.find(
      (r) => r.activity_id === procedure_id
    );
    if (row) {
      let cell = row.cells.find((c) => c.encounter_id === visit_id);
      if (!cell) {
        cell = { encounter_id: visit_id, is_applicable: false };
        row.cells.push(cell);
      }
      cell.is_applicable = is_applicable;
      cell.details = is_applicable ? timing || undefined : undefined;
    }
  }
  // Force reactivity update
  store.currentUsdm = { ...store.currentUsdm };
}

function handleAddArm() {
  if (!newArm.id || !newArm.name) {
    alert("Please enter Arm ID and Arm Name");
    return;
  }
  queueMutation({
    type: "arms",
    id: newArm.id.trim(),
    properties: { name: newArm.name.trim() },
  });
  newArm.id = "";
  newArm.name = "";
  newArm.concept_code = "";
}

function handleAddEpoch() {
  if (!newEpoch.id || !newEpoch.name) {
    alert("Please enter Epoch ID and Epoch Name");
    return;
  }
  queueMutation({
    type: "epochs",
    id: newEpoch.id.trim(),
    properties: {
      name: newEpoch.name.trim(),
      sequence: newEpoch.sequence,
      arm_id: newEpoch.arm_id || null,
    },
  });
  newEpoch.id = "";
  newEpoch.name = "";
  newEpoch.sequence = store.currentUsdm.epochs
    ? store.currentUsdm.epochs.length + 1
    : 1;
}

function handleAddEncounter() {
  if (!newEnc.id || !newEnc.name || !newEnc.epoch_id) {
    alert("Please populate all fields");
    return;
  }
  queueMutation({
    type: "visits",
    id: newEnc.id.trim(),
    properties: {
      name: newEnc.name.trim(),
      sequence: newEnc.sequence,
      epoch_id: newEnc.epoch_id,
    },
  });
  newEnc.id = "";
  newEnc.name = "";
  newEnc.concept_code = "";
  newEnc.sequence = store.currentUsdm.encounters
    ? store.currentUsdm.encounters.length + 1
    : 1;
  newEnc.concept_code = "";
}

function handleAddProcedure() {
  if (!newProc.id || !newProc.name) {
    alert("Please enter Activity ID and Name");
    return;
  }
  queueMutation({
    type: "procedures",
    id: newProc.id.trim(),
    properties: { name: newProc.name.trim() },
  });
  newProc.id = "";
  newProc.name = "";
}

function handleToggleApplicability(isApplicable) {
  if (!linkPayload.procedure_id || !linkPayload.visit_id) {
    alert("Please select both a Procedure and a Visit first");
    return;
  }
  queueMutation({
    type: "link",
    linkType: isApplicable ? "visit-procedure" : "epoch-visit", // Toggle link family types
    payload: {
      procedure_id: linkPayload.procedure_id,
      visit_id: linkPayload.visit_id,
      is_applicable: isApplicable,
      timing: linkPayload.timing.trim() || undefined,
    },
  });
}
</script>
