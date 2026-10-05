<template>
  <div class="ctms-subview ctms-monitoring-view">
    <div class="monitoring-header" style="margin-bottom: 20px">
      <h3>CRA Monitoring Visit Reports & SDV Verification Queue</h3>
      <p style="color: #64748b; font-size: 14px">
        Log monitoring visit schedules, execute Source Data Verification (SDV),
        and file trip reports.
      </p>
    </div>
    <CraVerificationConsole />
  </div>
</template>

<script setup>
import CraVerificationConsole from "../../components/persona/CraVerificationConsole.vue";
</script>
