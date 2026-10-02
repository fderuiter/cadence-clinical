<template>
  <div
    id="section-unavailable"
    class="dashboard-section active unavailable"
  >
    <div class="card unavailable-card">
      <h2>{{ heading }}</h2>
      <p>
        Screens come back once the Cadence services serve the API endpoints they
        need. Each one is waiting on a module of the clinical kernel.
      </p>
      <p>
        <a
          :href="UNAVAILABLE_SCREENS_ISSUE"
          target="_blank"
          rel="noopener"
        >
          See which screens are waiting and on what
        </a>
      </p>
      <button
        v-if="hasHome"
        type="button"
        class="btn btn-primary"
        @click="router.push('/')"
      >
        Go to an available screen
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useRoute, useRouter } from "vue-router";
import { UNAVAILABLE_SCREENS_ISSUE, screenTitle } from "../router/availability";

const route = useRoute();
const router = useRouter();

const heading = computed(() =>
  route.query.screen
    ? `${screenTitle(route.query.screen)} isn't available yet`
    : "No screen is available for your role yet"
);

// Without a screen, "/" sent the user here because they can open no
// available screen, so going "home" would land here again.
const hasHome = computed(() => Boolean(route.query.screen));
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
</style>
