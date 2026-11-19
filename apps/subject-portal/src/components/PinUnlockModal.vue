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
      <div
        v-if="sessionStore.pinUnlock.confirmingReset"
        id="pin-reset-confirmation"
      >
        <div id="pin-unlock-title" class="modal-header">
          Confirm Security PIN Reset
        </div>
        <div class="modal-body">
          <div
            id="pin-reset-warning"
            role="alert"
            style="
              padding: 12px;
              margin-bottom: 16px;
              border-radius: 6px;
              border: 1px solid var(--semantic-color-error);
              background-color: var(--semantic-color-error-bg);
              color: var(--semantic-color-error);
              font-size: 14px;
              line-height: 1.5;
            "
          >
            <strong>Warning:</strong> Resetting your security PIN will
            permanently clear any locked local offline diary entries and unsent
            drafts stored on this device. Already synchronized clinical records
            on the study server are not affected.
          </div>
          <p style="font-size: 14px; margin: 0">
            Do you want to clear locked local entries and set a new PIN?
          </p>
        </div>
        <div
          class="modal-footer"
          style="
            display: flex;
            gap: 8px;
            justify-content: flex-end;
            flex-wrap: wrap;
          "
        >
          <button
            id="btn-pin-reset-cancel"
            type="button"
            class="btn btn-secondary"
            @click="cancelPINReset"
          >
            Cancel
          </button>
          <button
            id="btn-pin-reset-confirm"
            type="button"
            class="btn btn-primary"
            style="
              background-color: var(--semantic-color-error);
              border-color: var(--semantic-color-error);
            "
            @click="confirmPINReset"
          >
            Clear Local Entries &amp; Reset PIN
          </button>
        </div>
      </div>

      <form v-else id="pin-unlock-form" @submit.prevent="handlePINUnlockSubmit">
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
        <div
          class="modal-footer"
          style="
            display: flex;
            gap: 8px;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
          "
        >
          <div style="display: flex; gap: 8px; flex-wrap: wrap">
            <button
              id="btn-pin-unlock-signout"
              type="button"
              class="btn btn-secondary"
              @click="handleSignOut"
            >
              Sign Out
            </button>
            <button
              id="btn-pin-unlock-reset"
              type="button"
              class="btn btn-secondary"
              @click="requestPINReset"
            >
              Forgot / Reset PIN
            </button>
          </div>
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
import { logout } from "../features/auth.js";
import {
  cancelPINReset,
  confirmPINReset,
  handlePINUnlockSubmit,
  requestPINReset,
} from "../features/pin.js";
import { useSessionStore } from "../stores/session.js";
import { trapTabKey } from "./focus-trap.js";

const sessionStore = useSessionStore();

function handleUnlockModalKeyDown(e) {
  trapTabKey(e, "portal-pin-unlock-modal", ["input", "button"]);
}

function handleSignOut() {
  logout();
}
</script>
