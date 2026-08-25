<template>
  <div class="lakehouse-view">
    <ClinicalLakehouseConsole
      :study-id="studyId"
      :initial-unblinded="isUnblinded"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";
import ClinicalLakehouseConsole from "../components/clinical/ClinicalLakehouseConsole.vue";
import { useAuthStore } from "../stores/auth";

const route = useRoute();
const authStore = useAuthStore();

const studyId = computed(() => (route.query.study_id as string) || "STUDY-901");

const isUnblinded = computed(() => {
  const roles = authStore.normalizedRoles || [];
  return roles.some((r: string) =>
    [
      "unblinded_statistician",
      "idmc",
      "emergency_unblinder",
      "pharmacist",
      "sponsor_admin",
    ].includes(r)
  );
});
</script>

<style scoped>
.lakehouse-view {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}
</style>
