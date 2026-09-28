<template>
  <AppShell>
    <router-view />
  </AppShell>
  <ContextualHelpDrawer />
</template>

<script setup>
import { onMounted } from "vue";
import { useClinicalStore } from "./stores/clinical";
import AppShell from "./components/AppShell.vue";
import ContextualHelpDrawer from "./components/ContextualHelpDrawer.vue";
import "./style.css";

const store = useClinicalStore();

onMounted(async () => {
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
  await store.startSyncTimer();
});
</script>

<style>
/* Any additional global or fallback styles if needed */
</style>
