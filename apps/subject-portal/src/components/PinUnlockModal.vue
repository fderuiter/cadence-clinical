<template>
  <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -->
  <div
    v-if="sessionStore.pinUnlock.isOpen"
    id="portal-pin-unlock-modal"
    class="modal-overlay"
    role="dialog"
    aria-modal="true"
    aria-labelledby="pin-unlock-title"
    tabindex="-1"
    @keydown="handleUnlockModalKeyDown"
  >
    <div class="modal">
      <form id="pin-unlock-form" @submit.prevent="handlePINUnlockSubmit">
        <div id="pin-unlock-title" class="modal-header">
          Device Locked - Enter Security PIN
        </div>
        <div class="modal-body">
          <p>
            Please enter your numeric security PIN to access the clinical portal
            and decrypt your offline data.
          </p>
          <div
            v-if="sessionStore.pinUnlock.error"
            id="pin-unlock-error"
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
            {{ sessionStore.pinUnlock.error }}
          </div>
          <div class="form-group">
            <label for="unlock-pin">Security PIN</label>
            <input
              id="unlock-pin"
              v-model="sessionStore.pinUnlock.pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              required
              @keydown.enter="handlePINUnlockSubmit"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-pin-unlock-confirm"
            type="submit"
            class="btn btn-primary"
          >
            Unlock
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { handlePINUnlockSubmit } from "../features/pin.js";
import { useSessionStore } from "../stores/session.js";
import { trapTabKey } from "./focus-trap.js";

const sessionStore = useSessionStore();

function handleUnlockModalKeyDown(e) {
  trapTabKey(e, "portal-pin-unlock-modal", ["input", "button"]);
}
</script>
