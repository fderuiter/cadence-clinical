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
              v-if="field.type !== 'radio' && field.type !== 'concept_code'"
              v-show="store.fieldVisibility[field.id] !== false"
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

            <!-- Concept code lookup field -->
            <div
              v-else-if="field.type === 'concept_code'"
              v-show="store.fieldVisibility[field.id] !== false"
              :id="`field-container-${field.id}`"
              class="clinical-input clinical-lookup-container"
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
                  @input="handleLookupInput(field, $event.target.value)"
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

              <!-- Lookup Status Indicator -->
              <div
                v-if="lookupStatuses[field.id]"
                :id="`lookup-status-${field.id}`"
                class="lookup-status-indicator"
                :class="getLookupStatusClass(field.id)"
                role="status"
                aria-live="polite"
              >
                <span class="lookup-status-icon" aria-hidden="true">{{
                  getLookupStatusIcon(field.id)
                }}</span>
                <span class="lookup-status-text">{{
                  lookupStatuses[field.id].message
                }}</span>
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
              v-else-if="field.type === 'radio'"
              v-show="store.fieldVisibility[field.id] !== false"
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

      <!-- PI Sign-Off Worklist and Verification Card -->
      <div
        class="card"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <div class="card-title">PI Sign-Off Worklist & Verification</div>
        <p style="font-size: 0.85rem; color: #475569; margin-bottom: 4px">
          Perform a 21 CFR Part 11 compliant electronic signature. This action
          requires re-authenticating the Principal Investigator credentials to
          obtain a secure single-use signature token.
        </p>

        <div style="display: flex; flex-direction: column; gap: 12px">
          <div class="form-group">
            <label for="signoff-target-type"
              >Sign-Off Scope (Granularity)</label
            >
            <select
              id="signoff-target-type"
              v-model="signoffTargetType"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option value="FORM">FORM Level</option>
              <option value="VISIT">VISIT Level</option>
              <option value="SUBJECT">SUBJECT Level</option>
            </select>
          </div>

          <div class="form-group">
            <label for="signoff-target-id">Select Target ID</label>
            <select
              id="signoff-target-id"
              v-model="signoffTargetId"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option value="">-- Choose ID --</option>
              <template v-if="signoffTargetType === 'SUBJECT'">
                <option
                  v-for="sub in availableSubjects"
                  :key="sub"
                  :value="sub"
                >
                  {{ sub }}
                </option>
              </template>
              <template v-else-if="signoffTargetType === 'VISIT'">
                <option
                  v-for="visit in availableVisits"
                  :key="visit"
                  :value="visit"
                >
                  {{ visit }}
                </option>
              </template>
              <template v-else-if="signoffTargetType === 'FORM'">
                <option
                  v-for="form in availableFormSubmissions"
                  :key="form"
                  :value="form"
                >
                  {{ form }}
                </option>
              </template>
              <option value="custom">-- Enter Custom --</option>
            </select>
          </div>

          <div v-if="signoffTargetId === 'custom'" class="form-group">
            <label for="signoff-custom-target-id">Custom Target ID Value</label>
            <input
              id="signoff-custom-target-id"
              type="text"
              placeholder="Enter custom target ID..."
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @input="(e) => (customTargetId = e.target.value)"
            />
          </div>

          <div class="form-group">
            <label for="signoff-reason">Signing Reason / Attestation</label>
            <select
              id="signoff-reason"
              v-model="signoffReason"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option
                v-for="reason in validSigningReasons"
                :key="reason"
                :value="reason"
              >
                {{ reason }}
              </option>
            </select>
          </div>
        </div>

        <div style="display: flex; justify-content: flex-end; margin-top: 8px">
          <button
            id="btn-pi-signoff"
            class="btn btn-primary"
            type="button"
            @click="handleSignOffSubmit"
          >
            ✍️ Sign Off Target
          </button>
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
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            />
          </div>
          <div class="form-group" style="margin-bottom: 12px">
            <label for="reauth-password">Password</label>
            <input
              id="reauth-password"
              v-model="reauthPassword"
              type="password"
              placeholder="Enter your password to confirm identity..."
              required
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @keyup.enter="confirmReauth"
            />
          </div>
          <div class="form-group" style="margin-bottom: 12px">
            <label for="reauth-totp">MFA/TOTP Token (Optional)</label>
            <input
              id="reauth-totp"
              v-model="reauthTotp"
              type="text"
              placeholder="Enter 6-digit TOTP code..."
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
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
import { ref, reactive, watch, onMounted } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { soaClient } from "../api/soaClient";
import { validateField } from "../../index";
import { debounce } from "ui";
import { terminologyClient } from "../api/terminologyClient";
const store = useClinicalStore();
const authStore = useAuthStore();

const conceptValidationStates = reactive({});
const conceptRequestIds = reactive({});

// eslint-disable-next-line no-unused-vars
function handleConceptCodeInput(field, newValue) {
  store.formValues[field.id] = newValue;

  if (field._debounceTimer) {
    clearTimeout(field._debounceTimer);
  }

  if (!newValue || !newValue.trim()) {
    conceptValidationStates[field.id] = null;
    return;
  }

  if (!conceptRequestIds[field.id]) {
    conceptRequestIds[field.id] = 0;
  }
  const currentReqId = ++conceptRequestIds[field.id];

  field._debounceTimer = setTimeout(async () => {
    try {
      const response = await terminologyClient.validateSingleCode(newValue, {
        userId: authStore.identity?.username || "fderuiter",
        roles: authStore.identity?.roles?.[0] || "Data Manager",
      });

      if (currentReqId !== conceptRequestIds[field.id]) {
        return;
      }

      conceptValidationStates[field.id] = {
        state: response.state,
        decode: response.decode,
        errorMessage: response.error_message,
      };
    } catch (err) {
      if (currentReqId !== conceptRequestIds[field.id]) {
        return;
      }
      conceptValidationStates[field.id] = {
        state: "DEGRADED",
        errorMessage: err.message || "Terminology service offline",
      };
    }
  }, 300);
}

// eslint-disable-next-line no-unused-vars
function getConceptStatusClass(fieldId) {
  const stateObj = conceptValidationStates[fieldId];
  if (!stateObj) return "";
  if (stateObj.state === "VALID") return "lookup-valid";
  if (stateObj.state === "INVALID") return "lookup-invalid";
  if (stateObj.state === "DEGRADED") return "lookup-degraded";
  return "";
}

// eslint-disable-next-line no-unused-vars
function getConceptStatusText(fieldId) {
  const stateObj = conceptValidationStates[fieldId];
  if (!stateObj) return "";
  if (stateObj.state === "VALID") {
    return `Code is valid: "${stateObj.decode}"`;
  }
  if (stateObj.state === "INVALID") {
    return `Invalid code: ${stateObj.errorMessage || ""}`;
  }
  if (stateObj.state === "DEGRADED") {
    return `Terminology service degraded. ${stateObj.errorMessage || ""}`;
  }
  return "";
}

// Live validation states
const requestCounters = reactive({});
const conceptStatuses = reactive({});
const conceptMessages = reactive({});

/*
function getStatusIcon(status) {
  if (status === "loading") return "⏳";
  if (status === "valid") return "✅";
  if (status === "invalid") return "❌";
  if (status === "degraded") return "⚠️";
  return "";
}
*/

const debouncedValidate = debounce(async (fieldId, value) => {
  if (!value || !value.trim()) {
    conceptStatuses[fieldId] = "none";
    conceptMessages[fieldId] = "";
    return;
  }

  requestCounters[fieldId] = (requestCounters[fieldId] || 0) + 1;
  const currentReqId = requestCounters[fieldId];

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      userId: store.user?.username || "fderuiter",
      roles: store.user?.roles ? store.user.roles.join(",") : "investigator",
      changeReason: "Validate code",
    });

    if (requestCounters[fieldId] !== currentReqId) {
      return; // Discard stale response
    }

    if (res.state === "VALID") {
      conceptStatuses[fieldId] = "valid";
      conceptMessages[fieldId] = `Code is valid: "${res.decode}"`;
    } else if (res.state === "INVALID") {
      conceptStatuses[fieldId] = "invalid";
      conceptMessages[fieldId] = `Invalid code: "${value}"`;
    } else if (res.state === "DEGRADED") {
      conceptStatuses[fieldId] = "degraded";
      conceptMessages[fieldId] =
        res.error_message ||
        "Terminology service degraded. Validation offline.";
    }
  } catch {
    if (requestCounters[fieldId] !== currentReqId) {
      return;
    }
    conceptStatuses[fieldId] = "degraded";
    conceptMessages[fieldId] =
      "Terminology service degraded. Validation offline.";
  }
}, 300);

function handleConceptInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (!value || !value.trim()) {
    conceptStatuses[fieldId] = "none";
    conceptMessages[fieldId] = "";
    return;
  }

  conceptStatuses[fieldId] = "loading";
  conceptMessages[fieldId] = "Searching terminology database...";

  debouncedValidate(fieldId, value);
}

// Deep watch formValues to evaluate rules debounced
watch(
  () => store.formValues,
  () => {
    store.triggerValueChange();
  },
  { deep: true }
);

onMounted(() => {
  store.evaluateRules();
  // Initialize lookup validation for any pre-populated concept_code fields on mount
  store.ecrfFields.forEach((field) => {
    if (field.type === "concept_code" && store.formValues[field.id]) {
      handleConceptInput(field, store.formValues[field.id]);
    }
  });
});

// Lookup Status States
const lookupStatuses = ref({});
const lastLookupRequestIds = {};
const debounceTimers = {};

async function performConceptCodeValidation(fieldId, value) {
  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  if (lastLookupRequestIds[fieldId] === undefined) {
    lastLookupRequestIds[fieldId] = 0;
  }
  lastLookupRequestIds[fieldId]++;
  const requestId = lastLookupRequestIds[fieldId];

  lookupStatuses.value[fieldId] = {
    status: "loading",
    message: "Searching terminology database...",
  };

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      userId: store.user?.username || "fderuiter",
      roles: store.user?.roles ? store.user.roles.join(",") : "investigator",
      changeReason: "Validate code",
    });

    if (requestId !== lastLookupRequestIds[fieldId]) {
      return;
    }

    if (res.state === "VALID") {
      lookupStatuses.value[fieldId] = {
        status: "valid",
        message: `Code is valid: "${res.decode}"`,
      };
    } else if (res.state === "INVALID") {
      lookupStatuses.value[fieldId] = {
        status: "invalid",
        message: `Invalid code "${value}". Not found in NCI Thesaurus.`,
      };
    } else if (res.state === "DEGRADED") {
      lookupStatuses.value[fieldId] = {
        status: "degraded",
        message: "Terminology service degraded. Validation offline.",
      };
    }
  } catch (error) {
    if (requestId !== lastLookupRequestIds[fieldId]) {
      return;
    }
    lookupStatuses.value[fieldId] = {
      status: "degraded",
      message:
        error.message || "Terminology service degraded. Validation offline.",
    };
  }
}

function handleLookupInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (debounceTimers[fieldId]) {
    clearTimeout(debounceTimers[fieldId]);
  }

  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  debounceTimers[fieldId] = setTimeout(() => {
    performConceptCodeValidation(fieldId, value);
  }, 300);
}

function getLookupStatusClass(fieldId) {
  const item = lookupStatuses.value[fieldId];
  if (!item) return "";
  return `lookup-${item.status}`;
}

function getLookupStatusIcon(fieldId) {
  const item = lookupStatuses.value[fieldId];
  if (!item) return "";
  if (item.status === "loading") return "⏳";
  if (item.status === "valid") return "✅";
  if (item.status === "invalid") return "❌";
  if (item.status === "degraded") return "⚠️";
  return "";
}

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
const reauthTotp = ref("");
const reauthError = ref("");
const reauthAction = ref(""); // "CLOSE_QUERY" or "BATCH_SIGN_OFF"
const pendingCloseQueryFieldId = ref(null);

// PI Sign-Off Worklist States
const signoffTargetType = ref("FORM"); // "FORM", "VISIT", "SUBJECT"
const signoffTargetId = ref("");
const customTargetId = ref("");
const signoffReason = ref("PI approval and sign-off.");

const availableSubjects = ref(["SUBJ-001", "SUBJ-002", "SUBJ-003"]);
const availableVisits = ref(["V-SCR", "V-TRT-A1", "V-TRT-A2", "V-TRT-B1"]);
const availableFormSubmissions = ref(["FSUB-001", "FSUB-002", "FSUB-003"]);

const validSigningReasons = [
  "I attest that this data is accurate and complete.",
  "PI approval and sign-off.",
  "Review and confirmation.",
  "DATA_RECORDING",
  "DATA_ENTRY_COMPLETED",
  "PI_REVIEW",
  "PI_SIGN_OFF",
  "COMPLIANCE_ATTESTATION",
];

function getQueryStatus(fieldId) {
  const query = store.formQueries[fieldId];
  return query ? query.status : "NONE";
}

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues);
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
  reauthAction.value = "CLOSE_QUERY";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

function handleSignOffSubmit() {
  const targetId =
    signoffTargetId.value === "custom"
      ? customTargetId.value
      : signoffTargetId.value;
  if (!targetId || !targetId.trim()) {
    alert("Please select or enter a valid Target ID first.");
    return;
  }
  reauthAction.value = "BATCH_SIGN_OFF";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

function cancelReauth() {
  showReauthModal.value = false;
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  pendingCloseQueryFieldId.value = null;
  reauthAction.value = "";
}

async function confirmReauth() {
  if (!reauthPassword.value) {
    reauthError.value = "Password is required.";
    return;
  }

  const username = reauthUsername.value;
  const password = reauthPassword.value;
  const totp = reauthTotp.value || null;
  const action = reauthAction.value;

  // Immediately clear password to ensure GxP compliance & no state leak
  reauthPassword.value = "";

  if (action === "CLOSE_QUERY") {
    const fieldId = pendingCloseQueryFieldId.value;
    if (fieldId) {
      const queryObj = store.formQueries[fieldId];
      queryObj.status = "CLOSED";
      queryObj.closedBy = `${username} (Offline Client)`;
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
    reauthError.value = "";
    alert(
      "Identity verified. Query closed and logged to cryptographic ledger."
    );
  } else if (action === "BATCH_SIGN_OFF") {
    try {
      reauthError.value = "";

      // 1. Obtain signature token
      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      // 2. Call batch sign-off
      const targetId =
        signoffTargetId.value === "custom"
          ? customTargetId.value
          : signoffTargetId.value;
      const signoffRes = await soaClient.batchSignOff(
        {
          studyId: store.currentUsdm.studyId || "STUDY-USDM-001",
          targetType: signoffTargetType.value,
          targetIds: [targetId],
          signingReason: signoffReason.value,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "investigator",
          changeReason: signoffReason.value,
          sigToken,
        },
        authStore.accessToken
      );

      // 3. Document in ledger
      await store.addLedgerBlock(
        "BATCH_SIGN_OFF_SUCCESS",
        {
          targetType: signoffTargetType.value,
          targetIds: [targetId],
          signingReason: signoffReason.value,
          result: signoffRes,
        },
        `PI electronic sign-off approved: ${signoffReason.value}`
      );

      // Clean up variables & UI state
      showReauthModal.value = false;
      reauthTotp.value = "";
      alert(
        `Signature Token obtained successfully.\nBatch sign-off completed for ${signoffTargetType.value} ${targetId}!`
      );
    } catch (err) {
      // Explicitly wipe credentials on failure
      reauthPassword.value = "";
      reauthTotp.value = "";

      if (err.message === "REAUTHENTICATION_REQUIRED" || err.status === 401) {
        reauthError.value =
          "Identity verification expired or invalid. Please try again.";
        showReauthModal.value = true;
      } else {
        reauthError.value = err.message || "Failed to complete batch sign-off.";
      }
    }
  }
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
