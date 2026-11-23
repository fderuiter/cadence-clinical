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
      <p style="font-size: 14px; color: var(--text-muted); margin-bottom: 20px">
        {{ bodyMessage }}
      </p>
      <button
        v-if="canReturnToTasks || canReturnToWallet"
        id="btn-unavailable-return-tasks"
        type="button"
        class="btn btn-primary"
        @click="returnToAvailableScreen"
      >
        {{
          canReturnToTasks
            ? "Return to My Tasks"
            : "Return to Participant Wallet"
        }}
      </button>
      <button
        v-else
        id="btn-unavailable-signin"
        type="button"
        class="btn btn-primary"
        @click="handleSignIn"
      >
        Sign In
      </button>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";

import { login } from "../features/auth.js";
import { showView } from "../features/navigation.js";
import { isScreenAvailable, screenTitle } from "../router/availability.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
const sessionStore = useSessionStore();

const canReturnToTasks = computed(() => {
  void sessionStore.session?.isDemoMode;
  void sessionStore.session?.token;
  void sessionStore.session?.userId;
  void sessionStore.session?.isOfflineMode;
  return isScreenAvailable("tasks");
});

const canReturnToWallet = computed(() => {
  void sessionStore.session?.isDemoMode;
  void sessionStore.session?.token;
  void sessionStore.session?.userId;
  void sessionStore.session?.isOfflineMode;
  return !canReturnToTasks.value && isScreenAvailable("wallet");
});

const heading = computed(() => {
  const screen = ui.unavailableScreen;
  if (screen) {
    return `${screenTitle(screen)} isn't available yet`;
  }
  if (!canReturnToTasks.value && !canReturnToWallet.value) {
    return "Sign in to access your participant portal";
  }
  return "This workflow isn't available yet";
});

const bodyMessage = computed(() => {
  if (
    !ui.unavailableScreen &&
    !canReturnToTasks.value &&
    !canReturnToWallet.value
  ) {
    return "Authenticate with your participant credentials to access your financial wallet and study stipends, or contact your study coordinator if you need assistance.";
  }
  if (canReturnToTasks.value) {
    return "This section is not open for your study right now. Nothing was changed or submitted. You can return to your tasks or contact your study coordinator if you have questions.";
  }
  if (canReturnToWallet.value) {
    return "This section is not open for your study right now. Nothing was changed or submitted. You can return to your participant wallet or contact your study coordinator if you have questions.";
  }
  return "This section is not open for your study right now. Nothing was changed or submitted. Sign in to access your participant wallet or contact your study coordinator if you have questions.";
});

function returnToAvailableScreen() {
  ui.unavailableScreen = null;
  showView(canReturnToTasks.value ? "view-tasks" : "view-wallet");
}

function handleSignIn() {
  login();
}
</script>
