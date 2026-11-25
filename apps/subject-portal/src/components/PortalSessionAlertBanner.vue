<template>
  <div
    v-if="showInlineSessionAlert"
    class="inline-session-alert-banner"
    role="alert"
  >
    <div class="inline-session-alert-copy">
      <ClinicalIcon name="alert-circle" :size="18" />
      <span
        v-if="sessionStore.authError"
        id="session-auth-error"
        class="inline-session-alert-text"
      >
        {{ sessionStore.authError }}
      </span>
      <span
        v-else
        id="session-expired-banner"
        class="inline-session-alert-text"
      >
        Your session has expired. Sign in again to continue — your local drafts
        are preserved on this device.
      </span>
    </div>
    <button
      type="button"
      class="btn btn-primary inline-session-alert-btn"
      :data-testid="isSessionExpired ? 'reauthenticate-btn' : 'signin-btn'"
      @click="login"
    >
      {{ isSessionExpired ? "Sign In to Continue" : "Sign In" }}
    </button>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";

import { usePortalSessionChrome } from "../composables/usePortalSessionChrome.js";
import { login } from "../features/auth.js";

const { sessionStore, isSessionExpired, showInlineSessionAlert } =
  usePortalSessionChrome();
</script>

<style scoped>
.inline-session-alert-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 10px 14px;
  margin-bottom: 12px;
  border-radius: 8px;
  border: 1px solid var(--danger);
  background-color: var(--danger-bg);
  color: var(--semantic-color-error-text-dark);
  font-size: 13px;
  font-weight: 600;
}

.inline-session-alert-copy {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex: 1 1 200px;
  min-width: 0;
}

.inline-session-alert-text {
  min-width: 0;
}

.btn.inline-session-alert-btn {
  padding: 6px 12px;
  font-size: 13px;
}
</style>
