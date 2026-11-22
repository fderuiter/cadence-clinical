<template>
  <div id="section-unavailable" class="dashboard-section active unavailable">
    <div class="card unavailable-card">
      <div class="unavailable-prerelease-tag" role="status">
        {{ PRE_RELEASE_ENVIRONMENT_NOTICE }}
      </div>

      <h2>{{ heading }}</h2>
      <p class="unavailable-summary">{{ metadata.summary }}</p>

      <div class="unavailable-cards-grid">
        <section
          id="unavailable-entitlement-card"
          class="unavailable-info-card"
          aria-labelledby="heading-unavailable-family"
        >
          <span class="info-card-label">Product Family &amp; Entitlement</span>
          <h3 id="heading-unavailable-family">{{ metadata.family }}</h3>
          <p v-if="metadata.isBaselineCapability" class="entitlement-status ok">
            Included in Cadence Core baseline — never gated behind paid add-ons
          </p>
          <p v-else-if="isFamilyEntitled" class="entitlement-status ok">
            Entitled in your {{ authStore.activeTier }} workspace plan
          </p>
          <p v-else class="entitlement-status upgrade">
            Packaged in {{ metadata.minimumTier }} tier (active plan:
            {{ authStore.activeTier }})
          </p>
          <p class="info-card-note">
            Commercial subscriptions govern product-family packaging; clinical
            study visibility remains governed separately by study access grants.
          </p>
        </section>

        <section
          id="unavailable-roadmap-card"
          class="unavailable-info-card"
          aria-labelledby="heading-unavailable-roadmap"
        >
          <span class="info-card-label">Pre-release Delivery Status</span>
          <h3 id="heading-unavailable-roadmap">Roadmap Status</h3>
          <p class="roadmap-stage">{{ metadata.roadmapStage }}</p>
          <p class="info-card-note">
            Core study design, visit eCRF capture, source verification, and site
            readiness workflows are active today in the Study Workspace.
          </p>
        </section>
      </div>

      <div class="unavailable-actions">
        <router-link
          v-if="screenKey === 'rules'"
          id="link-unavailable-canvas-validation"
          data-testid="link-unavailable-canvas-validation"
          class="btn btn-primary"
          :to="canvasValidationTarget"
        >
          Open Clinical Canvas Validation
        </router-link>
        <button
          id="btn-unavailable-study"
          type="button"
          :class="[
            'btn',
            screenKey === 'rules' ? 'btn-secondary' : 'btn-primary',
          ]"
          @click="router.push('/studies')"
        >
          Open study workspace
        </button>
        <button
          id="btn-unavailable-plans"
          type="button"
          class="btn btn-secondary"
          @click="router.push('/plans')"
        >
          Compare plans &amp; product families
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
import { computed, onMounted } from "vue";
import { useRoute, useRouter } from "vue-router";
import { PRE_RELEASE_ENVIRONMENT_NOTICE } from "../catalog/productCatalog.js";
import { screenMetadata, screenTitle } from "../router/availability";
import { useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const studyCanvas = useStudyCanvasStore();

onMounted(() => {
  if (authStore.isAuthenticated) {
    authStore.loadEntitlements();
  }
});

const screenKey = computed(() =>
  typeof route.query.screen === "string" ? route.query.screen : ""
);

const metadata = computed(() => screenMetadata(screenKey.value));

const isFamilyEntitled = computed(() => {
  if (metadata.value.isBaselineCapability) return true;
  return authStore.entitledFamilies.includes(metadata.value.family);
});

const heading = computed(() =>
  screenKey.value
    ? `${screenTitle(screenKey.value)} isn't available yet`
    : "This screen isn't available yet"
);

const canvasValidationTarget = computed(() => {
  const fromQuery =
    typeof route.query.studyId === "string" && route.query.studyId.trim()
      ? route.query.studyId.trim()
      : typeof route.query.study === "string" && route.query.study.trim()
        ? route.query.study.trim()
        : "";
  const activeCode =
    fromQuery ||
    studyCanvas.studyCode ||
    (Array.isArray(authStore.assignedStudies) &&
    authStore.assignedStudies.length > 0
      ? authStore.assignedStudies[0]
      : "CADENCE-101");
  return {
    path: `/studies/${encodeURIComponent(activeCode)}`,
    query: { stage: "data", section: "validation" },
  };
});
</script>

<style scoped>
.unavailable {
  max-width: 760px;
  margin: 36px auto;
  padding: 0 16px;
}

.unavailable-card {
  padding: 28px;
  text-align: left;
}

.unavailable-prerelease-tag {
  background: var(--semantic-color-warning-bg-light);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning-text);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.82rem;
  margin-bottom: 18px;
}

.unavailable-card h2 {
  margin: 0 0 8px;
}

.unavailable-summary {
  color: var(--semantic-color-text-secondary);
  margin-bottom: 20px;
  line-height: 1.5;
}

.unavailable-cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.unavailable-info-card {
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  border-radius: 10px;
  padding: 16px;
}

.info-card-label {
  display: block;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 6px;
}

.unavailable-info-card h3 {
  font-size: 1.05rem;
  margin: 0 0 8px;
  color: var(--semantic-color-text);
}

.entitlement-status {
  font-size: 0.88rem;
  font-weight: 600;
  margin: 0 0 8px;
}

.entitlement-status.ok {
  color: var(--semantic-color-success);
}

.entitlement-status.upgrade {
  color: var(--semantic-color-warning);
}

.roadmap-stage {
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
  margin: 0 0 8px;
}

.info-card-note {
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  line-height: 1.45;
}

.unavailable-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
</style>
