<template>
  <div
    v-if="codingStore.impactAnalysis.isOpen"
    class="drawer-overlay"
    @click.self="close"
  >
    <div class="drawer-panel">
      <!-- Drawer Header -->
      <div class="drawer-header">
        <div class="header-titles">
          <h3>Dictionary Up-Versioning Impact Analysis</h3>
          <p>
            Evaluate downstream recoding impact and structural changes before
            migrating active study dictionary versions.
          </p>
        </div>
        <button
          type="button"
          class="btn-close"
          @click="close"
        >
          ✕
        </button>
      </div>

      <!-- Drawer Content -->
      <div class="drawer-body">
        <!-- Configuration Card -->
        <div class="config-card">
          <h4>Analysis Target Parameters</h4>
          <div class="config-grid">
            <div class="drawer-input-block">
              <label
                for="impact-dict-type"
                class="drawer-input-label"
              >Target Dictionary</label>
              <select
                id="impact-dict-type"
                v-model="dictType"
                class="drawer-select-input"
              >
                <option value="MEDDRA">
                  MedDRA
                </option>
                <option value="WHODRUG">
                  WHODrug
                </option>
              </select>
            </div>

            <div class="drawer-input-block">
              <label
                for="impact-target-version"
                class="drawer-input-label"
              >Target Migration Version</label>
              <input
                id="impact-target-version"
                v-model="targetVersion"
                type="text"
                class="drawer-text-input"
                placeholder="e.g. 27.0 or 2024-09"
              >
            </div>
          </div>

          <div class="config-action">
            <button
              type="button"
              class="btn btn-primary"
              :disabled="
                codingStore.impactAnalysis.isLoading || !targetVersion.trim()
              "
              @click="runAnalysis"
            >
              {{
                codingStore.impactAnalysis.isLoading
                  ? "Evaluating Dictionary Delta..."
                  : "🚀 Run Impact Analysis"
              }}
            </button>
          </div>
        </div>

        <!-- Error State -->
        <div
          v-if="codingStore.impactAnalysis.error"
          class="error-banner"
        >
          <span class="error-icon">⚠️</span>
          <span>{{ codingStore.impactAnalysis.error }}</span>
        </div>

        <!-- Loading State -->
        <div
          v-if="codingStore.impactAnalysis.isLoading"
          class="analysis-loading-state"
        >
          <div class="spinner" />
          <h4>Running Up-Versioning Delta Engine...</h4>
          <p>
            Scanning all active coded assignments against target dictionary
            version <strong>{{ targetVersion }}</strong>.
          </p>
        </div>

        <!-- Analysis Results View -->
        <div
          v-else-if="codingStore.impactAnalysis.results"
          class="results-container"
        >
          <div class="results-summary-header">
            <h4>Impact Summary Report</h4>
            <span class="badge badge-accent">
              {{ dictType }} v{{ targetVersion }}
            </span>
          </div>

          <!-- 4 Metric Cards -->
          <div class="metrics-grid">
            <div class="metric-card metric-unchanged">
              <div class="metric-value">
                {{ metrics.unchanged }}
              </div>
              <div class="metric-label">
                Unchanged Terms
              </div>
              <div class="metric-desc">
                Terms and hierarchies remain 100% valid in new version.
              </div>
            </div>

            <div class="metric-card metric-reclassified">
              <div class="metric-value">
                {{ metrics.reclassified }}
              </div>
              <div class="metric-label">
                Reclassified Terms
              </div>
              <div class="metric-desc">
                Primary SOC or parent hierarchy shifted; recoding review
                advised.
              </div>
            </div>

            <div class="metric-card metric-deprecated">
              <div class="metric-value">
                {{ metrics.deprecated }}
              </div>
              <div class="metric-label">
                Deprecated Terms
              </div>
              <div class="metric-desc">
                Codes dropped or invalidated; mandatory re-assignment required.
              </div>
            </div>

            <div class="metric-card metric-skipped">
              <div class="metric-value">
                {{ metrics.skipped }}
              </div>
              <div class="metric-label">
                Skipped / Uncoded
              </div>
              <div class="metric-desc">
                Uncoded verbatims or query-pending entries bypassed.
              </div>
            </div>
          </div>

          <!-- Total Delta Ratio Bar -->
          <div class="delta-bar-card">
            <h5>Structural Migration Delta Ratio</h5>
            <div class="delta-bar">
              <div
                class="delta-seg seg-unchanged"
                :style="{ width: unchangedPct + '%' }"
                :title="`Unchanged: ${metrics.unchanged} (${unchangedPct}%)`"
              />
              <div
                class="delta-seg seg-reclassified"
                :style="{ width: reclassifiedPct + '%' }"
                :title="`Reclassified: ${metrics.reclassified} (${reclassifiedPct}%)`"
              />
              <div
                class="delta-seg seg-deprecated"
                :style="{ width: deprecatedPct + '%' }"
                :title="`Deprecated: ${metrics.deprecated} (${deprecatedPct}%)`"
              />
            </div>
            <div class="delta-legend">
              <div class="legend-item">
                <span class="legend-dot dot-unchanged" />
                <span>Unchanged ({{ unchangedPct }}%)</span>
              </div>
              <div class="legend-item">
                <span class="legend-dot dot-reclassified" />
                <span>Reclassified ({{ reclassifiedPct }}%)</span>
              </div>
              <div class="legend-item">
                <span class="legend-dot dot-deprecated" />
                <span>Deprecated ({{ deprecatedPct }}%)</span>
              </div>
            </div>
          </div>

          <!-- Recommendations Checklist -->
          <div class="recommendations-card">
            <h5>Regulatory &amp; SDTM Compliance Recommendations</h5>
            <ul class="rec-list">
              <li>
                <strong>Part 11 Audit Trail:</strong> All recoded items will
                generate an immutable ledger entry recording the migration
                decision.
              </li>
              <li>
                <strong>Freeze Check:</strong> Verify whether affected studies
                or forms are under active Data Lock before executing bulk
                upversioning.
              </li>
              <li v-if="metrics.deprecated > 0">
                <strong>Action Required:</strong>
                {{ metrics.deprecated }} deprecated code(s) must be manually
                recoded prior to database lock.
              </li>
            </ul>
          </div>
        </div>

        <!-- Initial Placeholder State -->
        <div
          v-else
          class="initial-placeholder-state"
        >
          <div class="placeholder-icon">
            📊
          </div>
          <h4>Ready for Impact Analysis</h4>
          <p>
            Select a target dictionary and version above and click "Run Impact
            Analysis" to calculate affected term metrics.
          </p>
        </div>
      </div>

      <!-- Drawer Footer -->
      <div class="drawer-footer">
        <button
          type="button"
          class="btn btn-secondary"
          @click="close"
        >
          Close
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useCodingStore } from "../stores/coding";

const codingStore = useCodingStore();

const dictType = ref("MEDDRA");
const targetVersion = ref("27.0");

const metrics = computed(() => {
  const r = codingStore.impactAnalysis.results;
  return {
    unchanged: r?.unchanged || 0,
    reclassified: r?.reclassified || 0,
    deprecated: r?.deprecated || 0,
    skipped: r?.skipped || 0,
  };
});

const totalEvaluated = computed(() => {
  return (
    metrics.value.unchanged +
    metrics.value.reclassified +
    metrics.value.deprecated
  );
});

const unchangedPct = computed(() => {
  if (totalEvaluated.value === 0) return 0;
  return Math.round((metrics.value.unchanged / totalEvaluated.value) * 100);
});

const reclassifiedPct = computed(() => {
  if (totalEvaluated.value === 0) return 0;
  return Math.round((metrics.value.reclassified / totalEvaluated.value) * 100);
});

const deprecatedPct = computed(() => {
  if (totalEvaluated.value === 0) return 0;
  return Math.round((metrics.value.deprecated / totalEvaluated.value) * 100);
});

async function runAnalysis() {
  try {
    await codingStore.runImpactAnalysis({
      dictionaryType: dictType.value,
      newVersion: targetVersion.value,
    });
  } catch (err) {
    console.error("Impact analysis execution error:", err);
  }
}

function close() {
  codingStore.closeImpactDrawer();
}
</script>

<style scoped>
.drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  justify-content: flex-end;
  z-index: 1060;
}

.drawer-panel {
  background-color: white;
  width: 100%;
  max-width: 640px;
  height: 100vh;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.25s ease-out;
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  padding: 20px 24px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  background-color: #f8fafc;
}

.header-titles h3 {
  margin: 0;
  font-size: 1.2rem;
  color: var(--primary);
}

.header-titles p {
  margin: 4px 0 0 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.3rem;
  cursor: pointer;
  color: var(--neutral-dark);
}

.drawer-body {
  padding: 24px;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.config-card {
  background-color: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.config-card h4 {
  margin: 0;
  font-size: 0.95rem;
  color: var(--primary);
}

.config-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.config-action {
  display: flex;
  justify-content: flex-end;
}

.error-banner {
  background-color: var(--error-bg);
  border: 1px solid #fecaca;
  color: var(--error);
  padding: 12px 16px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 0.88rem;
}

.results-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.results-summary-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.results-summary-header h4 {
  margin: 0;
  font-size: 1.05rem;
  color: var(--primary);
}

.metrics-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.metric-card {
  padding: 16px;
  border-radius: 8px;
  border: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  gap: 4px;
  background-color: white;
}

.metric-unchanged {
  border-left: 4px solid var(--success);
}

.metric-reclassified {
  border-left: 4px solid var(--warning);
}

.metric-deprecated {
  border-left: 4px solid var(--error);
}

.metric-skipped {
  border-left: 4px solid #64748b;
}

.metric-value {
  font-size: 1.8rem;
  font-weight: 800;
  line-height: 1;
  color: var(--primary);
}

.metric-label {
  font-weight: 700;
  font-size: 0.85rem;
}

.metric-desc {
  font-size: 0.75rem;
  color: var(--neutral-dark);
  line-height: 1.3;
}

.delta-bar-card,
.recommendations-card {
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.delta-bar-card h5,
.recommendations-card h5 {
  margin: 0;
  font-size: 0.9rem;
  color: var(--primary);
}

.delta-bar {
  display: flex;
  height: 14px;
  border-radius: 7px;
  overflow: hidden;
  background-color: #f1f5f9;
}

.seg-unchanged {
  background-color: var(--success);
}

.seg-reclassified {
  background-color: var(--warning);
}

.seg-deprecated {
  background-color: var(--error);
}

.delta-legend {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  font-size: 0.78rem;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.legend-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.dot-unchanged {
  background-color: var(--success);
}

.dot-reclassified {
  background-color: var(--warning);
}

.dot-deprecated {
  background-color: var(--error);
}

.rec-list {
  padding-left: 20px;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}

.rec-list li strong {
  color: var(--primary);
}

.initial-placeholder-state,
.analysis-loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 48px 24px;
  text-align: center;
  color: var(--neutral-dark);
}

.placeholder-icon {
  font-size: 3rem;
  margin-bottom: 12px;
}

.drawer-footer {
  padding: 16px 24px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  background-color: #f8fafc;
}

.drawer-input-block {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.drawer-input-label {
  font-size: 0.84rem;
  font-weight: 700;
  color: var(--primary);
  letter-spacing: 0.01em;
}

.drawer-select-input,
.drawer-text-input {
  padding: 9px 12px;
  border: 1.5px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
  font-family: inherit;
  background-color: white;
  color: var(--primary);
  transition: border-color 0.15s ease-in-out;
}

.drawer-select-input:focus,
.drawer-text-input:focus {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 3px rgba(67, 56, 202, 0.15);
}

.badge {
  display: inline-block;
  padding: 4px 10px;
  font-size: 0.76rem;
  font-weight: 800;
  border-radius: 12px;
}

.badge-accent {
  background-color: #e0e7ff;
  color: #3730a3;
}
</style>
