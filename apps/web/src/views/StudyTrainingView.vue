<template>
  <div id="section-study-training" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R2) §4</span>
          <span class="badge badge-standard">DIA TMF Zone 5 (05.02.04)</span>
          <span class="badge badge-standard">Automated EDC Write-Gating</span>
        </div>
        <h2 class="view-title">Study Training &amp; Qualification Hub</h2>
        <p class="view-subtitle">
          Automated delta-curricula for protocol amendments, interactive
          time-on-page tracking, comprehension quizzes (&ge;80%), 21 CFR Part 11
          electronic signatures, and real-time EDC write-permission gating.
        </p>
      </div>
    </div>

    <!-- Metric KPI Summary Cards -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon alert-icon">📚</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ activeCurriculaCount }}
          </div>
          <div class="stat-label">Active Protocol Curricula</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon capa-icon">✅</div>
        <div class="stat-details">
          <div class="stat-value text-success">
            {{ completedQualificationsCount }}
          </div>
          <div class="stat-label">Verified Qualifications</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon rbqm-icon">🔒</div>
        <div class="stat-details">
          <div class="stat-value text-warning">
            {{ pendingGatedCount }}
          </div>
          <div class="stat-label">Gated Staff Assignments</div>
        </div>
      </div>

      <div class="stat-card">
        <div class="stat-icon">🎯</div>
        <div class="stat-details">
          <div class="stat-value">80%</div>
          <div class="stat-label">Min Quiz Threshold</div>
        </div>
      </div>
    </div>

    <!-- Integrated Protocol Training Console -->
    <div class="console-wrapper">
      <ProtocolTrainingConsole />
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import ProtocolTrainingConsole from "../components/clinical/ProtocolTrainingConsole.vue";
import { trainingApi } from "../api/training";

const activeCurriculaCount = ref(1);
const completedQualificationsCount = ref(0);
const pendingGatedCount = ref(1);

onMounted(async () => {
  try {
    const curricula = await trainingApi.listCurricula();
    activeCurriculaCount.value = curricula.length || 1;

    const assignments = await trainingApi.listAssignments();
    completedQualificationsCount.value = assignments.filter(
      (a) => a.status === "COMPLETED"
    ).length;
    pendingGatedCount.value = assignments.filter(
      (a) => a.status !== "COMPLETED"
    ).length;
  } catch (err) {
    console.error("Failed to load training metrics:", err);
  }
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

.console-wrapper {
  margin-top: var(--spacing-md, 16px);
}
</style>
