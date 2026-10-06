<template>
  <section
    id="study-pulse"
    class="pulse"
    aria-labelledby="study-pulse-title"
    :aria-busy="canvas.pulseLoading || canvas.enrollmentVelocityLoading || canvas.siteRiskHeatmapLoading"
  >
    <div class="pulse-header">
      <h2 id="study-pulse-title" class="pulse-title">Study Pulse Analytics</h2>

      <!-- Tab Navigation -->
      <nav class="pulse-nav" aria-label="Study pulse sections">
        <div role="tablist" class="tab-list">
          <button
            id="tab-participants"
            type="button"
            role="tab"
            :aria-selected="activeTab === 'participants'"
            aria-controls="panel-participants"
            class="tab-btn"
            :class="{ active: activeTab === 'participants' }"
            @click="selectTab('participants')"
          >
            Participants
          </button>
          <button
            id="tab-velocity"
            type="button"
            role="tab"
            :aria-selected="activeTab === 'velocity'"
            aria-controls="panel-velocity"
            class="tab-btn"
            :class="{ active: activeTab === 'velocity' }"
            @click="selectTab('velocity')"
          >
            Predictive Velocity
          </button>
          <button
            id="tab-heatmap"
            type="button"
            role="tab"
            :aria-selected="activeTab === 'heatmap'"
            aria-controls="panel-heatmap"
            class="tab-btn"
            :class="{ active: activeTab === 'heatmap' }"
            @click="selectTab('heatmap')"
          >
            Site Risk Heatmap
          </button>
        </div>
      </nav>
    </div>

    <!-- Panel 1: Participants -->
    <div
      v-if="activeTab === 'participants'"
      id="panel-participants"
      role="tabpanel"
      aria-labelledby="tab-participants"
      class="tab-panel"
    >
      <p v-if="canvas.pulseError" class="pulse-status" role="status">
        The participant counts couldn't be loaded.
        <button
          id="btn-retry-pulse"
          type="button"
          class="btn btn-secondary"
          @click="canvas.loadPulse()"
        >
          Try again
        </button>
      </p>
      <p v-else-if="!canvas.pulse" class="pulse-status" role="status">
        Counting participants...
      </p>
      <p
        v-else-if="canvas.pulse.total === 0"
        id="pulse-empty"
        class="pulse-status"
        role="status"
      >
        No participants in {{ canvas.studyCode }} yet.
      </p>
      <template v-else>
        <button
          id="pulse-total"
          type="button"
          class="pulse-metric pulse-total"
          @click="emit('drill', '')"
        >
          <span class="pulse-value">{{ canvas.pulse.total }}</span>
          <span class="pulse-label">in the study</span>
        </button>
        <ul class="pulse-states" aria-label="Participants by state">
          <li v-for="state in shownStates" :key="state.code">
            <button
              :id="`pulse-${state.code.toLowerCase()}`"
              type="button"
              class="pulse-metric"
              @click="emit('drill', state.code)"
            >
              <span class="pulse-value">{{ state.count }}</span>
              <span class="pulse-label">{{ state.label }}</span>
            </button>
          </li>
        </ul>
      </template>
    </div>

    <!-- Panel 2: Predictive Velocity -->
    <div
      v-else-if="activeTab === 'velocity'"
      id="panel-velocity"
      role="tabpanel"
      aria-labelledby="tab-velocity"
      class="tab-panel"
    >
      <div v-if="canvas.enrollmentVelocityLoading" class="pulse-status" role="status">
        Simulating predictive enrollment velocity...
      </div>
      <div v-else-if="canvas.enrollmentVelocityError" class="pulse-status" role="status">
        Failed to load predictive enrollment velocity forecast.
        <button
          id="btn-retry-velocity"
          type="button"
          class="btn btn-secondary"
          @click="canvas.loadEnrollmentVelocity()"
        >
          Try again
        </button>
      </div>
      <div v-else-if="canvas.enrollmentVelocity" class="velocity-container">
        <!-- Status & Metrics Header -->
        <div class="velocity-metrics-bar">
          <div class="metric-card">
            <span class="metric-label">Status</span>
            <span
              class="status-badge"
              :class="getStatusClass(canvas.enrollmentVelocity.status)"
            >
              {{ canvas.enrollmentVelocity.status }}
            </span>
          </div>
          <div class="metric-card">
            <span class="metric-label">Enrolled / Target</span>
            <span class="metric-value">
              {{ getCurrentlyEnrolled(canvas.enrollmentVelocity) }} / {{ getTargetSubjects(canvas.enrollmentVelocity) }}
            </span>
          </div>
          <div class="metric-card">
            <span class="metric-label">Current Weekly Rate</span>
            <span class="metric-value">{{ getCurrentVelocity(canvas.enrollmentVelocity) }} / wk</span>
          </div>
          <div class="metric-card">
            <span class="metric-label">Required Velocity</span>
            <span class="metric-value">{{ getTargetVelocity(canvas.enrollmentVelocity) }} / wk</span>
          </div>
          <div class="metric-card">
            <span class="metric-label">Forecast Completion</span>
            <span class="metric-value">{{ getForecastDate(canvas.enrollmentVelocity) }}</span>
          </div>
          <div class="metric-card">
            <span class="metric-label">Schedule Delta</span>
            <span
              class="metric-value"
              :class="getDaysAheadOrBehind(canvas.enrollmentVelocity) < 0 ? 'text-danger' : 'text-success'"
            >
              {{ getDaysAheadOrBehind(canvas.enrollmentVelocity) }} days
            </span>
          </div>
        </div>

        <!-- Interactive Simulator Controls -->
        <div class="simulator-controls-card">
          <h3 class="card-title">Scenario Forecast Simulator</h3>
          <form class="simulator-form" @submit.prevent="runVelocitySimulation">
            <div class="form-group">
              <label for="sim-target-subjects">Target Subjects</label>
              <input
                id="sim-target-subjects"
                v-model.number="simParams.targetSubjects"
                type="number"
                min="1"
                class="form-control"
              />
            </div>
            <div class="form-group">
              <label for="sim-completion-date">Target Completion Date</label>
              <input
                id="sim-completion-date"
                v-model="simParams.targetCompletionDate"
                type="date"
                class="form-control"
              />
            </div>
            <div class="form-group">
              <label for="sim-historical-rate">Historical Weekly Rate</label>
              <input
                id="sim-historical-rate"
                v-model.number="simParams.historicalWeeklyRate"
                type="number"
                step="0.1"
                min="0.1"
                class="form-control"
              />
            </div>
            <div class="form-group">
              <label for="sim-horizon-weeks">Horizon (Weeks)</label>
              <input
                id="sim-horizon-weeks"
                v-model.number="simParams.simulationHorizonWeeks"
                type="number"
                min="1"
                max="104"
                class="form-control"
              />
            </div>
            <button
              id="btn-recalculate-sim"
              type="submit"
              class="btn btn-primary"
              :disabled="canvas.enrollmentVelocityLoading"
            >
              Recalculate Simulation
            </button>
          </form>
        </div>

        <!-- Weekly Trajectory Projections Table -->
        <div class="table-section">
          <h3 class="card-title">Weekly Enrollment Trajectory Projections</h3>
          <div class="table-wrapper">
            <table class="data-table" aria-label="Weekly enrollment trajectory projections">
              <thead>
                <tr>
                  <th scope="col">Week</th>
                  <th scope="col">Date</th>
                  <th scope="col">Projected Enrolled</th>
                  <th scope="col">95% CI (Lower - Upper)</th>
                  <th scope="col">Target Cumulative</th>
                  <th scope="col">Weekly Rate</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="point in getProjections(canvas.enrollmentVelocity)"
                  :key="point.weekIndex ?? point.week_index"
                >
                  <td>Week {{ point.weekIndex ?? point.week_index }}</td>
                  <td>{{ point.weekStartDate ?? point.week_start_date }}</td>
                  <td class="font-bold">{{ point.projectedCumulativeEnrolled ?? point.projected_cumulative_enrolled }}</td>
                  <td>
                    [{{ point.lowerBoundCI ?? point.lower_bound_ci }} - {{ point.upperBoundCI ?? point.upper_bound_ci }}]
                  </td>
                  <td>{{ point.targetCumulative ?? point.target_cumulative }}</td>
                  <td>{{ point.weeklyRate ?? point.weekly_rate }} / wk</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Site Velocity Breakdown Table -->
        <div class="table-section" v-if="getSiteVelocities(canvas.enrollmentVelocity).length > 0">
          <h3 class="card-title">Site Enrollment Velocity Breakdown</h3>
          <div class="table-wrapper">
            <table class="data-table" aria-label="Site enrollment velocity breakdown">
              <thead>
                <tr>
                  <th scope="col">Site</th>
                  <th scope="col">Active Subjects</th>
                  <th scope="col">Historical Weekly Rate</th>
                  <th scope="col">Projected Weekly Rate</th>
                  <th scope="col">Velocity Risk Tier</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="site in getSiteVelocities(canvas.enrollmentVelocity)"
                  :key="site.siteId ?? site.site_id"
                >
                  <td class="font-medium">{{ site.siteName ?? site.site_name }} ({{ site.siteId ?? site.site_id }})</td>
                  <td>{{ site.activeSubjects ?? site.active_subjects }}</td>
                  <td>{{ site.historicalWeeklyRate ?? site.historical_weekly_rate }} / wk</td>
                  <td>{{ site.projectedWeeklyRate ?? site.projected_weekly_rate }} / wk</td>
                  <td>
                    <span class="risk-badge" :class="getRiskBadgeClass(site.velocityRiskLevel ?? site.velocity_risk_level)">
                      {{ site.velocityRiskLevel ?? site.velocity_risk_level }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- Panel 3: Site Risk Heatmap -->
    <div
      v-else-if="activeTab === 'heatmap'"
      id="panel-heatmap"
      role="tabpanel"
      aria-labelledby="tab-heatmap"
      class="tab-panel"
    >
      <div v-if="canvas.siteRiskHeatmapLoading" class="pulse-status" role="status">
        Calculating RBQM site risk heatmap matrix...
      </div>
      <div v-else-if="canvas.siteRiskHeatmapError" class="pulse-status" role="status">
        Failed to load site risk heatmap matrix.
        <button
          id="btn-retry-heatmap"
          type="button"
          class="btn btn-secondary"
          @click="canvas.loadSiteRiskHeatmap()"
        >
          Try again
        </button>
      </div>
      <div v-else-if="canvas.siteRiskHeatmap" class="heatmap-container">
        <!-- Controls & Filter Toolbar -->
        <div class="heatmap-toolbar">
          <div class="search-box">
            <label for="heatmap-search" class="sr-only">Search sites</label>
            <input
              id="heatmap-search"
              v-model="heatmapSearchQuery"
              type="search"
              placeholder="Filter by site code or name..."
              class="form-control"
            />
          </div>

          <div class="filter-pills" role="radiogroup" aria-label="Filter by risk tier">
            <button
              id="pill-tier-all"
              type="button"
              role="radio"
              :aria-checked="selectedRiskTier === 'ALL'"
              class="pill-btn"
              :class="{ active: selectedRiskTier === 'ALL' }"
              @click="selectedRiskTier = 'ALL'"
            >
              All Sites
            </button>
            <button
              id="pill-tier-high"
              type="button"
              role="radio"
              :aria-checked="selectedRiskTier === 'HIGH'"
              class="pill-btn pill-high"
              :class="{ active: selectedRiskTier === 'HIGH' }"
              @click="selectedRiskTier = 'HIGH'"
            >
              High Risk
            </button>
            <button
              id="pill-tier-medium"
              type="button"
              role="radio"
              :aria-checked="selectedRiskTier === 'MEDIUM'"
              class="pill-btn pill-medium"
              :class="{ active: selectedRiskTier === 'MEDIUM' }"
              @click="selectedRiskTier = 'MEDIUM'"
            >
              Medium Risk
            </button>
            <button
              id="pill-tier-low"
              type="button"
              role="radio"
              :aria-checked="selectedRiskTier === 'LOW'"
              class="pill-btn pill-low"
              :class="{ active: selectedRiskTier === 'LOW' }"
              @click="selectedRiskTier = 'LOW'"
            >
              Low Risk
            </button>
          </div>

          <button
            id="btn-compute-heatmap"
            type="button"
            class="btn btn-primary"
            :disabled="canvas.siteRiskHeatmapLoading"
            @click="canvas.computeSiteRiskHeatmap()"
          >
            Compute Heatmap
          </button>
        </div>

        <!-- Summary Metrics Bar -->
        <div class="heatmap-summary-bar">
          <div class="summary-card">
            <span class="summary-label">Total Sites</span>
            <span class="summary-value">{{ canvas.siteRiskHeatmap.totalSites ?? canvas.siteRiskHeatmap.total_sites }}</span>
          </div>
          <div class="summary-card card-high">
            <span class="summary-label">High Risk</span>
            <span class="summary-value">{{ canvas.siteRiskHeatmap.highRiskSitesCount ?? canvas.siteRiskHeatmap.high_risk_sites_count }}</span>
          </div>
          <div class="summary-card card-medium">
            <span class="summary-label">Medium Risk</span>
            <span class="summary-value">{{ canvas.siteRiskHeatmap.mediumRiskSitesCount ?? canvas.siteRiskHeatmap.medium_risk_sites_count }}</span>
          </div>
          <div class="summary-card card-low">
            <span class="summary-label">Low Risk</span>
            <span class="summary-value">{{ canvas.siteRiskHeatmap.lowRiskSitesCount ?? canvas.siteRiskHeatmap.low_risk_sites_count }}</span>
          </div>
        </div>

        <!-- Matrix Heatmap Table Grid -->
        <div class="table-wrapper">
          <table id="heatmap-matrix-grid" class="heatmap-table" aria-label="Interactive Site Risk Heatmap Matrix">
            <thead>
              <tr>
                <th scope="col">Rank</th>
                <th scope="col">Site</th>
                <th scope="col">Composite Risk</th>
                <th scope="col">Risk Tier</th>
                <th scope="col">Safety</th>
                <th scope="col">Consent</th>
                <th scope="col">Data Quality</th>
                <th scope="col">Enrollment</th>
                <th scope="col">Supply</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in filteredHeatmapRows"
                :key="row.siteId ?? row.site_id"
                class="heatmap-row"
                tabindex="0"
                @click="openSiteDrilldownModal(row)"
                @keydown.enter="openSiteDrilldownModal(row)"
                @keydown.space.prevent="openSiteDrilldownModal(row)"
              >
                <td class="cell-rank">#{{ row.rank }}</td>
                <td class="cell-site">
                  <div class="site-name">{{ row.siteName ?? row.site_name }}</div>
                  <div class="site-code">{{ row.siteId ?? row.site_id }}</div>
                </td>
                <td class="cell-score">
                  {{ (getCompositeScore(row)).toFixed(2) }}
                </td>
                <td class="cell-tier">
                  <span class="risk-badge" :class="getRiskBadgeClass(row.riskTier ?? row.risk_tier)">
                    {{ row.riskTier ?? row.risk_tier }}
                  </span>
                </td>
                <!-- 5 Category Risk Score Cells -->
                <td
                  v-for="catKey in ['Safety', 'Consent', 'DataQuality', 'Enrollment', 'Supply']"
                  :key="catKey"
                  class="cell-category"
                  :class="getCategoryCellClass(row, catKey)"
                >
                  <div class="cat-score">
                    {{ getCategoryScoreVal(row, catKey).toFixed(2) }}
                  </div>
                  <div class="cat-tier">
                    {{ getCategoryTierVal(row, catKey) }}
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Site Drilldown Modal -->
        <div
          v-if="selectedSiteForModal"
          id="site-drilldown-modal"
          class="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="modal-site-title"
          @click.self="selectedSiteForModal = null"
        >
          <div class="modal-card">
            <div class="modal-header">
              <div>
                <h3 id="modal-site-title" class="modal-title">
                  {{ selectedSiteForModal.siteName ?? selectedSiteForModal.site_name }}
                  <span class="modal-subtitle">({{ selectedSiteForModal.siteId ?? selectedSiteForModal.site_id }})</span>
                </h3>
                <div class="modal-badges">
                  <span class="risk-badge" :class="getRiskBadgeClass(selectedSiteForModal.riskTier ?? selectedSiteForModal.risk_tier)">
                    Risk Tier: {{ selectedSiteForModal.riskTier ?? selectedSiteForModal.risk_tier }}
                  </span>
                  <span class="composite-badge">
                    Composite Risk Score: {{ getCompositeScore(selectedSiteForModal).toFixed(2) }}
                  </span>
                </div>
              </div>
              <button
                id="btn-close-modal"
                type="button"
                class="btn-close"
                aria-label="Close modal"
                @click="selectedSiteForModal = null"
              >
                &times;
              </button>
            </div>

            <div class="modal-body">
              <!-- Category Breakdown -->
              <h4 class="modal-section-title">RBQM Risk Category Breakdown</h4>
              <div class="modal-category-grid">
                <div
                  v-for="cat in getCategoryScores(selectedSiteForModal)"
                  :key="cat.categoryKey ?? cat.category_key ?? cat.categoryName ?? cat.category_name"
                  class="modal-cat-card"
                  :class="getRiskCardClass(cat.riskTier ?? cat.risk_tier)"
                >
                  <div class="cat-card-header">
                    <span class="cat-card-title">{{ cat.categoryName ?? cat.category_name ?? cat.categoryKey ?? cat.category_key }}</span>
                    <span class="cat-card-tier">{{ cat.riskTier ?? cat.risk_tier }}</span>
                  </div>
                  <div class="cat-card-score">Score: {{ (cat.riskScore ?? cat.risk_score ?? 0).toFixed(2) }}</div>
                  <p class="cat-card-details">{{ cat.details }}</p>
                </div>
              </div>

              <!-- Recommended CRA Actions -->
              <h4 class="modal-section-title">Recommended CRA Action Items</h4>
              <ul class="cra-action-list">
                <li
                  v-for="(action, idx) in getRecommendedActions(selectedSiteForModal)"
                  :key="idx"
                  class="cra-action-item"
                >
                  <span class="action-bullet">&bull;</span>
                  <span>{{ action }}</span>
                </li>
              </ul>
            </div>

            <div class="modal-footer">
              <button
                id="btn-modal-done"
                type="button"
                class="btn btn-secondary"
                @click="selectedSiteForModal = null"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <p class="pulse-footnote">
      Data, safety, sites, quality, supply and submission measures arrive as
      kernel indicators (#4845).
    </p>
  </section>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

const emit = defineEmits<{ drill: [status: string] }>();
const canvas = useStudyCanvasStore();

const activeTab = ref<"participants" | "velocity" | "heatmap">("participants");

const selectedRiskTier = ref<"ALL" | "HIGH" | "MEDIUM" | "LOW">("ALL");
const heatmapSearchQuery = ref("");
const selectedSiteForModal = ref<any | null>(null);

const simParams = ref({
  targetSubjects: 100,
  targetCompletionDate: new Date(Date.now() + 180 * 24 * 3600 * 1000).toISOString().split("T")[0],
  historicalWeeklyRate: 2.5,
  simulationHorizonWeeks: 24,
});

function selectTab(tab: "participants" | "velocity" | "heatmap") {
  activeTab.value = tab;
  if (tab === "velocity" && !canvas.enrollmentVelocity) {
    canvas.loadEnrollmentVelocity();
  } else if (tab === "heatmap" && !canvas.siteRiskHeatmap) {
    canvas.loadSiteRiskHeatmap();
  }
}

watch(
  () => canvas.enrollmentVelocity,
  (val) => {
    if (val && val.parameters) {
      simParams.value.targetSubjects = val.parameters.targetSubjects ?? val.parameters.target_subjects ?? 100;
      simParams.value.targetCompletionDate = val.parameters.targetCompletionDate ?? val.parameters.target_completion_date ?? simParams.value.targetCompletionDate;
      simParams.value.historicalWeeklyRate = val.parameters.historicalWeeklyRate ?? val.parameters.historical_weekly_rate ?? 2.5;
      simParams.value.simulationHorizonWeeks = val.parameters.simulationHorizonWeeks ?? val.parameters.simulation_horizon_weeks ?? 24;
    }
  },
  { immediate: true }
);

function runVelocitySimulation() {
  canvas.simulateEnrollmentVelocity(simParams.value);
}

const shownStates = computed(
  () => canvas.pulse?.byState.filter((state) => state.count > 0) ?? []
);

function getCurrentlyEnrolled(res: any): number {
  return res?.currentlyEnrolled ?? res?.currently_enrolled ?? 0;
}
function getTargetSubjects(res: any): number {
  return res?.targetSubjects ?? res?.target_subjects ?? 0;
}
function getCurrentVelocity(res: any): number {
  return res?.currentVelocityWeekly ?? res?.current_velocity_weekly ?? 0;
}
function getTargetVelocity(res: any): number {
  return res?.targetVelocityWeekly ?? res?.target_velocity_weekly ?? 0;
}
function getForecastDate(res: any): string {
  return res?.forecastedCompletionDate ?? res?.forecasted_completion_date ?? "N/A";
}
function getDaysAheadOrBehind(res: any): number {
  return res?.daysAheadOrBehind ?? res?.days_ahead_or_behind ?? 0;
}
function getProjections(res: any): any[] {
  return res?.weeklyProjections ?? res?.weekly_projections ?? [];
}
function getSiteVelocities(res: any): any[] {
  return res?.siteVelocities ?? res?.site_velocities ?? [];
}

function getCompositeScore(row: any): number {
  return row?.compositeRiskScore ?? row?.composite_risk_score ?? 0;
}
function getCategoryScores(row: any): any[] {
  return row?.categoryScores ?? row?.category_scores ?? [];
}
function getRecommendedActions(row: any): string[] {
  return row?.recommendedActions ?? row?.recommended_actions ?? [];
}

function getStatusClass(status: string) {
  switch (status) {
    case "AHEAD":
    case "ON_TRACK":
      return "status-on-track";
    case "AT_RISK":
      return "status-at-risk";
    case "BEHIND":
      return "status-behind";
    default:
      return "status-default";
  }
}

function getRiskBadgeClass(tier: string) {
  switch (tier?.toUpperCase()) {
    case "HIGH":
      return "badge-high";
    case "MEDIUM":
      return "badge-medium";
    case "LOW":
      return "badge-low";
    default:
      return "badge-neutral";
  }
}

function getRiskCardClass(tier: string) {
  switch (tier?.toUpperCase()) {
    case "HIGH":
      return "risk-card-high";
    case "MEDIUM":
      return "risk-card-medium";
    case "LOW":
      return "risk-card-low";
    default:
      return "";
  }
}

function getCategoryScoreVal(row: any, catKey: string): number {
  const scores = getCategoryScores(row);
  const found = scores.find(
    (c: any) =>
      (c.categoryKey ?? c.category_key ?? c.categoryName ?? c.category_name)
        ?.toString()
        .toLowerCase() === catKey.toLowerCase()
  );
  return found?.riskScore ?? found?.risk_score ?? 0;
}

function getCategoryTierVal(row: any, catKey: string): string {
  const scores = getCategoryScores(row);
  const found = scores.find(
    (c: any) =>
      (c.categoryKey ?? c.category_key ?? c.categoryName ?? c.category_name)
        ?.toString()
        .toLowerCase() === catKey.toLowerCase()
  );
  return found?.riskTier ?? found?.risk_tier ?? "LOW";
}

function getCategoryCellClass(row: any, catKey: string): string {
  const tier = getCategoryTierVal(row, catKey);
  switch (tier?.toUpperCase()) {
    case "HIGH":
      return "risk-cell-high";
    case "MEDIUM":
      return "risk-cell-medium";
    case "LOW":
      return "risk-cell-low";
    default:
      return "";
  }
}

const filteredHeatmapRows = computed(() => {
  if (!canvas.siteRiskHeatmap?.rows) return [];
  let rows = canvas.siteRiskHeatmap.rows;

  if (selectedRiskTier.value !== "ALL") {
    rows = rows.filter(
      (r: any) =>
        (r.riskTier ?? r.risk_tier)?.toUpperCase() === selectedRiskTier.value
    );
  }

  const query = heatmapSearchQuery.value.trim().toLowerCase();
  if (query) {
    rows = rows.filter((r: any) => {
      const name = (r.siteName ?? r.site_name ?? "").toLowerCase();
      const id = (r.siteId ?? r.site_id ?? "").toLowerCase();
      return name.includes(query) || id.includes(query);
    });
  }

  return rows;
});

function openSiteDrilldownModal(row: any) {
  selectedSiteForModal.value = row;
}

function handleKeyDown(event: KeyboardEvent) {
  if (event.key === "Escape" && selectedSiteForModal.value) {
    selectedSiteForModal.value = null;
  }
}

onMounted(() => {
  window.addEventListener("keydown", handleKeyDown);
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeyDown);
});
</script>

<style scoped>
.pulse {
  display: flex;
  flex-direction: column;
  gap: 16px;
  width: 100%;
}

.pulse-header {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.pulse-title {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
}

.tab-list {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--border, #e2e8f0);
}

.tab-btn {
  min-height: 44px;
  min-width: 44px;
  padding: 10px 18px;
  background: transparent;
  border: none;
  border-bottom: 3px solid transparent;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--neutral-dark, #475569);
  cursor: pointer;
  transition: all 0.2s ease;
}

.tab-btn:hover {
  color: var(--accent, #2563eb);
}

.tab-btn.active {
  border-bottom-color: var(--accent, #2563eb);
  color: var(--accent, #2563eb);
}

.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.pulse-states {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.pulse-metric {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 8rem;
  min-height: 44px;
  padding: 10px 14px;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  background: transparent;
  color: inherit;
  cursor: pointer;
  text-align: left;
}

.pulse-metric:hover,
.pulse-metric:focus-visible {
  border-color: var(--accent, #2563eb);
}

.pulse-total {
  background: var(--accent-bg, rgba(37, 99, 235, 0.08));
}

.pulse-value {
  font-size: 1.5rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.pulse-label,
.pulse-status,
.pulse-footnote {
  color: var(--neutral-dark, #475569);
  font-size: 0.85rem;
}

/* Velocity View Styles */
.velocity-container,
.heatmap-container {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.velocity-metrics-bar,
.heatmap-summary-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.metric-card,
.summary-card {
  flex: 1;
  min-width: 150px;
  padding: 12px 16px;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  background: var(--bg-card, #ffffff);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.metric-label,
.summary-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  color: var(--neutral-dark, #64748b);
  font-weight: 600;
}

.metric-value,
.summary-value {
  font-size: 1.25rem;
  font-weight: 700;
}

.status-badge,
.risk-badge {
  display: inline-block;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: uppercase;
  width: fit-content;
}

.status-on-track,
.badge-low {
  background-color: rgba(16, 185, 129, 0.15);
  color: #047857;
}

.status-at-risk,
.badge-medium {
  background-color: rgba(245, 158, 11, 0.15);
  color: #b45309;
}

.status-behind,
.badge-high {
  background-color: rgba(239, 68, 68, 0.15);
  color: #b91c1c;
}

.simulator-controls-card {
  padding: 16px 20px;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  background: var(--bg-card, #f8fafc);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.card-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
}

.simulator-form {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  align-items: flex-end;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 600;
}

.form-control {
  min-height: 44px;
  padding: 8px 12px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 6px;
  font-size: 0.9rem;
}

.btn {
  min-height: 44px;
  min-width: 44px;
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  border: none;
}

.btn-primary {
  background-color: var(--accent, #2563eb);
  color: #ffffff;
}

.btn-secondary {
  background-color: var(--border, #e2e8f0);
  color: var(--neutral-dark, #334155);
}

.table-wrapper {
  overflow-x: auto;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
}

.data-table,
.heatmap-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.data-table th,
.data-table td,
.heatmap-table th,
.heatmap-table td {
  padding: 10px 14px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  text-align: left;
}

.data-table th,
.heatmap-table th {
  background-color: var(--bg-card, #f1f5f9);
  font-weight: 700;
}

/* Heatmap Grid Styles */
.heatmap-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.search-box {
  flex: 1;
  min-width: 220px;
}

.filter-pills {
  display: flex;
  gap: 6px;
}

.pill-btn {
  min-height: 44px;
  min-width: 44px;
  padding: 6px 14px;
  border-radius: 20px;
  border: 1px solid var(--border, #cbd5e1);
  background: transparent;
  font-size: 0.85rem;
  cursor: pointer;
}

.pill-btn.active {
  background-color: var(--accent, #2563eb);
  color: #ffffff;
  border-color: var(--accent, #2563eb);
  font-weight: 600;
}

.heatmap-row {
  cursor: pointer;
}

.heatmap-row:hover,
.heatmap-row:focus-visible {
  background-color: rgba(37, 99, 235, 0.05);
}

.cell-rank {
  font-weight: 700;
}

.site-name {
  font-weight: 600;
}

.site-code {
  font-size: 0.75rem;
  color: var(--neutral-dark, #64748b);
}

.cell-category {
  text-align: center;
  border-left: 1px solid var(--border, #e2e8f0);
}

.risk-cell-high {
  background-color: rgba(239, 68, 68, 0.18);
  color: #991b1b;
}

.risk-cell-medium {
  background-color: rgba(245, 158, 11, 0.18);
  color: #92400e;
}

.risk-cell-low {
  background-color: rgba(16, 185, 129, 0.18);
  color: #065f46;
}

.cat-score {
  font-weight: 700;
  font-size: 0.95rem;
}

.cat-tier {
  font-size: 0.7rem;
  text-transform: uppercase;
}

/* Modal Styles */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background: #ffffff;
  border-radius: 12px;
  width: 90%;
  max-width: 680px;
  max-height: 85vh;
  overflow-y: auto;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.modal-title {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
}

.modal-subtitle {
  font-size: 0.9rem;
  color: #64748b;
  font-weight: 400;
}

.modal-badges {
  display: flex;
  gap: 10px;
  margin-top: 6px;
}

.btn-close {
  min-height: 44px;
  min-width: 44px;
  background: transparent;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
}

.modal-section-title {
  margin: 12px 0 6px 0;
  font-size: 1rem;
  font-weight: 700;
}

.modal-category-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 12px;
}

.modal-cat-card {
  padding: 12px;
  border-radius: 8px;
  border: 1px solid var(--border, #cbd5e1);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.cat-card-header {
  display: flex;
  justify-content: space-between;
  font-weight: 700;
  font-size: 0.85rem;
}

.cat-card-details {
  font-size: 0.8rem;
  margin: 0;
}

.cra-action-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.cra-action-item {
  display: flex;
  gap: 8px;
  font-size: 0.9rem;
}

.action-bullet {
  color: var(--accent, #2563eb);
  font-weight: 700;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  border: 0;
}
</style>
