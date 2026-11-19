<template>
  <section
    id="view-unavailable"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-unavailable' }"
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
      <p style="font-size: 14px; color: var(--text-muted); margin-bottom: 16px">
        This participant workflow is disabled in production until its backend
        clinical and authorization services are active. Nothing was submitted or
        modified.
      </p>
      <p style="font-size: 14px; margin-bottom: 20px">
        <a
          :href="UNAVAILABLE_SCREENS_ISSUE"
          target="_blank"
          rel="noopener noreferrer"
        >
          See workflow availability status
        </a>
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
import {
  UNAVAILABLE_SCREENS_ISSUE,
  screenTitle,
} from "../router/availability.js";
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
