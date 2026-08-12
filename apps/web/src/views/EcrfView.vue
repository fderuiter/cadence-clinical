<template>
  <div id="section-ecrf" class="dashboard-section active">
    <div class="section-header">
      <h2>eCRF Runtime Renderer</h2>
      <p>
        Live, dynamic data entry form populated from CDASH metadata with
        client-side field validation and real-time query management.
      </p>
    </div>

    <!-- Active User Role Badge & CRA Mode Toggle (for Sub-Issue 10) -->
    <div
      style="
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--spacing-sm);
        background-color: #f8fafc;
        border: 1px solid var(--border);
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: 20px;
      "
    >
      <div style="font-size: 0.9rem">
        Active Role:
        <span
          class="badge"
          style="
            background-color: var(--accent);
            color: white;
            padding: 4px 8px;
            border-radius: 4px;
            font-weight: bold;
          "
          >{{ activeUserRole.toUpperCase() }}</span
        >
      </div>

      <!-- Let user select role in demo mode to test Site Coordinator (CRC) vs Monitor (CRA) workflows -->
      <div
        style="
          display: flex;
          gap: var(--spacing-xs);
          align-items: center;
          flex-wrap: wrap;
        "
      >
        <label
          for="role-tester-select"
          style="font-size: 0.8rem; font-weight: bold"
          >Demo Role Toggle:</label
        >
        <select
          id="role-tester-select"
          v-model="demoRole"
          style="
            padding: 4px 8px;
            border-radius: 4px;
            border: 1px solid var(--border);
          "
        >
          <option value="site_investigator">Site Coordinator / CRC</option>
          <option value="cra">CRA Monitor (SDV Enabled)</option>
        </select>
      </div>
    </div>

    <div class="grid-2-responsive">
      <!-- Dynamic eCRF Form -->
      <div class="card">
        <div class="card-title">Subject eCRF Data Entry Form</div>

        <!-- Sub-Issue 9: Subject & Visit Selection Panel -->
        <div
          style="
            display: flex;
            flex-wrap: wrap;
            gap: var(--spacing-md);
            margin-bottom: var(--spacing-md);
            border-bottom: 1px solid var(--border);
            padding-bottom: 12px;
          "
        >
          <div class="form-group" style="flex: 1">
            <label for="ecrf-subject-selector" style="font-weight: bold"
              >Active Subject ID</label
            >
            <select
              id="ecrf-subject-selector"
              v-model="selectedSubjectId"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @change="loadEcrfSession"
            >
              <option value="SUBJ-001">SUBJ-001 (Mock Subject)</option>
              <option value="SUBJ-002">SUBJ-002 (Screened Cohort)</option>
              <option value="SUBJ-003">SUBJ-003 (Post-Randomization)</option>
            </select>
          </div>
          <div class="form-group" style="flex: 1">
            <label for="ecrf-visit-selector" style="font-weight: bold"
              >Active Visit / Encounter</label
            >
            <select
              id="ecrf-visit-selector"
              v-model="selectedVisitId"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @change="loadEcrfSession"
            >
              <option value="Screening">Screening / Day -7</option>
              <option value="Week2">Week 2 Treatment</option>
              <option value="Week4">Week 4 Treatment</option>
            </select>
          </div>
        </div>

        <!-- Batch Verification Action Bar -->
        <div
          v-if="selectedBatchFields.length > 0"
          id="batch-sdv-bar"
          style="
            background-color: #eff6ff;
            border: 1px solid #bfdbfe;
            border-radius: 8px;
            padding: 12px 16px;
            margin-bottom: var(--spacing-md);
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--spacing-sm);
          "
        >
          <div style="font-size: 0.9rem; font-weight: 600; color: #1e40af">
            Selected {{ selectedBatchFields.length }} fields for Batch Source
            Data Verification
          </div>
          <button
            id="btn-batch-verify"
            class="btn btn-primary"
            style="
              background-color: #2563eb;
              color: white;
              font-weight: bold;
              padding: 6px 12px;
              font-size: 0.85rem;
            "
            @click="initiateBatchVerify"
          >
            Batch Verify Selected ({{ selectedBatchFields.length }})
          </button>
        </div>

        <form
          id="form-VS_DEMO"
          class="clinical-form responsive-grid"
          @submit.prevent
        >
          <template v-for="field in store.ecrfFields" :key="field.id">
            <div
              v-show="store.fieldVisibility[field.id] !== false"
              :style="`grid-column: span ${field.gridSpan || 12}; display: flex; flex-direction: column; gap: 8px;`"
              style="margin-bottom: 8px"
            >
              <ClinicalFormField
                :field="field"
                :model-value="store.formValues[field.id]"
                :query="store.formQueries[field.id]"
                :error="getValidationError(field)"
                :lookup-status="lookupStatuses[field.id]"
                @update:model-value="store.formValues[field.id] = $event"
                @input="handleLookupInput(field, $event)"
                @change="(val, target) => handleFieldChange(field, val, target)"
                @create-query="createQuery(field.id, $event)"
                @respond-query="respondQuery(field.id, $event)"
                @close-query="closeQuery(field.id)"
                @reopen-query="reopenQuery(field.id)"
              />

              <!-- Sub-Issue 10: CRA Monitoring and SDV (Source Document Verification) checkbox -->
              <div
                v-if="isCraUser"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  background-color: #f0fdf4;
                  border: 1px dashed #bbf7d0;
                  padding: 8px;
                  border-radius: 4px;
                  margin-top: -6px;
                "
                class="sdv-box"
              >
                <input
                  :id="`sdv-${field.id}`"
                  type="checkbox"
                  :checked="sdvStates[getSdvKey(field.id)] === true"
                  style="cursor: pointer"
                  @change="handleSdvToggle(field.id, $event.target.checked)"
                />
                <label
                  :for="`sdv-${field.id}`"
                  style="
                    font-size: 0.8rem;
                    color: #166534;
                    font-weight: 600;
                    margin: 0;
                    cursor: pointer;
                  "
                >
                  Source Document Verified (SDV)
                </label>
              </div>

              <!-- Batch SDV Selection Checkbox -->
              <div
                v-if="isAuthorizedForBulkSdv"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  background-color: #eff6ff;
                  border: 1px dashed #bfdbfe;
                  padding: 8px;
                  border-radius: 4px;
                  margin-top: 4px;
                "
                class="batch-sdv-box"
              >
                <input
                  :id="`batch-sdv-${field.id}`"
                  v-model="selectedBatchFields"
                  type="checkbox"
                  :value="field.id"
                  style="cursor: pointer"
                  class="batch-sdv-checkbox"
                />
                <label
                  :for="`batch-sdv-${field.id}`"
                  style="
                    font-size: 0.8rem;
                    color: #1e40af;
                    font-weight: 600;
                    margin: 0;
                    cursor: pointer;
                  "
                >
                  Select for Batch SDV
                </label>
              </div>
            </div>
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
        <div class="card-title">PI Sign-Off Worklist &amp; Verification</div>
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

      <!-- Protocol Ingestion & Candidate Review Card -->
      <div
        class="card"
        style="
          display: flex;
          flex-direction: column;
          gap: 16px;
          grid-column: span 12;
        "
      >
        <div class="card-title">
          Ultimate CRF Builder: Protocol Ingestion &amp; Review
        </div>
        <p style="font-size: 0.85rem; color: #475569">
          Upload a clinical protocol document (PDF/DOCX) to automatically
          generate candidate SoA visits and form fields with trace citations and
          confidence levels. Accept, edit, or reject each item before promoting
          reviewed candidates into a formal study draft.
        </p>

        <!-- Upload File Section -->
        <div
          style="
            border: 1px dashed var(--border);
            border-radius: 8px;
            padding: 16px;
            text-align: center;
            background-color: #f8fafc;
          "
        >
          <input
            ref="fileInputRef"
            type="file"
            accept=".pdf,.docx"
            style="display: none"
            @change="triggerDocumentUpload"
          />
          <button
            class="btn"
            type="button"
            :disabled="store.ingestionLoading"
            @click="triggerFileSelect"
          >
            {{
              store.ingestionLoading
                ? "Processing Document..."
                : "📁 Select Protocol PDF/DOCX"
            }}
          </button>
          <div
            v-if="selectedFileName"
            style="margin-top: 8px; font-size: 0.8rem; color: #475569"
          >
            Selected:
            <strong class="selected-file-name">{{ selectedFileName }}</strong>
          </div>
        </div>

        <!-- Ingestion Error Display -->
        <div
          v-if="store.ingestionError"
          style="color: #ef4444; font-size: 0.85rem; margin-top: 8px"
        >
          Error: {{ store.ingestionError }}
        </div>

        <!-- Candidate Draft Item Review List -->
        <div
          v-if="store.candidateDraft"
          style="
            display: flex;
            flex-direction: column;
            gap: 16px;
            margin-top: 12px;
          "
          class="candidate-draft-section"
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              border-bottom: 1px solid var(--border);
              padding-bottom: 8px;
            "
          >
            <span style="font-size: 0.9rem; font-weight: 700">
              Candidate ID:
              <code
                style="
                  background-color: rgb(241 245 249);
                  padding: 2px 4px;
                  border-radius: 4px;
                "
                class="candidate-id"
                >{{ store.candidateDraft.id }}</code
              >
            </span>
            <span
              :class="[
                'badge',
                store.candidateDraft.status === 'PROMOTED'
                  ? 'lookup-valid'
                  : 'lookup-degraded',
              ]"
              style="font-size: 0.8rem; padding: 4px 8px; border-radius: 4px"
              class="candidate-status"
            >
              {{ store.candidateDraft.status }}
            </span>
          </div>

          <div
            style="font-size: 0.85rem; font-weight: bold; color: var(--primary)"
          >
            Candidate Items Under Review:
          </div>

          <div
            style="
              display: flex;
              flex-direction: column;
              gap: 12px;
              max-height: 400px;
              overflow-y: auto;
            "
          >
            <div
              v-for="item in Object.values(store.candidateDraft.items)"
              :key="item.id"
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
                display: flex;
                flex-direction: column;
                gap: 8px;
              "
              class="candidate-item-card"
            >
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: flex-start;
                "
              >
                <div>
                  <span
                    class="badge"
                    style="
                      background-color: rgb(226 232 240);
                      color: #475569;
                      font-size: 0.75rem;
                      text-transform: uppercase;
                      margin-right: 6px;
                    "
                  >
                    <!-- deid: ignore -->
                    {{ item.type }}
                  </span>
                  <strong class="item-label">{{
                    item.type === "visit" ? item.name : item.label
                  }}</strong>
                </div>

                <!-- Confidence badge and citations -->
                <div style="display: flex; align-items: center; gap: 6px">
                  <span
                    :class="[
                      'badge',
                      getConfidenceClass(item.confidence_level),
                    ]"
                    style="font-size: 0.7rem"
                    class="item-confidence"
                  >
                    {{ (item.confidence * 100).toFixed(0) }}% ({{
                      item.confidence_level
                    }})
                  </span>
                  <span
                    style="font-size: 0.75rem; color: #64748b"
                    title="Source Reference"
                    class="item-citation"
                  >
                    📖 {{ item.source_citation }}
                  </span>
                </div>
              </div>

              <!-- Item Edit Fields if user is editing -->
              <div
                v-if="editingItemId === item.id"
                style="
                  display: flex;
                  flex-direction: column;
                  gap: 8px;
                  background-color: #f8fafc;
                  padding: 8px;
                  border-radius: 6px;
                "
                class="item-edit-section"
              >
                <div class="form-group">
                  <label style="font-size: 0.75rem"
                    >Modify Candidate Name/Label</label
                  >
                  <input
                    v-model="editItemValue"
                    type="text"
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="edit-item-input"
                  />
                </div>
                <div class="form-group">
                  <label style="font-size: 0.75rem"
                    >Change Reason Justification (Mandatory)</label
                  >
                  <input
                    v-model="editItemReason"
                    type="text"
                    placeholder="Enter mandatory reason..."
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="edit-item-reason"
                  />
                </div>
                <div style="display: flex; justify-content: flex-end; gap: 6px">
                  <button class="btn btn-sm" @click="cancelEditItem">
                    Cancel
                  </button>
                  <button
                    class="btn btn-primary btn-sm save-edit-btn"
                    @click="saveEditItem(item.id)"
                  >
                    Save Edit
                  </button>
                </div>
              </div>

              <!-- Reason Prompt modal/input inline for Rejection -->
              <div
                v-else-if="rejectingItemId === item.id"
                style="
                  display: flex;
                  flex-direction: column;
                  gap: 8px;
                  background-color: #fef2f2;
                  padding: 8px;
                  border-radius: 6px;
                "
                class="item-reject-section"
              >
                <div class="form-group">
                  <label
                    style="
                      font-size: 0.75rem;
                      color: #ef4444;
                      font-weight: bold;
                    "
                    >Provide Rejection Reason (Mandatory)</label
                  >
                  <input
                    v-model="rejectItemReason"
                    type="text"
                    placeholder="Provide justification for rejecting candidate..."
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="reject-item-reason"
                  />
                </div>
                <div style="display: flex; justify-content: flex-end; gap: 6px">
                  <button class="btn btn-sm" @click="cancelRejectItem">
                    Cancel
                  </button>
                  <button
                    class="btn btn-primary btn-sm confirm-reject-btn"
                    style="background-color: #ef4444"
                    @click="confirmRejectItem(item.id)"
                  >
                    Confirm Reject
                  </button>
                </div>
              </div>

              <!-- General Item Actions & Metadata -->
              <div
                v-else
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: center;
                  font-size: 0.8rem;
                "
              >
                <div style="color: #64748b">
                  Status:
                  <span
                    :class="['badge', getStatusClass(item.review_status)]"
                    style="font-size: 0.75rem"
                    class="item-review-status"
                  >
                    {{ item.review_status }}
                  </span>
                  <span
                    v-if="item.reason"
                    style="margin-left: 6px; font-style: italic"
                    class="item-review-reason"
                  >
                    - "{{ item.reason }}"
                  </span>
                </div>

                <div
                  v-if="store.candidateDraft.status !== 'PROMOTED'"
                  style="display: flex; gap: 6px"
                >
                  <button
                    class="btn btn-sm accept-btn"
                    style="padding: 2px 8px; font-size: 0.75rem"
                    @click="acceptItem(item.id)"
                  >
                    ✔️ Accept
                  </button>
                  <button
                    class="btn btn-sm edit-btn"
                    style="padding: 2px 8px; font-size: 0.75rem"
                    @click="startEditItem(item)"
                  >
                    ✏️ Edit
                  </button>
                  <button
                    class="btn btn-sm reject-btn"
                    style="
                      padding: 2px 8px;
                      font-size: 0.75rem;
                      background-color: #fecaca;
                      color: #991b1b;
                    "
                    @click="startRejectItem(item.id)"
                  >
                    ❌ Reject
                  </button>
                </div>
              </div>
            </div>
          </div>

          <!-- Promotion Gating Controls -->
          <div
            v-if="store.candidateDraft.status !== 'PROMOTED'"
            style="
              border-top: 1px solid var(--border);
              padding-top: 12px;
              display: flex;
              flex-direction: column;
              gap: 12px;
            "
          >
            <div class="form-group">
              <label for="promote-change-reason" style="font-weight: bold"
                >Promotion Change Reason (Mandatory)</label
              >
              <input
                id="promote-change-reason"
                v-model="promoteChangeReason"
                type="text"
                placeholder="Enter justification to promote reviewed draft into formal protocol..."
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
                class="promote-change-reason"
              />
            </div>

            <div
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
              "
            >
              <span
                style="font-size: 0.8rem; color: #64748b"
                class="remaining-reviews-text"
              >
                {{
                  unreviewedCount === 0
                    ? "✅ All items reviewed. Ready to promote."
                    : `⚠️ ${unreviewedCount} items remaining to be reviewed.`
                }}
              </span>
              <button
                id="btn-promote-candidate"
                class="btn btn-primary"
                type="button"
                :disabled="
                  unreviewedCount > 0 ||
                  !promoteChangeReason.trim() ||
                  store.ingestionLoading
                "
                @click="promoteCandidate"
              >
                🚀 Promote Reviewed Candidate
              </button>
            </div>
          </div>
          <div
            v-else
            style="
              background-color: #f0fdf4;
              border: 1px solid #bbf7d0;
              border-radius: 8px;
              padding: 12px;
              color: #166534;
              font-size: 0.85rem;
              text-align: center;
            "
            class="promoted-success-banner"
          >
            🎉 This candidate draft has been successfully promoted to formal
            draft study version!
          </div>
        </div>
      </div>
    </div>

    <!-- Reason for Change Modal Dialog -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data field."
      :options="ecrfReasonOptions"
      default-option="Initial Entry"
      @confirm="saveChange"
      @cancel="cancelChange"
    />

    <!-- Conflict Resolution Modal Dialog -->
    <ConflictResolutionModal
      :show="showConflictModal"
      :conflict="activeConflict"
      @confirm="handleResolveConflict"
      @cancel="handleCancelConflict"
    />

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
            class="form-group"
            style="
              margin-bottom: 12px;
              display: flex;
              align-items: center;
              gap: 8px;
            "
          >
            <input
              id="reauth-simulate-delay"
              v-model="simulateDelay"
              type="checkbox"
              style="cursor: pointer"
            />
            <label
              for="reauth-simulate-delay"
              style="
                font-size: 0.8rem;
                color: #64748b;
                font-weight: 500;
                cursor: pointer;
                margin: 0;
              "
            >
              Simulate 65s delay (FDA 21 CFR Part 11 Timeout Test)
            </label>
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
            Verify &amp; Confirm
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, watch, onMounted, computed } from "vue";
import { useRoute } from "vue-router";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { soaClient } from "../api/soaClient";
import { validateField, debounce } from "ui"; // Consolidating debounce onto shared packages/ui (PR #566 alignment)
import ClinicalFormField from "../components/clinical/ClinicalFormField.vue";
import { evaluateAST } from "../evaluator.js";
import { terminologyClient } from "../api/terminologyClient";
import ReasonModal from "../components/ReasonModal.vue";
import ConflictResolutionModal from "../components/ConflictResolutionModal.vue";
import { useSyncStore } from "../stores/sync";
import { useNotificationsStore } from "../stores/notifications";
import { ClientSyncEngine } from "../utils/syncEngine";

const ecrfReasonOptions = [
  { value: "Initial Entry", text: "Initial Data Entry" },
  { value: "Typographical Error", text: "Correction of typographical error" },
  { value: "Re-measurement", text: "Re-measurement of vitals" },
  { value: "Transcription Error", text: "Correction of transcription error" },
  { value: "Other", text: "Other (specify below)" },
];

const store = useClinicalStore();
const authStore = useAuthStore();
const route = useRoute();

const syncStore = useSyncStore();
const syncEngine = new ClientSyncEngine();

if (typeof window !== "undefined") {
  window.syncStore = syncStore;
  window.syncEngine = syncEngine;
}

const showConflictModal = computed(
  () => syncStore.status === "CONFLICT_DETECTED"
);
const activeConflict = computed(() => syncStore.conflict);

async function handleResolveConflict({ strategy, reason }) {
  if (activeConflict.value && activeConflict.value.conflictItem) {
    await syncEngine.resolveConflict(
      activeConflict.value.conflictItem.deltaId,
      strategy,
      reason
    );
  }
}

function handleCancelConflict() {
  syncStore.setStatus("IDLE");
  syncStore.clearConflict();
}

// Consolidated lookup validation and state management (PR #566 alignment)
// Unifies and replaces legacy inline timer and request counters (conceptRequestIds, requestCounters, and lastLookupRequestIds)
const lookupStatuses = ref({});
const lookupRequestCounters = reactive({});
const debouncedLookups = {};

// Performs asynchronous validation against terminology service with strict stale-response protection
async function performConceptCodeValidation(fieldId, value) {
  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  // Increment counter atomically per field ID to act as our active stale-response guard
  const nextRequestId = (lookupRequestCounters[fieldId] || 0) + 1;
  lookupRequestCounters[fieldId] = nextRequestId;

  lookupStatuses.value[fieldId] = {
    status: "loading",
    message: "Searching terminology database...",
  };

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      changeReason: "Validate code",
    });

    // Stale guard check: discard if another request has been fired since
    if (nextRequestId !== lookupRequestCounters[fieldId]) {
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
        message:
          res.error_message ||
          "Terminology service degraded. Validation offline.",
      };
    }
  } catch (error) {
    if (nextRequestId !== lookupRequestCounters[fieldId]) {
      return;
    }
    lookupStatuses.value[fieldId] = {
      status: "degraded",
      message:
        error.message || "Terminology service degraded. Validation offline.",
    };
  }
}

// Retrieve or initialize the shared debounce wrapper around our consolidated validation
function getDebouncedLookup(fieldId) {
  if (!debouncedLookups[fieldId]) {
    debouncedLookups[fieldId] = debounce(async (value) => {
      await performConceptCodeValidation(fieldId, value);
    }, 300);
  }
  return debouncedLookups[fieldId];
}

function handleLookupInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  // Use shared debounce utility from packages/ui
  getDebouncedLookup(fieldId)(value);
}

// Sub-Issue 9 & 10 Subject, Visit, and SDV states
const selectedSubjectId = ref("SUBJ-001");
const selectedVisitId = ref("Screening");
const ecrfSessions = reactive({});
const sdvStates = reactive({}); // keyed by `${subjectId}:${visitId}:${fieldId}`

// Role Tester Toggle support
const demoRole = ref("site_investigator");

const activeUserRole = computed(() => {
  if (authStore.isAuthenticated) {
    const roles = authStore.normalizedRoles || [];
    if (roles.includes("cra") || roles.includes("monitor")) return "cra";
    if (roles.includes("site_investigator") || roles.includes("crc"))
      return "site_investigator";
    return roles[0] || "site_investigator";
  }
  return demoRole.value;
});

const isCraUser = computed(() => {
  return activeUserRole.value === "cra";
});

const selectedBatchFields = ref([]);
const simulateDelay = ref(false);

const isAuthorizedForBulkSdv = computed(() => {
  const role = activeUserRole.value;
  return role === "cra" || role === "monitor" || role === "data_manager";
});

function getSessionKey() {
  return `${selectedSubjectId.value}:${selectedVisitId.value}`;
}

function getSdvKey(fieldId) {
  return `${selectedSubjectId.value}:${selectedVisitId.value}:${fieldId}`;
}

// Handle SDV Toggle (Sub-Issue 10)
function handleSdvToggle(fieldId, checked) {
  pendingSdvToggle.value = { fieldId, checked };
  showReasonModal.value = true;
}

function initiateBatchVerify() {
  if (selectedBatchFields.value.length === 0) {
    alert("No fields selected for batch verification!");
    return;
  }
  reauthAction.value = "BULK_SDV";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

const pendingSdvToggle = ref(null);

function loadEcrfSession() {
  const key = getSessionKey();
  if (!ecrfSessions[key]) {
    ecrfSessions[key] = {
      values: {},
      queries: {},
    };
    store.ecrfFields.forEach((f) => {
      ecrfSessions[key].values[f.id] = "";
    });
  }

  // Swap Form values and queries references
  store.formValues = ecrfSessions[key].values;
  store.formQueries = ecrfSessions[key].queries;

  store.evaluateRules();
  // Initialize terminology lookups
  store.ecrfFields.forEach((field) => {
    if (field.type === "concept_code" && store.formValues[field.id]) {
      performConceptCodeValidation(field.id, store.formValues[field.id]);
    }
  });
}

// Save active form state back to sessions deep-watched
watch(
  () => store.formValues,
  (newValues) => {
    const key = getSessionKey();
    if (!ecrfSessions[key]) {
      ecrfSessions[key] = { values: {}, queries: {} };
    }
    ecrfSessions[key].values = newValues;
  },
  { deep: true }
);

watch(
  () => store.formQueries,
  (newQueries) => {
    const key = getSessionKey();
    if (!ecrfSessions[key]) {
      ecrfSessions[key] = { values: {}, queries: {} };
    }
    ecrfSessions[key].queries = newQueries;
  },
  { deep: true }
);

// Deep watch formValues to evaluate rules debounced
watch(
  () => store.formValues,
  () => {
    store.triggerValueChange();
  },
  { deep: true }
);

onMounted(() => {
  if (route && route.query) {
    if (route.query.studyId) store.activeStudyId = route.query.studyId;
    if (route.query.siteId) store.activeSiteId = route.query.siteId;
    if (route.query.subjectId) {
      store.activeSubjectId = route.query.subjectId;
      const sId = String(route.query.subjectId);
      if (sId.includes("002")) selectedSubjectId.value = "SUBJ-002";
      else if (sId.includes("003")) selectedSubjectId.value = "SUBJ-003";
      else selectedSubjectId.value = "SUBJ-001";
    }
    if (route.query.visitId) {
      store.activeVisitId = route.query.visitId;
      const vId = String(route.query.visitId);
      if (
        vId.toLowerCase().includes("week2") ||
        vId.toLowerCase().includes("week 2")
      )
        selectedVisitId.value = "Week2";
      else if (
        vId.toLowerCase().includes("week4") ||
        vId.toLowerCase().includes("week 4")
      )
        selectedVisitId.value = "Week4";
      else selectedVisitId.value = "Screening";
    }
  }
  loadEcrfSession();
});

// Reason Modal States
const showReasonModal = ref(false);
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

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues, evaluateAST);
  return res.valid ? null : res.message;
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
    showReasonModal.value = true;
  } else {
    commitChange(field, oldValue, newValue, "Initial Entry");
  }
}

function cancelChange() {
  if (pendingValueChange.value && pendingValueChange.value.targetEl) {
    if (pendingValueChange.value.targetEl.type === "radio") {
      // Vue handles radio binding automatically
    } else {
      pendingValueChange.value.targetEl.value =
        pendingValueChange.value.oldValue;
    }
  }
  showReasonModal.value = false;
  pendingValueChange.value = null;
  pendingSdvToggle.value = null;
}

function saveChange(finalReason) {
  if (pendingSdvToggle.value) {
    const { fieldId, checked } = pendingSdvToggle.value;
    const sKey = getSdvKey(fieldId);
    sdvStates[sKey] = checked;

    store.addLedgerBlock(
      "SDV_TOGGLE",
      {
        subjectId: selectedSubjectId.value,
        visitId: selectedVisitId.value,
        fieldId,
        is_sdv_verified: checked,
      },
      finalReason
    );

    pendingSdvToggle.value = null;
    showReasonModal.value = false;
    alert(`Source Document Verification (SDV) status updated and logged!`);
    return;
  }

  if (!pendingValueChange.value) return;

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

  // Check if field has an active SDV verification status
  const sKey = getSdvKey(field.id);
  if (sdvStates[sKey] === true) {
    sdvStates[sKey] = false;

    // Add ledger block for SDV_CLEAR
    store.addLedgerBlock(
      "SDV_CLEAR",
      {
        fieldId: field.id,
        label: field.label,
        subjectId: selectedSubjectId.value,
        visitId: selectedVisitId.value,
        oldValue,
        newValue,
      },
      "Verification cleared automatically due to field value modification"
    );

    // Dispatch alert to notifications store
    try {
      const notifStore = useNotificationsStore();
      const newNotif = {
        id:
          "notif-sdv-clear-" +
          Date.now() +
          "-" +
          Math.random().toString(36).substr(2, 4),
        recipient_user_id: store.user.username || "fderuiter",
        recipient_role: "monitor",
        category: "ALERTS",
        priority: "HIGH",
        channels: "IN_APP",
        message_content: `Verification cleared automatically: Field "${field.label}" was modified from "${oldValue}" to "${newValue}" for Subject ${selectedSubjectId.value}.`,
        related_entity_id: field.id,
        related_entity_type: "FIELD",
        status: "OPEN",
        delivery_state: "DELIVERED",
        created_at: new Date().toISOString(),
        created_by: "system",
      };
      notifStore.notifications.unshift(newNotif);
    } catch (e) {
      console.error("Failed to append notification alert", e);
    }
  }

  store.addLedgerBlock(
    "FIELD_CHANGE",
    {
      fieldId: field.id,
      label: field.label,
      cdash: field.cdash || "",
      oldValue,
      newValue,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    reason
  );
}

// Query Operations
function createQuery(fieldId, msgFromComponent = null) {
  const msg = msgFromComponent !== null ? msgFromComponent : "";
  if (!msg) {
    alert("Please enter a discrepancy message!");
    return;
  }

  const queryObj = {
    status: "OPEN",
    message: msg,
    createdBy: `${activeUserRole.value} (Client Monitor)`,
    createdAt: new Date().toISOString().slice(0, 10),
  };

  store.formQueries[fieldId] = queryObj;
  store.addLedgerBlock(
    "QUERY_CREATE",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    `Raised discrepancy: "${msg}"`
  );
}

function respondQuery(fieldId, respFromComponent = null) {
  const resp = respFromComponent !== null ? respFromComponent : "";
  if (!resp) {
    alert("Please enter a response!");
    return;
  }

  const queryObj = store.formQueries[fieldId];
  queryObj.status = "ANSWERED";
  queryObj.response = resp;
  queryObj.respondedBy = "Clinical Investigator / CRC";
  queryObj.respondedAt = new Date().toISOString().slice(0, 10);

  store.addLedgerBlock(
    "QUERY_RESPOND",
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
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
      queryObj.closedBy = `${username} (CRA Monitor)`;
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
          subjectId: selectedSubjectId.value,
          visitId: selectedVisitId.value,
          domain,
          testCode,
          query: queryObj,
        },
        "Discrepancy resolved and closed permanently by monitor."
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

      const studyId = store.currentUsdm.studyId || "STUDY-USDM-001";
      const targetType = signoffTargetType.value;
      const targetId =
        signoffTargetId.value === "custom"
          ? customTargetId.value
          : signoffTargetId.value;
      const targetIds = [targetId];
      const signingReason = signoffReason.value;

      // Compute canonical batch binding
      const normStudy = studyId.trim();
      const normType = targetType.trim().toUpperCase();
      const normIds = [...targetIds]
        .map((id) => String(id).trim())
        .sort()
        .join(",");
      const normReason = signingReason.trim();
      const bindingStr = `${normStudy}:${normType}:${normIds}:${normReason}`;

      // Calculate SHA-256 batchId
      const msgBuffer = new TextEncoder().encode(bindingStr);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const batchId = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");

      // 1. Obtain signature token
      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
          batchId,
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      // 2. Call batch sign-off
      const signoffRes = await soaClient.batchSignOff(
        {
          studyId,
          targetType,
          targetIds,
          signingReason,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "investigator",
          changeReason: signingReason,
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
  } else if (action === "BULK_SDV") {
    try {
      reauthError.value = "";

      const studyId = store.currentUsdm.studyId || "STUDY-USDM-001";
      const fieldsToVerify = [...selectedBatchFields.value];
      const signingReason = "Batch Source Data Verification (SDV)";

      // Calculate SHA-256 batchId of our selected fields
      const normStudy = studyId.trim();
      const normFields = fieldsToVerify.sort().join(",");
      const bindingStr = `${normStudy}:SDV:${normFields}:${signingReason}`;

      const msgBuffer = new TextEncoder().encode(bindingStr);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const batchId = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");

      // Start dual-factor credentials verification
      let tokenRequestedAt = Date.now();
      if (simulateDelay.value) {
        tokenRequestedAt -= 65000; // shift back to simulate 65s expired token
      }

      // 1. Obtain signature token
      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
          batchId,
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      // Check for compliance lockout: 60-second authentication window limit
      const elapsed = (Date.now() - tokenRequestedAt) / 1000;
      if (elapsed > 60) {
        throw new Error(
          "Compliance Lockout: The electronic signature verification token is older than 60 seconds."
        );
      }

      // 2. Call batch sign-off API
      await soaClient.batchSignOff(
        {
          studyId,
          targetType: "FORM",
          targetIds: fieldsToVerify,
          signingReason,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "monitor",
          changeReason: signingReason,
          sigToken,
        },
        authStore.accessToken
      );

      // 3. Update local SDV states for each verified field and write to ledger
      for (const fieldId of fieldsToVerify) {
        const sKey = getSdvKey(fieldId);
        sdvStates[sKey] = true;

        store.addLedgerBlock(
          "SDV_TOGGLE",
          {
            subjectId: selectedSubjectId.value,
            visitId: selectedVisitId.value,
            fieldId,
            is_sdv_verified: true,
          },
          "Batch Source Data Verification (SDV) confirmed"
        );
      }

      // Clean up variables & UI state
      selectedBatchFields.value = [];
      showReauthModal.value = false;
      reauthTotp.value = "";
      alert(
        `Identity verified. Batch Source Data Verification (SDV) completed successfully for selected fields!`
      );
    } catch (err) {
      reauthPassword.value = "";
      reauthTotp.value = "";

      if (err.message === "REAUTHENTICATION_REQUIRED" || err.status === 401) {
        reauthError.value =
          "Identity verification expired or invalid. Please try again.";
        showReauthModal.value = true;
      } else {
        reauthError.value = err.message || "Failed to complete batch SDV.";
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
    {
      fieldId,
      query: queryObj,
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    "Investigator response was rejected by clinical monitor."
  );
}

function clearForm() {
  store.ecrfFields.forEach((f) => {
    store.formValues[f.id] = "";
    delete store.formQueries[f.id];
  });
  store.addLedgerBlock(
    "FORM_CLEAR",
    {
      formId: "VS_DEMO",
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
    },
    "All eCRF form fields cleared by clinical staff."
  );
}

function submitEcrf() {
  let allValid = true;
  let errMsgs = [];

  store.ecrfFields.forEach((f) => {
    const res = validateField(
      f,
      store.formValues[f.id],
      store.formValues,
      evaluateAST
    );
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
      subjectId: selectedSubjectId.value,
      visitId: selectedVisitId.value,
      formValues: store.formValues,
      formQueries: store.formQueries,
    },
    "eCRF successfully verified, finalized, and electronically submitted."
  );

  alert(
    "eCRF Session successfully submitted to secure cryptographic database!"
  );
}

// Ingestion Review Setup Variables & Logic
const fileInputRef = ref(null);
const selectedFileName = ref("");
const editingItemId = ref(null);
const editItemValue = ref("");
const editItemReason = ref("");
const rejectingItemId = ref(null);
const rejectItemReason = ref("");
const promoteChangeReason = ref("");

function triggerFileSelect() {
  if (fileInputRef.value) {
    fileInputRef.value.click();
  }
}

const unreviewedCount = computed(() => {
  if (!store.candidateDraft || !store.candidateDraft.items) return 0;
  return Object.values(store.candidateDraft.items).filter(
    (item) => item.review_status === "PENDING"
  ).length;
});

async function triggerDocumentUpload(event) {
  const file = event.target.files[0];
  if (!file) return;

  selectedFileName.value = file.name;
  try {
    await store.uploadProtocolDocument(
      file,
      "Uploader triggers protocol ingestion scan."
    );
    alert("Protocol Document ingested successfully. Candidate draft loaded.");
  } catch (err) {
    alert("Ingestion failed: " + err.message);
  }
}

function getConfidenceClass(level) {
  if (level === "auto") return "lookup-valid";
  if (level === "needs-review") return "lookup-degraded";
  return "lookup-invalid";
}

function getStatusClass(status) {
  if (status === "ACCEPTED") return "lookup-valid";
  if (status === "EDITED") return "lookup-degraded";
  if (status === "REJECTED") return "lookup-invalid";
  return "";
}

async function acceptItem(itemId) {
  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "ACCEPTED",
      "Accepted by clinical reviewer"
    );
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

function startEditItem(item) {
  editingItemId.value = item.id;
  editItemValue.value = item.type === "visit" ? item.name : item.label;
  editItemReason.value = "";
}

function cancelEditItem() {
  editingItemId.value = null;
  editItemValue.value = "";
  editItemReason.value = "";
}

async function saveEditItem(itemId) {
  if (!editItemReason.value.trim()) {
    alert("Change reason justification is mandatory for edits!");
    return;
  }
  const item = store.candidateDraft.items[itemId];
  const payload =
    item.type === "visit"
      ? { name: editItemValue.value }
      : { label: editItemValue.value };

  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "EDITED",
      editItemReason.value,
      payload
    );
    editingItemId.value = null;
    editItemValue.value = "";
    editItemReason.value = "";
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

function startRejectItem(itemId) {
  rejectingItemId.value = itemId;
  rejectItemReason.value = "";
}

function cancelRejectItem() {
  rejectingItemId.value = null;
  rejectItemReason.value = "";
}

async function confirmRejectItem(itemId) {
  if (!rejectItemReason.value.trim()) {
    alert("Change reason justification is mandatory for rejection!");
    return;
  }
  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "REJECTED",
      rejectItemReason.value
    );
    rejectingItemId.value = null;
    rejectItemReason.value = "";
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

async function promoteCandidate() {
  if (!promoteChangeReason.value.trim()) {
    alert("Promotion change reason justification is mandatory!");
    return;
  }
  try {
    await store.promoteCandidateDraft(
      store.candidateDraft.id,
      promoteChangeReason.value
    );
    alert("Candidate promoted successfully into formal DRAFT version!");
    promoteChangeReason.value = "";
  } catch (err) {
    alert("Promotion failed: " + err.message);
  }
}
</script>
