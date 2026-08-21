<template>
  <div
    v-if="isOpen"
    class="modal-backdrop"
    @click.self="close"
  >
    <div
      class="modal-dialog ticket-create-modal"
      role="dialog"
      aria-modal="true"
      aria-labelledby="create-modal-title"
    >
      <div class="modal-header">
        <div class="modal-header-icon">
          🎫
        </div>
        <div>
          <h3
            id="create-modal-title"
            class="modal-title"
          >
            Log Clinical Issue / Ticket
          </h3>
          <p class="modal-subtitle">
            Register a protocol deviation, site query, safety event, or
            operational issue
          </p>
        </div>
        <button
          type="button"
          class="btn-close"
          aria-label="Close"
          @click="close"
        >
          ✕
        </button>
      </div>

      <div class="modal-body">
        <div class="form-row">
          <div class="form-group flex-2">
            <label
              for="ticket-title"
              class="form-label"
            >Issue Title <span class="text-danger">*</span></label>
            <input
              id="ticket-title"
              v-model="form.title"
              type="text"
              class="form-control"
              placeholder="e.g. Temperature Excursion in IP Storage Room 102"
              required
            >
          </div>
          <div class="form-group flex-1">
            <label
              for="ticket-category"
              class="form-label"
            >ICH GCP Category <span class="text-danger">*</span></label>
            <select
              id="ticket-category"
              v-model="form.category"
              class="form-control"
            >
              <option value="PROTOCOL_DEVIATION">
                Protocol Deviation
              </option>
              <option value="DATA_QUERY">
                Data Query / Discrepancy
              </option>
              <option value="SAFETY_ADVERSE_EVENT">
                Safety &amp; SAE Event
              </option>
              <option value="SUPPLY_EXCURSION">
                Supply &amp; Temp Excursion
              </option>
              <option value="SITE_OPERATIONS">
                Site Operations
              </option>
              <option value="MONITORING_FINDING">
                Monitoring Finding (MVR)
              </option>
              <option value="TECHNICAL_SYSTEM">
                Technical / System Bug
              </option>
              <option value="ACCESS_CONTROL">
                Access &amp; Role Provisioning
              </option>
              <option value="REGULATORY_QUERY">
                Regulatory &amp; Ethics Query
              </option>
              <option value="SYSTEM_SUPPORT">
                System Support
              </option>
            </select>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <label
              for="ticket-severity"
              class="form-label"
            >GxP Severity Rating <span class="text-danger">*</span></label>
            <select
              id="ticket-severity"
              v-model="form.gxp_severity"
              class="form-control"
            >
              <option value="MINOR">
                Minor (No safety or data integrity impact)
              </option>
              <option value="MAJOR">
                Major (Potential impact on protocol/endpoint)
              </option>
              <option value="CRITICAL">
                Critical (Immediate safety, integrity, or compliance breach)
              </option>
            </select>
          </div>
          <div class="form-group flex-1">
            <label
              for="ticket-priority"
              class="form-label"
            >Priority</label>
            <select
              id="ticket-priority"
              v-model="form.priority"
              class="form-control"
            >
              <option value="LOW">
                Low
              </option>
              <option value="MEDIUM">
                Medium
              </option>
              <option value="HIGH">
                High
              </option>
              <option value="CRITICAL">
                Critical
              </option>
            </select>
          </div>
        </div>

        <div class="form-group">
          <label
            for="ticket-desc"
            class="form-label"
          >Clinical Narrative &amp; Description
            <span class="text-danger">*</span></label>
          <textarea
            id="ticket-desc"
            v-model="form.description"
            rows="3"
            class="form-control"
            placeholder="Detailed description of the issue, circumstances, and immediate containment..."
            required
          />
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <label
              for="ticket-study"
              class="form-label"
            >Study ID</label>
            <input
              id="ticket-study"
              v-model="form.study_id"
              type="text"
              class="form-control"
              placeholder="e.g. STUDY-ONC-202"
            >
          </div>
          <div class="form-group flex-1">
            <label
              for="ticket-site"
              class="form-label"
            >Site ID</label>
            <input
              id="ticket-site"
              v-model="form.site_id"
              type="text"
              class="form-control"
              placeholder="e.g. SITE-101"
            >
          </div>
          <div class="form-group flex-1">
            <label
              for="ticket-subject"
              class="form-label"
            >Subject ID</label>
            <input
              id="ticket-subject"
              v-model="form.subject_id"
              type="text"
              class="form-control"
              placeholder="e.g. SUBJ-8812"
            >
          </div>
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <label
              for="ticket-entity-type"
              class="form-label"
            >Linked Entity Type</label>
            <select
              id="ticket-entity-type"
              v-model="form.entity_type"
              class="form-control"
            >
              <option value="">
                None / General
              </option>
              <option value="SUBJECT">
                Clinical Subject
              </option>
              <option value="CRF_FORM">
                eCRF Form Submission
              </option>
              <option value="SAE_CASE">
                Safety SAE Case
              </option>
              <option value="CAPA_ACTION">
                Quality CAPA Record
              </option>
              <option value="TMF_DOCUMENT">
                eTMF Document
              </option>
              <option value="MVR_VISIT">
                Monitoring Visit Finding
              </option>
              <option value="SUPPLY_KIT">
                Investigational Product Kit
              </option>
            </select>
          </div>
          <div class="form-group flex-2">
            <label
              for="ticket-entity-id"
              class="form-label"
            >Linked Entity ID</label>
            <input
              id="ticket-entity-id"
              v-model="form.entity_id"
              type="text"
              class="form-control"
              placeholder="e.g. FORM-AE-001 or CAPA-2026-09"
            >
          </div>
        </div>

        <div class="form-group">
          <label
            for="ticket-reason"
            class="form-label"
          >GxP Justification / Reason for Creation
            <span class="text-danger">*</span></label>
          <input
            id="ticket-reason"
            v-model="form.reason_for_change"
            type="text"
            class="form-control"
            placeholder="Mandatory 21 CFR Part 11 audit reason..."
            required
          >
        </div>

        <div
          v-if="errorMessage"
          class="error-banner"
        >
          ⚠️ {{ errorMessage }}
        </div>
      </div>

      <div class="modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="loading"
          @click="close"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="loading || !isFormValid"
          @click="handleSubmit"
        >
          <span
            v-if="loading"
            class="spinner"
          />
          <span v-else>🚀 Log Issue</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, computed } from "vue";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  defaultStudyId: {
    type: String,
    default: "",
  },
  defaultSiteId: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["close", "created"]);

const loading = ref(false);
const errorMessage = ref("");

const form = reactive({
  title: "",
  description: "",
  category: "PROTOCOL_DEVIATION",
  gxp_severity: "MINOR",
  priority: "MEDIUM",
  study_id: props.defaultStudyId || "STUDY-ONC-202",
  site_id: props.defaultSiteId || "SITE-101",
  subject_id: "",
  entity_type: "",
  entity_id: "",
  reason_for_change: "Initial clinical issue logged",
});

const isFormValid = computed(() => {
  return (
    form.title.trim().length > 0 &&
    form.description.trim().length > 0 &&
    form.reason_for_change.trim().length > 0
  );
});

const close = () => {
  errorMessage.value = "";
  emit("close");
};

const handleSubmit = async () => {
  if (!isFormValid.value) {
    errorMessage.value = "Please complete all required fields.";
    return;
  }
  loading.value = true;
  errorMessage.value = "";
  try {
    const payload = {
      title: form.title,
      description: form.description,
      category: form.category,
      gxp_severity: form.gxp_severity,
      priority: form.priority,
      study_id: form.study_id || null,
      site_id: form.site_id || null,
      subject_id: form.subject_id || null,
      entity_type: form.entity_type || null,
      entity_id: form.entity_id || null,
      reason_for_change: form.reason_for_change,
    };
    emit("created", payload);
    close();
  } catch (err) {
    errorMessage.value = err.message || "Failed to create ticket.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
.modal-body {
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  overflow-y: auto;
}

.form-row {
  display: flex;
  gap: 12px;
}

.flex-1 {
  flex: 1;
}

.flex-2 {
  flex: 2;
}

.text-danger {
  color: #dc2626;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: #334155;
}

.form-control {
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.875rem;
  color: #1e293b;
  background: #ffffff;
  outline: none;
  transition: border-color 0.15s;
}

.form-control:focus {
  border-color: #2563eb;
  box-shadow: 0 0 0 2px rgba(37, 99, 235, 0.15);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  background: #f8fafc;
  border-top: 1px solid #e2e8f0;
}

.spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid #ffffff;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
