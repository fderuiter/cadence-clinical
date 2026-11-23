<template>
  <section
    id="view-questionnaire"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-questionnaire' }"
    :hidden="ui.currentView !== 'view-questionnaire'"
    :inert="ui.currentView !== 'view-questionnaire' || undefined"
    role="tabpanel"
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
        ← Back to My Tasks
      </button>
      <h2 id="questionnaire-title">Questionnaire</h2>
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
            font-size: var(--font-size-sm, 0.875rem);
            font-weight: 600;
          "
        />
        <div id="questionnaire-form-container">
          <!-- Dynamically compiled CDASH-inputs go here -->
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
            type="submit"
            class="btn btn-primary"
          >
            Sign and Submit
          </button>
        </div>
      </form>
    </div>

    <!-- Accessible Cancel Confirmation Dialog -->
    <div
      v-if="ui.questionnaireCancelConfirmOpen"
      id="questionnaire-cancel-confirm"
      ref="cancelDialogRef"
      class="modal-overlay"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="cancel-confirm-title"
      aria-describedby="cancel-confirm-desc"
      tabindex="-1"
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
    </div>
  </section>
</template>

<script setup>
import { computed, nextTick, onMounted, ref, watch } from "vue";
import { useFocusTrap } from "ui";

import {
  confirmCancelQuestionnaire,
  dismissCancelQuestionnaire,
  requestCancelQuestionnaire,
  saveAndLeaveQuestionnaire,
} from "../features/questionnaire.js";
import { useUiStore } from "../stores/ui.js";

const cancelDialogRef = ref(null);
const ui = useUiStore();
const hasTasksTab = ref(false);

function syncTasksTabPresence() {
  hasTasksTab.value =
    typeof document !== "undefined" &&
    Boolean(document.getElementById("btn-tab-btn-tasks"));
}

onMounted(syncTasksTabPresence);

watch(
  () => ui.currentView,
  () => {
    syncTasksTabPresence();
    nextTick(syncTasksTabPresence);
  }
);

const questionnaireLabelledBy = computed(() =>
  hasTasksTab.value ? "btn-tab-btn-tasks" : "questionnaire-title"
);

useFocusTrap(cancelDialogRef, {
  isOpen: () => ui.questionnaireCancelConfirmOpen,
  onEscape: dismissCancelQuestionnaire,
  initialFocus: "#btn-cancel-keep-editing",
});

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
