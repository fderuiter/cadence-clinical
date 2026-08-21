<template>
  <div class="card subject-enrollment-card">
    <div class="card-header">
      <div class="card-title-row">
        <h3 class="card-title">
          <span>🩺</span> Subject Registration &amp; Screening
        </h3>
        <span
          v-if="screeningStatus"
          class="badge"
          :class="screeningBadgeClass"
        >
          {{ screeningStatus }}
        </span>
      </div>
      <p class="card-subtitle">
        Enroll new participants and evaluate clinical protocol eligibility criteria in real time.
      </p>
    </div>

    <form
      class="enrollment-form-body"
      @submit.prevent="handleEnrollSubmit"
    >
      <div class="form-grid">
        <!-- Subject ID (Required) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-subject-id"
            v-model="formData.subject_id"
            label="Subject ID *"
            :error="errors.subject_id"
            :attributes="{
              placeholder: 'e.g. SUBJ-101',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('subject_id')"
          />
        </div>

        <!-- Study ID (Required) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-study-id"
            v-model="formData.study_id"
            label="Study Protocol ID *"
            :error="errors.study_id"
            :attributes="{
              placeholder: 'e.g. CADENCE-101',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('study_id')"
          />
        </div>

        <!-- Full Name (Demographics) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-name"
            v-model="formData.name"
            label="Subject Full Name / Initials"
            :error="errors.name"
            :attributes="{
              placeholder: 'e.g. J. Doe',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('name')"
          />
        </div>

        <!-- Birth Date (Demographics) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-birthdate"
            v-model="formData.birthdate"
            label="Birth Date (YYYY-MM-DD)"
            :error="errors.birthdate"
            :attributes="{
              placeholder: 'YYYY-MM-DD',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('birthdate')"
          />
        </div>

        <!-- Gender (Demographics) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-gender"
            v-model="formData.gender"
            label="Gender"
            :error="errors.gender"
            :attributes="{
              placeholder: 'e.g. Male, Female, Other',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('gender')"
          />
        </div>

        <!-- Race (Demographics) -->
        <div class="form-group col-6">
          <ClinicalInput
            id="enroll-race"
            v-model="formData.race"
            label="Race / Ethnicity"
            :error="errors.race"
            :attributes="{
              placeholder: 'e.g. White, Asian, Hispanic',
              disabled: !canEnroll || disabled,
            }"
            @change="validateSingleField('race')"
          />
        </div>
      </div>

      <!-- Screening Results Inspector -->
      <div
        v-if="screeningResult"
        class="screening-evaluation-pane"
        :class="{
          'eligible-pane': screeningResult.eligible === true,
          'ineligible-pane': screeningResult.eligible === false,
          'indeterminate-pane': screeningResult.eligible === null || screeningResult.eligible === undefined,
        }"
      >
        <div class="screening-header-row">
          <div class="screening-outcome-title">
            <span class="outcome-icon">{{ outcomeIcon }}</span>
            <span class="outcome-text">{{ outcomeSummaryText }}</span>
          </div>
          <span
            class="badge"
            :class="screeningBadgeClass"
          >
            {{ outcomeBadgeText }}
          </span>
        </div>

        <!-- Failed Criteria Chips -->
        <div
          v-if="screeningResult.failed_criteria && screeningResult.failed_criteria.length > 0"
          class="criteria-list-section failed-criteria"
        >
          <div class="criteria-list-title">
            Failed Inclusion/Exclusion Criteria:
          </div>
          <div class="criteria-chips-row">
            <span
              v-for="crit in screeningResult.failed_criteria"
              :key="crit"
              class="badge badge-danger criterion-chip"
            >
              ✕ {{ crit }}
            </span>
          </div>
        </div>

        <!-- Indeterminate Criteria Chips -->
        <div
          v-if="screeningResult.indeterminate_criteria && screeningResult.indeterminate_criteria.length > 0"
          class="criteria-list-section indeterminate-criteria"
        >
          <div class="criteria-list-title">
            Indeterminate / Pending Criteria:
          </div>
          <div class="criteria-chips-row">
            <span
              v-for="crit in screeningResult.indeterminate_criteria"
              :key="crit"
              class="badge badge-warning criterion-chip"
            >
              ? {{ crit }}
            </span>
          </div>
        </div>
      </div>

      <!-- Action Buttons -->
      <div class="form-actions-row">
        <button
          type="button"
          class="btn btn-secondary btn-screen-action"
          :disabled="!canEnroll || disabled || isScreening"
          @click="handleScreenClick"
        >
          <span v-if="isScreening">Evaluating...</span>
          <span v-else>🔍 Check Eligibility</span>
        </button>

        <button
          type="submit"
          class="btn btn-primary btn-enroll-action"
          :disabled="!canEnroll || disabled || isEnrolling"
        >
          <span v-if="isEnrolling">Registering...</span>
          <span v-else>➕ Enroll Subject</span>
        </button>
      </div>
    </form>
  </div>
</template>

<script setup>
import { reactive, computed } from "vue";
import { ClinicalInput, validateField } from "ui";

const props = defineProps({
  canEnroll: {
    type: Boolean,
    default: true,
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  defaultStudyId: {
    type: String,
    default: "CADENCE-101",
  },
  screeningResult: {
    type: Object,
    default: null,
  },
  isScreening: {
    type: Boolean,
    default: false,
  },
  isEnrolling: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["enroll", "screen"]);

const formData = reactive({
  subject_id: "",
  study_id: props.defaultStudyId || "CADENCE-101",
  name: "",
  birthdate: "",
  gender: "",
  race: "",
});

const errors = reactive({
  subject_id: null,
  study_id: null,
  name: null,
  birthdate: null,
  gender: null,
  race: null,
});

const fieldMetadata = {
  subject_id: {
    id: "subject_id",
    label: "Subject ID",
    validation: { required: true },
  },
  study_id: {
    id: "study_id",
    label: "Study Protocol ID",
    validation: { required: true },
  },
  name: {
    id: "name",
    label: "Subject Name",
    validation: { required: false },
  },
  birthdate: {
    id: "birthdate",
    label: "Birth Date",
    validation: {
      required: false,
      pattern: "^(\\d{4}-\\d{2}-\\d{2})?$",
      message: "Birth date must follow YYYY-MM-DD format",
    },
  },
  gender: {
    id: "gender",
    label: "Gender",
    validation: { required: false },
  },
  race: {
    id: "race",
    label: "Race",
    validation: { required: false },
  },
};

function validateSingleField(fieldKey) {
  const meta = fieldMetadata[fieldKey];
  if (!meta) return true;
  const value = formData[fieldKey];
  const res = validateField(meta, value);
  if (res && res.valid === false) {
    errors[fieldKey] = res.message || "Invalid field value";
    return false;
  }
  errors[fieldKey] = null;
  return true;
}

function validateAllFields() {
  let isValid = true;
  for (const key of Object.keys(fieldMetadata)) {
    const valid = validateSingleField(key);
    if (!valid) isValid = false;
  }
  return isValid;
}

function handleScreenClick() {
  if (!formData.subject_id || !formData.subject_id.trim()) {
    errors.subject_id = "Subject ID is required for screening";
    return;
  }
  errors.subject_id = null;
  emit("screen", {
    subjectId: formData.subject_id.trim(),
    studyId: formData.study_id.trim(),
  });
}

function handleEnrollSubmit() {
  if (!validateAllFields()) {
    return;
  }

  const demographics = {};
  if (formData.name && formData.name.trim()) demographics.name = formData.name.trim();
  if (formData.birthdate && formData.birthdate.trim()) demographics.birthdate = formData.birthdate.trim();
  if (formData.gender && formData.gender.trim()) demographics.gender = formData.gender.trim();
  if (formData.race && formData.race.trim()) demographics.race = formData.race.trim();

  emit("enroll", {
    subject_id: formData.subject_id.trim(),
    study_id: formData.study_id.trim(),
    demographics,
  });
}

const screeningStatus = computed(() => {
  if (!props.screeningResult) return null;
  if (props.screeningResult.eligible === true) return "ELIGIBLE";
  if (props.screeningResult.eligible === false) return "INELIGIBLE";
  return "INDETERMINATE";
});

const screeningBadgeClass = computed(() => {
  if (!props.screeningResult) return "badge-secondary";
  if (props.screeningResult.eligible === true) return "badge-success";
  if (props.screeningResult.eligible === false) return "badge-danger";
  return "badge-warning";
});

const outcomeIcon = computed(() => {
  if (!props.screeningResult) return "ℹ️";
  if (props.screeningResult.eligible === true) return "✅";
  if (props.screeningResult.eligible === false) return "🚫";
  return "⚠️";
});

const outcomeSummaryText = computed(() => {
  if (!props.screeningResult) return "Pending Evaluation";
  if (props.screeningResult.eligible === true) return "Subject meets all protocol inclusion criteria";
  if (props.screeningResult.eligible === false) return "Subject failed one or more eligibility criteria";
  return "Criteria evaluation requires manual review";
});

const outcomeBadgeText = computed(() => {
  return screeningStatus.value || "NOT EVALUATED";
});
</script>

<style scoped>
.subject-enrollment-card {
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: var(--spacing-md, 16px);
  margin-bottom: var(--spacing-md, 20px);
}

.card-header {
  margin-bottom: var(--spacing-md, 16px);
}

.card-title-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--spacing-sm, 8px);
}

.card-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--text-primary, #0f172a);
  display: flex;
  align-items: center;
  gap: 8px;
}

.card-subtitle {
  margin: 4px 0 0 0;
  font-size: 0.85rem;
  color: var(--text-secondary, #64748b);
}

.enrollment-form-body {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(12, 1fr);
  gap: var(--spacing-sm, 12px);
}

.col-6 {
  grid-column: span 6;
}

@media (max-width: 640px) {
  .col-6 {
    grid-column: span 12;
  }
}

.screening-evaluation-pane {
  padding: 12px 16px;
  border-radius: var(--radius-md, 6px);
  border: 1px solid var(--border, #e2e8f0);
  background-color: var(--surface-secondary, #f8fafc);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.eligible-pane {
  background-color: #f0fdf4;
  border-color: #86efac;
}

.ineligible-pane {
  background-color: #fef2f2;
  border-color: #fca5a5;
}

.indeterminate-pane {
  background-color: #fffbeb;
  border-color: #fde68a;
}

.screening-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.screening-outcome-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 0.9rem;
}

.criteria-list-section {
  font-size: 0.8rem;
}

.criteria-list-title {
  font-weight: 600;
  color: var(--text-secondary, #475569);
  margin-bottom: 4px;
}

.criteria-chips-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.criterion-chip {
  padding: 2px 8px;
  font-size: 0.75rem;
  border-radius: 4px;
}

.form-actions-row {
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-sm, 12px);
  padding-top: var(--spacing-sm, 8px);
  border-top: 1px solid var(--border, #f1f5f9);
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 0.9rem;
  font-weight: 600;
  border-radius: var(--radius-sm, 6px);
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
}

.btn-primary {
  background-color: var(--primary, #2563eb);
  color: #ffffff;
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--primary-hover, #1d4ed8);
}

.btn-secondary {
  background-color: var(--surface, #ffffff);
  border-color: var(--border, #cbd5e1);
  color: var(--text-primary, #334155);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--surface-hover, #f8fafc);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.badge {
  display: inline-block;
  padding: 3px 8px;
  font-size: 0.75rem;
  font-weight: 700;
  border-radius: 9999px;
  text-transform: uppercase;
}

.badge-success {
  background-color: #dcfce7;
  color: #15803d;
}

.badge-danger {
  background-color: #fee2e2;
  color: #b91c1c;
}

.badge-warning {
  background-color: #fef3c7;
  color: #b45309;
}

.badge-secondary {
  background-color: #f1f5f9;
  color: #64748b;
}
</style>
