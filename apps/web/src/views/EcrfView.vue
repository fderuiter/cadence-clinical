<template>
  <div id="section-ecrf" class="dashboard-section active">
    <div class="section-header">
      <h2>eCRF Runtime Renderer</h2>
      <p>
        Live, dynamic data entry form populated from CDASH metadata with
        client-side field validation and real-time query management.
      </p>
    </div>

    <div class="grid-2">
      <!-- Dynamic eCRF Form -->
      <div class="card">
        <div class="card-title">Subject eCRF Data Entry Form</div>
        <form
          id="form-VS_DEMO"
          class="clinical-form clinical-form-grid"
          style="
            display: grid;
            grid-template-columns: repeat(12, 1fr);
            gap: 16px;
          "
          @submit.prevent
        >
          <template v-for="field in store.ecrfFields" :key="field.id">
            <!-- Text input field -->
            <div
              v-if="field.type !== 'radio'"
              :id="`field-container-${field.id}`"
              class="clinical-input"
              :class="{ 'has-error': getValidationError(field) }"
              :style="`grid-column: span ${field.gridSpan || 12};`"
            >
              <label :for="field.id">{{ field.label }}</label>
              <div class="input-wrapper">
                <input
                  :id="field.id"
                  type="text"
                  :name="field.id"
                  :value="store.formValues[field.id]"
                  @change="
                    handleFieldChange(field, $event.target.value, $event.target)
                  "
                />

                <!-- Query Flag -->
                <button
                  :id="`query-flag-${field.id}`"
                  class="query-flag"
                  :class="`query-status-${getQueryStatus(field.id).toLowerCase()}`"
                  type="button"
                  @click="toggleQueryPanel(field.id)"
                >
                  {{ getQueryStatus(field.id) === "NONE" ? "💬" : "⚠️" }}
                </button>
              </div>

              <!-- Validation Error -->
              <div
                v-if="
                  getValidationError(field) && store.formValues[field.id] !== ''
                "
                class="validation-error-msg"
              >
                {{ getValidationError(field) }}
              </div>

              <!-- Query Panel -->
              <div
                v-if="activeQueryPanels[field.id]"
                :id="`query-panel-${field.id}`"
                class="query-panel"
                role="region"
              >
                <div class="query-panel-header">
                  <span class="query-panel-title"
                    >Query Manager - {{ field.id }}</span
                  >
                  <button
                    type="button"
                    class="btn-close-panel"
                    @click="toggleQueryPanel(field.id)"
                  >
                    ×
                  </button>
                </div>
                <div class="query-panel-body">
                  <!-- No Query State -->
                  <div
                    v-if="getQueryStatus(field.id) === 'NONE'"
                    class="query-create-section"
                  >
                    <p class="query-panel-instruction">
                      Raise a query for this field:
                    </p>
                    <div class="form-group">
                      <label :for="`query-message-${field.id}`"
                        >Discrepancy Message</label
                      >
                      <textarea
                        :id="`query-message-${field.id}`"
                        v-model="queryInputs[field.id]"
                        placeholder="Enter clinical discrepancy details..."
                        required
                      />
                    </div>
                    <button
                      type="button"
                      class="btn-submit-query"
                      @click="createQuery(field.id)"
                    >
                      Submit Query
                    </button>
                  </div>

                  <!-- Open/Reopened Query State -->
                  <div
                    v-else-if="
                      getQueryStatus(field.id) === 'OPEN' ||
                      getQueryStatus(field.id) === 'REOPENED'
                    "
                    class="query-details"
                  >
                    <div
                      class="query-status-badge"
                      :class="`badge-${getQueryStatus(field.id).toLowerCase()}`"
                    >
                      Status: {{ getQueryStatus(field.id) }}
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-meta">
                      Raised by:
                      {{ store.formQueries[field.id].createdBy || "System" }} on
                      {{ store.formQueries[field.id].createdAt }}
                    </p>
                    <div class="query-respond-section" style="margin-top: 12px">
                      <div class="form-group">
                        <label :for="`query-response-${field.id}`"
                          >Your Response</label
                        >
                        <textarea
                          :id="`query-response-${field.id}`"
                          v-model="queryResponses[field.id]"
                          placeholder="Enter clinical justification or resolution explanation..."
                          required
                        />
                      </div>
                      <button
                        type="button"
                        class="btn-respond-query"
                        @click="respondQuery(field.id)"
                      >
                        Submit Response
                      </button>
                    </div>
                  </div>

                  <!-- Answered Query State -->
                  <div
                    v-else-if="getQueryStatus(field.id) === 'ANSWERED'"
                    class="query-details"
                  >
                    <div class="query-status-badge badge-answered">
                      Status: ANSWERED
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-response-msg">
                      <strong>Response:</strong>
                      {{ store.formQueries[field.id].response }}
                    </p>
                    <p class="query-meta">
                      Responded by:
                      {{ store.formQueries[field.id].respondedBy }} on
                      {{ store.formQueries[field.id].respondedAt }}
                    </p>
                    <div
                      class="query-actions-section"
                      style="margin-top: 12px; display: flex; gap: 8px"
                    >
                      <button
                        type="button"
                        class="btn-close-query"
                        @click="closeQuery(field.id)"
                      >
                        Close Query (Resolve)
                      </button>
                      <button
                        type="button"
                        class="btn-reopen-query"
                        @click="reopenQuery(field.id)"
                      >
                        Reopen Query
                      </button>
                    </div>
                  </div>

                  <!-- Closed Query State -->
                  <div
                    v-else-if="getQueryStatus(field.id) === 'CLOSED'"
                    class="query-details"
                  >
                    <div class="query-status-badge badge-closed">
                      Status: CLOSED
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-response-msg">
                      <strong>Response:</strong>
                      {{ store.formQueries[field.id].response }}
                    </p>
                    <p class="query-meta">
                      Closed by: {{ store.formQueries[field.id].closedBy }} on
                      {{ store.formQueries[field.id].closedAt }}
                    </p>
                    <p class="query-history-info">
                      This query is permanently resolved and closed.
                    </p>
                  </div>
                </div>
              </div>
            </div>

            <!-- Radio input field -->
            <fieldset
              v-else
              :id="`field-container-${field.id}`"
              class="clinical-radio-grid"
              :style="`grid-column: span ${field.gridSpan || 12};`"
            >
              <legend>{{ field.label }}</legend>
              <div class="radio-options-wrapper">
                <div class="radio-options">
                  <div
                    v-for="(opt, idx) in field.options"
                    :key="idx"
                    class="radio-option"
                  >
                    <input
                      :id="`${field.id}_option_${idx}`"
                      type="radio"
                      :name="field.id"
                      :value="opt.value"
                      :checked="store.formValues[field.id] === opt.value"
                      @change="
                        handleFieldChange(field, opt.value, $event.target)
                      "
                    />
                    <label :for="`${field.id}_option_${idx}`">{{
                      opt.label
                    }}</label>
                  </div>
                </div>

                <!-- Query Flag -->
                <button
                  :id="`query-flag-${field.id}`"
                  class="query-flag"
                  :class="`query-status-${getQueryStatus(field.id).toLowerCase()}`"
                  type="button"
                  @click="toggleQueryPanel(field.id)"
                >
                  {{ getQueryStatus(field.id) === "NONE" ? "💬" : "⚠️" }}
                </button>
              </div>

              <!-- Query Panel -->
              <div
                v-if="activeQueryPanels[field.id]"
                :id="`query-panel-${field.id}`"
                class="query-panel"
                role="region"
              >
                <div class="query-panel-header">
                  <span class="query-panel-title"
                    >Query Manager - {{ field.id }}</span
                  >
                  <button
                    type="button"
                    class="btn-close-panel"
                    @click="toggleQueryPanel(field.id)"
                  >
                    ×
                  </button>
                </div>
                <div class="query-panel-body">
                  <!-- No Query State -->
                  <div
                    v-if="getQueryStatus(field.id) === 'NONE'"
                    class="query-create-section"
                  >
                    <p class="query-panel-instruction">
                      Raise a query for this field:
                    </p>
                    <div class="form-group">
                      <label :for="`query-message-${field.id}`"
                        >Discrepancy Message</label
                      >
                      <textarea
                        :id="`query-message-${field.id}`"
                        v-model="queryInputs[field.id]"
                        placeholder="Enter clinical discrepancy details..."
                        required
                      />
                    </div>
                    <button
                      type="button"
                      class="btn-submit-query"
                      @click="createQuery(field.id)"
                    >
                      Submit Query
                    </button>
                  </div>

                  <!-- Open/Reopened Query State -->
                  <div
                    v-else-if="
                      getQueryStatus(field.id) === 'OPEN' ||
                      getQueryStatus(field.id) === 'REOPENED'
                    "
                    class="query-details"
                  >
                    <div
                      class="query-status-badge"
                      :class="`badge-${getQueryStatus(field.id).toLowerCase()}`"
                    >
                      Status: {{ getQueryStatus(field.id) }}
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-meta">
                      Raised by:
                      {{ store.formQueries[field.id].createdBy || "System" }} on
                      {{ store.formQueries[field.id].createdAt }}
                    </p>
                    <div class="query-respond-section" style="margin-top: 12px">
                      <div class="form-group">
                        <label :for="`query-response-${field.id}`"
                          >Your Response</label
                        >
                        <textarea
                          :id="`query-response-${field.id}`"
                          v-model="queryResponses[field.id]"
                          placeholder="Enter clinical justification or resolution explanation..."
                          required
                        />
                      </div>
                      <button
                        type="button"
                        class="btn-respond-query"
                        @click="respondQuery(field.id)"
                      >
                        Submit Response
                      </button>
                    </div>
                  </div>

                  <!-- Answered Query State -->
                  <div
                    v-else-if="getQueryStatus(field.id) === 'ANSWERED'"
                    class="query-details"
                  >
                    <div class="query-status-badge badge-answered">
                      Status: ANSWERED
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-response-msg">
                      <strong>Response:</strong>
                      {{ store.formQueries[field.id].response }}
                    </p>
                    <p class="query-meta">
                      Responded by:
                      {{ store.formQueries[field.id].respondedBy }} on
                      {{ store.formQueries[field.id].respondedAt }}
                    </p>
                    <div
                      class="query-actions-section"
                      style="margin-top: 12px; display: flex; gap: 8px"
                    >
                      <button
                        type="button"
                        class="btn-close-query"
                        @click="closeQuery(field.id)"
                      >
                        Close Query (Resolve)
                      </button>
                      <button
                        type="button"
                        class="btn-reopen-query"
                        @click="reopenQuery(field.id)"
                      >
                        Reopen Query
                      </button>
                    </div>
                  </div>

                  <!-- Closed Query State -->
                  <div
                    v-else-if="getQueryStatus(field.id) === 'CLOSED'"
                    class="query-details"
                  >
                    <div class="query-status-badge badge-closed">
                      Status: CLOSED
                    </div>
                    <p class="query-current-msg">
                      <strong>Discrepancy:</strong>
                      {{ store.formQueries[field.id].message }}
                    </p>
                    <p class="query-response-msg">
                      <strong>Response:</strong>
                      {{ store.formQueries[field.id].response }}
                    </p>
                    <p class="query-meta">
                      Closed by: {{ store.formQueries[field.id].closedBy }} on
                      {{ store.formQueries[field.id].closedAt }}
                    </p>
                    <p class="query-history-info">
                      This query is permanently resolved and closed.
                    </p>
                  </div>
                </div>
              </div>
            </fieldset>
          </template>
        </form>

        <div class="form-actions">
          <button id="btn-clear-ecrf" class="btn" @click="clearForm">
            Clear Form
          </button>
          <button
            id="btn-submit-ecrf"
            class="btn btn-primary"
            @click="submitEcrf"
          >
            Submit eCRF Session
          </button>
        </div>
      </div>

      <!-- Live Form State & Meta -->
      <div
        class="card"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <div>
          <div class="card-title">CDASH Metadata Specification</div>
          <p style="font-size: 0.85rem; color: #475569; margin-bottom: 8px">
            The fields on the left are dynamically rendered using structural
            CDASH metadata tags (e.g. <code>DM.BRTHDT</code>,
            <code>VS.VSSBP</code>).
          </p>
        </div>

        <div
          style="
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 12px;
            background-color: #f8fafc;
          "
        >
          <h3
            style="
              font-size: 0.9rem;
              font-weight: 700;
              margin-bottom: 8px;
              color: var(--primary);
            "
          >
            Real-time Field Validation Rules:
          </h3>
          <ul
            style="
              font-size: 0.8rem;
              padding-left: 20px;
              color: #475569;
              display: flex;
              flex-direction: column;
              gap: 6px;
            "
          >
            <li><strong>Birth Date:</strong> Must match YYYY-MM-DD pattern.</li>
            <li>
              <strong>Systolic BP:</strong> Numeric value between 50 and 250
              mmHg.
            </li>
            <li>
              <strong>Diastolic BP:</strong> Numeric value between 30 and 150
              mmHg.
            </li>
            <li>
              <strong>Pulse Rate:</strong> Numeric value between 30 and 200 bpm.
            </li>
          </ul>
        </div>

        <div
          style="
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 12px;
            background-color: #f8fafc;
          "
        >
          <h3
            style="
              font-size: 0.9rem;
              font-weight: 700;
              margin-bottom: 8px;
              color: var(--primary);
            "
          >
            Query Management Actions:
          </h3>
          <p style="font-size: 0.8rem; color: #475569; line-height: 1.4">
            Click the 💬 / ⚠️ flags next to input fields to raise, answer,
            close, or reopen discrepancy notes. All query transitions are
            audit-logged in real-time.
          </p>
        </div>
      </div>
    </div>

    <!-- Reason for Change Modal Dialog -->
    <div
      v-if="showReasonModal"
      id="reason-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal">
        <div class="modal-header">Reason for Change Required</div>
        <div class="modal-body">
          <p>
            To comply with <strong>21 CFR Part 11 / EU Annex 11</strong>, you
            must document a reason for changing this clinical data field.
          </p>
          <div class="form-group" style="margin-bottom: 12px">
            <label for="change-reason-select">Select Standard Reason</label>
            <select id="change-reason-select" v-model="selectedReason">
              <option value="Initial Entry">Initial Data Entry</option>
              <option value="Typographical Error">
                Correction of typographical error
              </option>
              <option value="Re-measurement">Re-measurement of vitals</option>
              <option value="Transcription Error">
                Correction of transcription error
              </option>
              <option value="Other">Other (specify below)</option>
            </select>
          </div>
          <div class="form-group">
            <label for="change-reason-text"
              >Custom Explanation (Optional)</label
            >
            <textarea
              id="change-reason-text"
              v-model="customReasonExplanation"
              placeholder="Explain the clinical reason for this modification..."
            />
          </div>
        </div>
        <div class="modal-footer">
          <button id="btn-cancel-change" class="btn" @click="cancelChange">
            Cancel Change
          </button>
          <button
            id="btn-save-change"
            class="btn btn-primary"
            @click="saveChange"
          >
            Sign & Save
          </button>
        </div>
      </div>
    </div>

    <!-- Re-authentication Modal Dialog -->
    <div
      v-if="showReauthModal"
      id="reauth-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal">
        <div class="modal-header">Identity Re-Authentication Required</div>
        <div class="modal-body">
          <p>
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must re-verify your identity before performing this
            high-security action.
          </p>
          <div class="form-group" style="margin-bottom: 12px">
            <label for="reauth-username">Username</label>
            <input
              id="reauth-username"
              v-model="reauthUsername"
              type="text"
              readonly
              style="background-color: #f1f5f9"
            />
          </div>
          <div class="form-group">
            <label for="reauth-password">Password</label>
            <input
              id="reauth-password"
              v-model="reauthPassword"
              type="password"
              placeholder="Enter your password to confirm identity..."
              required
              @keyup.enter="confirmReauth"
            />
          </div>
          <div
            v-if="reauthError"
            class="validation-error-msg"
            style="margin-top: 8px; color: #ef4444"
          >
            {{ reauthError }}
          </div>
        </div>
        <div class="modal-footer">
          <button id="btn-cancel-reauth" class="btn" @click="cancelReauth">
            Cancel
          </button>
          <button
            id="btn-confirm-reauth"
            class="btn btn-primary"
            @click="confirmReauth"
          >
            Verify & Confirm
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { validateField } from "../../index";

const store = useClinicalStore();

// UI States
const activeQueryPanels = reactive({});
const queryInputs = reactive({});
const queryResponses = reactive({});

// Reason Modal States
const showReasonModal = ref(false);
const selectedReason = ref("Initial Entry");
const customReasonExplanation = ref("");
const pendingValueChange = ref(null);

// Re-authentication Modal States
const showReauthModal = ref(false);
const reauthUsername = ref(store.user.username);
const reauthPassword = ref("");
const reauthError = ref("");
const pendingCloseQueryFieldId = ref(null);

function getQueryStatus(fieldId) {
  const query = store.formQueries[fieldId];
  return query ? query.status : "NONE";
}

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value);
  return res.valid ? null : res.message;
}

function toggleQueryPanel(fieldId) {
  activeQueryPanels[fieldId] = !activeQueryPanels[fieldId];
}

// Reason Modal logic
function handleFieldChange(field, newValue, targetEl) {
  const oldValue = store.formValues[field.id] || "";
  if (newValue === oldValue) return;

  if (oldValue !== "" && oldValue !== null && oldValue !== undefined) {
    pendingValueChange.value = {
      field,
      oldValue,
      newValue,
      targetEl,
    };
    selectedReason.value = "Initial Entry";
    customReasonExplanation.value = "";
    showReasonModal.value = true;
  } else {
    commitChange(field, oldValue, newValue, "Initial Entry");
  }
}

function cancelChange() {
  if (pendingValueChange.value && pendingValueChange.value.targetEl) {
    if (pendingValueChange.value.targetEl.type === "radio") {
      // Vue handles radio binding automatically, but let's force re-sync if needed
    } else {
      pendingValueChange.value.targetEl.value =
        pendingValueChange.value.oldValue;
    }
  }
  showReasonModal.value = false;
  pendingValueChange.value = null;
}

function saveChange() {
  if (!pendingValueChange.value) return;

  const sel = selectedReason.value;
  const cust = customReasonExplanation.value.trim();
  const finalReason =
    sel === "Other" && cust ? cust : `${sel}${cust ? ": " + cust : ""}`;

  commitChange(
    pendingValueChange.value.field,
    pendingValueChange.value.oldValue,
    pendingValueChange.value.newValue,
    finalReason
  );

  showReasonModal.value = false;
  pendingValueChange.value = null;
}

function commitChange(field, oldValue, newValue, reason) {
  store.formValues[field.id] = newValue;
  store.addLedgerBlock(
    "FIELD_CHANGE",
    {
      fieldId: field.id,
      label: field.label,
      cdash: field.cdash || "",
      oldValue,
      newValue,
    },
    reason
  );
}

// Query Operations
function createQuery(fieldId) {
  const msg = (queryInputs[fieldId] || "").trim();
  if (!msg) {
    alert("Please enter a discrepancy message!");
    return;
  }

  const queryObj = {
    status: "OPEN",
    message: msg,
    createdBy: "Data Monitor (Offline Client)",
    createdAt: new Date().toISOString().slice(0, 10),
  };

  store.formQueries[fieldId] = queryObj;
  queryInputs[fieldId] = "";
  store.addLedgerBlock(
    "QUERY_CREATE",
    { fieldId, query: queryObj },
    `Raised discrepancy: "${msg}"`
  );
}

function respondQuery(fieldId) {
  const resp = (queryResponses[fieldId] || "").trim();
  if (!resp) {
    alert("Please enter a response!");
    return;
  }

  const queryObj = store.formQueries[fieldId];
  queryObj.status = "ANSWERED";
  queryObj.response = resp;
  queryObj.respondedBy = "Clinical Investigator (Offline Client)";
  queryObj.respondedAt = new Date().toISOString().slice(0, 10);

  queryResponses[fieldId] = "";
  store.addLedgerBlock(
    "QUERY_RESPOND",
    { fieldId, query: queryObj },
    `Responded to query: "${resp}"`
  );
}

function closeQuery(fieldId) {
  pendingCloseQueryFieldId.value = fieldId;
  showReauthModal.value = true;
}

function cancelReauth() {
  showReauthModal.value = false;
  reauthPassword.value = "";
  reauthError.value = "";
  pendingCloseQueryFieldId.value = null;
}

function confirmReauth() {
  if (!reauthPassword.value) {
    reauthError.value = "Password is required.";
    return;
  }

  const fieldId = pendingCloseQueryFieldId.value;
  if (fieldId) {
    const queryObj = store.formQueries[fieldId];
    queryObj.status = "CLOSED";
    queryObj.closedBy = `${store.user.username} (Offline Client)`;
    queryObj.closedAt = new Date().toISOString().slice(0, 10);

    const fieldMeta = store.ecrfFields.find((f) => f.id === fieldId);
    const cdash = fieldMeta ? fieldMeta.cdash : "";
    const [domain, testCode] = cdash
      ? cdash.split(".")
      : ["VS", fieldId.toUpperCase()];

    store.addLedgerBlock(
      "QUERY_CLOSE",
      {
        fieldId,
        studyId: store.currentUsdm.studyId || "STUDY-USDM-001",
        subjectId: "SUBJ-001",
        visitId: "Screening",
        domain,
        testCode,
        query: queryObj,
      },
      "Discrepancy resolved and closed permanently."
    );
    pendingCloseQueryFieldId.value = null;
  }

  showReauthModal.value = false;
  reauthPassword.value = "";
  reauthError.value = "";

  alert("Identity verified. Query closed and logged to cryptographic ledger.");
}

function reopenQuery(fieldId) {
  const queryObj = store.formQueries[fieldId];
  queryObj.status = "REOPENED";
  queryObj.message =
    queryObj.message + " [Reopened due to insufficient response]";

  store.addLedgerBlock(
    "QUERY_REOPEN",
    { fieldId, query: queryObj },
    "Investigator response was rejected. Query reopened."
  );
}

function clearForm() {
  store.ecrfFields.forEach((f) => {
    store.formValues[f.id] = "";
    delete store.formQueries[f.id];
  });
  store.addLedgerBlock(
    "FORM_CLEAR",
    { formId: "VS_DEMO" },
    "All eCRF form fields cleared by clinical staff."
  );
}

function submitEcrf() {
  let allValid = true;
  let errMsgs = [];

  store.ecrfFields.forEach((f) => {
    const res = validateField(f, store.formValues[f.id]);
    if (!res.valid) {
      allValid = false;
      errMsgs.push(`${f.label}: ${res.message}`);
    }
  });

  if (!allValid) {
    alert(
      "Cannot submit eCRF! The form contains validation errors:\n\n" +
        errMsgs.join("\n")
    );
    return;
  }

  store.addLedgerBlock(
    "SESSION_SUBMIT",
    {
      formId: "VS_DEMO",
      formValues: store.formValues,
      formQueries: store.formQueries,
    },
    "eCRF successfully verified, finalized, and electronically submitted."
  );

  alert(
    "eCRF Session successfully submitted to secure cryptographic database!"
  );
}
</script>
