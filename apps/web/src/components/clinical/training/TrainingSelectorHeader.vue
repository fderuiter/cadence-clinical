<template>
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
      <h2 class="console-title">Study Training &amp; Qualification Hub</h2>
      <p class="console-subtitle">
        Procedural delta curricula, Kernel site-grant batch assignment, quiz
        verification, and 21 CFR Part 11 TMF 05.02.04 qualification filing.
      </p>
    </div>

    <div class="header-actions">
      <button
        v-if="canAssignCurriculum"
        id="btn-open-author-curriculum"
        type="button"
        class="btn btn-primary"
        :disabled="loading"
        @click="emit('openAuthorModal')"
      >
        <span class="btn-icon" aria-hidden="true">
          <ClinicalIcon name="plus" :size="16" />
        </span>
        Author Curriculum
      </button>
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="loading"
        @click="emit('refresh')"
      >
        <span class="btn-icon" aria-hidden="true">
          <ClinicalIcon name="refresh" :size="16" />
        </span>
        Refresh
      </button>
    </div>
  </div>

  <div class="assignment-selector-panel">
    <div class="form-group">
      <label for="assignment-select">
        Active Training Assignment / Curriculum:
      </label>
      <select
        id="assignment-select"
        :value="selectedAssignmentId"
        class="select-input control-touch"
        @change="onSelectAssignment"
      >
        <option v-for="asn in assignments" :key="asn.id" :value="asn.id">
          [{{ formatClinicalStatus(asn.status) }}]
          {{
            asn.curriculum?.title ||
            activeCurriculum?.title ||
            asn.study_id + " Protocol Training"
          }}
          — Site {{ asn.site_id }}
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
        class="select-input control-touch"
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
        <span class="chip-value font-mono">
          {{
            activeCurriculum?.protocol_version
              ? `v${activeCurriculum.protocol_version}`
              : "—"
          }}
        </span>
      </div>
      <div class="summary-chip">
        <span class="chip-label">Quiz Score:</span>
        <span class="chip-value tabular-nums">
          {{ formattedQuizScore }}
        </span>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalIcon } from "ui";
import { formatClinicalStatus } from "../../../utils/clinicalLabels";
import { normalizePercentScore } from "./useProtocolTrainingConsole";

const props = defineProps({
  loading: {
    type: Boolean,
    default: false,
  },
  isGated: {
    type: Boolean,
    default: false,
  },
  canAssignCurriculum: {
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
  liveQuizPercent: {
    type: Number,
    default: null,
  },
});

const emit = defineEmits([
  "refresh",
  "openAuthorModal",
  "update:selectedAssignmentId",
  "update:selectedCurriculumId",
  "assignmentChange",
  "curriculumChange",
]);

const formattedQuizScore = computed(() => {
  const passing =
    normalizePercentScore(props.activeCurriculum?.passing_score) ?? 80;
  const recorded = normalizePercentScore(props.activeAssignment?.quiz_score);
  if (recorded !== null) {
    return `${recorded}% (Passing >= ${passing}%)`;
  }
  if (props.liveQuizPercent !== null && props.liveQuizPercent !== undefined) {
    return `${props.liveQuizPercent}% draft (Passing >= ${passing}%)`;
  }
  return `Passing >= ${passing}%`;
});

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
