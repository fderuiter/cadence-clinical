<template>
  <AppShell>
    <router-view />
  </AppShell>
  <ContextualHelpDrawer v-if="showContextualHelp" />
  <Teleport to="body">
    <ToastHost />
  </Teleport>
  <ConfirmationDialogHost />
</template>

<script setup>
import { computed, onMounted } from "vue";
import { getActivePinia } from "pinia";
import { useRoute } from "vue-router";
import AppShell from "./components/AppShell.vue";
import ContextualHelpDrawer from "./components/ContextualHelpDrawer.vue";
import ToastHost from "./components/ToastHost.vue";
import ConfirmationDialogHost from "./components/ConfirmationDialogHost.vue";
import { useAuthStore } from "./stores/auth.js";
import { isDemoBuild } from "./utils/demoMode.js";
import "./style.css";

const pinia = getActivePinia();
const route = useRoute();
const authStore = useAuthStore();

const showContextualHelp = computed(
  () =>
    Boolean(authStore.isAuthenticated) &&
    route?.name !== "login" &&
    route?.path !== "/login"
);

onMounted(async () => {
  if (!isDemoBuild() || !pinia) {
    return;
  }
  const { useClinicalStore } = await import("./stores/clinical");
  const store = useClinicalStore(pinia);
  if (store.ledgerBlocks.length === 0) {
    await store.addLedgerBlock(
      "GENESIS",
      {
        platform: import.meta.env.VITE_BRAND_NAME || "Cadence Clinical",
        environment: "Interactive Web Sandbox",
        compliantStandards: [
          "21 CFR Part 11",
          "GAMP 5 (Category 4/5)",
          /* deid: ignore */ "IEC 62304",
          /* deid: ignore */ "ISO 14155:2020",
        ],
      },
      "System boot and cryptographic ledger initialized successfully."
    );
  }
});
</script>

<style>
/* Any additional global or fallback styles if needed */
</style>
