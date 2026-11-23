<template>
  <section
    id="view-questionnaire"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-questionnaire' }"
    :hidden="ui.currentView !== 'view-questionnaire'"
    :inert="ui.currentView !== 'view-questionnaire' || undefined"
    :role="hasTasksTab ? 'tabpanel' : 'region'"
    :aria-labelledby="questionnaireLabelledBy"
    tabindex="0"
  >
    <div class="view-header">
      <button
        id="btn-back-to-tasks"
        type="button"
        class="btn-back"
        data-vue-bound="true"
        @click="handleBackToTasks"
      >
        <ArrowLeft :size="18" aria-hidden="true" />
        <span>Back to My Tasks</span>
      </button>
      <h2 id="questionnaire-title" tabindex="-1">Questionnaire</h2>
      <p id="questionnaire-desc">Please answer all questions accurately.</p>
    </div>

    <div class="card questionnaire-card">
      <form id="questionnaire-form" @submit.prevent="submitActiveQuestionnaire">
        <div
          id="questionnaire-form-error"
          role="alert"
          aria-live="assertive"
          style="
            display: none;
            margin-bottom: 16px;
            padding: 12px 16px;
            border-radius: 8px;
            background: var(--semantic-color-error-bg);
            border: 1px solid var(--semantic-color-error);
            color: var(--semantic-color-error);
            font-size: 14px;
            font-weight: 600;
          "
        />
        <div id="questionnaire-form-container" data-vue-questionnaire="true">
          <div v-if="activeItems.length > 0" class="clinical-form-grid">
            <h3 class="form-title">Your Survey Questions</h3>
            <template
              v-for="item in activeItems"
              :key="`${activeAssignmentId}-${item.id}`"
            >
              <ClinicalSlider
                v-if="item.kind === 'slider'"
                :id="item.id"
                :label="item.field.label"
                :min="item.min"
                :max="item.max"
                :step="item.step"
                :min-label="item.minLabel"
                :max-label="item.maxLabel"
                :grid-span="12"
                :show-query-flag="false"
                :error="getFieldError(item.id)"
                :model-value="getAnswerValue(item.id)"
                :touched="getSliderTouched(item.id)"
                @update:model-value="onAnswerChange(item.id, $event)"
                @update:touched="onSliderTouched(item.id, $event)"
              />
              <ClinicalRadioGroup
                v-else-if="item.kind === 'choice_single'"
                :id="item.id"
                :label="item.field.label"
                :options="item.field.options || []"
                :grid-span="12"
                :show-query-flag="false"
                :error="getFieldError(item.id)"
                :model-value="getAnswerValue(item.id)"
                @update:model-value="onAnswerChange(item.id, $event)"
              />
              <ClinicalInput
                v-else
                :id="item.id"
                :label="item.field.label"
                :input-type="item.inputType"
                :input-attributes="item.inputAttributes"
                :grid-span="12"
                :show-query-flag="false"
                :error="getFieldError(item.id)"
                :model-value="getAnswerValue(item.id)"
                @update:model-value="onAnswerChange(item.id, $event)"
              />
            </template>
          </div>
        </div>
        <div class="form-actions">
          <button
            id="btn-cancel-questionnaire"
            type="button"
            class="btn btn-secondary"
            data-vue-bound="true"
            @click="handleRequestCancel"
          >
            Cancel
          </button>
          <button
            id="btn-submit-questionnaire"
            type="button"
            class="btn btn-primary"
          >
            Sign and Submit
          </button>
        </div>
      </form>
    </div>

    <!-- Accessible Cancel Confirmation Dialog -->
    <ClinicalModal
      id="questionnaire-cancel-confirm"
      overlay-id="questionnaire-cancel-confirm-backdrop"
      role="alertdialog"
      :open="ui.questionnaireCancelConfirmOpen"
      :teleport="false"
      :show-header="false"
      :show-footer="false"
      :show-close-button="false"
      :close-on-backdrop="false"
      aria-labelledby="cancel-confirm-title"
      aria-describedby="cancel-confirm-desc"
      body-class="modal-body-flush"
      initial-focus="#btn-cancel-keep-editing"
      @close="dismissCancelQuestionnaire"
    >
      <div class="modal">
        <div id="cancel-confirm-title" class="modal-header">
          Discard Survey Draft?
        </div>
        <div class="modal-body">
          <p id="cancel-confirm-desc">
            Canceling will discard your entered answers for this survey. If you
            want to step away and keep your progress, choose
            <strong>Keep Editing</strong> and use
            <strong>Back to My Tasks</strong> instead.
          </p>
        </div>
        <div class="modal-footer modal-footer-wrap">
          <button
            id="btn-cancel-keep-editing"
            type="button"
            class="btn btn-secondary"
            @click="dismissCancelQuestionnaire"
          >
            Keep Editing
          </button>
          <button
            id="btn-cancel-confirm-discard"
            type="button"
            class="btn btn-primary btn-danger-solid"
            @click="confirmCancelQuestionnaire"
          >
            Discard Draft &amp; Leave
          </button>
        </div>
      </div>
    </ClinicalModal>
  </section>
</template>

<script setup>
import {
  computed,
  getCurrentInstance,
  nextTick,
  onMounted,
  ref,
  watch,
} from "vue";
import { ArrowLeft } from "lucide-vue-next";
import {
  ClinicalInput,
  ClinicalModal,
  ClinicalRadioGroup,
  ClinicalSlider,
} from "ui";

import {
  buildQuestionnaireItemDescriptors,
  confirmCancelQuestionnaire,
  debouncedSaveActiveDraft,
  dismissCancelQuestionnaire,
  requestCancelQuestionnaire,
  saveAndLeaveQuestionnaire,
} from "../features/questionnaire.js";
import { useSessionStore } from "../stores/session.js";
import { useTasksStore } from "../stores/tasks.js";
import { useUiStore } from "../stores/ui.js";

const instance = getCurrentInstance();
const ui = useUiStore();
const session = useSessionStore();
const tasksStore = useTasksStore();
const hasTasksTab = ref(false);

const activeAssignmentId = computed(
  () => tasksStore.activeQuestionnaire?.assignment?.id || "none"
);

const activeItems = computed(() =>
  buildQuestionnaireItemDescriptors(tasksStore.activeQuestionnaire?.instrument)
);

const activeErrorsSignature = computed(() =>
  JSON.stringify(tasksStore.activeQuestionnaire?.errors || {})
);

watch(
  () => [
    activeAssignmentId.value,
    activeItems.value.length,
    activeErrorsSignature.value,
  ],
  () => {
    if (instance?.isMounted && typeof instance.update === "function") {
      instance.update();
    }
  },
  { flush: "sync" }
);

function getAnswerValue(fieldId) {
  const val = tasksStore.activeQuestionnaire?.answers?.[fieldId];
  return val === undefined || val === null ? "" : val;
}

function getFieldError(fieldId) {
  return tasksStore.activeQuestionnaire?.errors?.[fieldId] || "";
}

function getSliderTouched(fieldId) {
  const active = tasksStore.activeQuestionnaire;
  if (!active) return false;
  if (active.touchedSliders && fieldId in active.touchedSliders) {
    return Boolean(active.touchedSliders[fieldId]);
  }
  const val = active.answers?.[fieldId];
  return val !== undefined && val !== null && val !== "";
}

function onAnswerChange(fieldId, value) {
  const active = tasksStore.activeQuestionnaire;
  if (!active) return;
  if (!active.answers) {
    active.answers = {};
  }
  active.answers[fieldId] = value;
  if (active.errors && active.errors[fieldId]) {
    delete active.errors[fieldId];
  }
  debouncedSaveActiveDraft();
}

function onSliderTouched(fieldId, touched) {
  const active = tasksStore.activeQuestionnaire;
  if (!active) return;
  if (!active.touchedSliders) {
    active.touchedSliders = {};
  }
  active.touchedSliders[fieldId] = Boolean(touched);
  debouncedSaveActiveDraft();
}

function syncTasksTabPresence() {
  hasTasksTab.value =
    typeof document !== "undefined" &&
    Boolean(document.getElementById("btn-tab-btn-tasks"));
}

onMounted(syncTasksTabPresence);

watch(
  [
    () => ui.currentView,
    () => session.session?.isDemoMode,
    () => session.session?.token,
  ],
  () => {
    syncTasksTabPresence();
    nextTick(syncTasksTabPresence);
  }
);

const questionnaireLabelledBy = computed(() =>
  hasTasksTab.value ? "btn-tab-btn-tasks" : "questionnaire-title"
);

function submitActiveQuestionnaire() {
  const submitBtn = document.getElementById("btn-submit-questionnaire");
  if (submitBtn) {
    submitBtn.click();
  }
}

function handleBackToTasks() {
  saveAndLeaveQuestionnaire();
}

function handleRequestCancel() {
  requestCancelQuestionnaire();
}
</script>
