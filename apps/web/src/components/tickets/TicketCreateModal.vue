<template>
  <ClinicalModal
    :is-open="isOpen"
    :teleport="false"
    :show-footer="false"
    size="lg"
    dialog-class="ticket-create-modal"
    body-class="ticket-create-modal-body"
    aria-labelledby="create-modal-title"
    close-label="Close"
    @close="close"
  >
    <template #header>
      <div class="modal-header-title-wrap">
        <div class="modal-header-icon" aria-hidden="true">🎫</div>
        <div>
          <h3 id="create-modal-title" class="modal-title">
            Log Clinical Issue / Ticket
          </h3>
          <p class="modal-subtitle">
            Register a protocol deviation, site query, safety event, or
            operational issue
          </p>
        </div>
      </div>
    </template>

    <form
      id="ticket-create-form"
      class="ticket-create-form"
      novalidate
      @submit.prevent="handleSubmit"
    >
      <div class="modal-body-fields">
        <div class="form-row">
          <div class="form-group flex-2">
            <ClinicalInput
              id="ticket-title"
              v-model="form.title"
              label="Issue Title"
              label-class="form-label"
              required
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
              placeholder="e.g. Temperature Excursion in IP Storage Room 102"
            />
          </div>
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-category"
              v-model="form.category"
              as="select"
              label="ICH GCP Category"
              label-class="form-label"
              required
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
            >
              <option value="PROTOCOL_DEVIATION">Protocol Deviation</option>
              <option value="DATA_QUERY">Data Query / Discrepancy</option>
              <option value="SAFETY_ADVERSE_EVENT">
                Safety &amp; SAE Event
              </option>
              <option value="SUPPLY_EXCURSION">
                Supply &amp; Temp Excursion
              </option>
              <option value="SITE_OPERATIONS">Site Operations</option>
              <option value="MONITORING_FINDING">
                Monitoring Finding (MVR)
              </option>
              <option value="TECHNICAL_SYSTEM">Technical / System Bug</option>
              <option value="ACCESS_CONTROL">
                Access &amp; Role Provisioning
              </option>
              <option value="REGULATORY_QUERY">
                Regulatory &amp; Ethics Query
              </option>
              <option value="SYSTEM_SUPPORT">System Support</option>
            </ClinicalInput>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-severity"
              v-model="form.gxp_severity"
              as="select"
              label="GxP Severity Rating"
              label-class="form-label"
              required
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
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
            </ClinicalInput>
          </div>
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-priority"
              v-model="form.priority"
              as="select"
              label="Priority"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
            >
              <option value="LOW">Low</option>
              <option value="MEDIUM">Medium</option>
              <option value="HIGH">High</option>
              <option value="CRITICAL">Critical</option>
            </ClinicalInput>
          </div>
        </div>

        <div class="form-group">
          <ClinicalInput
            id="ticket-desc"
            v-model="form.description"
            as="textarea"
            :rows="3"
            label="Clinical Narrative & Description"
            label-class="form-label"
            required
            density="compact"
            :show-query-flag="false"
            control-class="form-control"
            placeholder="Detailed description of the issue, circumstances, and immediate containment..."
          />
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-study"
              v-model="form.study_id"
              label="Study ID"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
              placeholder="e.g. CADENCE-101"
            />
          </div>
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-site"
              v-model="form.site_id"
              label="Site ID"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
              placeholder="e.g. SITE-01"
            />
          </div>
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-subject"
              v-model="form.subject_id"
              label="Subject ID"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
              placeholder="e.g. SUBJ-8812"
            />
          </div>
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <ClinicalInput
              id="ticket-entity-type"
              v-model="form.entity_type"
              as="select"
              label="Linked Entity Type"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
            >
              <option value="">None / General</option>
              <option value="SUBJECT">Clinical Subject</option>
              <option value="CRF_FORM">eCRF Form Submission</option>
              <option value="SAE_CASE">Safety SAE Case</option>
              <option value="CAPA_ACTION">Quality CAPA Record</option>
              <option value="TMF_DOCUMENT">eTMF Document</option>
              <option value="MVR_VISIT">Monitoring Visit Finding</option>
              <option value="SUPPLY_KIT">Investigational Product Kit</option>
            </ClinicalInput>
          </div>
          <div class="form-group flex-2">
            <ClinicalInput
              id="ticket-entity-id"
              v-model="form.entity_id"
              label="Linked Entity ID"
              label-class="form-label"
              density="compact"
              :show-query-flag="false"
              control-class="form-control"
              placeholder="e.g. FORM-AE-001 or CAPA-2026-09"
            />
          </div>
        </div>

        <div class="form-group">
          <ClinicalInput
            id="ticket-reason"
            v-model="form.reason_for_change"
            label="GxP Justification / Reason for Creation"
            label-class="form-label"
            required
            density="compact"
            :show-query-flag="false"
            control-class="form-control"
            placeholder="Mandatory 21 CFR Part 11 audit reason..."
          />
        </div>

        <div
          v-if="errorMessage"
          id="ticket-escalation-refusal"
          class="error-banner"
          role="alert"
        >
          {{ errorMessage }}
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
          id="btn-submit-ticket"
          type="submit"
          class="btn btn-primary"
          :disabled="loading || !isFormValid"
          @click.prevent="handleSubmit"
        >
          <span v-if="loading" class="spinner" />
          <span v-else>Log Issue</span>
        </button>
      </div>
    </form>
  </ClinicalModal>
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import { ClinicalInput, ClinicalModal } from "ui";
import { describeApiError } from "../../utils/apiErrors.js";

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
  defaultSubjectId: {
    type: String,
    default: "",
  },
  initialData: {
    type: Object,
    default: () => ({}),
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
  study_id: props.defaultStudyId || "",
  site_id: props.defaultSiteId || "",
  subject_id: props.defaultSubjectId || "",
  entity_type: "",
  entity_id: "",
  reason_for_change: "Initial clinical issue logged",
});

function applyInitialData() {
  const init = props.initialData || {};
  form.title = init.title || "";
  form.description = init.description || "";
  form.category = init.category || "PROTOCOL_DEVIATION";
  form.gxp_severity = init.gxp_severity || "MINOR";
  form.priority = init.priority || "MEDIUM";
  form.study_id = init.study_id ?? props.defaultStudyId ?? "";
  form.site_id = init.site_id ?? props.defaultSiteId ?? "";
  form.subject_id = init.subject_id ?? props.defaultSubjectId ?? "";
  form.entity_type = init.entity_type || "";
  form.entity_id = init.entity_id || "";
  form.reason_for_change =
    init.reason_for_change || "Initial clinical issue logged";
}

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      errorMessage.value = "";
      applyInitialData();
    }
  },
  { immediate: true }
);

watch(
  () => [props.defaultStudyId, props.defaultSiteId, props.defaultSubjectId],
  ([nextStudy, nextSite, nextSubject]) => {
    if (!props.isOpen) {
      form.study_id = nextStudy || "";
      form.site_id = nextSite || "";
      form.subject_id = nextSubject || "";
    }
  }
);

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
  if (loading.value) return;
  if (!isFormValid.value) {
    errorMessage.value = "Please complete all required fields.";
    return;
  }
  loading.value = true;
  errorMessage.value = "";
  try {
    const reason = form.reason_for_change.trim();
    const requestPayload = {
      title: form.title.trim(),
      description: form.description.trim(),
      category: form.category,
      gxp_severity: form.gxp_severity,
      priority: form.priority,
      study_id: form.study_id?.trim() || null,
      site_id: form.site_id?.trim() || null,
      subject_id: form.subject_id?.trim() || null,
      entity_type: form.entity_type || null,
      entity_id: form.entity_id?.trim() || null,
      source_route: props.initialData?.source_route || null,
      persona: props.initialData?.persona || null,
    };
    const { ticketsApi } = await import("../../api/ticketsApi");
    const createdTicket = await ticketsApi.createTicket(requestPayload, reason);
    emit("created", createdTicket);
    close();
  } catch (err) {
    errorMessage.value =
      describeApiError(err) || err?.message || "Failed to create ticket.";
  } finally {
    loading.value = false;
  }
};
</script>

<style scoped>
:deep(.ticket-create-modal-body) {
  padding: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.modal-header-title-wrap {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
}

.modal-header-icon {
  font-size: 1.5rem;
  line-height: 1.2;
  flex-shrink: 0;
}

.modal-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.modal-subtitle {
  margin: 4px 0 0 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.ticket-create-form {
  display: flex;
  flex-direction: column;
  min-height: 0;
  flex: 1;
}

.modal-body-fields {
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

.form-control {
  transition: border-color 0.15s;
}

.form-control:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}

.modal-footer {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 24px;
  background: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
}

@media (max-width: 640px) {
  .form-row {
    flex-direction: column;
  }

  .modal-body-fields {
    padding: 16px;
  }

  .modal-footer {
    padding: 12px 16px;
  }
}

@media (max-height: 480px) {
  :deep(.ticket-create-modal-body) {
    overflow: visible;
  }

  .modal-body-fields {
    overflow-y: visible;
  }
}

.spinner {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid var(--semantic-color-surface);
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

<style scoped src="./ticketModalControls.css"></style>
