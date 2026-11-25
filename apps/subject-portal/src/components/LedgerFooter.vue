<template>
  <footer class="portal-footer">
    <div class="footer-title">My Recent Activity</div>
    <div
      id="portal-ledger-timeline"
      class="ledger-timeline"
      role="region"
      aria-label="Recent activity audit ledger"
      tabindex="0"
    >
      <!-- Ledger records shown here -->
    </div>
  </footer>
</template>

<script setup>
import { onMounted } from "vue";
import { useAuditStore } from "../stores/audit.js";

const audit = useAuditStore();

onMounted(() => {
  if (audit.ledgerBlocks?.length > 0) {
    void import("../features/audit.js").then(({ renderLedger }) =>
      renderLedger()
    );
  }
});
</script>
