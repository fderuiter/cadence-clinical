<template>
  <ClinicalModal
    :model-value="modelValue"
    :teleport="false"
    :dismissible="!submitting"
    dialog-class="modal-card"
    title="Author Protocol Training Curriculum"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <p class="modal-description">
      Publish a protocol procedural delta curriculum with verification quiz
      threshold for study <strong>{{ studyId || "Active Study" }}</strong
      >.
    </p>
    <p v-if="errorMessage" class="badge badge-error" role="alert">
      {{ errorMessage }}
    </p>

    <div class="author-form-stack">
      <div class="form-group">
        <label for="curriculum-author-title">Curriculum Title</label>
        <input
          id="curriculum-author-title"
          :value="form.title"
          type="text"
          class="text-input control-touch"
          :disabled="submitting"
          placeholder="Protocol v2.0 Core Qualification"
          @input="updateField('title', $event.target.value)"
        />
      </div>

      <div class="author-two-col">
        <div class="form-group">
          <label for="curriculum-author-version">Protocol Version</label>
          <input
            id="curriculum-author-version"
            :value="form.protocolVersion"
            type="text"
            class="text-input control-touch"
            :disabled="submitting"
            placeholder="2.0"
            @input="updateField('protocolVersion', $event.target.value)"
          />
        </div>
        <div class="form-group">
          <label for="curriculum-author-passing">Passing Threshold (%)</label>
          <input
            id="curriculum-author-passing"
            :value="form.passingPercent"
            type="number"
            min="50"
            max="100"
            class="text-input control-touch tabular-nums"
            :disabled="submitting"
            @input="updateField('passingPercent', Number($event.target.value))"
          />
        </div>
      </div>

      <div class="form-group">
        <label for="curriculum-author-mod-title">Primary Module Title</label>
        <input
          id="curriculum-author-mod-title"
          :value="form.moduleTitle"
          type="text"
          class="text-input control-touch"
          :disabled="submitting"
          placeholder="Protocol Synopsis & Eligibility Deltas"
          @input="updateField('moduleTitle', $event.target.value)"
        />
      </div>

      <div class="form-group">
        <label for="curriculum-author-mod-summary">
          Procedural Delta Summary
        </label>
        <textarea
          id="curriculum-author-mod-summary"
          :value="form.moduleSummary"
          rows="3"
          class="text-input control-touch"
          :disabled="submitting"
          placeholder="Summarize eligibility, dosing, or visit schedule deltas..."
          @input="updateField('moduleSummary', $event.target.value)"
        ></textarea>
      </div>

      <div class="form-group">
        <label for="curriculum-author-quiz-prompt">
          Verification Quiz Question
        </label>
        <input
          id="curriculum-author-quiz-prompt"
          :value="form.quizPrompt"
          type="text"
          class="text-input control-touch"
          :disabled="submitting"
          placeholder="What is the updated eGFR eligibility threshold?"
          @input="updateField('quizPrompt', $event.target.value)"
        />
      </div>

      <div class="author-two-col">
        <div class="form-group">
          <label for="curriculum-author-opt-correct">Correct Option</label>
          <input
            id="curriculum-author-opt-correct"
            :value="form.correctOptionLabel"
            type="text"
            class="text-input control-touch"
            :disabled="submitting"
            placeholder=">= 45 mL/min/1.73m2"
            @input="updateField('correctOptionLabel', $event.target.value)"
          />
        </div>
        <div class="form-group">
          <label for="curriculum-author-opt-distractor">
            Distractor Option
          </label>
          <input
            id="curriculum-author-opt-distractor"
            :value="form.distractorOptionLabel"
            type="text"
            class="text-input control-touch"
            :disabled="submitting"
            placeholder=">= 60 mL/min/1.73m2 (superseded)"
            @input="updateField('distractorOptionLabel', $event.target.value)"
          />
        </div>
      </div>

      <div class="form-group">
        <label for="curriculum-author-reason">Reason for Change</label>
        <input
          id="curriculum-author-reason"
          :value="form.reasonForChange"
          type="text"
          class="text-input control-touch"
          :disabled="submitting"
          placeholder="Release protocol amendment training curriculum"
          @input="updateField('reasonForChange', $event.target.value)"
        />
      </div>
    </div>

    <template #footer>
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="submitting"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </button>
      <button
        id="btn-submit-curriculum"
        type="button"
        class="btn btn-primary"
        :disabled="
          submitting ||
          !form.title.trim() ||
          !form.protocolVersion.trim() ||
          !form.moduleTitle.trim() ||
          !form.moduleSummary.trim() ||
          !form.reasonForChange.trim()
        "
        @click="emit('submit')"
      >
        {{ submitting ? "Publishing..." : "Publish Curriculum" }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ClinicalModal } from "ui";

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  studyId: {
    type: String,
    default: "",
  },
  form: {
    type: Object,
    required: true,
  },
  submitting: {
    type: Boolean,
    default: false,
  },
  errorMessage: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["update:modelValue", "update:form", "submit"]);

function updateField(key, value) {
  emit("update:form", {
    ...props.form,
    [key]: value,
  });
}
</script>

<style scoped src="../protocolTrainingConsole.css"></style>
