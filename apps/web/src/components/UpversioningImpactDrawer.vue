<template>
  <ClinicalDrawer
    :model-value="codingStore.impactAnalysis.isOpen"
    :teleport="false"
    size="lg"
    overlay-class="drawer-overlay upversioning-impact-drawer-overlay"
    panel-class="drawer-panel upversioning-impact-drawer-panel"
    body-class="drawer-body"
    aria-labelledby="upversioning-impact-title"
    :dismissible="!codingStore.impactAnalysis.isLoading"
    @close="close"
  >
    <template #header>
      <header class="drawer-header">
        <div class="header-titles">
          <GitCompare :size="18" aria-hidden="true" class="header-icon" />
          <h3 id="upversioning-impact-title">
            Dictionary Up-Versioning Impact Analysis
          </h3>
        </div>
        <button
          type="button"
          class="btn-close"
          aria-label="Close impact analysis drawer"
          :disabled="codingStore.impactAnalysis.isLoading"
          @click="close"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </header>
    </template>

    <p class="drawer-lead">
      Evaluate downstream recoding impact and structural changes before
      migrating active study dictionary versions.
    </p>

    <!-- Configuration Card -->
    <div class="config-card">
      <h4>Analysis Target Parameters</h4>
      <div class="config-grid">
        <div class="drawer-input-block">
          <label for="impact-dict-type" class="drawer-input-label"
            >Target Dictionary</label
          >
          <select
            id="impact-dict-type"
            v-model="dictType"
            class="drawer-select-input"
          >
            <option value="MEDDRA">MedDRA</option>
            <option value="WHODRUG">WHODrug</option>
          </select>
        </div>

        <div class="drawer-input-block">
          <label for="impact-target-version" class="drawer-input-label"
            >Target Migration Version</label
          >
          <input
            id="impact-target-version"
            v-model="targetVersion"
            type="text"
            class="drawer-text-input"
            placeholder="e.g. 27.0 or 2024-09"
          />
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
          <Play
            v-if="!codingStore.impactAnalysis.isLoading"
            :size="15"
            aria-hidden="true"
          />
          {{
            codingStore.impactAnalysis.isLoading
              ? "Evaluating Dictionary Delta..."
              : "Run Impact Analysis"
          }}
        </button>
      </div>
    </div>

    <!-- Error State -->
    <ModalErrorAlert
      :error="codingStore.impactAnalysis.error"
      retry-label="Attempt Again"
      dismiss-label="Dismiss"
      @retry="runAnalysis"
      @dismiss="codingStore.impactAnalysis.error = null"
    />

    <!-- Loading State -->
    <div
      v-if="codingStore.impactAnalysis.isLoading"
      class="analysis-loading-state"
    >
      <div class="spinner" />
      <h4>Running Up-Versioning Delta Engine...</h4>
      <p>
        Scanning all active coded assignments against target dictionary version
        <strong>{{ targetVersion }}</strong>.
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
          <div class="metric-value tabular-nums">
            {{ metrics.unchanged }}
          </div>
          <div class="metric-label">Unchanged Terms</div>
          <div class="metric-desc">
            Terms and hierarchies remain 100% valid in new version.
          </div>
        </div>

        <div class="metric-card metric-reclassified">
          <div class="metric-value tabular-nums">
            {{ metrics.reclassified }}
          </div>
          <div class="metric-label">Reclassified Terms</div>
          <div class="metric-desc">
            Primary SOC or parent hierarchy shifted; recoding review advised.
          </div>
        </div>

        <div class="metric-card metric-deprecated">
          <div class="metric-value tabular-nums">
            {{ metrics.deprecated }}
          </div>
          <div class="metric-label">Deprecated Terms</div>
          <div class="metric-desc">
            Codes dropped or invalidated; mandatory re-assignment required.
          </div>
        </div>

        <div class="metric-card metric-skipped">
          <div class="metric-value tabular-nums">
            {{ metrics.skipped }}
          </div>
          <div class="metric-label">Skipped / Uncoded</div>
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
            generate an immutable ledger entry recording the migration decision.
          </li>
          <li>
            <strong>Freeze Check:</strong> Verify whether affected studies or
            forms are under active Data Lock before executing bulk upversioning.
          </li>
          <li v-if="metrics.deprecated > 0">
            <strong>Action Required:</strong>
            {{ metrics.deprecated }} deprecated code(s) must be manually recoded
            prior to database lock.
          </li>
        </ul>
      </div>
    </div>

    <!-- Initial Placeholder State -->
    <div v-else class="initial-placeholder-state">
      <div class="placeholder-icon" aria-hidden="true">
        <BarChart3 :size="36" aria-hidden="true" />
      </div>
      <h4>Ready for Impact Analysis</h4>
      <p>
        Select a target dictionary and version above and click "Run Impact
        Analysis" to calculate affected term metrics.
      </p>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="codingStore.impactAnalysis.isLoading"
          @click="close"
        >
          Close
        </button>
      </div>
    </template>
  </ClinicalDrawer>
</template>

<script setup>
import { ref, computed } from "vue";
import { BarChart3, GitCompare, Play, X } from "lucide-vue-next";
import { ClinicalDrawer, ModalErrorAlert } from "ui";
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
:global(.upversioning-impact-drawer-overlay) {
  z-index: 1060;
}

.drawer-header {
  padding: 8px 16px;
  min-height: 48px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  background-color: var(--semantic-color-surface-muted);
  box-sizing: border-box;
}

.header-titles {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1;
}

.header-icon {
  flex-shrink: 0;
  color: var(--accent);
}

.header-titles h3 {
  margin: 0;
  font-size: var(--font-size-md);
  font-weight: 700;
  color: var(--text-main);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-lead {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--neutral-dark);
}

.btn-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  min-width: 44px;
  min-height: 44px;
  padding: 6px;
  border-radius: 6px;
  background: transparent;
  border: none;
  cursor: pointer;
  color: var(--neutral-dark);
}

.btn-close:hover:not(:disabled) {
  background: var(--semantic-color-border);
}

:deep(.drawer-body) {
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.config-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.config-card h4 {
  margin: 0;
  font-size: var(--font-size-base);
  color: var(--text-main);
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
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--error);
  padding: 12px 16px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: var(--font-size-sm);
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
  font-size: var(--font-size-md);
  color: var(--text-main);
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
  background-color: var(--semantic-color-surface);
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
  border-left: 4px solid var(--semantic-color-border-control);
}

.metric-value {
  font-size: var(--font-size-3xl);
  font-weight: 800;
  line-height: 1;
  color: var(--text-main);
}

.metric-label {
  font-weight: 700;
  font-size: var(--font-size-sm);
}

.metric-desc {
  font-size: var(--font-size-2xs);
  color: var(--neutral-dark);
  line-height: 1.3;
}

.delta-bar-card,
.recommendations-card {
  background-color: var(--semantic-color-surface);
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
  font-size: var(--font-size-sm);
  color: var(--text-main);
}

.delta-bar {
  display: flex;
  height: 14px;
  border-radius: 7px;
  overflow: hidden;
  background-color: var(--semantic-color-surface-header);
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
  font-size: var(--font-size-2xs);
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
  font-size: var(--font-size-xs);
  color: var(--neutral-dark);
}

.rec-list li strong {
  color: var(--text-main);
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
  color: var(--semantic-color-text-secondary);
  margin-bottom: 12px;
}

:global(.upversioning-impact-drawer-panel > .clinical-drawer-footer) {
  padding: 0;
  border-top: none;
}

.drawer-footer {
  width: 100%;
  padding: 10px 16px;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  background-color: var(--semantic-color-surface-muted);
  box-sizing: border-box;
}

.btn {
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.drawer-input-block {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.drawer-input-label {
  font-size: var(--font-size-xs);
  font-weight: 700;
  color: var(--text-main);
  letter-spacing: 0.01em;
}

.drawer-select-input,
.drawer-text-input {
  width: 100%;
  max-width: 100%;
  min-height: 44px;
  padding: 9px 12px;
  border: 1.5px solid var(--border);
  border-radius: 6px;
  font-size: var(--font-size-md);
  font-family: inherit;
  background-color: var(--semantic-color-surface);
  color: var(--text-main);
  box-sizing: border-box;
  transition: border-color 0.15s ease-in-out;
}

@media (max-width: 480px) {
  .config-grid,
  .metrics-grid {
    grid-template-columns: 1fr;
  }

  .config-action .btn,
  .drawer-footer .btn {
    width: 100%;
  }
}

.drawer-select-input:focus-visible,
.drawer-text-input:focus-visible {
  border-color: var(--accent);
}

.badge {
  display: inline-block;
  padding: 4px 10px;
  font-size: var(--font-size-2xs);
  font-weight: 800;
  border-radius: 12px;
}

.badge-accent {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}
</style>
