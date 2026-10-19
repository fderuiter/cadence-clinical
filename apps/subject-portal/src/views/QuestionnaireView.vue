<template>
  <section
    id="view-questionnaire"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-questionnaire' }"
  >
    <div class="view-header">
      <button id="btn-back-to-tasks" type="button" class="btn-back">
        ← Back to My Tasks
      </button>
      <h2 id="questionnaire-title">Questionnaire</h2>
      <p id="questionnaire-desc">Please answer all questions accurately.</p>
    </div>

    <div class="card questionnaire-card">
      <form id="questionnaire-form" @submit.prevent="submitActiveQuestionnaire">
        <div id="questionnaire-form-container">
          <!-- Dynamically compiled CDASH-inputs go here -->
        </div>
        <div class="form-actions">
          <button
            id="btn-cancel-questionnaire"
            type="button"
            class="btn btn-secondary"
            @click="cancelQuestionnaire"
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
  </section>
</template>

<script setup>
import { showView } from "../features/navigation.js";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();

function submitActiveQuestionnaire() {
  const submitBtn = document.getElementById("btn-submit-questionnaire");
  if (submitBtn) {
    submitBtn.click();
  }
}

function cancelQuestionnaire() {
  showView("view-tasks");
}
</script>
