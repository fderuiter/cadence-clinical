<template>
  <div id="section-study-training" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R2) §4</span>
          <span class="badge badge-standard">DIA TMF Zone 5 (05.02.04)</span>
        </div>
        <h2 class="view-title">Study Training &amp; Qualification Hub</h2>
        <p class="view-subtitle">
          Protocol curricula, role- and site-scoped batch curriculum assignment,
          and 21 CFR Part 11 Principal Investigator delegation sign-off.
        </p>
      </div>
      <div class="protocol-filter-group">
        <template v-if="!props.studyId">
          <label for="training-study-filter">Study:</label>
          <input
            id="training-study-filter"
            v-model="studyFilter"
            type="text"
            class="protocol-filter-input"
            autocomplete="off"
            placeholder="All studies"
            @change="applyScopeFilters"
          />
        </template>
        <label for="training-site-filter">Site:</label>
        <input
          id="training-site-filter"
          v-model="siteFilter"
          type="text"
          class="protocol-filter-input"
          autocomplete="off"
          placeholder="All sites"
          @change="applyScopeFilters"
        />
        <label for="training-protocol-filter">Protocol Version:</label>
        <input
          id="training-protocol-filter"
          v-model="protocolVersionFilter"
          type="text"
          class="protocol-filter-input"
          placeholder="All versions (e.g. 2.0)"
          @change="loadMetrics"
        />
      </div>
    </div>

    <p
      v-if="loadError"
      data-testid="training-load-error"
      class="error-banner"
      role="alert"
    >
      Couldn't load study training metrics: {{ loadError }}
    </p>

    <!-- Metric KPI Summary Cards -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon alert-icon">📚</div>
        <div class="stat-details">
          <div id="kpi-active-curricula" class="stat-value">
            {{ activeCurriculaCount }}
          </div>
          <div class="stat-label">Active Protocol Curricula</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon capa-icon">✅</div>
        <div class="stat-details">
          <div
            id="kpi-completed-qualifications"
            class="stat-value text-success"
          >
            {{ completedQualificationsCount }}
          </div>
          <div class="stat-label">Verified Qualifications</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon rbqm-icon">🔒</div>
        <div class="stat-details">
          <div id="kpi-pending-assignments" class="stat-value text-warning">
            {{ pendingGatedCount }}
          </div>
          <div class="stat-label">Pending Staff Assignments</div>
        </div>
      </div>
    </div>

    <!-- Integrated Protocol Training Console -->
    <div class="console-wrapper">
      <ProtocolTrainingConsole
        :study-id="effectiveStudyId"
        :site-id="effectiveSiteId"
        :protocol-version="effectiveProtocolVersion"
      />
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import * as VueRouter from "vue-router";
import ProtocolTrainingConsole from "../components/clinical/ProtocolTrainingConsole.vue";
import { trainingApi } from "../api/training";
import { useToastStore } from "../stores/toasts.js";
import {
  resolveHydratedWorkspaceScope,
  syncWorkspaceScope,
} from "../stores/workspaceScope";

const props = defineProps({
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
  protocolVersion: {
    type: String,
    default: "",
  },
});

let route = null;
let router = null;
try {
  route =
    typeof VueRouter.useRoute === "function" ? VueRouter.useRoute() : null;
  router =
    "useRouter" in VueRouter && typeof VueRouter.useRouter === "function"
      ? VueRouter.useRouter()
      : null;
} catch {
  route = null;
  router = null;
}

const hydratedScope = resolveHydratedWorkspaceScope(route, {
  studyId: props.studyId,
  siteId: props.siteId,
});
if (hydratedScope.studyId || hydratedScope.siteId) {
  syncWorkspaceScope({
    ...(hydratedScope.studyId ? { studyId: hydratedScope.studyId } : {}),
    ...(hydratedScope.siteId || hydratedScope.studyId
      ? { siteId: hydratedScope.siteId }
      : {}),
    router,
    route,
    syncRouteQuery: !props.studyId,
  });
}

const activeCurriculaCount = ref(0);
const completedQualificationsCount = ref(0);
const pendingGatedCount = ref(0);
const loadError = ref("");
const studyFilter = ref(hydratedScope.studyId);
const siteFilter = ref(hydratedScope.siteId);
const protocolVersionFilter = ref(
  props.protocolVersion || String(route?.query?.protocolVersion || "")
);

const effectiveStudyId = computed(
  () => props.studyId.trim() || studyFilter.value.trim()
);
const effectiveSiteId = computed(
  () => props.siteId.trim() || siteFilter.value.trim()
);
const effectiveProtocolVersion = computed(
  () =>
    protocolVersionFilter.value.trim() ||
    props.protocolVersion ||
    String(route?.query?.protocolVersion || "")
);

function applyScopeFilters() {
  const nextStudy = props.studyId.trim() || studyFilter.value.trim();
  const nextSite = siteFilter.value.trim();
  const synced = syncWorkspaceScope({
    studyId: nextStudy,
    siteId: nextSite,
    router,
    route,
    syncRouteQuery: !props.studyId,
  });
  studyFilter.value = synced.studyId;
  siteFilter.value = synced.siteId;
}

watch(
  [
    () => props.studyId,
    () => props.siteId,
    () => route?.query?.studyId,
    () => route?.query?.siteId,
    () => route?.query?.site,
  ],
  ([propStudy, propSite, queryStudy, querySiteId, querySite]) => {
    const nextStudy =
      (propStudy || "").trim() ||
      (typeof queryStudy === "string" ? queryStudy.trim() : "");
    const nextSite =
      (propSite || "").trim() ||
      (typeof querySiteId === "string" && querySiteId.trim()
        ? querySiteId.trim()
        : typeof querySite === "string"
          ? querySite.trim()
          : "");
    if (nextStudy && nextStudy !== studyFilter.value) {
      studyFilter.value = nextStudy;
    }
    if (nextSite !== siteFilter.value && (nextSite || propSite !== undefined)) {
      if (nextSite || propStudy) {
        siteFilter.value = nextSite;
      }
    }
    if (nextStudy || nextSite) {
      syncWorkspaceScope({
        ...(nextStudy ? { studyId: nextStudy } : {}),
        ...(nextSite || nextStudy ? { siteId: nextSite } : {}),
      });
    }
  }
);

async function loadMetrics() {
  loadError.value = "";
  try {
    const curriculaQuery = {};
    if (effectiveStudyId.value)
      curriculaQuery.study_id = effectiveStudyId.value;
    if (effectiveProtocolVersion.value) {
      curriculaQuery.protocol_version = effectiveProtocolVersion.value;
    }

    const assignmentQuery = {};
    if (effectiveStudyId.value)
      assignmentQuery.study_id = effectiveStudyId.value;
    if (effectiveSiteId.value) assignmentQuery.site_id = effectiveSiteId.value;

    const [curricula, assignments] = await Promise.all([
      trainingApi.listCurricula(curriculaQuery),
      trainingApi.listAssignments(assignmentQuery),
    ]);

    activeCurriculaCount.value = Array.isArray(curricula)
      ? curricula.length
      : 0;
    const list = Array.isArray(assignments) ? assignments : [];
    completedQualificationsCount.value = list.filter(
      (a) => a.status === "COMPLETED"
    ).length;
    pendingGatedCount.value = list.filter(
      (a) => a.status !== "COMPLETED"
    ).length;
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      "Couldn't load study training metrics",
      err
    );
    loadError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  }
}

watch([effectiveStudyId, effectiveSiteId, effectiveProtocolVersion], () => {
  loadMetrics();
});

onMounted(() => {
  loadMetrics();
});
</script>

<style scoped>
.dashboard-section {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg, 24px);
  padding: var(--spacing-lg, 24px);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
  flex-wrap: wrap;
}

.badge-gxp {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-weight: 700;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.badge-regulatory {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.badge-standard {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border);
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.view-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: 0.95rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  max-width: 900px;
  line-height: 1.5;
}

.error-banner {
  margin: 0;
  padding: 10px 14px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.875rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--spacing-md, 16px);
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 16px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 16px 20px;
}

.stat-icon {
  font-size: 2rem;
}

.stat-details {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.stat-label {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.text-success {
  color: var(--semantic-color-success-dark);
}

.text-warning {
  color: var(--semantic-color-warning);
}
</style>
