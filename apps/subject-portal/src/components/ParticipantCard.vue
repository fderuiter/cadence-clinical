<template>
  <div class="participant-info-card">
    <div class="info-title">Participant Session</div>
    <div class="info-row">
      <span class="info-lbl">ID:</span>
      <span id="session-subject-id" class="info-val">{{
        sessionStore.session.userId || "Loading..."
      }}</span>
    </div>
    <div class="info-row">
      <span class="info-lbl">Status:</span>
      <span class="info-val text-success">Active</span>
    </div>
    <div class="info-row">
      <span class="info-lbl">Stipend Wallet:</span>
      <span
        id="session-wallet-balance"
        class="info-val text-success"
        style="font-weight: bold; color: var(--semantic-color-success)"
      >
        ${{
          wallet.walletBalance !== undefined && wallet.walletBalance !== null
            ? Number(wallet.walletBalance).toFixed(2)
            : "0.00"
        }}
      </span>
    </div>
    <div class="info-row" style="margin-top: 12px">
      <button
        id="btn-logout"
        type="button"
        class="btn btn-secondary"
        style="width: 100%; padding: 4px 8px; font-size: 12px"
        @click="handleLogout"
      >
        Logout
      </button>
    </div>
  </div>
</template>

<script setup>
import { logout } from "../features/auth.js";
import { useSessionStore } from "../stores/session.js";
import { useWalletStore } from "../stores/wallet.js";

const sessionStore = useSessionStore();
const wallet = useWalletStore();

function handleLogout() {
  logout();
}
</script>
