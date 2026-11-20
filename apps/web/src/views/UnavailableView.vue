<template>
  <div id="section-unavailable" class="dashboard-section active unavailable">
    <div class="card unavailable-card">
      <h2>{{ heading }}</h2>
      <p>
        This workflow isn't available in your workspace yet. Use the study
        workspace to review participants, case report forms, protocol versions,
        queries, and proposals that are active today.
      </p>
      <div class="unavailable-actions">
        <button
          id="btn-unavailable-study"
          type="button"
          class="btn btn-primary"
          @click="router.push('/studies')"
        >
          Open study workspace
        </button>
        <button
          id="btn-unavailable-home"
          type="button"
          class="btn btn-secondary"
          @click="router.push('/')"
        >
          Go to an available screen
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import { screenTitle } from "../router/availability";

const route = useRoute();
const router = useRouter();

const heading = computed(() =>
  route.query.screen
    ? `${screenTitle(route.query.screen)} isn't available yet`
    : "This screen isn't available yet"
);
</script>

<style scoped>
.unavailable {
  max-width: 600px;
  margin: 40px auto;
  text-align: center;
}

.unavailable-card {
  padding: 30px;
}

.unavailable-card h2 {
  margin-bottom: 12px;
}

.unavailable-card p {
  margin-bottom: 16px;
}

.unavailable-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 12px;
}
</style>
