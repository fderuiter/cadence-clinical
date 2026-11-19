<template>
  <div
    v-if="sessionStore.pinSetup.isOpen"
    id="portal-pin-setup-modal"
    ref="modalRef"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-labelledby="pin-setup-title"
    tabindex="-1"
  >
    <div class="modal">
      <form id="pin-setup-form" @submit.prevent="handlePINSetupSubmit">
        <div id="pin-setup-title" class="modal-header">
          Configure Security PIN
        </div>
        <div class="modal-body">
          <p>
            To secure your offline clinical data, please choose a numeric local
            security PIN. This PIN will wrap and encrypt your offline database
            encryption key.
          </p>
          <div
            v-if="sessionStore.pinSetup.error"
            class="error-state"
            style="
              color: var(--danger);
              font-weight: bold;
              font-size: 14px;
              margin-bottom: 12px;
            "
            role="status"
            aria-live="polite"
          >
            {{ sessionStore.pinSetup.error }}
          </div>
          <div class="form-group mb-12">
            <label for="setup-pin">Choose Numeric PIN</label>
            <input
              id="setup-pin"
              v-model="sessionStore.pinSetup.pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              required
            />
          </div>
          <div class="form-group">
            <label for="confirm-setup-pin">Confirm Numeric PIN</label>
            <input
              id="confirm-setup-pin"
              v-model="sessionStore.pinSetup.confirmPin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              required
            />
          </div>
        </div>
        <div
          class="modal-footer"
          style="
            display: flex;
            gap: 8px;
            justify-content: space-between;
            flex-wrap: wrap;
          "
        >
          <button
            id="btn-pin-setup-signout"
            type="button"
            class="btn btn-secondary"
            @click="handleSignOut"
          >
            Sign Out
          </button>
          <button
            id="btn-pin-setup-confirm"
            type="submit"
            class="btn btn-primary"
          >
            Save and Configure
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { useFocusTrap } from "ui";

import { logout } from "../features/auth.js";
import { handlePINSetupSubmit } from "../features/pin.js";
import { useSessionStore } from "../stores/session.js";

const modalRef = ref(null);
const sessionStore = useSessionStore();

useFocusTrap(modalRef, {
  isOpen: () => sessionStore.pinSetup.isOpen,
  escapeDeactivates: false,
  initialFocus: "#setup-pin",
});

function handleSignOut() {
  logout();
}
</script>
