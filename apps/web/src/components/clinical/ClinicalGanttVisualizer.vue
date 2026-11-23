<template>
  <div class="card" style="margin-top: 24px; padding: 20px">
    <div
      style="
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
        border-bottom: 1px solid var(--border);
        padding-bottom: 12px;
      "
    >
      <div>
        <h3 style="font-weight: bold; margin: 0; color: var(--primary)">
          <ClinicalIcon name="map" :size="20" /> Interactive Arm-Aware Gantt
          Pathway Visualizer
        </h3>
        <p
          style="
            font-size: 0.85rem;
            color: var(--semantic-color-text-secondary);
            margin: 4px 0 0 0;
          "
        >
          Direct visual flow mapping of protocol phases, encounter sequences,
          crossover paths, and target timing windows.
        </p>
      </div>
      <div
        style="
          font-size: 0.8rem;
          color: var(--semantic-color-text-secondary);
          font-style: italic;
        "
      >
        Click any visit node (circle) to configure target days and offsets
      </div>
    </div>

    <!-- SVG Container -->
    <div style="width: 100%; overflow-x: auto">
      <svg
        :width="timelineWidth"
        :height="svgHeight"
        :viewBox="`0 0 ${timelineWidth} ${svgHeight}`"
        style="
          background-color: var(--semantic-color-surface-muted);
          border-radius: 8px;
          border: 1px solid var(--border);
          display: block;
        "
      >
        <defs>
          <!-- Arrow marker for crossover vectors -->
          <marker
            id="crossover-arrow"
            viewBox="0 0 10 10"
            refX="6"
            refY="5"
            markerWidth="6"
            markerHeight="6"
            orient="auto-start-reverse"
          >
            <path
              d="M 0 1 L 10 5 L 0 9 z"
              fill="var(--semantic-color-pink-text)"
            />
          </marker>
        </defs>

        <!-- No Epochs Fallback -->
        <g v-if="sortedEpochSeqs.length === 0">
          <text
            :x="timelineWidth / 2"
            :y="svgHeight / 2"
            text-anchor="middle"
            fill="var(--semantic-color-text-secondary)"
            font-size="14px"
          >
            No active epoch phase timeline defined. Use the builder above to add
            epochs.
          </text>
        </g>

        <g v-else>
          <!-- Epoch Column Transition Delineator Boundaries & Headers -->
          <g v-for="(seq, index) in sortedEpochSeqs" :key="`header-${seq}`">
            <!-- Header Label -->
            <text
              :x="getColumnCenter(seq)"
              y="25"
              text-anchor="middle"
              font-weight="bold"
              font-size="12px"
              fill="var(--semantic-color-text-secondary)"
            >
              Phase {{ seq }}
            </text>

            <!-- Vertical Boundary Lines (Delineate transitions) -->
            <line
              v-if="index < sortedEpochSeqs.length - 1"
              :x1="getColumnEnd(seq)"
              y1="35"
              :x2="getColumnEnd(seq)"
              :y2="svgHeight - 15"
              stroke="var(--semantic-color-border-control)"
              stroke-width="1.5"
              stroke-dasharray="4,4"
            />
          </g>

          <!-- Study Arm Tracks (Lanes) -->
          <g v-for="(arm, armIdx) in lanes" :key="arm.arm_id">
            <!-- Horizontal lane pathway background track -->
            <line
              :x1="laneXOffset"
              :y1="getLaneYCenter(armIdx)"
              :x2="timelineWidth - 30"
              :y2="getLaneYCenter(armIdx)"
              stroke="var(--semantic-color-border)"
              stroke-width="6"
              stroke-linecap="round"
            />

            <!-- Track Header / Label (Arm Name) -->
            <rect
              x="10"
              :y="getLaneYCenter(armIdx) - 20"
              width="150"
              height="40"
              rx="6"
              fill="var(--semantic-color-text-inverse)"
              stroke="var(--semantic-color-border-control)"
              stroke-width="1"
            />
            <text
              x="20"
              :y="getLaneYCenter(armIdx) + 4"
              font-weight="bold"
              font-size="11px"
              fill="var(--semantic-color-text-dark)"
            >
              <tspan x="20" dy="0">{{ truncate(arm.arm_name, 22) }}</tspan>
            </text>

            <!-- Epoch Blocks and Encounter Nodes in each lane -->
            <g
              v-for="seq in sortedEpochSeqs"
              :key="`lane-${arm.arm_id}-seq-${seq}`"
            >
              <!-- Get epoch for this lane and sequence -->
              <g v-if="getEpochForLane(arm.arm_id, seq)">
                <!-- Epoch block background rect -->
                <rect
                  :x="getColumnStart(seq) + 10"
                  :y="getLaneYCenter(armIdx) - 25"
                  :width="getColumnWidth(seq) - 20"
                  height="50"
                  rx="8"
                  :fill="
                    getEpochStyles(getEpochForLane(arm.arm_id, seq).epoch_name)
                      .fill
                  "
                  :stroke="
                    getEpochStyles(getEpochForLane(arm.arm_id, seq).epoch_name)
                      .stroke
                  "
                  stroke-width="1.5"
                />

                <!-- Epoch Block Title Text -->
                <text
                  :x="getColumnCenter(seq)"
                  :y="getLaneYCenter(armIdx) - 12"
                  text-anchor="middle"
                  font-size="9px"
                  font-weight="bold"
                  :fill="
                    getEpochStyles(getEpochForLane(arm.arm_id, seq).epoch_name)
                      .text
                  "
                >
                  {{
                    truncate(getEpochForLane(arm.arm_id, seq).epoch_name, 20)
                  }}
                </text>

                <!-- Visit/Encounter Nodes for this Epoch -->
                <g
                  v-for="(visit, visitIdx) in getEpochVisits(
                    getEpochForLane(arm.arm_id, seq).epoch_id
                  )"
                  :key="visit.encounter_id"
                >
                  <!-- Connection line between sequential visits in same epoch block -->
                  <line
                    v-if="visitIdx > 0"
                    :x1="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx - 1,
                        seq
                      )
                    "
                    :y1="getLaneYCenter(armIdx) + 8"
                    :x2="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx,
                        seq
                      )
                    "
                    :y2="getLaneYCenter(armIdx) + 8"
                    stroke="var(--semantic-color-border-control)"
                    stroke-width="1.5"
                    stroke-dasharray="2,2"
                  />

                  <!-- Visit Node Circle -->
                  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
                  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
                  <circle
                    :cx="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx,
                        seq
                      )
                    "
                    :cy="getLaneYCenter(armIdx) + 8"
                    r="13"
                    fill="var(--semantic-color-interactive-primary)"
                    stroke="var(--semantic-color-surface)"
                    stroke-width="2.5"
                    style="
                      cursor: pointer;
                      filter: drop-shadow(0 1px 2px rgba(0, 0, 0, 0.1));
                    "
                    @click="openEditModal(visit)"
                  />

                  <!-- Sequence Number Inside Circle -->
                  <text
                    :x="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx,
                        seq
                      )
                    "
                    :y="getLaneYCenter(armIdx) + 11"
                    text-anchor="middle"
                    font-size="9px"
                    font-weight="bold"
                    fill="var(--semantic-color-surface)"
                    style="pointer-events: none"
                  >
                    {{ visitIdx + 1 }}
                  </text>

                  <!-- Visit name label above the circle -->
                  <text
                    :x="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx,
                        seq
                      )
                    "
                    :y="getLaneYCenter(armIdx) - 3"
                    text-anchor="middle"
                    font-size="10px"
                    font-weight="bold"
                    fill="var(--semantic-color-text-dark)"
                    style="pointer-events: none"
                  >
                    {{ truncate(visit.encounter_name, 14) }}
                  </text>

                  <!-- Timing parameters text label below the circle -->
                  <text
                    :x="
                      getVisitX(
                        getEpochForLane(arm.arm_id, seq).epoch_id,
                        visitIdx,
                        seq
                      )
                    "
                    :y="getLaneYCenter(armIdx) + 31"
                    text-anchor="middle"
                    font-size="8.5px"
                    font-weight="600"
                    fill="var(--semantic-color-text-secondary)"
                    style="pointer-events: none"
                  >
                    Day {{ getPlannedDay(visit) }}
                    <tspan
                      fill="var(--semantic-color-text-secondary)"
                      font-weight="normal"
                    >
                      ({{ getOffsetString(visit) }})
                    </tspan>
                  </text>
                </g>
              </g>
            </g>
          </g>

          <!-- Crossover Vectors Connector Paths (Pink dashed curve lines with arrows) -->
          <g
            v-for="(cross, crossIdx) in crossovers"
            :key="`crossover-${crossIdx}`"
          >
            <g
              v-if="
                nodePositions[cross.from_visit_id] &&
                nodePositions[cross.to_visit_id]
              "
            >
              <path
                :d="calculateCrossoverPath(cross)"
                stroke="var(--semantic-color-pink-text)"
                stroke-width="2.5"
                stroke-dasharray="5,3"
                fill="none"
                marker-end="url(#crossover-arrow)"
              />
              <!-- Crossover Label Background -->
              <rect
                :x="calculateCrossoverTextPos(cross).x - 55"
                :y="calculateCrossoverTextPos(cross).y - 8"
                width="110"
                height="15"
                rx="3"
                fill="var(--semantic-color-pink-bg)"
                stroke="var(--semantic-color-pink-border)"
                stroke-width="0.75"
              />
              <!-- Crossover Label Text -->
              <text
                :x="calculateCrossoverTextPos(cross).x"
                :y="calculateCrossoverTextPos(cross).y + 3"
                text-anchor="middle"
                font-size="8px"
                font-weight="bold"
                fill="var(--semantic-color-pink-text)"
              >
                {{ truncate(cross.label || "Crossover", 18) }}
              </text>
            </g>
          </g>
        </g>
      </svg>
    </div>

    <!-- timing properties Popover/Modal -->
    <ClinicalModal
      v-model="isModalOpen"
      :teleport="false"
      size="md"
      backdrop-class="gantt-modal-backdrop"
      dialog-class="gantt-modal-content card"
      title="Edit Encounter Timing Property"
      @close="closeModal"
    >
      <div
        style="
          margin-bottom: 16px;
          background-color: var(--semantic-color-surface-muted);
          border: 1px solid var(--semantic-color-border);
          padding: 10px;
          border-radius: 6px;
        "
      >
        <div
          style="
            font-weight: bold;
            font-size: 0.9rem;
            color: var(--semantic-color-text-dark);
          "
        >
          Encounter: {{ selectedVisit?.encounter_name }}
        </div>
        <div
          style="
            font-size: 0.75rem;
            color: var(--semantic-color-text-secondary);
            font-family: monospace;
            margin-top: 2px;
          "
        >
          ID: {{ selectedVisit?.encounter_id }} | Epoch:
          {{ selectedVisit?.epoch_id }}
        </div>
      </div>

      <!-- Popover Fields -->
      <div style="display: flex; flex-direction: column; gap: 14px">
        <!-- Target / Planned Day -->
        <div class="form-group">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label
            style="
              font-weight: 600;
              display: block;
              margin-bottom: 4px;
              font-size: 0.85rem;
              color: var(--semantic-color-text-body);
            "
          >
            Target Day Offset
          </label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model.number="form.planned_day"
            type="number"
            min="0"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
              font-size: 0.9rem;
            "
            placeholder="e.g. 14"
          />
        </div>

        <!-- Min/Max Offsets -->
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 14px">
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label
              style="
                font-weight: 600;
                display: block;
                margin-bottom: 4px;
                font-size: 0.85rem;
                color: var(--semantic-color-text-body);
              "
            >
              Min Offset (Days)
            </label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model.number="form.min_offset"
              type="number"
              min="0"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
                font-size: 0.9rem;
              "
              placeholder="e.g. 1"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label
              style="
                font-weight: 600;
                display: block;
                margin-bottom: 4px;
                font-size: 0.85rem;
                color: var(--semantic-color-text-body);
              "
            >
              Max Offset (Days)
            </label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model.number="form.max_offset"
              type="number"
              min="0"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
                font-size: 0.9rem;
              "
              placeholder="e.g. 3"
            />
          </div>
        </div>

        <!-- Anchor Reference -->
        <div class="form-group">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label
            style="
              font-weight: 600;
              display: block;
              margin-bottom: 4px;
              font-size: 0.85rem;
              color: var(--semantic-color-text-body);
            "
          >
            Anchor Reference Milestone
          </label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model="form.anchor_reference"
            type="text"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
              font-size: 0.9rem;
            "
            placeholder="e.g. Screening, Day 1, baseline_visit"
          />
        </div>

        <!-- Required Justification (21 CFR Part 11) -->
        <div
          class="form-group"
          style="
            border-top: 1px dashed var(--semantic-color-border-control);
            padding-top: 14px;
            margin-top: 4px;
          "
        >
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label
            style="
              font-weight: bold;
              display: block;
              margin-bottom: 4px;
              font-size: 0.85rem;
              color: var(--error);
            "
          >
            Compliance Change Justification *
          </label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <textarea
            v-model="form.justification"
            rows="2.5"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
              font-size: 0.85rem;
            "
            placeholder="Specify the scientific, regulatory, or operational reason for this schedule alteration..."
          />
          <span
            style="
              font-size: 0.75rem;
              color: var(--semantic-color-text-secondary);
              margin-top: 2px;
              display: block;
            "
          >
            Change audit ledger entry will be logged under Annex 11 protocol.
          </span>
        </div>

        <!-- Inline Validation Error Alert Banner -->
        <div
          v-if="validationError"
          role="alert"
          style="
            background-color: var(--semantic-color-error-bg-soft);
            border: 1px solid var(--semantic-color-error-border);
            color: var(--semantic-color-error);
            padding: 10px;
            border-radius: 6px;
            font-size: 0.8rem;
          "
        >
          <strong>Validation Error:</strong> {{ validationError }}
        </div>
      </div>

      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          style="padding: 8px 14px; font-size: 0.85rem"
          @click="closeModal"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="!isFormValid"
          style="
            padding: 8px 16px;
            font-size: 0.85rem;
            display: flex;
            align-items: center;
            gap: 6px;
          "
          @click="saveTiming"
        >
          <ClinicalIcon name="save" :size="16" /> Save Changes
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { ClinicalIcon, ClinicalModal } from "ui";
import { ref, computed, reactive } from "vue";
import { useClinicalStore } from "../../stores/clinical";
import { useToastStore } from "../../stores/toasts.js";

const store = useClinicalStore();
const toasts = useToastStore();

// UI Dimensions
const timelineWidth = 960;
const laneXOffset = 180; // Margin on left for arm header titles
const rightMargin = 40;
const usableWidth = timelineWidth - laneXOffset - rightMargin;

// State
const isModalOpen = ref(false);
const selectedVisit = ref(null);

const form = reactive({
  planned_day: 0,
  min_offset: 0,
  max_offset: 0,
  anchor_reference: "",
  justification: "",
});

// Dynamic Lanes computation based on defined study arms
const lanes = computed(() => {
  const definedArms = store.currentUsdm?.arms || [];
  if (definedArms.length === 0) {
    return [{ arm_id: "shared", arm_name: "Common Study Path" }];
  }
  return definedArms;
});

// Layout geometry heights
const laneHeight = 70;
const gap = 45;
const topMargin = 50;
const bottomMargin = 40;
const svgHeight = computed(() => {
  const numArms = lanes.value.length;
  return topMargin + numArms * (laneHeight + gap) + bottomMargin;
});

// Get unique epoch sequences in chronological order
const sortedEpochSeqs = computed(() => {
  const epochsList = store.currentUsdm?.epochs || [];
  const seqs = new Set();
  epochsList.forEach((e) => {
    if (e.sequence !== undefined) {
      seqs.add(e.sequence);
    } else if (e.sequenceNumber !== undefined) {
      seqs.add(e.sequenceNumber);
    }
  });
  if (seqs.size === 0) {
    return [1]; // default fallback phase
  }
  return Array.from(seqs).sort((a, b) => a - b);
});

// Column coordinates
function getColumnWidth(_seq) {
  const totalCols = sortedEpochSeqs.value.length;
  return usableWidth / totalCols;
}

function getColumnStart(seq) {
  const idx = sortedEpochSeqs.value.indexOf(seq);
  return laneXOffset + idx * getColumnWidth(seq);
}

function getColumnEnd(seq) {
  return getColumnStart(seq) + getColumnWidth(seq);
}

function getColumnCenter(seq) {
  return getColumnStart(seq) + getColumnWidth(seq) / 2;
}

function getLaneYCenter(armIdx) {
  return topMargin + armIdx * (laneHeight + gap) + laneHeight / 2;
}

// Map Epoch to a Lane and Sequence
function getEpochForLane(armId, seq) {
  const epochsList = store.currentUsdm?.epochs || [];
  // 1. Find specific arm epoch at this sequence
  let found = epochsList.find((e) => {
    const epochSeq = e.sequence !== undefined ? e.sequence : e.sequenceNumber;
    return epochSeq === seq && e.arm_id === armId;
  });

  // 2. Fallback to common/shared epoch (no arm_id) at this sequence
  if (!found) {
    found = epochsList.find((e) => {
      const epochSeq = e.sequence !== undefined ? e.sequence : e.sequenceNumber;
      return epochSeq === seq && (!e.arm_id || e.arm_id === "shared");
    });
  }
  return found;
}

// Get visits belonging to an epoch
function getEpochVisits(epochId) {
  if (!epochId) return [];
  const list = store.currentUsdm?.encounters || [];
  return list
    .filter((v) => v.epoch_id === epochId)
    .sort((a, b) => (a.sequence || 0) - (b.sequence || 0));
}

// Calculate precise X coordinate for dynamic node placement
function getVisitX(epochId, visitIdx, seq) {
  const start = getColumnStart(seq) + 20;
  const width = getColumnWidth(seq) - 40;
  const visits = getEpochVisits(epochId);
  const K = visits.length;

  if (K <= 1) {
    return start + width / 2;
  }
  return start + (visitIdx * width) / (K - 1);
}

// Mapping node coordinates dynamically to trace crossover paths accurately
const nodePositions = computed(() => {
  const positions = {};
  lanes.value.forEach((arm, armIdx) => {
    sortedEpochSeqs.value.forEach((seq) => {
      const epoch = getEpochForLane(arm.arm_id, seq);
      if (epoch) {
        const visits = getEpochVisits(epoch.epoch_id);
        visits.forEach((visit, visitIdx) => {
          positions[visit.encounter_id] = {
            x: getVisitX(epoch.epoch_id, visitIdx, seq),
            y: getLaneYCenter(armIdx) + 8,
          };
        });
      }
    });
  });
  return positions;
});

// Trace Crossover Paths
const crossovers = computed(() => {
  // If defined in USDM study, use them
  if (
    store.currentUsdm?.crossovers &&
    store.currentUsdm.crossovers.length > 0
  ) {
    return store.currentUsdm.crossovers;
  }

  // Dynamic Demo crossovers fallback for treatment parallel cross sequence tracing
  const encounters = store.currentUsdm?.encounters || [];
  const hasA2 = encounters.some((e) => e.encounter_id === "V-TRT-A2");
  const hasB1 = encounters.some((e) => e.encounter_id === "V-TRT-B1");

  if (hasA2 && hasB1) {
    return [
      {
        from_visit_id: "V-TRT-A2",
        to_visit_id: "V-TRT-B1",
        label: "Crossover to Placebo",
      },
    ];
  }
  return [];
});

function calculateCrossoverPath(cross) {
  const fromPos = nodePositions.value[cross.from_visit_id];
  const toPos = nodePositions.value[cross.to_visit_id];
  if (!fromPos || !toPos) return "";

  const dx = toPos.x - fromPos.x;

  // Nice curved cubic Bezier profile
  const cx1 = fromPos.x + dx * 0.4;
  const cy1 = fromPos.y;
  const cx2 = fromPos.x + dx * 0.6;
  const cy2 = toPos.y;

  return `M ${fromPos.x} ${fromPos.y} C ${cx1} ${cy1}, ${cx2} ${cy2}, ${toPos.x} ${toPos.y}`;
}

function calculateCrossoverTextPos(cross) {
  const fromPos = nodePositions.value[cross.from_visit_id];
  const toPos = nodePositions.value[cross.to_visit_id];
  if (!fromPos || !toPos) return { x: 0, y: 0 };

  // Locate center of crossover path curve for label placement
  const x = (fromPos.x + toPos.x) / 2;
  const y = (fromPos.y + toPos.y) / 2 - 25; // Shifted upwards slightly for readability
  return { x, y };
}

// Timing Getters
function getPlannedDay(visit) {
  if (visit.planned_day !== undefined) return visit.planned_day;
  if (visit.target_day !== undefined) return visit.target_day;
  return 0; // Default
}

function getOffsetString(visit) {
  const min = visit.min_offset !== undefined ? visit.min_offset : 0;
  const max = visit.max_offset !== undefined ? visit.max_offset : 0;
  return `-${min}/+${max}`;
}

// Epoch styling color-coding mapper for beautiful phase visualization
function getEpochStyles(name = "") {
  const n = name.toLowerCase();
  if (n.includes("screening") || n.includes("scr")) {
    return {
      fill: "var(--semantic-color-success-bg-soft)",
      stroke: "var(--semantic-color-success-border)",
      text: "var(--semantic-color-success-dark)",
    };
  } else if (n.includes("treatment") || n.includes("trt")) {
    return {
      fill: "var(--semantic-color-info-bg-soft)",
      stroke: "var(--semantic-color-info-border-accent)",
      text: "var(--semantic-color-primary-dark)",
    };
  } else if (n.includes("follow") || n.includes("flw") || n.includes("post")) {
    return {
      fill: "var(--semantic-color-purple-bg)",
      stroke: "var(--semantic-color-purple-border)",
      text: "var(--semantic-color-purple-text)",
    };
  } else {
    return {
      fill: "var(--semantic-color-orange-bg)",
      stroke: "var(--semantic-color-orange-border)",
      text: "var(--semantic-color-orange-text)",
    };
  }
}

// Popover Editing Handlers
function openEditModal(visit) {
  selectedVisit.value = visit;
  form.planned_day = getPlannedDay(visit);
  form.min_offset = visit.min_offset !== undefined ? visit.min_offset : 0;
  form.max_offset = visit.max_offset !== undefined ? visit.max_offset : 0;
  form.anchor_reference = visit.anchor_reference || "";
  form.justification = ""; // enforce fresh entry
  isModalOpen.value = true;
}

function closeModal() {
  isModalOpen.value = false;
  selectedVisit.value = null;
}

// Timing Validation Guards
const validationError = computed(() => {
  if (
    form.planned_day === "" ||
    form.planned_day === null ||
    isNaN(form.planned_day)
  ) {
    return "Target Day Offset is required.";
  }
  if (form.planned_day < 0) {
    return "Target Day cannot be negative.";
  }
  if (form.min_offset < 0) {
    return "Minimum timing offset cannot be negative.";
  }
  if (form.max_offset < 0) {
    return "Maximum timing offset cannot be negative.";
  }
  if (form.min_offset > form.max_offset) {
    return "Minimum offset cannot exceed maximum offset window constraint.";
  }
  return null;
});

// Enforces compliance and verification block saving
const isFormValid = computed(() => {
  if (validationError.value) {
    return false;
  }

  // Justification MUST be provided and not empty
  if (!form.justification || !form.justification.trim()) {
    return false;
  }

  // Check if any timing value changed to allow save
  const hasChanged =
    form.planned_day !== getPlannedDay(selectedVisit.value) ||
    form.min_offset !== (selectedVisit.value.min_offset ?? 0) ||
    form.max_offset !== (selectedVisit.value.max_offset ?? 0) ||
    form.anchor_reference !== (selectedVisit.value.anchor_reference ?? "");

  return hasChanged;
});

// Synchronize and update local and DB state
async function saveTiming() {
  if (!isFormValid.value) return;

  const visitId = selectedVisit.value.encounter_id;
  const updatedProperties = {
    encounter_name: selectedVisit.value.encounter_name,
    epoch_id: selectedVisit.value.epoch_id,
    sequence: selectedVisit.value.sequence,
    encounterType: selectedVisit.value.encounterType || "Visit",
    planned_day: form.planned_day,
    target_day: form.planned_day,
    min_offset: form.min_offset,
    max_offset: form.max_offset,
    anchor_reference: form.anchor_reference.trim(),
  };

  const justification = form.justification.trim();

  try {
    // Standard GxP audit push mutation save
    await store.pushSoAMutation(
      "visits",
      visitId,
      updatedProperties,
      justification
    );

    // Mirror the saved change locally for immediate reactive state sync
    const idx = store.currentUsdm.encounters.findIndex(
      (e) => e.encounter_id === visitId
    );
    if (idx !== -1) {
      store.currentUsdm.encounters[idx] = {
        ...store.currentUsdm.encounters[idx],
        ...updatedProperties,
      };
      // Force Pinia store update triggers
      store.currentUsdm = { ...store.currentUsdm };
    }
    closeModal();
  } catch (err) {
    // The visit window was not saved (#4523): keep the modal open with the user's edits and report it.
    toasts.apiError("Visit window update failed", err);
  }
}

// Helpers
function truncate(str, len) {
  if (!str) return "";
  return str.length > len ? str.substring(0, len - 3) + "..." : str;
}
</script>

<style scoped>
.gantt-modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}
</style>
