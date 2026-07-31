<template>
  <div id="section-soa" class="dashboard-section active">
    <div class="section-header">
      <h2>Interactive Schedule of Activities (SoA) Builder Workspace</h2>
      <p>
        Design, configure, and manage study arms, epochs, encounters/visits, and procedures
        to build a GxP-compliant Schedule of Activities (SoA) matrix.
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
          SoA Authoring Workspace
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
          <strong>API Sync Failed:</strong>
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
                placeholder="e.g. ARM-A"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-arm-name">Arm Name</label>
              <input
                id="new-arm-name"
                v-model="newArm.name"
                type="text"
                placeholder="e.g. Arm A: Active"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-arm-type">Arm Type</label>
              <input
                id="new-arm-type"
                v-model="newArm.type"
                type="text"
                placeholder="e.g. Treatment"
                style="width: 100%; padding: 6px"
              />
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="promptMutation('arm')"
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
                placeholder="e.g. EP-SCR"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-epoch-name">Epoch Name</label>
              <input
                id="new-epoch-name"
                v-model="newEpoch.name"
                type="text"
                placeholder="e.g. Screening"
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
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="promptMutation('epoch')"
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
            <legend style="font-weight: bold; padding: 0 6px">Add Visit</legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-visit-id">Visit ID</label>
              <input
                id="new-visit-id"
                v-model="newVisit.id"
                type="text"
                placeholder="e.g. V-SCR"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-visit-name">Visit Name</label>
              <input
                id="new-visit-name"
                v-model="newVisit.name"
                type="text"
                placeholder="e.g. Week 1"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-visit-seq">Sequence</label>
              <input
                id="new-visit-seq"
                v-model.number="newVisit.sequence"
                type="number"
                style="width: 100%; padding: 6px"
              />
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="promptMutation('visit')"
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
              Add Procedure
            </legend>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-proc-id">Procedure ID</label>
              <input
                id="new-proc-id"
                v-model="newProcedure.id"
                type="text"
                placeholder="e.g. ACT-VS"
                style="width: 100%; padding: 6px"
              />
            </div>
            <div class="form-group" style="margin-bottom: 8px">
              <label for="new-proc-name">Procedure Name</label>
              <input
                id="new-proc-name"
                v-model="newProcedure.name"
                type="text"
                placeholder="e.g. Vital Signs"
                style="width: 100%; padding: 6px"
              />
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%"
              @click="promptMutation('procedure')"
            >
              Add Procedure
            </button>
          </fieldset>

          <!-- Associations Form -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 12px;
              grid-column: span 2;
            "
          >
            <legend style="font-weight: bold; padding: 0 6px">
              Link Associations
            </legend>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
              <div class="form-group">
                <label for="link-type">Link Type</label>
                <select id="link-type" v-model="linkSetup.type" style="width: 100%; padding: 6px">
                  <option value="epoch-visit">Epoch → Visit</option>
                  <option value="visit-procedure">Visit → Procedure</option>
                </select>
              </div>

              <div v-if="linkSetup.type === 'epoch-visit'" class="form-group">
                <label for="link-epoch">Epoch ID</label>
                <input
                  id="link-epoch"
                  v-model="linkSetup.epochId"
                  type="text"
                  placeholder="e.g. EP-SCR"
                  style="width: 100%; padding: 6px"
                />
              </div>

              <div v-if="linkSetup.type === 'epoch-visit'" class="form-group">
                <label for="link-visit">Visit ID</label>
                <input
                  id="link-visit"
                  v-model="linkSetup.visitId"
                  type="text"
                  placeholder="e.g. V-SCR"
                  style="width: 100%; padding: 6px"
                />
              </div>

              <div v-if="linkSetup.type === 'visit-procedure'" class="form-group">
                <label for="link-visit-p">Visit ID</label>
                <input
                  id="link-visit-p"
                  v-model="linkSetup.visitId"
                  type="text"
                  placeholder="e.g. V-SCR"
                  style="width: 100%; padding: 6px"
                />
              </div>

              <div v-if="linkSetup.type === 'visit-procedure'" class="form-group">
                <label for="link-proc">Procedure ID</label>
                <input
                  id="link-proc"
                  v-model="linkSetup.procedureId"
                  type="text"
                  placeholder="e.g. ACT-VS"
                  style="width: 100%; padding: 6px"
                />
              </div>
            </div>
            <button
              class="btn btn-primary"
              style="width: 100%; margin-top: 12px"
              @click="promptMutation('link')"
            >
              Establish Link Connection
            </button>
          </fieldset>
        </div>
      </div>
    </div>

    <!-- Projected SoA Matrix View -->
    <div class="card" style="padding: 20px">
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 16px;
        "
      >
        <h3 style="font-weight: bold; margin: 0; color: var(--text-main)">
          Projected Schedule of Activities Matrix
        </h3>
        <div style="display: flex; gap: 8px; align-items: center">
          <button class="btn btn-secondary" @click="fetchProjection">
            🔄 Refresh Projection
          </button>
          <span
            v-if="store.soaLoading"
            style="font-size: 0.8rem; font-weight: normal; color: #64748b"
            >(Syncing...)</span
          >
        </div>
      </div>

      <div id="soa-matrix-container">
        <ClinicalSoAMatrix :soa-data="soaData" />
      </div>
    </div>

    <!-- Part 11 Change Reason Modal -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical study design."
      :options="soaReasonOptions"
      default-option="Initial Entry"
      @confirm="confirmMutation"
      @cancel="cancelMutation"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, reactive, onMounted } from "vue";
import { useClinicalStore } from "../stores/clinical";
import ClinicalSoAMatrix from "../components/clinical/ClinicalSoAMatrix.vue";
import ReasonModal from "../components/ReasonModal.vue";

const store = useClinicalStore();

// UI and control state
const builderMode = ref(false);
const showReasonModal = ref(false);
const pendingActionType = ref("");

// Standard SoA change reasons
const sooaReasonOptions = [
  { value: "Initial Entry", text: "Initial Study Configuration" },
  { value: "Protocol Amendment", text: "Protocol Amendment / Fork" },
  { value: "Design Correction", text: "Correction of Entry Error" },
  { value: "Regulatory Mandate", text: "Regulatory Agency Mandated Change" },
];
const soaReasonOptions = ref(sooaReasonOptions);

// Creation payloads
const newArm = reactive({ id: "", name: "", type: "" });
const newEpoch = reactive({ id: "", name: "", sequence: 1 });
const newVisit = reactive({ id: "", name: "", sequence: 1 });
const newProcedure = reactive({ id: "", name: "" });
const linkSetup = reactive({ type: "epoch-visit", epochId: "", visitId: "", procedureId: "" });

// Projected matrix computed data
const soaData = computed(() => {
  return store.currentUsdm;
});

onMounted(async () => {
  await fetchProjection();
});

async function fetchProjection() {
  await store.fetchSoAProjection();
}

function promptMutation(type) {
  pendingActionType.value = type;
  showReasonModal.value = true;
}

function cancelMutation() {
  showReasonModal.value = false;
  pendingActionType.value = "";
}

async function confirmMutation(reasonObj) {
  const reason = reasonObj.customText || reasonObj.selectedOption || "SoA Mutation";
  showReasonModal.value = false;

  try {
    if (pendingActionType.value === "arm") {
      await store.pushSoAMutation(
        "arms",
        newArm.id,
        { name: newArm.name, type: newArm.type },
        reason
      );
      // Reset
      newArm.id = "";
      newArm.name = "";
      newArm.type = "";
    } else if (pendingActionType.value === "epoch") {
      await store.pushSoAMutation(
        "epochs",
        newEpoch.id,
        { name: newEpoch.name, sequence: newEpoch.sequence },
        reason
      );
      newEpoch.id = "";
      newEpoch.name = "";
      newEpoch.sequence = 1;
    } else if (pendingActionType.value === "visit") {
      await store.pushSoAMutation(
        "visits",
        newVisit.id,
        { name: newVisit.name, sequence: newVisit.sequence },
        reason
      );
      newVisit.id = "";
      newVisit.name = "";
      newVisit.sequence = 1;
    } else if (pendingActionType.value === "procedure") {
      await store.pushSoAMutation(
        "procedures",
        newProcedure.id,
        { name: newProcedure.name },
        reason
      );
      newProcedure.id = "";
      newProcedure.name = "";
    } else if (pendingActionType.value === "link") {
      if (linkSetup.type === "epoch-visit") {
        await store.pushSoALink(
          "epoch-visit",
          { epoch_id: linkSetup.epochId, visit_id: linkSetup.visitId },
          reason
        );
      } else if (linkSetup.type === "visit-procedure") {
        await store.pushSoALink(
          "visit-procedure",
          { visit_id: linkSetup.visitId, procedure_id: linkSetup.procedureId },
          reason
        );
      }
    }
  } catch (err) {
    console.error("Mutation failed", err);
  } finally {
    pendingActionType.value = "";
  }
}
</script>
