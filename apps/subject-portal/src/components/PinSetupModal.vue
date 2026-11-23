<template>
  <ClinicalModal
    id="portal-pin-setup-dialog"
    overlay-id="portal-pin-setup-modal"
    :is-open="sessionStore.pinSetup.isOpen"
    :teleport="false"
    :dismissible="false"
    :close-on-backdrop="false"
    :show-header="false"
    :show-footer="false"
    initial-focus="#setup-pin"
    aria-labelledby="pin-setup-title"
    backdrop-class="modal-overlay"
    dialog-class="modal"
    body-class="modal-content-passthrough"
  >
    <form id="pin-setup-form" @submit.prevent="handlePINSetupSubmit">
      <div id="pin-setup-title" class="modal-header">
        Configure Security PIN
      </div>
      <div class="modal-body">
        <p>
          Choose a numeric security PIN to keep your saved survey answers
          private on this device, even when you are offline.
        </p>
        <div
          v-if="sessionStore.pinSetup.error"
          class="error-state pin-error-msg"
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
      <div class="modal-footer modal-footer-between">
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
      <div class="modal-support-strip">
        <SiteContactLinks
          id="pin-setup-site-contacts"
          compact
          heading="Study Site Coordinator"
          region-label="PIN setup study site coordinator contacts"
        />
      </div>
    </form>
  </ClinicalModal>
</template>

<script setup>
import { ClinicalModal } from "ui";

import { logout } from "../features/auth.js";
import { handlePINSetupSubmit } from "../features/pin.js";
import { useSessionStore } from "../stores/session.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const sessionStore = useSessionStore();

function handleSignOut() {
  logout();
}
</script>
