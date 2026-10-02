<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div
    v-if="isOpen"
    class="drawer-backdrop"
    aria-modal="true"
    role="dialog"
    aria-labelledby="bulk-drawer-title"
    @click.self="closeDrawer"
  >
    <div class="drawer-panel">
      <!-- Header -->
      <div class="drawer-header">
        <div class="header-content">
          <h3 id="bulk-drawer-title" class="drawer-title">
            🏷️ Bulk Edit Document Metadata
          </h3>
          <p class="drawer-subtitle">
            Update DIA TMF Reference Model taxonomy and site tags across
            selected documents.
          </p>
        </div>
        <button
          class="btn-close-drawer"
          aria-label="Close drawer"
          @click="closeDrawer"
        >
          ×
        </button>
      </div>

      <!-- Selected Document Preview Summary -->
      <div class="preview-summary-box">
        <div class="summary-stat">
          <span class="stat-value">{{ totalCount }}</span>
          <span class="stat-label">Total Selected</span>
        </div>
        <div class="summary-stat active-stat">
          <span class="stat-value">{{ editableCount }}</span>
          <span class="stat-label">Editable Files</span>
        </div>
        <div v-if="holdCount > 0" class="summary-stat hold-stat">
          <span class="stat-value">{{ holdCount }}</span>
          <span class="stat-label">Retention Hold (Excluded)</span>
        </div>
      </div>

      <div v-if="holdCount > 0" class="alert alert-warning hold-warning">
        ⚠️ {{ holdCount }} document(s) on retention hold will be preserved and
        excluded from bulk edits.
      </div>

      <!-- Drawer Form -->
      <form class="drawer-body" @submit.prevent="handleSubmit">
        <p class="form-instruction">
          Check the fields you wish to modify. Non-selected fields will preserve
          their existing values.
        </p>

        <!-- Field 1: Zone -->
        <div class="field-group" :class="{ 'field-active': editZone }">
          <div class="field-toggle-header">
            <label class="toggle-label">
              <input
                v-model="editZone"
                type="checkbox"
                class="field-checkbox"
                aria-label="Update Zone"
              />
              <span class="field-title">Zone</span>
            </label>
            <span class="field-badge" v-if="editZone">Modifying</span>
          </div>

          <select
            v-model="selectedZone"
            :disabled="!editZone"
            class="form-select"
            aria-label="Select TMF Zone"
          >
            <option v-for="z in zonesList" :key="z.code" :value="z.code">
              Zone {{ z.code }}: {{ z.name }}
            </option>
          </select>
        </div>

        <!-- Field 2: Section -->
        <div class="field-group" :class="{ 'field-active': editSection }">
          <div class="field-toggle-header">
            <label class="toggle-label">
              <input
                v-model="editSection"
                type="checkbox"
                class="field-checkbox"
                aria-label="Update Section"
              />
              <span class="field-title">Section</span>
            </label>
            <span class="field-badge" v-if="editSection">Modifying</span>
          </div>

          <select
            v-model="selectedSection"
            :disabled="!editSection"
            class="form-select"
            aria-label="Select TMF Section"
          >
            <option v-for="s in filteredSections" :key="s.code" :value="s.code">
              {{ s.code }} - {{ s.name }}
            </option>
          </select>
        </div>

        <!-- Field 3: Artifact -->
        <div class="field-group" :class="{ 'field-active': editArtifact }">
          <div class="field-toggle-header">
            <label class="toggle-label">
              <input
                v-model="editArtifact"
                type="checkbox"
                class="field-checkbox"
                aria-label="Update Artifact"
              />
              <span class="field-title">Artifact</span>
            </label>
            <span class="field-badge" v-if="editArtifact">Modifying</span>
          </div>

          <select
            v-model="selectedArtifactCode"
            :disabled="!editArtifact"
            class="form-select"
            aria-label="Select TMF Artifact"
            @change="onArtifactChange"
          >
            <option v-for="a in artifactsList" :key="a.code" :value="a.code">
              {{ a.code }} - {{ a.name }}
            </option>
          </select>
        </div>

        <!-- Field 4: Site -->
        <div class="field-group" :class="{ 'field-active': editSite }">
          <div class="field-toggle-header">
            <label class="toggle-label">
              <input
                v-model="editSite"
                type="checkbox"
                class="field-checkbox"
                aria-label="Update Site"
              />
              <span class="field-title">Site Assignment</span>
            </label>
            <span class="field-badge" v-if="editSite">Modifying</span>
          </div>

          <select
            v-model="selectedSiteId"
            :disabled="!editSite"
            class="form-select"
            aria-label="Select Site"
          >
            <option :value="null">Global (Unassigned Site)</option>
            <option v-for="s in sitesList" :key="s.id" :value="s.id">
              {{ s.id }} - {{ s.name }}
            </option>
          </select>
        </div>

        <!-- Field 5: Milestone -->
        <div class="field-group" :class="{ 'field-active': editMilestone }">
          <div class="field-toggle-header">
            <label class="toggle-label">
              <input
                v-model="editMilestone"
                type="checkbox"
                class="field-checkbox"
                aria-label="Update Milestone"
              />
              <span class="field-title">Study Milestone</span>
            </label>
            <span class="field-badge" v-if="editMilestone">Modifying</span>
          </div>

          <select
            v-model="selectedMilestone"
            :disabled="!editMilestone"
            class="form-select"
            aria-label="Select Milestone"
          >
            <option v-for="m in milestonesList" :key="m.value" :value="m.value">
              {{ m.label }}
            </option>
          </select>
        </div>

        <!-- 21 CFR Part 11 Change Reason -->
        <div class="reason-box">
          <label for="bulk-change-reason" class="reason-label">
            Reason for Change <span class="required-star">*</span> (21 CFR Part
            11)
          </label>
          <textarea
            id="bulk-change-reason"
            v-model="reasonForChange"
            rows="3"
            class="reason-textarea"
            placeholder="Provide a detailed audit justification (at least 10 characters)..."
            aria-required="true"
          ></textarea>
          <div class="reason-footer">
            <span
              class="char-counter"
              :class="{ 'char-valid': reasonForChange.trim().length >= 10 }"
            >
              {{ reasonForChange.trim().length }} / 10 characters minimum
            </span>
          </div>
        </div>

        <!-- Validation Error Message -->
        <div v-if="validationError" class="alert alert-danger validation-alert">
          🚫 {{ validationError }}
        </div>

        <!-- Footer Actions -->
        <div class="drawer-footer">
          <button
            type="button"
            class="btn btn-outline-secondary btn-cancel"
            @click="closeDrawer"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="btn btn-primary btn-save"
            :disabled="!!validationError || isSubmitting || editableCount === 0"
          >
            <span v-if="isSubmitting">Saving...</span>
            <span v-else>Apply Bulk Updates ({{ editableCount }})</span>
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useFileshareStore, DEFAULT_STUDIES } from "../../stores/fileshare";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  selectedFiles: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["close", "submitted"]);

const fileshareStore = useFileshareStore();

// Counts
const totalCount = computed(() => props.selectedFiles.length);
const holdCount = computed(
  () =>
    props.selectedFiles.filter((f) => f.is_on_hold || f.status === "ON_HOLD")
      .length
);
const editableCount = computed(() => totalCount.value - holdCount.value);

// Field Enable Toggles
const editZone = ref(false);
const editSection = ref(false);
const editArtifact = ref(false);
const editSite = ref(false);
const editMilestone = ref(false);

// Field Inputs
const selectedZone = ref(1);
const selectedSection = ref("01.01");
const selectedArtifactCode = ref("01.01.01");
const selectedArtifactType = ref("Clinical Trial Protocol");
const selectedSiteId = ref("SITE-101");
const selectedMilestone = ref("SITE_INITIATION");
const reasonForChange = ref("");

const isSubmitting = ref(false);

// Lists
const zonesList = computed(() => [
  { code: 1, name: "Trial Management" },
  { code: 2, name: "Central Trial Documents" },
  { code: 3, name: "Regulatory" },
  { code: 4, name: "IRB/IEC & other Approvals" },
  { code: 5, name: "Site Management" },
  { code: 6, name: "IP & Trial Supplies" },
  { code: 7, name: "Safety Reporting" },
  { code: 8, name: "Centralized & Local Testing" },
  { code: 9, name: "Third Parties" },
  { code: 10, name: "Data Management" },
  { code: 11, name: "Statistics" },
]);

const sectionsList = [
  { code: "01.01", name: "Trial Oversight & Protocol", zone: 1 },
  { code: "01.02", name: "Trial Committee & Governance", zone: 1 },
  { code: "02.01", name: "Investigator Brochure", zone: 2 },
  { code: "02.02", name: "Core Informed Consent", zone: 2 },
  { code: "03.01", name: "Competent Authority", zone: 3 },
  { code: "03.02", name: "IRB / IEC Governance", zone: 3 },
  { code: "04.01", name: "Feasibility & Selection", zone: 4 },
  { code: "04.02", name: "Site Initiation & Training", zone: 4 },
  { code: "05.01", name: "Subject Eligibility", zone: 5 },
  { code: "05.02", name: "eCRF & Source Records", zone: 5 },
];

const filteredSections = computed(() => {
  if (editZone.value) {
    const zNum = Number(selectedZone.value);
    const matched = sectionsList.filter((s) => s.zone === zNum);
    return matched.length > 0 ? matched : sectionsList;
  }
  return sectionsList;
});

const artifactsList = [
  { code: "01.01.01", name: "Clinical Trial Protocol" },
  { code: "01.01.02", name: "Protocol Amendment" },
  { code: "02.01.01", name: "Investigator's Brochure" },
  { code: "02.02.01", name: "Master Informed Consent Form" },
  { code: "04.02.01", name: "IRB/IEC Approval Notification" },
  { code: "04.02.02", name: "Delegation of Authority Log (DOA)" },
  { code: "05.02.01", name: "FDA Form 1572" },
  { code: "05.02.02", name: "Financial Disclosure" },
  { code: "05.02.03", name: "Investigator CV" },
];

function onArtifactChange() {
  const found = artifactsList.find(
    (a) => a.code === selectedArtifactCode.value
  );
  if (found) {
    selectedArtifactType.value = found.name;
  }
}

const sitesList = computed(() => {
  const sites = [];
  DEFAULT_STUDIES.forEach((st) => {
    st.sites.forEach((site) => {
      if (!sites.some((s) => s.id === site.id)) {
        sites.push({ id: site.id, name: site.name });
      }
    });
  });
  return sites;
});

const milestonesList = [
  { value: "SITE_INITIATION", label: "Site Initiation" },
  { value: "STUDY_STARTUP", label: "Study Startup" },
  { value: "INTERIM_ANALYSIS", label: "Interim Analysis" },
  { value: "STUDY_CLOSEOUT", label: "Study Closeout" },
  { value: "ANNUAL_RENEWAL", label: "Annual Renewal" },
];

// Validation
const validationError = computed(() => {
  if (editableCount.value === 0) {
    return "All selected files are on retention hold and cannot be modified.";
  }
  if (
    !editZone.value &&
    !editSection.value &&
    !editArtifact.value &&
    !editSite.value &&
    !editMilestone.value
  ) {
    return "Please select at least one metadata field to update.";
  }
  if (reasonForChange.value.trim().length < 10) {
    return "Reason for change must be at least 10 characters long for 21 CFR Part 11 compliance.";
  }
  return null;
});

// Reset form when drawer opens
watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      editZone.value = false;
      editSection.value = false;
      editArtifact.value = false;
      editSite.value = false;
      editMilestone.value = false;
      reasonForChange.value = "";
      isSubmitting.value = false;
    }
  }
);

function closeDrawer() {
  emit("close");
}

async function handleSubmit() {
  if (validationError.value || isSubmitting.value) return;

  isSubmitting.value = true;
  try {
    const editableDocIds = props.selectedFiles
      .filter((f) => !f.is_on_hold && f.status !== "ON_HOLD")
      .map((f) => f.id);

    const payload = {
      documentIds: editableDocIds,
      reasonForChange: reasonForChange.value.trim(),
      zone: editZone.value ? Number(selectedZone.value) : undefined,
      section: editSection.value ? selectedSection.value : undefined,
      artifactCode: editArtifact.value ? selectedArtifactCode.value : undefined,
      artifactType: editArtifact.value ? selectedArtifactType.value : undefined,
      siteId: editSite.value ? selectedSiteId.value : undefined,
      milestone: editMilestone.value ? selectedMilestone.value : undefined,
    };

    await fileshareStore.bulkUpdateMetadata(payload);
    emit("submitted", payload);
    closeDrawer();
  } catch (err) {
    console.error("Bulk metadata update failed:", err);
  } finally {
    isSubmitting.value = false;
  }
}
</script>

<style scoped>
.drawer-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 1050;
  display: flex;
  justify-content: flex-end;
}

.drawer-panel {
  width: 520px;
  max-width: 100vw;
  height: 100vh;
  background-color: #ffffff;
  box-shadow: -4px 0 20px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  animation: slideIn 0.25s ease-out;
}

@keyframes slideIn {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  padding: 20px 24px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  background-color: #f8fafc;
}

.drawer-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: #0f172a;
}

.drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: 0.8rem;
  color: #64748b;
}

.btn-close-drawer {
  background: none;
  border: none;
  font-size: 1.6rem;
  line-height: 1;
  color: #94a3b8;
  cursor: pointer;
  padding: 0 4px;
}

.btn-close-drawer:hover {
  color: #0f172a;
}

.preview-summary-box {
  display: flex;
  padding: 12px 24px;
  background-color: #f1f5f9;
  border-bottom: 1px solid #e2e8f0;
  gap: 16px;
}

.summary-stat {
  display: flex;
  flex-direction: column;
  align-items: center;
  flex: 1;
  background-color: #ffffff;
  padding: 8px;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.stat-value {
  font-size: 1.1rem;
  font-weight: 700;
  color: #0f172a;
}

.active-stat .stat-value {
  color: #0284c7;
}

.hold-stat .stat-value {
  color: #d97706;
}

.stat-label {
  font-size: 0.7rem;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-align: center;
}

.hold-warning {
  margin: 12px 24px 0 24px;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  background-color: #fef3c7;
  color: #92400e;
  border: 1px solid #fde68a;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-instruction {
  font-size: 0.82rem;
  color: #475569;
  margin: 0;
}

.field-group {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px;
  background-color: #f8fafc;
  transition: all 0.2s;
}

.field-group.field-active {
  border-color: #0284c7;
  background-color: #ffffff;
  box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.08);
}

.field-toggle-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.toggle-label {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  user-select: none;
}

.field-checkbox {
  width: 16px;
  height: 16px;
  accent-color: #0284c7;
  cursor: pointer;
}

.field-title {
  font-size: 0.88rem;
  font-weight: 600;
  color: #1e293b;
}

.field-badge {
  font-size: 0.7rem;
  font-weight: 600;
  background-color: #e0f2fe;
  color: #0369a1;
  padding: 2px 8px;
  border-radius: 12px;
}

.form-select {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.85rem;
  background-color: #ffffff;
  color: #0f172a;
}

.form-select:disabled {
  background-color: #f1f5f9;
  color: #94a3b8;
  cursor: not-allowed;
}

.reason-box {
  margin-top: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.reason-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #0f172a;
}

.required-star {
  color: #ef4444;
}

.reason-textarea {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.85rem;
  resize: vertical;
  box-sizing: border-box;
}

.reason-textarea:focus {
  outline: none;
  border-color: #0284c7;
  box-shadow: 0 0 0 3px rgba(2, 132, 199, 0.12);
}

.reason-footer {
  display: flex;
  justify-content: flex-end;
}

.char-counter {
  font-size: 0.75rem;
  color: #ef4444;
}

.char-counter.char-valid {
  color: #16a34a;
}

.validation-alert {
  padding: 10px 14px;
  border-radius: 6px;
  font-size: 0.82rem;
  background-color: #fef2f2;
  color: #991b1b;
  border: 1px solid #fecaca;
}

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid #e2e8f0;
}

.btn {
  padding: 9px 16px;
  font-size: 0.85rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-primary {
  background-color: #0284c7;
  color: #ffffff;
  border: 1px solid #0284c7;
}

.btn-primary:hover:not(:disabled) {
  background-color: #0369a1;
}

.btn-primary:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-outline-secondary {
  background-color: #ffffff;
  color: #475569;
  border: 1px solid #cbd5e1;
}

.btn-outline-secondary:hover {
  background-color: #f8fafc;
  color: #0f172a;
}
</style>
