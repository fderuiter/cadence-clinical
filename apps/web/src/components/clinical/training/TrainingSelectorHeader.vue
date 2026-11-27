<template>
  <!-- Header -->
  <div class="console-header">
    <div class="header-titles">
      <div class="badge-row">
        <span class="badge badge-gxp">21 CFR Part 11</span>
        <span class="badge badge-regulatory">ICH GCP E6(R2) §4</span>
        <span class="badge badge-standard">DIA TMF 05.02.04</span>
        <span
          class="badge"
          :class="isGated ? 'badge-locked' : 'badge-unlocked'"
        >
          {{ isGated ? "Qualification Pending" : "Qualification Completed" }}
        </span>
      </div>
      <h2 class="console-title">
        Protocol Amendment Training &amp; Qualification Console
      </h2>
      <p class="console-subtitle">
        Procedural delta curricula, multi-site role-based batch assignment, and
        21 CFR Part 11 Principal Investigator delegation sign-off.
      </p>
    </div>

    <div class="header-actions">
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="loading"
        @click="emit('refresh')"
      >
        <span class="btn-icon" aria-hidden="true"
          ><ClinicalIcon name="refresh" :size="16"
        /></span>
        Refresh
      </button>
    </div>
  </div>

  <!-- Active Protocol / Assignment Selector -->
  <div class="assignment-selector-panel">
    <div class="form-group">
      <label for="assignment-select"
        >Active Training Assignment / Curriculum:</label
      >
      <select
        id="assignment-select"
        :value="selectedAssignmentId"
        class="select-input"
        @change="onSelectAssignment"
      >
        <option v-for="asn in assignments" :key="asn.id" :value="asn.id">
          [{{ formatClinicalStatus(asn.status) }}]
          {{ asn.curriculum?.title || asn.study_id + " Protocol Training" }} —
          Site {{ asn.site_id }}
        </option>
        <option v-if="assignments.length === 0" value="" disabled>
          No active training assignments found for current user
        </option>
      </select>
    </div>

    <div v-if="curricula.length > 0" class="form-group">
      <label for="curriculum-select">Study Curriculum:</label>
      <select
        id="curriculum-select"
        :value="selectedCurriculumId"
        class="select-input"
        @change="onSelectCurriculum"
      >
        <option v-for="cur in curricula" :key="cur.id" :value="cur.id">
          {{ cur.title }} (v{{ cur.protocol_version }})
        </option>
      </select>
    </div>

    <div class="status-summary-chips">
      <div class="summary-chip">
        <span class="chip-label">Status:</span>
        <span
          class="chip-value"
          :class="
            'status-' + (activeAssignment?.status || 'PENDING').toLowerCase()
          "
        >
          {{ formatClinicalStatus(activeAssignment?.status || "PENDING") }}
        </span>
      </div>
      <div class="summary-chip">
        <span class="chip-label">Protocol Version:</span>
        <span class="chip-value">
          {{ activeCurriculum?.protocol_version || "—" }}
        </span>
      </div>
      <div class="summary-chip">
        <span class="chip-label">Quiz Score:</span>
        <span class="chip-value tabular-nums">
          {{
            activeAssignment?.quiz_score != null
              ? Math.round(activeAssignment.quiz_score * 100) + "%"
              : "Not Recorded"
          }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { formatClinicalStatus } from "../../../utils/clinicalLabels";

defineProps({
  loading: {
    type: Boolean,
    default: false,
  },
  isGated: {
    type: Boolean,
    default: false,
  },
  assignments: {
    type: Array,
    default: () => [],
  },
  curricula: {
    type: Array,
    default: () => [],
  },
  selectedAssignmentId: {
    type: String,
    default: "",
  },
  selectedCurriculumId: {
    type: String,
    default: "",
  },
  activeAssignment: {
    type: Object,
    default: null,
  },
  activeCurriculum: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits([
  "refresh",
  "update:selectedAssignmentId",
  "update:selectedCurriculumId",
  "assignmentChange",
  "curriculumChange",
]);

function onSelectAssignment(event) {
  emit("update:selectedAssignmentId", event.target.value);
  emit("assignmentChange");
}

function onSelectCurriculum(event) {
  emit("update:selectedCurriculumId", event.target.value);
  emit("curriculumChange");
}
</script>

<style scoped src="../protocolTrainingConsole.css"></style>
