<template>
  <AppShell>
    <router-view />
  </AppShell>
  <ContextualHelpDrawer />
  <Teleport to="body">
    <EntityPeekDrawerHost v-if="peekState.open" />
    <ToastHost />
  </Teleport>
  <ConfirmationDialogHost v-if="confirmStore.activeRequest" />
</template>

<script setup>
import { defineAsyncComponent, onMounted } from "vue";
import { getActivePinia } from "pinia";
import AppShell from "./components/AppShell.vue";
import ContextualHelpDrawer from "./components/ContextualHelpDrawer.vue";
import ToastHost from "./components/ToastHost.vue";
import { useEntityPeekDrawer } from "./composables/useEntityPeekDrawer";
import { useConfirmStore } from "./stores/confirm.js";
import { isDemoBuild } from "./utils/demoMode.js";
import "./style.css";

const EntityPeekDrawerHost = defineAsyncComponent(
  () => import("./components/canvas/EntityPeekDrawerHost.vue")
);
const ConfirmationDialogHost = defineAsyncComponent(
  () => import("./components/ConfirmationDialogHost.vue")
);

const { state: peekState } = useEntityPeekDrawer();
const pinia = getActivePinia();
const confirmStore = useConfirmStore(pinia);

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
