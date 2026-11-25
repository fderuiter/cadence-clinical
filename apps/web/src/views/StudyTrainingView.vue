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
        <h2 id="study-training-title" class="view-title" tabindex="-1">
          Study Training &amp; Qualification Hub
        </h2>
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
            v-model="standaloneStudyFilter"
            type="text"
            class="protocol-filter-input font-mono"
            placeholder="All studies (e.g. CADENCE-101)"
            @change="applyStandaloneScope"
          />
          <label for="training-site-filter">Site:</label>
          <input
            id="training-site-filter"
            v-model="standaloneSiteFilter"
            type="text"
            class="protocol-filter-input font-mono"
            placeholder="All sites (e.g. SITE-101)"
            @change="applyStandaloneScope"
          />
        </template>
        <label for="training-protocol-filter">Protocol Version:</label>
        <input
          id="training-protocol-filter"
          v-model="protocolVersionFilter"
          type="text"
          class="protocol-filter-input"
          placeholder="All versions (e.g. 2.0)"
          @change="applyStandaloneScope"
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
          <div id="kpi-active-curricula" class="stat-value tabular-nums">
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
            class="stat-value text-success tabular-nums"
          >
            {{ completedQualificationsCount }}
          </div>
          <div class="stat-label">Verified Qualifications</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon rbqm-icon">🔒</div>
        <div class="stat-details">
          <div
            id="kpi-pending-assignments"
            class="stat-value text-warning tabular-nums"
          >
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
import ProtocolTrainingConsole from "../components/clinical/ProtocolTrainingConsole.vue";
import { trainingApi } from "../api/training";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { useClinicalScopeStore } from "../stores/clinicalScope";
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

const { route, router } = resolveRouteAndRouter();
const scopeStore = useClinicalScopeStore();
const { activeStudyCode, setActiveStudyCode } = useActiveStudyScope();
const initialRouteScope = readRouteScopeQuery(route);
const hydratedScope = resolveHydratedWorkspaceScope(route, {
  studyId: props.studyId,
  siteId: props.siteId,
});

const activeCurriculaCount = ref(0);
const completedQualificationsCount = ref(0);
const pendingGatedCount = ref(0);
const loadError = ref("");
const standaloneStudyFilter = ref(
  props.studyId ||
    initialRouteScope.studyId ||
    (!props.studyId && route
      ? hydratedScope.studyId ||
        activeStudyCode.value ||
        scopeStore.activeStudyId
      : "")
);
const standaloneSiteFilter = ref(
  props.siteId ||
    initialRouteScope.siteId ||
    (!props.studyId && route
      ? hydratedScope.siteId || scopeStore.activeSiteId
      : "")
);
const protocolVersionFilter = ref(
  props.protocolVersion || String(route?.query?.protocolVersion || "")
);

if (!props.studyId && route?.query) {
  if (initialRouteScope.studyId) {
    setActiveStudyCode(initialRouteScope.studyId);
    scopeStore.activeStudyId = initialRouteScope.studyId;
  } else if (!scopeStore.activeStudyId && activeStudyCode.value) {
    scopeStore.activeStudyId = activeStudyCode.value;
  }
  if (initialRouteScope.siteId) {
    scopeStore.activeSiteId = initialRouteScope.siteId;
  }
  if (initialRouteScope.subjectId) {
    scopeStore.activeSubjectId = initialRouteScope.subjectId;
  }
}

const effectiveStudyId = computed(
  () =>
    props.studyId.trim() ||
    standaloneStudyFilter.value.trim() ||
    String(route?.query?.studyId || "").trim() ||
    activeStudyCode.value
);
const effectiveSiteId = computed(
  () =>
    props.siteId.trim() ||
    standaloneSiteFilter.value.trim() ||
    String(route?.query?.siteId || route?.query?.site || "").trim()
);

watch(
  () => [props.studyId, route?.query?.studyId],
  ([propStudy, queryStudy]) => {
    const incoming =
      (typeof propStudy === "string" && propStudy.trim()) ||
      (typeof queryStudy === "string" && queryStudy.trim()) ||
      "";
    if (incoming) {
      setActiveStudyCode(incoming);
    }
  },
  { immediate: true }
);
const effectiveProtocolVersion = computed(
  () =>
    protocolVersionFilter.value.trim() ||
    props.protocolVersion ||
    String(route?.query?.protocolVersion || "")
);

function syncStandaloneRouteScope() {
  if (props.studyId) return;
  const nextStudy = standaloneStudyFilter.value.trim();
  const nextSite = standaloneSiteFilter.value.trim();
  const nextProtocol = protocolVersionFilter.value.trim();
  if (nextStudy) {
    setActiveStudyCode(nextStudy);
  }
  const synced = syncWorkspaceScope({
    studyId: nextStudy,
    siteId: nextSite,
  });
  standaloneStudyFilter.value = synced.studyId;
  standaloneSiteFilter.value = synced.siteId;
  void replaceRouteQueryParams(router, route, {
    studyId: synced.studyId || null,
    siteId: synced.siteId || null,
    subjectId: scopeStore.activeSubjectId || null,
    protocolVersion: nextProtocol || null,
  });
}

function applyStandaloneScope() {
  syncStandaloneRouteScope();
  void loadMetrics();
}

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

watch(
  () => [
    route?.query?.studyId,
    route?.query?.siteId,
    route?.query?.site,
    route?.query?.subjectId,
    route?.query?.participant,
    route?.query?.protocolVersion,
  ],
  () => {
    if (props.studyId || !route?.query) return;
    const nextScope = readRouteScopeQuery(route);
    const resolvedStudy = nextScope.studyId || activeStudyCode.value;
    if (nextScope.studyId) {
      setActiveStudyCode(nextScope.studyId);
    }
    standaloneStudyFilter.value = resolvedStudy;
    standaloneSiteFilter.value = nextScope.siteId;
    syncWorkspaceScope({
      ...(resolvedStudy ? { studyId: resolvedStudy } : {}),
      ...(nextScope.siteId || resolvedStudy
        ? { siteId: nextScope.siteId }
        : {}),
    });
    scopeStore.activeSubjectId = nextScope.subjectId;
    const nextPv =
      typeof route.query.protocolVersion === "string"
        ? route.query.protocolVersion
        : "";
    if (nextPv !== protocolVersionFilter.value) {
      protocolVersionFilter.value = nextPv;
    }
  }
);
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
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.header-content-group {
  min-width: 0;
  max-width: 100%;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  flex-wrap: wrap;
  min-width: 0;
}

.protocol-filter-group {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  max-width: 100%;
  min-width: 0;
}

.protocol-filter-input {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: 0;
  width: 100%;
  max-width: 180px;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: var(--font-size-md);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-sizing: border-box;
}

.font-mono {
  font-family: var(--font-mono);
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
  font-size: var(--font-size-2xs);
}

.badge-regulatory {
  background-color: var(--semantic-badge-regulatory-bg);
  color: var(--semantic-badge-regulatory-text);
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  border: 1px solid var(--semantic-badge-regulatory-border);
}

.badge-standard {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border);
  padding: 4px 8px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
}

.view-title {
  font-size: var(--font-size-2xl);
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: var(--font-size-base);
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
  font-size: var(--font-size-sm);
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
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
  min-width: 0;
}

@media (max-width: 640px) {
  .dashboard-section {
    padding: 12px 8px;
  }

  .stat-card {
    padding: 12px;
    gap: 12px;
  }
}

.stat-icon {
  font-size: var(--font-size-4xl);
}

.stat-details {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: var(--font-size-2xl);
  font-weight: 800;
  color: var(--semantic-color-text);
}

.stat-label {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
}

.text-success {
  color: var(--semantic-color-success-dark);
}

.text-warning {
  color: var(--semantic-color-warning);
}
</style>
