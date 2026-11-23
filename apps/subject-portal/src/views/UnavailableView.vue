<template>
  <section
    id="view-unavailable"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-unavailable' }"
    :hidden="ui.currentView !== 'view-unavailable'"
    :inert="ui.currentView !== 'view-unavailable' || undefined"
    aria-labelledby="unavailable-view-heading"
    tabindex="0"
  >
    <div
      class="card unavailable-card"
      style="
        max-width: 600px;
        margin: 24px auto;
        padding: 32px;
        text-align: center;
      "
    >
      <h2
        id="unavailable-view-heading"
        style="margin-top: 0; margin-bottom: 12px"
      >
        {{ heading }}
      </h2>
      <p
        style="
          font-size: var(--font-size-sm, 0.875rem);
          color: var(--text-muted);
          margin-bottom: 20px;
        "
      >
        This section is not open for your study right now. Nothing was changed
        or submitted. You can return to your tasks or contact your study
        coordinator if you have questions.
      </p>
      <button
        id="btn-unavailable-return-tasks"
        type="button"
        class="btn btn-primary"
        @click="returnToTasks"
      >
        Return to My Tasks
      </button>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";

import { showView } from "../features/navigation.js";
import { screenTitle } from "../router/availability.js";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();

const heading = computed(() => {
  const screen = ui.unavailableScreen;
  return screen
    ? `${screenTitle(screen)} isn't available yet`
    : "This workflow isn't available yet";
});

function returnToTasks() {
  ui.unavailableScreen = null;
  showView("view-tasks");
}
</script>
