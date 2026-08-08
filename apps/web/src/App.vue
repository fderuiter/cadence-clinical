<template>
  <AppShell>
    <router-view />
  </AppShell>
</template>

<script setup>
import { onMounted } from "vue";
import { useClinicalStore } from "./stores/clinical";
import AppShell from "./components/AppShell.vue";
import "./style.css";

const store = useClinicalStore();

onMounted(async () => {
  try {
    const apiBaseUrl =
      import.meta.env?.VITE_API_BASE_URL || "http://localhost:8000";
    const res = await fetch(`${apiBaseUrl}/api/v1/gateway/config`);
    if (res.ok) {
      const config = await res.json();
      if (config && config.theme) {
        for (const [key, val] of Object.entries(config.theme)) {
          document.documentElement.style.setProperty(key, val);
        }
      }
      if (config && config.brand_name) {
        if (window.BRAND_NAME_REF) {
          window.BRAND_NAME_REF.value = config.brand_name;
        }
        window.BRAND_NAME = config.brand_name;
      }
    }
  } catch (err) {
    console.error(
      "Failed to load gateway dynamic whitelabel branding config:",
      err
    );
  }

  if (store.ledgerBlocks.length === 0) {
    await store.addLedgerBlock(
      "GENESIS",
      {
        platform:
          window.BRAND_NAME ||
          import.meta.env.VITE_BRAND_NAME ||
          "Cadence Clinical",
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
