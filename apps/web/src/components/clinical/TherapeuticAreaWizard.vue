<template>
  <div class="therapeutic-area-wizard card">
    <div class="wizard-header">
      <div class="header-title-box">
        <h3>🪄 Guided Therapeutic Area Protocol Wizard</h3>
        <p>
          Select a pre-configured therapeutic area template to generate a
          complete CDISC USDM v4.0 study protocol in &lt; 2 seconds.
        </p>
      </div>
      <div class="wizard-badges">
        <span class="badge badge-usdm">USDM v4.0</span>
        <span class="badge badge-gxp">21 CFR Part 11</span>
      </div>
    </div>

    <!-- Storage Error / Auto-Save Failure Alert Banner -->
    <div
      v-if="storageError"
      class="storage-warning-banner alert alert-warning"
      role="alert"
    >
      <div class="banner-content">
        <span class="banner-icon" aria-hidden="true">⚠️</span>
        <div class="banner-text">
          <strong>Auto-Save Warning:</strong> {{ storageError }}
        </div>
      </div>
      <div class="banner-actions">
        <button
          type="button"
          class="btn btn-sm btn-outline-warning btn-retry-save"
          @click="saveState"
        >
          🔄 Retry Auto-Save
        </button>
        <button
          type="button"
          class="btn btn-sm btn-link btn-dismiss-warning"
          aria-label="Dismiss storage warning"
          @click="dismissStorageError"
        >
          ✕ Dismiss
        </button>
      </div>
    </div>

    <!-- Step 1: TA Template Selection Cards -->
    <div class="ta-selection-section">
      <h4 class="section-subtitle">
        1. Select Therapeutic Area Starter Template
      </h4>
      <div class="ta-cards-grid">
        <div
          v-for="tpl in templateList"
          :key="tpl.id"
          class="ta-card"
          :class="{ selected: selectedTemplateId === tpl.id }"
          role="button"
          tabindex="0"
          :aria-pressed="selectedTemplateId === tpl.id"
          @click="selectTemplate(tpl.id)"
          @keydown.enter.prevent="selectTemplate(tpl.id)"
          @keydown.space.prevent="selectTemplate(tpl.id)"
        >
          <div class="ta-card-header">
            <span class="ta-icon">{{ tpl.icon }}</span>
            <span class="badge badge-category">{{ tpl.category }}</span>
          </div>
          <h5 class="ta-title">
            {{ tpl.title }}
          </h5>
          <p class="ta-desc">
            {{ tpl.description }}
          </p>
          <div class="ta-card-meta">
            <span class="meta-tag"
              >Phase: {{ tpl.defaultPhase.replace("_", " ") }}</span
            >
            <span class="meta-tag">Arms: {{ tpl.arms.length }}</span>
            <span class="meta-tag">Visits: {{ tpl.visits.length }}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Step 2: Interactive Design & Customization Panel -->
    <div v-if="activeConfig" class="ta-customization-section">
      <h4 class="section-subtitle">2. Customize Trial Design Parameters</h4>

      <!-- Overview Form Fields -->
      <div class="form-grid-three">
        <div class="form-group">
          <label for="ta-protocol-id" class="form-label"
            >Protocol Identifier *</label
          >
          <input
            id="ta-protocol-id"
            v-model="activeConfig.protocol_id"
            type="text"
            class="form-control input-protocol-id"
            placeholder="e.g. CDNC-2026-001"
          />
        </div>
        <div class="form-group">
          <label for="ta-dev-phase" class="form-label"
            >Development Phase *</label
          >
          <select
            id="ta-dev-phase"
            v-model="activeConfig.phase"
            class="form-control select-phase"
          >
            <option value="PHASE_I">Phase I</option>
            <option value="PHASE_II">Phase II</option>
            <option value="PHASE_III">Phase III</option>
            <option value="PHASE_IV">Phase IV</option>
          </select>
        </div>
        <div class="form-group">
          <label for="ta-rand-ratio" class="form-label"
            >Randomization Allocation Ratio</label
          >
          <select
            id="ta-rand-ratio"
            v-model="activeConfig.randomizationRatio"
            class="form-control select-rand-ratio"
          >
            <option value="1:1">1:1 (Equal 2-Arm)</option>
            <option value="2:1">2:1 (Experimental Heavy)</option>
            <option value="1:1:1">1:1:1 (Equal 3-Arm)</option>
            <option value="3:1">3:1 (Safety Focus)</option>
          </select>
        </div>
      </div>

      <div class="form-group wide">
        <label for="ta-study-title" class="form-label"
          >Official Study Protocol Title *</label
        >
        <input
          id="ta-study-title"
          v-model="activeConfig.study_title"
          type="text"
          class="form-control input-study-title"
          placeholder="Study Title"
        />
      </div>

      <!-- Tabbed Customization Views: Arms & Sample Sizes, Epochs, Visit Schedule -->
      <div class="customization-tabs-nav">
        <button
          type="button"
          class="btn btn-tab"
          :class="{ active: configTab === 'arms' }"
          @click="configTab = 'arms'"
        >
          🗺️ Study Arms &amp; Cohorts ({{ activeConfig.arms.length }})
        </button>
        <button
          type="button"
          class="btn btn-tab"
          :class="{ active: configTab === 'epochs' }"
          @click="configTab = 'epochs'"
        >
          ⏱️ Study Epochs ({{ activeConfig.epochs.length }})
        </button>
        <button
          type="button"
          class="btn btn-tab"
          :class="{ active: configTab === 'visits' }"
          @click="configTab = 'visits'"
        >
          📅 Visit Schedule Matrix ({{ activeConfig.visits.length }})
        </button>
      </div>

      <!-- Customization Tab 1: Arms & Sample Sizes -->
      <div v-if="configTab === 'arms'" class="tab-pane-content">
        <div class="pane-header">
          <h5>Study Cohort Arms &amp; Sample Size Allocation</h5>
          <button
            type="button"
            class="btn btn-secondary btn-sm btn-add-arm"
            @click="addArm"
          >
            ➕ Add Cohort Arm
          </button>
        </div>
        <div class="arms-table-container">
          <table class="table arms-table">
            <thead>
              <tr>
                <th>Arm Title</th>
                <th>Type</th>
                <th>Target Sample Size</th>
                <th>Description</th>
                <th style="width: 80px">Action</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(arm, idx) in activeConfig.arms" :key="arm.id || idx">
                <td>
                  <input
                    v-model="arm.name"
                    :aria-label="`Arm ${idx + 1} name`"
                    type="text"
                    class="form-control form-control-sm input-arm-name"
                    placeholder="Arm Title"
                  />
                </td>
                <td>
                  <select
                    v-model="arm.arm_type"
                    :aria-label="`Arm ${idx + 1} type`"
                    class="form-control form-control-sm select-arm-type"
                  >
                    <option value="EXPERIMENTAL">EXPERIMENTAL</option>
                    <option value="ACTIVE_COMPARATOR">ACTIVE COMPARATOR</option>
                    <option value="PLACEBO">PLACEBO</option>
                  </select>
                </td>
                <td>
                  <input
                    v-model.number="arm.target_sample_size"
                    :aria-label="`Arm ${idx + 1} target sample size`"
                    type="number"
                    min="1"
                    class="form-control form-control-sm input-sample-size"
                  />
                </td>
                <td>
                  <input
                    v-model="arm.description"
                    :aria-label="`Arm ${idx + 1} description`"
                    type="text"
                    class="form-control form-control-sm input-arm-desc"
                    placeholder="Arm Description"
                  />
                </td>
                <td>
                  <ClinicalIconButton
                    class="btn btn-danger btn-sm btn-remove-arm"
                    :disabled="activeConfig.arms.length <= 1"
                    :label="`Remove study arm ${arm.name || idx + 1}`"
                    @click="removeArm(idx)"
                  >
                    🗑️
                  </ClinicalIconButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Customization Tab 2: Study Epochs -->
      <div v-if="configTab === 'epochs'" class="tab-pane-content">
        <div class="pane-header">
          <h5>Study Epoch Sequence &amp; Durations</h5>
          <button
            type="button"
            class="btn btn-secondary btn-sm btn-add-epoch"
            @click="addEpoch"
          >
            ➕ Add Epoch
          </button>
        </div>
        <div class="epochs-table-container">
          <table class="table epochs-table">
            <thead>
              <tr>
                <th>Seq #</th>
                <th>Epoch Name</th>
                <th>Epoch Type</th>
                <th>Duration (Weeks)</th>
                <th style="width: 80px">Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(epoch, idx) in activeConfig.epochs"
                :key="epoch.id || idx"
              >
                <td class="seq-num">
                  {{ epoch.sequence_index }}
                </td>
                <td>
                  <input
                    v-model="epoch.name"
                    :aria-label="`Epoch ${idx + 1} name`"
                    type="text"
                    class="form-control form-control-sm input-epoch-name"
                  />
                </td>
                <td>
                  <select
                    v-model="epoch.epoch_type"
                    :aria-label="`Epoch ${idx + 1} type`"
                    class="form-control form-control-sm select-epoch-type"
                  >
                    <option value="SCREENING">SCREENING</option>
                    <option value="TREATMENT">TREATMENT</option>
                    <option value="WASHOUT">WASHOUT</option>
                    <option value="FOLLOW_UP">FOLLOW_UP</option>
                  </select>
                </td>
                <td>
                  <input
                    v-model.number="epoch.duration_weeks"
                    :aria-label="`Epoch ${idx + 1} duration in weeks`"
                    type="number"
                    min="1"
                    class="form-control form-control-sm input-epoch-duration"
                  />
                </td>
                <td>
                  <ClinicalIconButton
                    class="btn btn-danger btn-sm btn-remove-epoch"
                    :disabled="activeConfig.epochs.length <= 1"
                    :label="`Remove study epoch ${epoch.name || idx + 1}`"
                    @click="removeEpoch(idx)"
                  >
                    🗑️
                  </ClinicalIconButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Customization Tab 3: Visit Schedule -->
      <div v-if="configTab === 'visits'" class="tab-pane-content">
        <div class="pane-header">
          <h5>Planned Encounter Visits &amp; Target Day Windows</h5>
          <button
            type="button"
            class="btn btn-secondary btn-sm btn-add-visit"
            @click="addVisit"
          >
            ➕ Add Visit Encounter
          </button>
        </div>
        <div class="visits-table-container">
          <table class="table visits-table">
            <thead>
              <tr>
                <th>Visit Name</th>
                <th>Assigned Epoch</th>
                <th>Target Day</th>
                <th>Window (-)</th>
                <th>Window (+)</th>
                <th>Mandatory</th>
                <th style="width: 80px">Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(vis, idx) in activeConfig.visits"
                :key="vis.id || idx"
              >
                <td>
                  <input
                    v-model="vis.visit_name"
                    :aria-label="`Visit ${idx + 1} name`"
                    type="text"
                    class="form-control form-control-sm input-visit-name"
                  />
                </td>
                <td>
                  <select
                    v-model="vis.epoch_name"
                    :aria-label="`Visit ${idx + 1} epoch`"
                    class="form-control form-control-sm select-visit-epoch"
                  >
                    <option
                      v-for="ep in activeConfig.epochs"
                      :key="ep.name"
                      :value="ep.name"
                    >
                      {{ ep.name }}
                    </option>
                  </select>
                </td>
                <td>
                  <input
                    v-model.number="vis.target_day"
                    :aria-label="`Visit ${idx + 1} target day`"
                    type="number"
                    class="form-control form-control-sm input-visit-target-day"
                  />
                </td>
                <td>
                  <input
                    v-model.number="vis.window_lower_days"
                    :aria-label="`Visit ${idx + 1} window before (days)`"
                    type="number"
                    min="0"
                    class="form-control form-control-sm input-window-lower"
                  />
                </td>
                <td>
                  <input
                    v-model.number="vis.window_upper_days"
                    :aria-label="`Visit ${idx + 1} window after (days)`"
                    type="number"
                    min="0"
                    class="form-control form-control-sm input-window-upper"
                  />
                </td>
                <td class="text-center">
                  <input
                    v-model="vis.is_mandatory"
                    :aria-label="`Visit ${idx + 1} mandatory`"
                    type="checkbox"
                    class="checkbox-mandatory"
                  />
                </td>
                <td>
                  <ClinicalIconButton
                    class="btn btn-danger btn-sm btn-remove-visit"
                    :disabled="activeConfig.visits.length <= 1"
                    :label="`Remove planned visit ${vis.visit_name || idx + 1}`"
                    @click="removeVisit(idx)"
                  >
                    🗑️
                  </ClinicalIconButton>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Real-Time Graph & USDM Entity Summary Box -->
      <div class="usdm-live-summary-card">
        <div class="summary-meta-row">
          <div class="summary-pill">
            <strong>Target Participants:</strong>
            <span>{{ totalSampleSize }} subjects</span>
          </div>
          <div class="summary-pill">
            <strong>Cohorts:</strong>
            <span>{{ activeConfig.arms.length }} arms</span>
          </div>
          <div class="summary-pill">
            <strong>Epochs:</strong>
            <span>{{ activeConfig.epochs.length }} phases</span>
          </div>
          <div class="summary-pill">
            <strong>Encounters:</strong>
            <span>{{ activeConfig.visits.length }} visits</span>
          </div>
          <div class="summary-pill">
            <strong>SoA Procedures:</strong>
            <span>{{ activeConfig.activities.length }} activities</span>
          </div>
        </div>
      </div>

      <!-- Step Action Bar -->
      <div class="wizard-action-bar">
        <button
          type="button"
          class="btn btn-secondary btn-reset-wizard"
          @click="resetActiveTemplate"
        >
          🔄 Reset Template
        </button>
        <button
          type="button"
          class="btn btn-primary btn-synthesize-protocol"
          @click="synthesizeProtocol"
        >
          ⚡ Synthesize USDM Protocol Graph ➜
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalIconButton } from "ui";
import {
  THERAPEUTIC_AREA_TEMPLATES,
  type TherapeuticAreaTemplate,
  type StudyArm,
  type StudyEpoch,
  type PlannedVisit,
} from "../../data/therapeuticAreaTemplates";
import { useOnboardingStore } from "../../stores/onboarding";
import { useToastStore } from "../../stores/toasts.js";

const emit = defineEmits<{
  (e: "synthesize", payload: Record<string, any>): void;
}>();

const onboardingStore = useOnboardingStore();
const toastStore = useToastStore();

const STORAGE_KEY = "cadence_ta_wizard_state";

const storageError = ref<string | null>(null);
const isStorageAvailable = ref<boolean>(true);

const templateList = computed(() => Object.values(THERAPEUTIC_AREA_TEMPLATES));

const selectedTemplateId = ref<string>("oncology");
const configTab = ref<"arms" | "epochs" | "visits">("arms");

// Helper to deep clone a template
function cloneTemplate(template: TherapeuticAreaTemplate) {
  return JSON.parse(JSON.stringify(template));
}

// Load initial configuration state from localStorage if available
function loadSavedState(): { templateId: string; config: any } | null {
  if (typeof window === "undefined") {
    isStorageAvailable.value = false;
    return null;
  }
  try {
    const storage = window.localStorage;
    if (!storage) {
      isStorageAvailable.value = false;
      return null;
    }
    const raw = storage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed && parsed.templateId && parsed.config) {
        isStorageAvailable.value = true;
        return parsed;
      }
    }
  } catch (e: any) {
    console.warn("Failed to load TA wizard state from localStorage", e);
    isStorageAvailable.value = false;
    const detail =
      e?.message || "Storage read access failed or corrupted data.";
    storageError.value = `Failed to load saved state: ${detail}`;
    toastStore.push({
      tone: "warning",
      title: "Saved State Load Failure",
      message:
        "Could not restore saved protocol design from local storage. Default template loaded.",
    });
  }
  return null;
}

const saved = loadSavedState();
if (saved) {
  selectedTemplateId.value = saved.templateId;
}

const activeConfig = ref<any>(
  saved
    ? saved.config
    : cloneTemplate(THERAPEUTIC_AREA_TEMPLATES[selectedTemplateId.value])
);

// Persist active configuration to localStorage
function saveState(): boolean {
  if (typeof window === "undefined") {
    isStorageAvailable.value = false;
    storageError.value = "Browser environment unavailable for local storage.";
    return false;
  }
  if (!activeConfig.value) return false;

  try {
    const storage = window.localStorage;
    if (!storage) {
      isStorageAvailable.value = false;
      storageError.value =
        "Local storage is disabled or restricted in this browser session.";
      return false;
    }
    storage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        templateId: selectedTemplateId.value,
        config: activeConfig.value,
      })
    );
    isStorageAvailable.value = true;
    if (storageError.value) {
      storageError.value = null;
    }
    return true;
  } catch (e: any) {
    console.warn("Failed to persist TA wizard state to localStorage", e);
    isStorageAvailable.value = false;
    const detail =
      e?.message || "Storage quota exceeded or write permissions restricted.";
    const newMsg = `Auto-save paused: ${detail}`;

    if (!storageError.value) {
      toastStore.push({
        tone: "warning",
        title: "Auto-Save Failed",
        message:
          "Failed to persist protocol design to local storage. Your changes remain preserved in memory.",
      });
    }
    storageError.value = newMsg;
    return false;
  }
}

function dismissStorageError() {
  storageError.value = null;
}

// Watch activeConfig and selectedTemplateId for changes to persist
watch(
  () => [selectedTemplateId.value, activeConfig.value],
  () => {
    saveState();
  },
  { deep: true }
);

function selectTemplate(templateId: string) {
  if (!THERAPEUTIC_AREA_TEMPLATES[templateId]) return;
  selectedTemplateId.value = templateId;
  const baseTpl = THERAPEUTIC_AREA_TEMPLATES[templateId];
  activeConfig.value = cloneTemplate(baseTpl);
  activeConfig.value.randomizationRatio = baseTpl.defaultRandomizationRatio;
}

function resetActiveTemplate() {
  selectTemplate(selectedTemplateId.value);
}

const totalSampleSize = computed(() => {
  if (!activeConfig.value || !activeConfig.value.arms) return 0;
  return activeConfig.value.arms.reduce(
    (sum: number, arm: StudyArm) => sum + (Number(arm.target_sample_size) || 0),
    0
  );
});

function addArm() {
  if (!activeConfig.value) return;
  const newArmIndex = activeConfig.value.arms.length + 1;
  const newArm: StudyArm = {
    id: `arm_${Date.now()}`,
    name: `Exploratory Arm ${newArmIndex}`,
    arm_type: "EXPERIMENTAL",
    description: "Exploratory cohort arm",
    target_sample_size: 50,
  };
  activeConfig.value.arms.push(newArm);
}

function removeArm(index: number) {
  if (!activeConfig.value || activeConfig.value.arms.length <= 1) return;
  activeConfig.value.arms.splice(index, 1);
}

function addEpoch() {
  if (!activeConfig.value) return;
  const newIndex = activeConfig.value.epochs.length + 1;
  const newEpoch: StudyEpoch = {
    id: `ep_${Date.now()}`,
    name: `Exploratory Epoch ${newIndex}`,
    epoch_type: "TREATMENT",
    sequence_index: newIndex,
    duration_weeks: 4,
  };
  activeConfig.value.epochs.push(newEpoch);
}

function removeEpoch(index: number) {
  if (!activeConfig.value || activeConfig.value.epochs.length <= 1) return;
  activeConfig.value.epochs.splice(index, 1);
}

function addVisit() {
  if (!activeConfig.value) return;
  const lastVisit =
    activeConfig.value.visits[activeConfig.value.visits.length - 1];
  const targetDay = lastVisit ? lastVisit.target_day + 14 : 14;
  const defaultEpoch = activeConfig.value.epochs[0]?.name || "Treatment Epoch";
  const newVisit: PlannedVisit = {
    id: `vis_${Date.now()}`,
    visit_name: `Exploratory Visit (Day ${targetDay})`,
    epoch_name: defaultEpoch,
    target_day: targetDay,
    window_lower_days: 2,
    window_upper_days: 2,
    is_mandatory: false,
  };
  activeConfig.value.visits.push(newVisit);
}

function removeVisit(index: number) {
  if (!activeConfig.value || activeConfig.value.visits.length <= 1) return;
  activeConfig.value.visits.splice(index, 1);
}

function synthesizeProtocol() {
  if (!activeConfig.value) return;

  const synthesizedUSDM = {
    study_title: activeConfig.value.study_title || "Synthesized USDM Study",
    protocol_id: activeConfig.value.protocol_id || "CDNC-2026-USDM",
    phase: activeConfig.value.phase || "PHASE_II",
    therapeutic_area:
      activeConfig.value.category || activeConfig.value.title || "Oncology",
    confidence_score: activeConfig.value.confidence_score || 1.0,
    randomization_ratio: activeConfig.value.randomizationRatio || "1:1",
    arms: JSON.parse(JSON.stringify(activeConfig.value.arms)),
    epochs: JSON.parse(JSON.stringify(activeConfig.value.epochs)),
    visits: JSON.parse(JSON.stringify(activeConfig.value.visits)),
    activities: JSON.parse(JSON.stringify(activeConfig.value.activities)),
    criteria: JSON.parse(JSON.stringify(activeConfig.value.criteria)),
  };

  // Mark task_protocol as completed in onboarding store
  if (onboardingStore && typeof onboardingStore.completeTask === "function") {
    onboardingStore.completeTask("task_protocol");
  }

  emit("synthesize", synthesizedUSDM);
}
</script>

<style scoped>
.therapeutic-area-wizard {
  padding: 24px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
}

.wizard-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 24px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
}

.storage-warning-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 6px;
  padding: 12px 16px;
  margin-bottom: 20px;
  color: var(--semantic-color-warning-dark);
  font-size: 0.875rem;
}

.banner-content {
  display: flex;
  align-items: center;
  gap: 10px;
}

.banner-icon {
  font-size: 1.2rem;
}

.banner-text {
  line-height: 1.4;
}

.banner-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.btn-retry-save {
  background-color: var(--semantic-color-warning-bg-badge);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning-dark);
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
  transition: all 0.2s ease;
}

.btn-retry-save:hover {
  background-color: var(--semantic-color-warning-bg-badge);
}

.btn-dismiss-warning {
  background: none;
  border: none;
  color: var(--semantic-color-warning-dark);
  font-weight: 600;
  cursor: pointer;
  padding: 4px 8px;
  font-size: 0.85rem;
  text-decoration: underline;
}

.btn-dismiss-warning:hover {
  color: var(--semantic-color-warning-dark);
}

.header-title-box h3 {
  margin: 0 0 4px 0;
  font-weight: 700;
  color: var(--primary);
  font-size: 1.25rem;
}

.header-title-box p {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.875rem;
}

.wizard-badges {
  display: flex;
  gap: 8px;
}

.badge-usdm {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  padding: 4px 10px;
  border-radius: 4px;
  font-weight: 600;
  font-size: 0.75rem;
}

.badge-gxp {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
  padding: 4px 10px;
  border-radius: 4px;
  font-weight: 600;
  font-size: 0.75rem;
}

.section-subtitle {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-body);
  margin: 0 0 16px 0;
}

.ta-cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
  margin-bottom: 28px;
}

.ta-card {
  background-color: var(--semantic-color-surface-muted);
  border: 2px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  flex-direction: column;
}

.ta-card:hover {
  border-color: var(--semantic-color-border-control);
  transform: translateY(-2px);
}

.ta-card.selected {
  border-color: var(--semantic-color-interactive-primary);
  background-color: var(--semantic-color-info-bg);
  box-shadow: 0 4px 12px rgba(37, 99, 235, 0.12);
}

.ta-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.ta-icon {
  font-size: 1.75rem;
}

.badge-category {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: var(--font-size-2xs);
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 600;
}

.ta-title {
  margin: 0 0 8px 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.ta-desc {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 12px 0;
  line-height: 1.4;
  flex: 1;
}

.ta-card-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.meta-tag {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
}

.ta-customization-section {
  border-top: 1px dashed var(--semantic-color-border-control);
  padding-top: 24px;
}

.form-grid-three {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.form-group.wide {
  margin-bottom: 24px;
}

.form-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 6px;
}

.form-control {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.875rem;
  color: var(--semantic-color-text-dark);
  background-color: var(--semantic-color-surface);
}

.form-control-sm {
  padding: 4px 8px;
  font-size: 0.8rem;
}

.customization-tabs-nav {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
  border-bottom: 2px solid var(--semantic-color-border);
  padding-bottom: 8px;
}

.btn-tab {
  background: none;
  border: none;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.btn-tab.active {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
}

.tab-pane-content {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 16px;
  margin-bottom: 20px;
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.pane-header h5 {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.table th {
  text-align: left;
  padding: 8px;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  border-bottom: 2px solid var(--semantic-color-border);
}

.table td {
  padding: 6px 8px;
  vertical-align: middle;
}

.seq-num {
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-align: center;
}

.text-center {
  text-align: center;
}

.btn-danger {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error);
  cursor: pointer;
  border-radius: 4px;
}

.btn-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg);
}

.btn-danger:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.usdm-live-summary-card {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  border-radius: 6px;
  padding: 12px 16px;
  margin-bottom: 24px;
}

.summary-meta-row {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

.summary-pill {
  font-size: 0.8rem;
  color: var(--semantic-color-success-dark);
}

.summary-pill strong {
  margin-right: 4px;
}

.wizard-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.btn-synthesize-protocol {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  font-weight: 700;
  padding: 10px 24px;
  font-size: 0.95rem;
  border-radius: 6px;
  cursor: pointer;
}

.btn-synthesize-protocol:hover {
  background-color: var(--semantic-color-success);
}

.btn-reset-wizard {
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
}
</style>
