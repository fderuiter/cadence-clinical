<template>
  <div id="section-econsent-authoring" class="dashboard-section active">
    <div class="section-header">
      <h2>eConsent Template Authoring</h2>
      <p>
        Draft, compose, order clauses, specify comprehension checks, and publish
        eConsent templates.
      </p>
    </div>

    <!-- Inline access-denied defense-in-depth card -->
    <div
      v-if="!hasEditAccess"
      class="card"
      id="access-denied-card"
      style="
        border-left: 4px solid var(--error);
        background-color: var(--error-bg);
        padding: 24px;
      "
    >
      <div style="display: flex; gap: 16px; align-items: flex-start">
        <span style="font-size: 2rem">🚫</span>
        <div>
          <h3
            style="color: var(--error); font-weight: bold; margin-bottom: 8px"
          >
            21 CFR Part 11 Role Gating - Access Denied
          </h3>
          <p
            style="
              color: var(--neutral-dark);
              font-size: 0.95rem;
              line-height: 1.6;
            "
          >
            You do not have the required <strong>SPONSOR_DESIGNER</strong>,
            <strong>DATA_MANAGER</strong>, or
            <strong>SPONSOR_ADMIN</strong> role to edit consent templates.
            Please authenticate with an authorized clinical token.
          </p>
        </div>
      </div>
    </div>

    <!-- Authorized view -->
    <div
      v-else
      class="grid-2"
      style="display: grid; grid-template-columns: 1fr 1fr; gap: 24px"
    >
      <!-- Templates List Pane -->
      <div
        class="card"
        style="display: flex; flex-direction: column; height: fit-content"
      >
        <div
          class="card-title"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
          "
        >
          <span>Consent Templates</span>
          <span
            v-if="loading"
            style="font-size: 0.85rem; color: #64748b"
            role="status"
            >Loading...</span
          >
        </div>

        <!-- Connection Error Banner if any -->
        <div
          v-if="connectionError"
          class="connection-error"
          style="
            background-color: var(--warning-bg);
            border: 1px solid var(--warning);
            color: var(--warning);
            padding: 10px;
            border-radius: 6px;
            margin-bottom: 12px;
            font-size: 0.85rem;
          "
        >
          <strong>API Mode Degraded:</strong> Running in Local Sandbox VM. Sync
          with GxP Server offline.
        </div>

        <!-- List container -->
        <div
          style="
            flex: 1;
            max-height: 600px;
            overflow-y: auto;
            margin-bottom: 16px;
            padding-right: 4px;
          "
        >
          <div
            v-if="templates.length === 0"
            style="
              color: #64748b;
              font-style: italic;
              padding: 12px 0;
              text-align: center;
            "
          >
            No templates configured. Click "Create New Template" to get started.
          </div>
          <div v-else>
            <div
              v-for="tpl in templates"
              :key="tpl.template_id + '-' + tpl.version_index"
              class="template-card"
              :id="'template-card-' + tpl.template_id"
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 16px;
                margin-bottom: 12px;
                background-color: var(--neutral-light);
                transition: all 0.2s;
              "
            >
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: flex-start;
                  margin-bottom: 8px;
                "
              >
                <strong style="color: var(--accent); font-size: 0.95rem">{{
                  tpl.template_name
                }}</strong>
                <div style="display: flex; gap: 6px">
                  <button
                    class="btn btn-edit"
                    style="
                      padding: 4px 10px;
                      font-size: 0.75rem;
                      cursor: pointer;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      background: white;
                    "
                    @click="openEditor(tpl)"
                  >
                    Edit
                  </button>
                  <button
                    v-if="!tpl.is_published"
                    class="btn btn-publish"
                    style="
                      padding: 4px 10px;
                      font-size: 0.75rem;
                      background-color: var(--success);
                      color: white;
                      border: none;
                      border-radius: 4px;
                      cursor: pointer;
                    "
                    @click="promptPublish(tpl)"
                  >
                    Publish
                  </button>
                  <span
                    v-else
                    class="badge status-badge"
                    style="
                      background-color: var(--success);
                      color: white;
                      font-size: 0.7rem;
                      padding: 4px 8px;
                      border-radius: 4px;
                    "
                  >
                    Published
                  </span>
                </div>
              </div>
              <div style="font-size: 0.85rem; color: var(--neutral-dark)">
                <div>
                  <strong>Template ID:</strong>
                  <code>{{ tpl.template_id }}</code>
                </div>
                <div><strong>Study:</strong> {{ tpl.study_id }}</div>
                <div>
                  <strong>Version:</strong> {{ tpl.version_index }} (Protocol:
                  {{ tpl.protocol_version }})
                </div>
                <div>
                  <strong>Re-consent required:</strong>
                  {{ tpl.requires_reconsent ? "Yes" : "No" }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <button
          class="btn btn-primary"
          id="btn-create-template"
          style="
            width: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 8px;
          "
          @click="openEditor(null)"
        >
          <span>➕</span> Create New Template
        </button>
      </div>

      <!-- Template Editor Split Pane -->
      <div
        v-if="showEditor"
        class="card"
        id="template-editor-pane"
        style="display: flex; flex-direction: column"
      >
        <div class="card-title" id="editor-title" style="margin-bottom: 16px">
          {{ isEdit ? "Edit Consent Template" : "Compose Consent Template" }}
        </div>

        <!-- Accessible inline error banner -->
        <div
          v-if="validationError"
          class="validation-error-msg"
          id="editor-validation-error"
          style="
            background-color: #fef2f2;
            border: 1px solid #fee2e2;
            color: #b91c1c;
            padding: 12px;
            border-radius: 6px;
            margin-bottom: 16px;
            font-size: 0.85rem;
            font-weight: 600;
          "
          role="status"
          aria-live="polite"
        >
          ⚠️ {{ validationError }}
        </div>

        <div
          style="
            display: flex;
            flex-direction: column;
            gap: 16px;
            overflow-y: auto;
            max-height: 700px;
            padding-right: 4px;
          "
        >
          <!-- Metadata Configuration -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Metadata & Configuration
            </legend>
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
              <div class="form-group">
                <label
                  for="input-study-id"
                  style="
                    display: block;
                    margin-bottom: 4px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                  >Study ID</label
                >
                <input
                  v-model="editorForm.study_id"
                  id="input-study-id"
                  type="text"
                  placeholder="e.g. study-01"
                  required
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                />
              </div>

              <div class="form-group">
                <label
                  for="input-template-name"
                  style="
                    display: block;
                    margin-bottom: 4px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                  >Template Name</label
                >
                <input
                  v-model="editorForm.template_name"
                  id="input-template-name"
                  type="text"
                  placeholder="e.g. Main Informed Consent"
                  required
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                />
              </div>

              <div class="form-group">
                <label
                  for="input-protocol-version"
                  style="
                    display: block;
                    margin-bottom: 4px;
                    font-weight: 600;
                    font-size: 0.85rem;
                  "
                  >Protocol Version</label
                >
                <input
                  v-model="editorForm.protocol_version"
                  id="input-protocol-version"
                  type="text"
                  placeholder="e.g. v1.2"
                  required
                  style="
                    width: 100%;
                    padding: 8px;
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                />
              </div>

              <div
                class="form-group"
                style="display: flex; align-items: center; margin-top: 24px"
              >
                <input
                  v-model="editorForm.requires_reconsent"
                  id="checkbox-reconsent"
                  type="checkbox"
                  style="margin-right: 8px"
                />
                <label
                  for="checkbox-reconsent"
                  style="font-weight: 600; font-size: 0.85rem; cursor: pointer"
                  >Requires Re-consent</label
                >
              </div>
            </div>
          </fieldset>

          <!-- Clause Composition -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Consent Clauses & Order
            </legend>
            <p style="font-size: 0.8rem; color: #64748b; margin-bottom: 12px">
              Add and sort referenced clause IDs below in their exact display
              order.
            </p>

            <div
              style="display: flex; flex-direction: column; gap: 8px"
              id="clauses-list-wrapper"
            >
              <div
                v-for="(clauseId, index) in editorForm.clauses"
                :key="index"
                class="clause-order-row"
                style="display: flex; gap: 8px; align-items: center"
              >
                <span
                  style="
                    font-family: monospace;
                    font-size: 0.85rem;
                    width: 24px;
                    text-align: center;
                  "
                  >{{ index + 1 }}.</span
                >
                <input
                  v-model="editorForm.clauses[index]"
                  type="text"
                  placeholder="e.g. clause-risk-disclosure"
                  class="clause-id-input"
                  style="
                    flex: 1;
                    padding: 6px 10px;
                    border: 1px solid var(--border);
                    border-radius: 4px;
                  "
                />
                <button
                  type="button"
                  class="btn btn-move-up"
                  style="padding: 4px 8px; font-size: 0.75rem"
                  :disabled="index === 0"
                  @click="moveClause(index, -1)"
                >
                  ▲
                </button>
                <button
                  type="button"
                  class="btn btn-move-down"
                  style="padding: 4px 8px; font-size: 0.75rem"
                  :disabled="index === editorForm.clauses.length - 1"
                  @click="moveClause(index, 1)"
                >
                  ▼
                </button>
                <button
                  type="button"
                  class="btn btn-remove-clause"
                  style="
                    padding: 4px 8px;
                    font-size: 0.75rem;
                    background-color: var(--error);
                    color: white;
                    border: none;
                    border-radius: 4px;
                  "
                  @click="removeClauseRow(index)"
                >
                  ✕
                </button>
              </div>
            </div>

            <button
              type="button"
              class="btn btn-secondary btn-add-clause"
              style="margin-top: 12px; padding: 6px 12px; font-size: 0.8rem"
              @click="addClauseRow"
            >
              ➕ Add Clause Reference
            </button>
          </fieldset>

          <!-- Workflow Steps -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Workflow Steps Definition
            </legend>
            <p style="font-size: 0.8rem; color: #64748b; margin-bottom: 12px">
              Templates must include a comprehension-check and a signature step
              to be published.
            </p>

            <div
              style="display: flex; flex-direction: column; gap: 8px"
              id="steps-list-wrapper"
            >
              <div
                v-for="(step, index) in editorForm.workflow_steps"
                :key="index"
                class="step-config-row"
                style="
                  border: 1px dashed var(--border);
                  padding: 10px;
                  border-radius: 6px;
                  display: flex;
                  flex-direction: column;
                  gap: 8px;
                "
              >
                <div
                  style="
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                  "
                >
                  <strong>Step #{{ index + 1 }} - {{ step.type }}</strong>
                  <button
                    type="button"
                    class="btn btn-remove-step"
                    style="
                      padding: 2px 6px;
                      font-size: 0.7rem;
                      background-color: var(--error);
                      color: white;
                      border: none;
                      border-radius: 4px;
                    "
                    @click="removeStep(index)"
                  >
                    Remove
                  </button>
                </div>
                <div v-if="step.type === 'comprehension_check'">
                  <label
                    :for="'step-question-' + index"
                    style="font-size: 0.75rem"
                    >Verification prompt</label
                  >
                  <input
                    v-model="step.question"
                    :id="'step-question-' + index"
                    type="text"
                    placeholder="Understood and agree to risk factors?"
                    style="width: 100%; padding: 4px 8px; font-size: 0.8rem"
                  />
                </div>
                <div v-else-if="step.type === 'signature_placeholder'">
                  <label :for="'step-role-' + index" style="font-size: 0.75rem"
                    >Required Role</label
                  >
                  <input
                    v-model="step.role"
                    :id="'step-role-' + index"
                    type="text"
                    placeholder="subject"
                    style="width: 100%; padding: 4px 8px; font-size: 0.8rem"
                  />
                </div>
              </div>
            </div>

            <div style="display: flex; gap: 8px; margin-top: 12px">
              <button
                type="button"
                class="btn btn-secondary btn-add-step-comp"
                style="padding: 6px 12px; font-size: 0.8rem"
                @click="addWorkflowStep('comprehension_check')"
              >
                ➕ Add Comprehension Check Step
              </button>
              <button
                type="button"
                class="btn btn-secondary btn-add-step-sig"
                style="padding: 6px 12px; font-size: 0.8rem"
                @click="addWorkflowStep('signature_placeholder')"
              >
                ➕ Add Signature Placeholder Step
              </button>
            </div>
          </fieldset>

          <!-- Composed Live Preview -->
          <fieldset
            style="
              border: 1px solid var(--border);
              border-radius: 8px;
              padding: 16px;
            "
          >
            <legend
              style="padding: 0 8px; font-weight: bold; color: var(--accent)"
            >
              Live Preview (Compose Dry-run)
            </legend>
            <div
              style="
                background-color: #f1f5f9;
                padding: 12px;
                border-radius: 8px;
                border: 1px solid var(--border);
                min-height: 100px;
              "
            >
              <!-- deid: ignore -->
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: center;
                  margin-bottom: 8px;
                "
              >
                <strong style="color: var(--primary); font-size: 0.85rem"
                  >Hydrated Clauses Preview:</strong
                >
                <button
                  type="button"
                  class="btn btn-preview"
                  style="padding: 4px 10px; font-size: 0.75rem"
                  @click="triggerComposePreview"
                >
                  Refresh Preview
                </button>
              </div>
              <div
                v-if="previewLoading"
                style="font-size: 0.8rem; font-style: italic"
              >
                Loading composition...
              </div>
              <div
                v-else-if="previewClauses.length === 0"
                style="font-size: 0.8rem; color: #64748b; font-style: italic"
              >
                No clauses compiled or template not yet saved on server.
              </div>
              <div
                v-else
                style="display: flex; flex-direction: column; gap: 8px"
              >
                <div
                  v-for="c in previewClauses"
                  :key="c.clause_id"
                  style="border-bottom: 1px solid #e2e8f0; padding-bottom: 8px"
                >
                  <h4
                    style="
                      margin: 0 0 4px 0;
                      font-size: 0.85rem;
                      color: var(--accent);
                    "
                  >
                    {{ c.title }}
                  </h4>
                  <p
                    style="
                      margin: 0;
                      font-size: 0.8rem;
                      line-height: 1.4;
                      color: var(--neutral-dark);
                    "
                  >
                    {{ c.text }}
                  </p>
                </div>
              </div>
            </div>
          </fieldset>
        </div>

        <!-- Action buttons -->
        <div
          style="
            margin-top: 20px;
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            border-top: 1px solid var(--border);
            padding-top: 16px;
          "
        >
          <button
            class="btn btn-cancel"
            style="padding: 8px 16px; cursor: pointer"
            @click="showEditor = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary btn-save"
            style="padding: 8px 16px; cursor: pointer"
            @click="promptSave"
          >
            Save Template Version
          </button>
        </div>
      </div>
    </div>

    <!-- Reason Modal with consent specific options -->
    <ReasonModal
      :show="showReasonModal"
      title="Electronic Signature Reason Capture"
      description="In compliance with 21 CFR Part 11 and clinical data integrity controls, please state your justification for this template change."
      :options="consentReasonOptions"
      default-option="Protocol Amendment"
      @confirm="confirmChangeReason"
      @cancel="closeReasonModal"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, reactive } from "vue";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { econsentService } from "../api/econsent";
import ReasonModal from "../components/ReasonModal.vue";

const authStore = useAuthStore();
const clinicalStore = useClinicalStore();

const consentReasonOptions = [
  { value: "Protocol Amendment", text: "Protocol Amendment" },
  { value: "Regulatory Update", text: "Regulatory Update" },
  { value: "Language Correction", text: "Language Correction" },
  { value: "Initial Setup", text: "Initial Template Drafting" },
  { value: "Other", text: "Other (specify below)" },
];

const hasEditAccess = computed(() => {
  const roles = authStore.normalizedRoles || [];
  return roles.some(
    (role) =>
      role === "sponsor_designer" ||
      role === "sponsor_admin" ||
      role === "data_manager" ||
      role === "admin"
  );
});

// State
const templates = ref([]);
const loading = ref(false);
const connectionError = ref(false);
const showEditor = ref(false);
const isEdit = ref(false);
const activeTemplateId = ref(null);
const validationError = ref("");

// Editor Form Reactive State
const editorForm = reactive({
  study_id: "",
  template_name: "",
  protocol_version: "",
  requires_reconsent: false,
  clauses: [],
  workflow_steps: [],
});

// Preview State
const previewLoading = ref(false);
const previewClauses = ref([]);

// Reason Modal Staging
const showReasonModal = ref(false);
const pendingAction = ref(null); // { type: 'save' | 'publish', payload?: any, template_id?: string }

// Fetch existing templates from econsent microservice
async function fetchTemplates() {
  loading.value = true;
  connectionError.value = false;
  try {
    const data = await econsentService.listTemplates(
      {},
      { changeReason: "List templates" }
    );
    templates.value = data || [];
  } catch (err) {
    console.warn("eConsent templates service query degraded:", err);
    connectionError.value = true;
    templates.value = [];
  } finally {
    loading.value = false;
  }
}

function openEditor(template = null) {
  validationError.value = "";
  previewClauses.value = [];
  if (template) {
    isEdit.value = true;
    activeTemplateId.value = template.template_id;
    editorForm.study_id = template.study_id || "";
    editorForm.template_name = template.template_name || "";
    editorForm.protocol_version = template.protocol_version || "";
    editorForm.requires_reconsent = template.requires_reconsent || false;
    editorForm.clauses = [...(template.clauses || [])];
    editorForm.workflow_steps = JSON.parse(
      JSON.stringify(template.workflow_steps || [])
    );
  } else {
    isEdit.value = false;
    activeTemplateId.value = null;
    editorForm.study_id = "";
    editorForm.template_name = "";
    editorForm.protocol_version = "";
    editorForm.requires_reconsent = false;
    editorForm.clauses = [""];
    editorForm.workflow_steps = [
      {
        type: "comprehension_check",
        question: "Do you understand the study conditions?",
      },
      { type: "signature_placeholder", role: "subject" },
    ];
  }
  showEditor.value = true;
  if (isEdit.value) {
    triggerComposePreview();
  }
}

// Order Management Helpers
function moveClause(index, direction) {
  const targetIndex = index + direction;
  if (targetIndex < 0 || targetIndex >= editorForm.clauses.length) return;
  const temp = editorForm.clauses[index];
  editorForm.clauses[index] = editorForm.clauses[targetIndex];
  editorForm.clauses[targetIndex] = temp;
}

function addClauseRow() {
  editorForm.clauses.push("");
}

function removeClauseRow(index) {
  editorForm.clauses.splice(index, 1);
}

function addWorkflowStep(type) {
  if (type === "comprehension_check") {
    editorForm.workflow_steps.push({
      type: "comprehension_check",
      question: "",
    });
  } else {
    editorForm.workflow_steps.push({
      type: "signature_placeholder",
      role: "subject",
    });
  }
}

function removeStep(index) {
  editorForm.workflow_steps.splice(index, 1);
}

// Dry-run Compose Preview Helper
async function triggerComposePreview() {
  if (!activeTemplateId.value) return;
  previewLoading.value = true;
  try {
    const res = await econsentService.composeTemplate(activeTemplateId.value);
    previewClauses.value = res.clauses || [];
  } catch (err) {
    console.warn("Live template composition failed:", err);
    previewClauses.value = [];
  } finally {
    previewLoading.value = false;
  }
}

// Stage-save Actions
function promptSave() {
  validationError.value = "";
  if (!editorForm.study_id.trim()) {
    validationError.value = "Study ID is strictly required.";
    return;
  }
  if (!editorForm.template_name.trim()) {
    validationError.value = "Template Name is strictly required.";
    return;
  }
  if (!editorForm.protocol_version.trim()) {
    validationError.value = "Protocol Version is strictly required.";
    return;
  }

  // Filter out any blank clause inputs
  const cleanedClauses = editorForm.clauses.filter((c) => c.trim() !== "");

  const payload = {
    study_id: editorForm.study_id.trim(),
    template_name: editorForm.template_name.trim(),
    protocol_version: editorForm.protocol_version.trim(),
    requires_reconsent: editorForm.requires_reconsent,
    clauses: cleanedClauses,
    workflow_steps: editorForm.workflow_steps,
    reason_for_change: "Change Reason Default",
  };

  pendingAction.value = {
    type: "save",
    payload,
  };
  showReasonModal.value = true;
}

function promptPublish(tpl) {
  validationError.value = "";
  pendingAction.value = {
    type: "publish",
    template_id: tpl.template_id,
  };
  showReasonModal.value = true;
}

function closeReasonModal() {
  showReasonModal.value = false;
  pendingAction.value = null;
}

async function confirmChangeReason(reasonText) {
  const action = pendingAction.value;
  if (!action) return;

  try {
    if (action.type === "save") {
      action.payload.reason_for_change = reasonText;
      let res;
      if (isEdit.value) {
        res = await econsentService.updateTemplate(
          activeTemplateId.value,
          action.payload,
          {
            changeReason: reasonText,
          }
        );
      } else {
        res = await econsentService.createTemplate(action.payload, {
          changeReason: reasonText,
        });
      }

      // Add to local audit/compliance block if store is loaded
      if (clinicalStore && typeof clinicalStore.addLedgerBlock === "function") {
        await clinicalStore.addLedgerBlock(
          "ECONSENT_TEMPLATE_SAVE",
          { template_id: res.template_id, version_index: res.version_index },
          reasonText
        );
      }
    } else if (action.type === "publish") {
      const res = await econsentService.publishTemplate(action.template_id, {
        changeReason: reasonText,
      });

      if (clinicalStore && typeof clinicalStore.addLedgerBlock === "function") {
        await clinicalStore.addLedgerBlock(
          "ECONSENT_TEMPLATE_PUBLISH",
          { template_id: res.template_id, version_index: res.version_index },
          reasonText
        );
      }
    }

    showEditor.value = false;
    showReasonModal.value = false;
    pendingAction.value = null;
    await fetchTemplates();
  } catch (err) {
    // Render flat { detail: string } validation failures
    validationError.value =
      err.message ||
      err.data?.detail ||
      "An error occurred during GxP action validation.";
    showReasonModal.value = false;
  }
}

onMounted(() => {
  if (hasEditAccess.value) {
    fetchTemplates();
  }
});
</script>

<style scoped>
.template-card:hover {
  border-color: var(--accent) !important;
  box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.05);
}
</style>
