<template>
  <div id="section-econsent-authoring" class="dashboard-section active">
    <div class="section-header">
      <h2>{{ $t("econsent.authoring.title") }}</h2>
      <p>
        {{ $t("econsent.authoring.description") }}
      </p>
    </div>

    <!-- Inline access-denied defense-in-depth card -->
    <div
      v-if="!hasEditAccess"
      id="access-denied-card"
      class="card access-denied-banner"
    >
      <div class="access-denied-row">
        <ClinicalIcon name="x-circle" :size="20" class="access-denied-icon" />
        <div>
          <h3 class="access-denied-heading">
            {{ $t("econsent.authoring.accessDeniedTitle") }}
          </h3>
          <p class="access-denied-copy">
            {{ $t("econsent.authoring.accessDeniedMsg") }}
          </p>
        </div>
      </div>
    </div>

    <!-- Authorized view -->
    <ClinicalSplitWorkspace
      v-else
      ref="consentSplitRef"
      v-model="showEditor"
      :inline-open="showEditor"
      :has-selection="true"
      keep-mounted
      :teleport="false"
      master-width="minmax(0, 1fr)"
      detail-width="minmax(0, 1fr)"
      gap="var(--spacing-lg, 24px)"
      :drawer-title="
        isEdit
          ? $t('econsent.authoring.editTitle')
          : $t('econsent.authoring.composeTitle')
      "
      drawer-size="xl"
    >
      <template #master>
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
            <span>{{ $t("econsent.authoring.templatesTitle") }}</span>
            <span
              v-if="loading"
              style="
                font-size: 0.85rem;
                color: var(--semantic-color-text-secondary);
              "
              role="status"
              >{{ $t("econsent.authoring.loading") }}</span
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
            {{ $t("econsent.authoring.degradedMsg") }}
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
                color: var(--semantic-color-text-secondary);
                font-style: italic;
                padding: 12px 0;
                text-align: center;
              "
            >
              {{ $t("econsent.authoring.noTemplates") }}
            </div>
            <div v-else>
              <div
                v-for="tpl in templates"
                :id="'template-card-' + tpl.template_id"
                :key="tpl.template_id + '-' + tpl.version_index"
                class="template-card"
                style="
                  border: 1px solid var(--border);
                  border-radius: 8px;
                  padding: 16px;
                  margin-bottom: 12px;
                  background-color: var(--neutral-light);
                  transition:
                    background-color 0.2s,
                    border-color 0.2s;
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
                      type="button"
                      class="btn btn-edit"
                      :aria-current="
                        showEditor &&
                        isEdit &&
                        activeTemplateId === tpl.template_id
                          ? 'true'
                          : undefined
                      "
                      style="
                        padding: 4px 10px;
                        font-size: 0.75rem;
                        cursor: pointer;
                        border: 1px solid var(--border);
                        border-radius: 4px;
                        background: var(--semantic-color-surface);
                      "
                      @click="openEditor(tpl, $event)"
                    >
                      {{ $t("econsent.authoring.edit") }}
                    </button>
                    <button
                      v-if="!tpl.is_published"
                      class="btn btn-publish"
                      style="
                        padding: 4px 10px;
                        font-size: 0.75rem;
                        background-color: var(
                          --semantic-color-success-interactive
                        );
                        color: var(--semantic-color-text-on-solid);
                        border: none;
                        border-radius: 4px;
                        cursor: pointer;
                      "
                      @click="promptPublish(tpl)"
                    >
                      {{ $t("econsent.authoring.publish") }}
                    </button>
                    <span
                      v-else
                      class="badge status-badge"
                      style="
                        background-color: var(
                          --semantic-color-success-interactive
                        );
                        color: var(--semantic-color-text-on-solid);
                        font-size: var(--font-size-2xs);
                        padding: 4px 8px;
                        border-radius: 4px;
                      "
                    >
                      {{ $t("econsent.authoring.published") }}
                    </span>
                  </div>
                </div>
                <div style="font-size: 0.85rem; color: var(--neutral-dark)">
                  <div>
                    <strong>{{ $t("econsent.authoring.templateId") }}</strong>
                    <code>{{ tpl.template_id }}</code>
                  </div>
                  <div>
                    <strong>{{ $t("econsent.authoring.study") }}</strong>
                    {{ tpl.study_id }}
                  </div>
                  <div>
                    <strong>{{ $t("econsent.authoring.version") }}</strong>
                    {{ tpl.version_index }} ({{
                      $t("econsent.authoring.protocol")
                    }}
                    {{ tpl.protocol_version }})
                  </div>
                  <div>
                    <strong>{{
                      $t("econsent.authoring.reconsentRequired")
                    }}</strong>
                    {{
                      tpl.requires_reconsent
                        ? $t("econsent.authoring.yes")
                        : $t("econsent.authoring.no")
                    }}
                  </div>
                </div>
              </div>
            </div>
          </div>

          <button
            id="btn-create-template"
            type="button"
            class="btn btn-primary"
            style="
              width: 100%;
              display: flex;
              align-items: center;
              justify-content: center;
              gap: 8px;
            "
            @click="openEditor(null, $event)"
          >
            <span><ClinicalIcon name="plus" :size="16" /></span>
            {{ $t("econsent.authoring.createNew") }}
          </button>
        </div>
      </template>

      <template #detail>
        <!-- Template Editor Split Pane -->
        <div
          id="template-editor-pane"
          class="card"
          style="display: flex; flex-direction: column"
        >
          <div id="editor-title" class="card-title" style="margin-bottom: 16px">
            {{
              isEdit
                ? $t("econsent.authoring.editTitle")
                : $t("econsent.authoring.composeTitle")
            }}
          </div>

          <!-- Accessible inline error banner -->
          <div
            v-if="validationError"
            id="editor-validation-error"
            class="validation-error-msg"
            style="
              background-color: var(--semantic-color-error-bg-soft);
              border: 1px solid var(--semantic-color-error-bg);
              color: var(--semantic-color-error);
              padding: 12px;
              border-radius: 6px;
              margin-bottom: 16px;
              font-size: 0.85rem;
              font-weight: 600;
            "
            role="status"
            aria-live="polite"
          >
            {{ validationError }}
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
                {{ $t("econsent.authoring.metadataHeader") }}
              </legend>
              <div class="grid-2-responsive">
                <div class="form-group">
                  <label
                    for="input-study-id"
                    style="
                      display: block;
                      margin-bottom: 4px;
                      font-weight: 600;
                      font-size: 0.85rem;
                    "
                    >{{ $t("econsent.authoring.studyIdLabel") }}</label
                  >
                  <input
                    id="input-study-id"
                    v-model="editorForm.study_id"
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
                    >{{ $t("econsent.authoring.templateNameLabel") }}</label
                  >
                  <input
                    id="input-template-name"
                    v-model="editorForm.template_name"
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
                    >{{ $t("econsent.authoring.protocolVersionLabel") }}</label
                  >
                  <input
                    id="input-protocol-version"
                    v-model="editorForm.protocol_version"
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
                    id="checkbox-reconsent"
                    v-model="editorForm.requires_reconsent"
                    type="checkbox"
                    style="margin-right: 8px"
                  />
                  <label
                    for="checkbox-reconsent"
                    style="
                      font-weight: 600;
                      font-size: 0.85rem;
                      cursor: pointer;
                    "
                    >{{
                      $t("econsent.authoring.requiresReconsentLabel")
                    }}</label
                  >
                </div>

                <div
                  v-if="editorForm.requires_reconsent"
                  class="notification-channels-config"
                  style="
                    margin-top: 12px;
                    padding: 12px;
                    background-color: var(--semantic-color-surface-muted);
                    border: 1px solid var(--border);
                    border-radius: 6px;
                  "
                >
                  <div
                    style="
                      font-size: 0.8rem;
                      font-weight: 600;
                      margin-bottom: 8px;
                      color: var(--text-main);
                    "
                  >
                    Automated Re-Consent Notification Channels:
                  </div>
                  <ReconsentChannelPicker
                    v-model="editorForm.notification_channels"
                  />
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
                {{ $t("econsent.authoring.clausesHeader") }}
              </legend>
              <p
                style="
                  font-size: 0.8rem;
                  color: var(--semantic-color-text-secondary);
                  margin-bottom: 12px;
                "
              >
                {{ $t("econsent.authoring.clausesHelp") }}
              </p>

              <div
                id="clauses-list-wrapper"
                style="display: flex; flex-direction: column; gap: 8px"
              >
                <div
                  v-for="(clauseId, index) in editorForm.clauses"
                  :key="index"
                  class="clause-order-row"
                  style="
                    display: flex;
                    gap: var(--spacing-xs);
                    align-items: center;
                    flex-wrap: wrap;
                  "
                >
                  <span
                    style="
                      font-family: var(--semantic-font-mono);
                      font-size: 0.85rem;
                      width: 24px;
                      text-align: center;
                    "
                    >{{ index + 1 }}.</span
                  >
                  <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
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
                  <ClinicalIconButton
                    class="btn btn-move-up touch-target-interactive"
                    style="font-size: 0.75rem"
                    :disabled="index === 0"
                    :label="`Move clause ${editorForm.clauses[index] || index + 1} up`"
                    @click="moveClause(index, -1)"
                  >
                    ▲
                  </ClinicalIconButton>
                  <ClinicalIconButton
                    class="btn btn-move-down touch-target-interactive"
                    style="font-size: 0.75rem"
                    :disabled="index === editorForm.clauses.length - 1"
                    :label="`Move clause ${editorForm.clauses[index] || index + 1} down`"
                    @click="moveClause(index, 1)"
                  >
                    ▼
                  </ClinicalIconButton>
                  <ClinicalIconButton
                    class="btn btn-remove-clause touch-target-interactive"
                    style="
                      font-size: 0.75rem;
                      background-color: var(--semantic-color-error-interactive);
                      color: var(--semantic-color-text-on-solid);
                      border: none;
                      border-radius: 4px;
                    "
                    :label="`Remove clause ${editorForm.clauses[index] || index + 1}`"
                    @click="removeClauseRow(index)"
                  >
                    ✕
                  </ClinicalIconButton>
                </div>
              </div>

              <button
                type="button"
                class="btn btn-secondary btn-add-clause"
                style="margin-top: 12px; padding: 6px 12px; font-size: 0.8rem"
                @click="addClauseRow"
              >
                {{ $t("econsent.authoring.addClause") }}
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
                {{ $t("econsent.authoring.stepsHeader") }}
              </legend>
              <p
                style="
                  font-size: 0.8rem;
                  color: var(--semantic-color-text-secondary);
                  margin-bottom: 12px;
                "
              >
                {{ $t("econsent.authoring.stepsHelp") }}
              </p>

              <div
                id="steps-list-wrapper"
                style="display: flex; flex-direction: column; gap: 8px"
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
                    <strong>{{
                      $t("econsent.authoring.stepTitle", {
                        index: index + 1,
                        type: step.type,
                      })
                    }}</strong>
                    <button
                      type="button"
                      class="btn btn-remove-step"
                      style="
                        padding: 2px 6px;
                        font-size: var(--font-size-2xs);
                        background-color: var(
                          --semantic-color-error-interactive
                        );
                        color: var(--semantic-color-text-on-solid);
                        border: none;
                        border-radius: 4px;
                      "
                      @click="removeStep(index)"
                    >
                      {{ $t("econsent.authoring.remove") }}
                    </button>
                  </div>
                  <div v-if="step.type === 'comprehension_check'">
                    <label
                      :for="'step-question-' + index"
                      style="font-size: 0.75rem"
                      >{{ $t("econsent.authoring.verificationPrompt") }}</label
                    >
                    <input
                      :id="'step-question-' + index"
                      v-model="step.question"
                      type="text"
                      placeholder="Understood and agree to risk factors?"
                      style="width: 100%; padding: 4px 8px; font-size: 0.8rem"
                    />
                  </div>
                  <div v-else-if="step.type === 'signature_placeholder'">
                    <label
                      :for="'step-role-' + index"
                      style="font-size: 0.75rem"
                      >{{ $t("econsent.authoring.requiredRole") }}</label
                    >
                    <input
                      :id="'step-role-' + index"
                      v-model="step.role"
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
                  {{ $t("econsent.authoring.addComprehensionStep") }}
                </button>
                <button
                  type="button"
                  class="btn btn-secondary btn-add-step-sig"
                  style="padding: 6px 12px; font-size: 0.8rem"
                  @click="addWorkflowStep('signature_placeholder')"
                >
                  {{ $t("econsent.authoring.addSignatureStep") }}
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
                {{ $t("econsent.authoring.previewHeader") }}
              </legend>
              <div
                style="
                  background-color: var(--semantic-color-surface-header);
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
                  <strong style="color: var(--text-main); font-size: 0.85rem">{{
                    $t("econsent.authoring.previewSubheader")
                  }}</strong>
                  <button
                    type="button"
                    class="btn btn-preview"
                    style="padding: 4px 10px; font-size: 0.75rem"
                    @click="triggerComposePreview"
                  >
                    {{ $t("econsent.authoring.refreshPreview") }}
                  </button>
                </div>
                <div
                  v-if="previewLoading"
                  style="font-size: 0.8rem; font-style: italic"
                >
                  {{ $t("econsent.authoring.previewLoading") }}
                </div>
                <div
                  v-else-if="previewClauses.length === 0"
                  style="
                    font-size: 0.8rem;
                    color: var(--semantic-color-text-secondary);
                    font-style: italic;
                  "
                >
                  {{ $t("econsent.authoring.noClauses") }}
                </div>
                <div
                  v-else
                  style="display: flex; flex-direction: column; gap: 8px"
                >
                  <div
                    v-for="c in previewClauses"
                    :key="c.clause_id"
                    style="
                      border-bottom: 1px solid var(--semantic-color-border);
                      padding-bottom: 8px;
                    "
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
              {{ $t("econsent.authoring.cancel") }}
            </button>
            <button
              type="button"
              class="btn btn-primary btn-save"
              style="padding: 8px 16px; cursor: pointer"
              @click="promptSave"
            >
              {{ $t("econsent.authoring.saveTemplate") }}
            </button>
          </div>
        </div>
      </template>
    </ClinicalSplitWorkspace>

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
import ReconsentChannelPicker from "../components/econsent/ReconsentChannelPicker.vue";
import { ref, computed, onMounted, reactive } from "vue";
import { ClinicalIconButton, ClinicalIcon, ClinicalSplitWorkspace } from "ui";
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
const consentSplitRef = ref(null);
const isEdit = ref(false);
const activeTemplateId = ref(null);
const validationError = ref("");

// Editor Form Reactive State
const editorForm = reactive({
  study_id: "",
  template_name: "",
  protocol_version: "",
  requires_reconsent: false,
  notification_channels: ["SMS", "Mobile Push", "Email"],
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

function openEditor(template = null, event = null) {
  validationError.value = "";
  previewClauses.value = [];
  if (template) {
    isEdit.value = true;
    activeTemplateId.value = template.template_id;
    editorForm.study_id = template.study_id || "";
    editorForm.template_name = template.template_name || "";
    editorForm.protocol_version = template.protocol_version || "";
    editorForm.requires_reconsent = template.requires_reconsent || false;
    editorForm.notification_channels = template.notification_channels || [
      "SMS",
      "Mobile Push",
      "Email",
    ];
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
    editorForm.notification_channels = ["SMS", "Mobile Push", "Email"];
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
  consentSplitRef.value?.openDetail(event?.currentTarget ?? null);
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
    channels: editorForm.notification_channels,
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
.access-denied-banner {
  border-left: 4px solid var(--error);
  background-color: var(--error-bg);
  padding: 24px;
}

.access-denied-row {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}

.access-denied-icon {
  color: var(--error);
  margin-top: 2px;
}

.access-denied-heading {
  color: var(--error);
  font-weight: 700;
  margin-bottom: 8px;
}

.access-denied-copy {
  color: var(--neutral-dark);
  font-size: 0.95rem;
  line-height: 1.6;
}

.template-card:hover {
  border-color: var(--accent) !important;
  box-shadow: var(--shadow-md);
}
</style>
