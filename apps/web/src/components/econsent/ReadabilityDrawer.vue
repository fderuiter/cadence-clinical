<template>
  <aside
    ref="drawerRef"
    class="readability-drawer"
    :class="{ open: isOpen }"
    role="dialog"
    :aria-modal="isOpen ? 'true' : undefined"
    :aria-hidden="!isOpen ? 'true' : undefined"
    :inert="!isOpen || undefined"
    aria-label="Readability Harmonization Assistant"
    tabindex="-1"
  >
    <div class="drawer-header">
      <div class="drawer-title">
        <span class="icon">📖</span>
        <h3>eConsent Readability Harmonizer</h3>
      </div>
      <button
        type="button"
        class="btn-close"
        title="Close Assistant"
        aria-label="Close Assistant"
        @click="$emit('close')"
      >
        ✕
      </button>
    </div>

    <div class="drawer-body">
      <!-- Readability Metrics Cards -->
      <section class="metrics-grid">
        <div class="metric-card">
          <span class="metric-label">Grade Level (FKGL)</span>
          <div class="metric-value-row">
            <span class="metric-value tabular-nums">{{
              metrics ? metrics.flesch_kincaid_grade_level : "--"
            }}</span>
            <span
              v-if="metrics"
              class="badge"
              :class="
                metrics.is_target_grade_level
                  ? 'badge-success'
                  : 'badge-warning'
              "
            >
              {{
                metrics.is_target_grade_level ? "Target (6th–8th)" : "Complex"
              }}
            </span>
          </div>
          <span class="metric-sub">{{
            metrics ? metrics.interpretation : "Awaiting analysis"
          }}</span>
        </div>

        <div class="metric-card">
          <span class="metric-label">Dale-Chall Score</span>
          <div class="metric-value-row">
            <span class="metric-value tabular-nums">{{
              metrics ? metrics.dale_chall_score : "--"
            }}</span>
            <span class="badge badge-info">{{
              metrics ? metrics.dale_chall_grade_level : "--"
            }}</span>
          </div>
          <span class="metric-sub">{{
            metrics
              ? `${metrics.difficult_word_count} difficult words (${metrics.word_count} total)`
              : "Standard Vocabulary"
          }}</span>
        </div>
      </section>

      <!-- Action Row -->
      <div class="action-bar">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="loading"
          @click="analyzeText"
        >
          🔍 Recalculate Scores
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="loading"
          @click="harmonizeWithAI"
        >
          <span v-if="loading">⏳ Harmonizing...</span>
          <span v-else>✨ Harmonize with AI (Tier 2)</span>
        </button>
      </div>

      <!-- Substitution Diffs Pane -->
      <section class="suggestions-section">
        <div class="section-title-row">
          <h4>Medical Jargon Simplification Diffs</h4>
          <span v-if="substitutions.length" class="count-badge tabular-nums"
            >{{ substitutions.length }} Terms</span
          >
        </div>

        <div v-if="substitutions.length === 0" class="empty-state">
          <p v-if="!loading">
            No complex medical jargon detected. Run Harmonize with AI to scan
            for patient-friendly term substitutions.
          </p>
          <p v-else>
            Scanning clinical clauses through AI Gateway Tier 2 for jargon
            simplification...
          </p>
        </div>

        <div v-else class="substitutions-list">
          <div
            v-for="(sub, idx) in substitutions"
            :key="idx"
            class="substitution-card"
          >
            <div class="sub-header">
              <span class="category-tag">{{ sub.category }}</span>
              <span class="confidence-tag"
                >Confidence:
                {{ Math.round((sub.confidence_score || 0.95) * 100) }}%</span
              >
            </div>

            <div class="diff-view">
              <div class="diff-original">
                <span class="diff-tag del">Jargon</span>
                <span class="diff-text del-text">{{ sub.original_term }}</span>
              </div>
              <span class="arrow">➔</span>
              <div class="diff-suggested">
                <span class="diff-tag ins">Plain</span>
                <span class="diff-text ins-text">{{ sub.suggested_term }}</span>
              </div>
            </div>

            <p class="rationale-text">
              <strong>Rationale:</strong> {{ sub.rationale }}
            </p>

            <div class="sub-actions">
              <button
                type="button"
                class="btn-apply-single"
                @click="applySingleSubstitution(sub)"
              >
                ✓ Apply Substitution
              </button>
            </div>
          </div>

          <div class="bulk-actions">
            <button
              type="button"
              class="btn btn-success btn-block"
              @click="applyAllSubstitutions"
            >
              ✓ Apply All Harmonizations ({{ substitutions.length }})
            </button>
          </div>
        </div>
      </section>

      <!-- Protocol Amendment & Version Integrity Section -->
      <section class="audit-link-section">
        <h4>21 CFR Part 11 IRB Amendment Linking</h4>
        <div class="form-group">
          <label for="proto-version-input">Protocol Amendment Version:</label>
          <input
            id="proto-version-input"
            v-model="protocolVersion"
            type="text"
            class="form-control"
            placeholder="e.g. Protocol Amendment v2.0"
          />
        </div>
        <div class="form-group">
          <label for="change-reason-input"
            >Change Justification / Audit Reason:</label
          >
          <textarea
            id="change-reason-input"
            v-model="changeReason"
            class="form-control"
            rows="2"
            placeholder="e.g. Readability Harmonization: Simplified medical jargon for 6th-8th grade reading level."
          />
        </div>
      </section>
    </div>
  </aside>
</template>

<script setup>
import { ref, watch, onMounted } from "vue";
import { useFocusTrap } from "ui";
import { econsentService } from "../../api/econsent.js";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  text: {
    type: String,
    default: "",
  },
  clauseId: {
    type: String,
    default: "",
  },
  studyId: {
    type: String,
    default: "CADENCE-101",
  },
});

const emit = defineEmits([
  "close",
  "apply-substitution",
  "apply-all",
  "update-metrics",
]);

const drawerRef = ref(null);
const loading = ref(false);

useFocusTrap(drawerRef, {
  isOpen: () => props.isOpen,
  onEscape: () => {
    if (props.isOpen) {
      emit("close");
    }
  },
});
const metrics = ref(null);
const substitutions = ref([]);
const protocolVersion = ref("v2.0");
const changeReason = ref(
  "Readability Harmonization: Simplified medical jargon for 6th-8th grade reading level."
);

async function analyzeText() {
  if (!props.text || !props.text.trim()) return;
  try {
    loading.value = true;
    const res = await econsentService.analyzeReadability({
      text: props.text,
      study_id: props.studyId,
    });
    metrics.value = res.metrics;
    emit("update-metrics", res.metrics);
  } catch (err) {
    console.error("Failed to analyze readability:", err);
  } finally {
    loading.value = false;
  }
}

async function harmonizeWithAI() {
  if (!props.text || !props.text.trim()) return;
  try {
    loading.value = true;
    const res = await econsentService.harmonizeReadability({
      text: props.text,
      study_id: props.studyId,
      target_grade_level: 8.0,
      protocol_version: protocolVersion.value,
    });
    metrics.value = res.harmonized_metrics;
    substitutions.value = res.substitutions || [];
    emit("update-metrics", res.harmonized_metrics);
  } catch (err) {
    console.error("Failed to harmonize readability:", err);
  } finally {
    loading.value = false;
  }
}

function applySingleSubstitution(sub) {
  emit("apply-substitution", {
    substitution: sub,
    protocolVersion: protocolVersion.value,
    changeReason: changeReason.value,
  });
  // Remove from candidate list
  substitutions.value = substitutions.value.filter(
    (s) => s.original_term !== sub.original_term
  );
}

function applyAllSubstitutions() {
  emit("apply-all", {
    substitutions: substitutions.value,
    protocolVersion: protocolVersion.value,
    changeReason: changeReason.value,
  });
  substitutions.value = [];
}

watch(
  () => props.text,
  () => {
    if (props.isOpen) {
      analyzeText();
    }
  }
);

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      analyzeText();
    }
  }
);

onMounted(() => {
  if (props.isOpen && props.text) {
    analyzeText();
  }
});
</script>

<style scoped>
.readability-drawer {
  position: fixed;
  top: 0;
  right: 0;
  bottom: 0;
  width: var(
    --semantic-drawer-width-sm,
    var(--drawer-width-sm, min(100vw, 440px))
  );
  max-width: 100vw;
  background-color: var(--semantic-color-surface);
  border-left: 1px solid var(--border);
  box-shadow: -4px 0 16px rgba(0, 0, 0, 0.08);
  z-index: 1000;
  display: flex;
  flex-direction: column;
  visibility: hidden;
  outline: none;
  transform: translateX(100%);
  transition:
    transform 0.3s cubic-bezier(0.16, 1, 0.3, 1),
    visibility 0s linear 0.3s;
}

.readability-drawer.open {
  visibility: visible;
  transform: translateX(0);
  transition:
    transform 0.3s cubic-bezier(0.16, 1, 0.3, 1),
    visibility 0s linear 0s;
}

.drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--border);
}

.drawer-title {
  display: flex;
  align-items: center;
  gap: 10px;
}

.drawer-title h3 {
  margin: 0;
  font-size: 1.05rem;
  color: var(--semantic-color-text);
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: var(--semantic-color-text-secondary);
  border-radius: 4px;
  padding: 4px 8px;
}

.btn-close:hover {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.drawer-body {
  padding: 20px;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.metrics-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.metric-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.metric-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.metric-value-row {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
}

.metric-value {
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.metric-sub {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.badge {
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 12px;
  font-weight: 600;
}

.badge-success {
  background-color: var(--success-bg);
  color: var(--success);
}

.badge-warning {
  background-color: var(--warning-bg);
  color: var(--warning);
}

.badge-info {
  background-color: var(--accent-bg);
  color: var(--accent);
}

.action-bar {
  display: flex;
  gap: 10px;
}

.action-bar .btn {
  flex: 1;
  padding: 8px 12px;
  font-size: 0.85rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
}

.btn-secondary {
  background-color: var(--semantic-color-surface-muted);
  border-color: var(--border);
  color: var(--semantic-color-text);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-header);
}

.btn-primary {
  background-color: var(--primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover {
  background-color: var(--semantic-color-primary-dark);
}

.suggestions-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.section-title-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.section-title-row h4 {
  margin: 0;
  font-size: 0.9rem;
  color: var(--semantic-color-text);
}

.count-badge {
  background-color: var(--semantic-color-info-bg);
  color: var(--primary);
  font-size: 0.75rem;
  padding: 2px 8px;
  border-radius: 10px;
  font-weight: 600;
}

.empty-state {
  padding: 24px;
  text-align: center;
  background-color: var(--semantic-color-surface-muted);
  border: 1px dashed var(--border);
  border-radius: 8px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.substitutions-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.substitution-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.sub-header {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
}

.category-tag {
  background-color: var(--semantic-color-surface-muted);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--semantic-color-text-secondary);
}

.confidence-tag {
  color: var(--semantic-color-text-secondary);
}

.diff-view {
  display: flex;
  align-items: center;
  gap: 8px;
  background-color: var(--semantic-color-surface-muted);
  padding: 8px;
  border-radius: 6px;
}

.diff-original,
.diff-suggested {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.diff-tag {
  font-size: 0.65rem;
  text-transform: uppercase;
  font-weight: 700;
}

.diff-tag.del {
  color: var(--error);
}

.diff-tag.ins {
  color: var(--success);
}

.diff-text.del-text {
  font-size: 0.85rem;
  text-decoration: line-through;
  color: var(--error);
}

.diff-text.ins-text {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--success);
}

.arrow {
  color: var(--semantic-color-text-secondary);
  font-size: 0.9rem;
}

.rationale-text {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  line-height: 1.4;
}

.btn-apply-single {
  background-color: transparent;
  border: 1px solid var(--primary);
  color: var(--primary);
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  align-self: flex-start;
}

.btn-apply-single:hover {
  background-color: var(--primary);
  color: var(--semantic-color-surface);
}

.btn-success {
  background-color: var(--success);
  color: var(--semantic-color-surface);
  padding: 10px;
  font-weight: 600;
  border-radius: 6px;
  border: none;
  cursor: pointer;
}

.btn-success:hover {
  background-color: var(--semantic-color-success);
}

.btn-block {
  width: 100%;
}

.audit-link-section {
  border-top: 1px solid var(--border);
  padding-top: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.audit-link-section h4 {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.form-control {
  border: 1px solid var(--border);
  border-radius: 4px;
  padding: 6px 10px;
  font-size: 0.85rem;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  max-width: 100%;
}

/* Mobile Breakpoint Overrides (max-width: 640px) */
@media (max-width: 640px) {
  .readability-drawer {
    width: 100vw;
    max-width: 100vw;
  }

  .drawer-header,
  .drawer-body {
    padding-left: 16px;
    padding-right: 16px;
  }

  .metrics-grid {
    grid-template-columns: 1fr;
  }

  .action-bar {
    flex-direction: column;
  }

  .action-bar .btn {
    width: 100%;
    min-height: var(--semantic-touch-target-min, 48px);
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }

  .diff-view {
    flex-direction: column;
    align-items: stretch;
  }

  .arrow {
    text-align: center;
  }

  .btn-apply-single,
  .btn-success {
    min-height: var(--semantic-touch-target-min, 48px);
    width: 100%;
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }
}
</style>
