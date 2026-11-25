<template>
  <div :id="`${idPrefix}-panel`" class="protocol-import-panel">
    <form
      :id="`${idPrefix}-form`"
      class="import-card"
      @submit.prevent="submitImport"
    >
      <div class="import-card-header">
        <h3 :id="`${idPrefix}-heading`">
          Import protocol draft (ODM v1.3.2 XML / USDM v4 JSON / CDISC Library)
        </h3>
        <p class="import-subtitle">
          Upload an ODM v1.3.2 XML or USDM v4 JSON file (up to 10 MB) or choose
          bundled CDISC Library eCRF templates. Imported definitions enter
          <strong>Draft</strong> status and require formal review and electronic
          approval before site activation.
        </p>
      </div>

      <div
        v-if="importing"
        :id="`${idPrefix}-progress`"
        class="banner banner-progress"
        role="status"
      >
        Importing protocol definition and validating forms and items…
      </div>

      <div
        v-if="successMessage"
        :id="`${idPrefix}-success`"
        class="banner banner-success"
        role="status"
      >
        {{ successMessage }}
      </div>

      <div
        v-if="errorMessage"
        :id="`${idPrefix}-error`"
        class="banner banner-error"
        role="alert"
      >
        {{ errorMessage }}
      </div>

      <div class="file-chooser-box">
        <label :for="`${idPrefix}-file`" class="sr-only">
          Protocol ODM v1.3.2 XML or USDM v4 JSON file
        </label>
        <input
          :id="`${idPrefix}-file`"
          ref="fileInputRef"
          type="file"
          accept=".xml,.json,application/xml,text/xml,application/json"
          class="hidden-file-input"
          @change="onFileSelected"
        />
        <div class="file-chooser-actions">
          <button
            :id="`${idPrefix}-choose-file`"
            type="button"
            class="btn btn-secondary"
            :disabled="importing"
            @click="openFilePicker"
          >
            Choose ODM XML or USDM JSON file
          </button>
          <button
            v-if="selectedFile"
            :id="`${idPrefix}-clear-file`"
            type="button"
            class="btn btn-link"
            :disabled="importing"
            @click="onClearFileClick"
          >
            Clear file
          </button>
        </div>
        <p
          v-if="selectedFile"
          :id="`${idPrefix}-selected-file`"
          class="file-meta"
        >
          Selected file: <strong>{{ selectedFile.name }}</strong> ({{
            formatFileSize(selectedFile.size)
          }})
        </p>
        <p v-else class="file-hint">
          Accepted formats: CDISC ODM v1.3.2 (<code>.xml</code>) or CDISC USDM
          v4 (<code>.json</code>), maximum 10 MB.
        </p>
      </div>

      <div v-if="templates.length > 0" class="template-picker">
        <div class="template-picker-header">
          <label :for="`${idPrefix}-template-filter`" class="field-label">
            Bundled CDISC Library eCRF templates ({{
              selectedTemplateIds.length
            }}
            selected of {{ templates.length }})
          </label>
          <input
            :id="`${idPrefix}-template-filter`"
            v-model="templateFilter"
            type="search"
            class="text-input template-search"
            placeholder="Filter by form name or domain, e.g. Vital Signs or DM"
          />
        </div>
        <div
          :id="`${idPrefix}-template-list`"
          class="template-grid"
          role="group"
          aria-label="CDISC Library eCRF templates"
        >
          <label
            v-for="tpl in filteredTemplates"
            :key="tpl.id"
            :for="`${idPrefix}-tpl-${tpl.id}`"
            class="template-option"
          >
            <input
              :id="`${idPrefix}-tpl-${tpl.id}`"
              v-model="selectedTemplateIds"
              type="checkbox"
              :value="tpl.id"
              :disabled="importing"
            />
            <span class="template-domain">{{ tpl.category }}</span>
            <span class="template-title">{{ tpl.form_name }}</span>
            <span
              v-if="tpl.repeating"
              class="template-repeating-badge"
              :data-testid="`tpl-repeating-${tpl.id}`"
            >
              Repeating
            </span>
          </label>
        </div>

        <div
          v-if="selectedTemplates.length > 0"
          :id="`${idPrefix}-template-assignments`"
          data-testid="template-visit-assignments"
          class="template-assignments-box"
          role="group"
          aria-label="Template visit schedule assignments"
        >
          <div class="template-assignments-header">
            <span class="field-label">Template visit schedule assignments</span>
            <span class="file-hint">
              Non-repeating templates default to starter protocol visits;
              repeating log templates stay unscheduled unless assigned to
              specific visits.
            </span>
          </div>
          <div
            v-for="tpl in selectedTemplates"
            :key="`assign-${tpl.id}`"
            class="template-assignment-row"
            :data-testid="`template-assignment-${tpl.id}`"
          >
            <div class="template-assignment-main">
              <label
                :for="`${idPrefix}-assign-mode-${tpl.id}`"
                class="template-assignment-label"
              >
                <strong>{{ tpl.form_name }}</strong>
                <span v-if="tpl.repeating" class="template-repeating-badge">
                  Repeating
                </span>
              </label>
              <select
                :id="`${idPrefix}-assign-mode-${tpl.id}`"
                :value="assignmentModeFor(tpl.id)"
                class="text-input template-assignment-select"
                :disabled="importing"
                @change="
                  setAssignmentMode(
                    tpl.id,
                    ($event.target as HTMLSelectElement).value as AssignmentMode
                  )
                "
              >
                <option value="default">
                  {{
                    tpl.repeating
                      ? "Default (Unscheduled / Log only)"
                      : "Default schedule (Screening, Baseline, Week 4, End of Study)"
                  }}
                </option>
                <option value="unscheduled">Unscheduled / Log only</option>
                <option value="custom">Assign to specific visits…</option>
              </select>
            </div>
            <div
              v-if="assignmentModeFor(tpl.id) === 'custom'"
              class="template-visit-checkboxes"
              role="group"
              :aria-label="`Visits for ${tpl.form_name}`"
            >
              <label
                v-for="visit in availableAssignmentVisits"
                :key="`${tpl.id}-${visit.id}`"
                :for="`${idPrefix}-assign-visit-${tpl.id}-${visit.id}`"
                class="template-visit-chip"
              >
                <input
                  :id="`${idPrefix}-assign-visit-${tpl.id}-${visit.id}`"
                  type="checkbox"
                  :checked="isVisitAssigned(tpl.id, visit.id)"
                  :disabled="importing"
                  @change="
                    toggleVisitAssignment(
                      tpl.id,
                      visit.id,
                      ($event.target as HTMLInputElement).checked
                    )
                  "
                />
                <span>{{ visit.name }}</span>
                <span v-if="visit.studyDay !== null" class="visit-chip-day">
                  Day {{ visit.studyDay }}
                </span>
              </label>
            </div>
          </div>
        </div>
      </div>

      <div class="import-fields-row">
        <div class="field-group">
          <label :for="`${idPrefix}-reason`" class="field-label">
            Reason for change (required)
          </label>
          <input
            :id="`${idPrefix}-reason`"
            v-model="changeReason"
            type="text"
            class="text-input"
            maxlength="500"
            placeholder="e.g. Import initial CDISC protocol draft for review"
            :disabled="importing"
          />
        </div>
        <div class="field-group">
          <label
            :for="`${idPrefix}-amendment-classification`"
            class="field-label"
          >
            Amendment classification (when amending)
          </label>
          <select
            :id="`${idPrefix}-amendment-classification`"
            v-model="amendmentClassification"
            class="text-input select-input"
            :disabled="importing"
          >
            <option value="">Default (Non-substantial)</option>
            <option value="NonSubstantial">
              Non-substantial (NonSubstantial)
            </option>
            <option value="Substantial">Substantial (Substantial)</option>
            <option value="UrgentSafetyMeasure">
              Urgent safety measure (UrgentSafetyMeasure)
            </option>
          </select>
        </div>
        <div class="field-group">
          <label :for="`${idPrefix}-transition-policy`" class="field-label">
            Participant transition policy (when amending)
          </label>
          <select
            :id="`${idPrefix}-transition-policy`"
            v-model="transitionPolicy"
            class="text-input select-input"
            :disabled="importing"
          >
            <option value="">Default (New participants only)</option>
            <option value="NewParticipantsOnly">
              New participants only (NewParticipantsOnly)
            </option>
            <option value="RequiresReconsent">
              Requires re-consent (RequiresReconsent)
            </option>
            <option value="Automatic">Automatic transition (Automatic)</option>
            <option value="ByCohort">Transition by cohort (ByCohort)</option>
          </select>
        </div>
        <div class="field-group checkbox-group">
          <label :for="`${idPrefix}-replace`" class="checkbox-label">
            <input
              :id="`${idPrefix}-replace`"
              v-model="replaceForms"
              type="checkbox"
              :disabled="importing"
            />
            <span>Replace existing draft forms instead of merging</span>
          </label>
        </div>
      </div>

      <div class="import-submit-row">
        <button
          :id="`${idPrefix}-submit`"
          type="submit"
          class="btn btn-primary"
          :disabled="!canSubmit"
        >
          {{ importing ? "Importing draft…" : "Import protocol draft" }}
        </button>
      </div>
    </form>

    <section
      v-if="summaryLoading"
      :id="`${idPrefix}-summary-loading`"
      class="draft-summary-card"
      aria-live="polite"
    >
      Loading protocol draft forms and items…
    </section>

    <section
      v-else-if="draftSummary"
      :id="`${idPrefix}-summary`"
      class="draft-summary-card"
      :aria-labelledby="`${idPrefix}-summary-title`"
    >
      <div class="draft-summary-header">
        <div>
          <span class="summary-eyebrow">
            Version {{ draftSummary.version.number }} ·
            <span class="status-pill">{{ draftSummary.version.status }}</span>
          </span>
          <h3
            :id="`${idPrefix}-summary-title`"
            class="summary-title"
            tabindex="-1"
          >
            {{ draftSummary.title }}
          </h3>
        </div>
        <span :id="`${idPrefix}-summary-counts`" class="summary-counts">
          {{ draftSummary.form_count }}
          {{ draftSummary.form_count === 1 ? "form" : "forms" }} ·
          {{ draftSummary.item_count }}
          {{ draftSummary.item_count === 1 ? "item" : "items" }}
        </span>
      </div>

      <p v-if="draftSummary.version.status === 'Draft'" class="governance-note">
        Draft status: formal review and electronic approval are required before
        any site can be activated on this protocol version.
      </p>

      <div
        v-if="draftSummary.amendment"
        :id="`${idPrefix}-amendment`"
        data-testid="draft-amendment-summary"
        class="amendment-summary-box"
      >
        <div class="amendment-summary-header">
          <strong>Amendment metadata</strong>
          <span class="amendment-badges">
            <span class="amendment-badge">
              {{ draftSummary.amendment.classification }}
            </span>
            <span class="amendment-badge">
              {{ draftSummary.amendment.transition_policy }}
            </span>
          </span>
        </div>
        <p class="amendment-rationale">
          {{ draftSummary.amendment.summary_of_changes }}
        </p>
        <div
          v-if="draftSummary.amendment.impacted_elements?.length"
          class="amendment-elements"
        >
          <span class="amendment-elements-label">
            Impacted elements ({{
              draftSummary.amendment.impacted_elements.length
            }}):
          </span>
          <code
            v-for="elemId in draftSummary.amendment.impacted_elements"
            :key="elemId"
            class="amendment-element-code"
          >
            {{ elemId }}
          </code>
        </div>
      </div>

      <div
        v-if="draftSummary.imported_rule_pack"
        :id="`${idPrefix}-rule-pack`"
        data-testid="imported-rule-pack-banner"
        class="banner banner-success"
      >
        <span>
          Compiled ODM edit checks into validation rule pack
          <strong>{{ draftSummary.imported_rule_pack.key }}</strong>
          (v{{ draftSummary.imported_rule_pack.version }},
          {{ draftSummary.imported_rule_pack.rule_count }}
          {{
            draftSummary.imported_rule_pack.rule_count === 1 ? "rule" : "rules"
          }}, status:
          {{ formatClinicalStatus(draftSummary.imported_rule_pack.status) }}).
        </span>
        <router-link
          v-if="studyCode"
          :id="`${idPrefix}-open-validation`"
          class="btn btn-secondary"
          :to="validationLocation"
        >
          Review &amp; pin in Validation
        </router-link>
      </div>

      <div
        v-if="draftSummary.import_notes && draftSummary.import_notes.length > 0"
        :id="`${idPrefix}-notes`"
        class="import-notes-box"
      >
        <h4 class="notes-heading">
          Import notes ({{ draftSummary.import_notes.length }})
        </h4>
        <ul class="notes-list">
          <li
            v-for="(note, idx) in draftSummary.import_notes"
            :key="`${note.source}-${idx}`"
            class="note-item"
          >
            <code class="note-source">{{ note.source }}</code>
            <span>{{ note.message }}</span>
          </li>
        </ul>
      </div>

      <div
        v-if="draftSummary.forms && draftSummary.forms.length > 0"
        :id="`${idPrefix}-forms`"
        class="draft-forms-list"
      >
        <article
          v-for="form in draftSummary.forms"
          :key="form.id"
          class="draft-form-card"
        >
          <header class="draft-form-header">
            <h4 class="draft-form-name">
              {{ form.name }}
            </h4>
            <span class="draft-form-meta">
              <span
                v-if="form.repeating"
                class="template-repeating-badge"
                :data-testid="`draft-form-repeating-${form.id}`"
              >
                Repeating
              </span>
              <code>{{ form.id }}</code> · {{ form.item_count }}
              {{ form.item_count === 1 ? "item" : "items" }}
            </span>
          </header>
          <ol class="draft-items-list">
            <li
              v-for="item in form.items"
              :key="item.id"
              class="draft-item-row"
            >
              <span class="draft-item-prompt">{{ item.prompt }}</span>
              <span class="draft-item-badges">
                <span
                  class="badge-mandatory"
                  :class="{ required: item.required }"
                >
                  {{ item.required ? "Mandatory" : "Optional" }}
                </span>
                <span v-if="item.value_kind" class="badge-kind">
                  {{ item.value_kind }}{{ item.unit ? ` (${item.unit})` : "" }}
                </span>
                <code class="draft-item-id">{{ item.id }}</code>
              </span>
            </li>
          </ol>
        </article>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { handoffFocus } from "ui";
import {
  describeKernelError,
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolTemplate,
  type KernelTemplateAssignmentInput,
} from "../../api/kernelApi";
import { formatClinicalStatus } from "../../utils/clinicalLabels";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";
import { useScopedNavigation } from "../../composables/useScopedNavigation";

const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;

const props = withDefaults(
  defineProps<{
    studyRef: string;
    studyCode: string;
    selectedVersionRef?: string;
    idPrefix?: string;
  }>(),
  {
    selectedVersionRef: "",
    idPrefix: "protocol-import",
  }
);

const emit = defineEmits<{
  imported: [summary: KernelProtocolDraftSummary];
}>();

const { scopedStudyLocation } = useScopedNavigation(() => props.studyCode);
const validationLocation = computed(() =>
  scopedStudyLocation({ stage: "data", section: "validation" })
);

type AssignmentMode = "default" | "unscheduled" | "custom";

interface AssignmentVisitOption {
  id: string;
  name: string;
  studyDay: number | null;
}

const STARTER_ASSIGNMENT_VISITS: AssignmentVisitOption[] = [
  { id: "V.SCREENING", name: "Screening", studyDay: -7 },
  { id: "V.BASELINE", name: "Baseline", studyDay: 1 },
  { id: "V.WEEK4", name: "Week 4", studyDay: 28 },
  { id: "V.EOS", name: "End of Study", studyDay: 84 },
];

const fileInputRef = ref<HTMLInputElement | null>(null);
const selectedFile = ref<File | null>(null);
const templates = ref<KernelProtocolTemplate[]>([]);
const templateFilter = ref("");
const selectedTemplateIds = ref<string[]>([]);
const templateAssignmentModes = ref<Record<string, AssignmentMode>>({});
const templateCustomVisits = ref<Record<string, string[]>>({});
const draftScheduleVisits = ref<AssignmentVisitOption[]>([]);
const changeReason = ref("");
const replaceForms = ref(false);
const amendmentClassification = ref("");
const transitionPolicy = ref("");
const pendingIdempotencyKey = ref("");

const importing = ref(false);
const successMessage = ref("");
const errorMessage = ref("");

const draftSummary = ref<KernelProtocolDraftSummary | null>(null);
const summaryLoading = ref(false);
let summaryRequest = 0;

function makeIdempotencyKey(): string {
  if (isDemoBuild()) {
    return `import-${demoRandomToken(12, 36, "protocol-import.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `import-${demoRandomToken(12, 36, "protocol-import.idempotency")}`;
}

const filteredTemplates = computed(() => {
  const needle = templateFilter.value.trim().toLowerCase();
  if (!needle) return templates.value;
  return templates.value.filter((tpl) =>
    `${tpl.category} ${tpl.form_oid} ${tpl.form_name} ${tpl.file_name}`
      .toLowerCase()
      .includes(needle)
  );
});

const selectedTemplates = computed(() => {
  const byId = new Map(templates.value.map((t) => [t.id, t]));
  return selectedTemplateIds.value
    .map((id) => byId.get(id))
    .filter((tpl): tpl is KernelProtocolTemplate => Boolean(tpl));
});

const availableAssignmentVisits = computed<AssignmentVisitOption[]>(() =>
  draftScheduleVisits.value.length > 0 && !replaceForms.value
    ? draftScheduleVisits.value
    : STARTER_ASSIGNMENT_VISITS
);

function assignmentModeFor(templateId: string): AssignmentMode {
  return templateAssignmentModes.value[templateId] ?? "default";
}

function setAssignmentMode(templateId: string, mode: AssignmentMode) {
  templateAssignmentModes.value = {
    ...templateAssignmentModes.value,
    [templateId]: mode,
  };
  if (
    mode === "custom" &&
    !Object.prototype.hasOwnProperty.call(
      templateCustomVisits.value,
      templateId
    )
  ) {
    const tpl = templates.value.find((t) => t.id === templateId);
    const initial = tpl?.repeating
      ? []
      : availableAssignmentVisits.value.map((v) => v.id);
    templateCustomVisits.value = {
      ...templateCustomVisits.value,
      [templateId]: initial,
    };
  }
  pendingIdempotencyKey.value = "";
}

function isVisitAssigned(templateId: string, activityId: string): boolean {
  return (templateCustomVisits.value[templateId] ?? []).includes(activityId);
}

function toggleVisitAssignment(
  templateId: string,
  activityId: string,
  checked: boolean
) {
  const current = new Set(templateCustomVisits.value[templateId] ?? []);
  if (checked) current.add(activityId);
  else current.delete(activityId);
  const ordered = availableAssignmentVisits.value
    .map((v) => v.id)
    .filter((id) => current.has(id));
  templateCustomVisits.value = {
    ...templateCustomVisits.value,
    [templateId]: ordered,
  };
  pendingIdempotencyKey.value = "";
}

function buildTemplateAssignmentsPayload(): KernelTemplateAssignmentInput[] {
  const out: KernelTemplateAssignmentInput[] = [];
  for (const templateId of selectedTemplateIds.value) {
    const mode = assignmentModeFor(templateId);
    if (mode === "unscheduled") {
      out.push({ templateId, activityIds: [] });
    } else if (mode === "custom") {
      out.push({
        templateId,
        activityIds: [...(templateCustomVisits.value[templateId] ?? [])],
      });
    }
  }
  return out;
}

const canSubmit = computed(
  () =>
    !importing.value &&
    Boolean(props.studyRef || props.studyCode) &&
    changeReason.value.trim().length > 0 &&
    (Boolean(selectedFile.value) || selectedTemplateIds.value.length > 0)
);

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}

function openFilePicker() {
  errorMessage.value = "";
  fileInputRef.value?.click();
}

function clearSelectedFile() {
  selectedFile.value = null;
  pendingIdempotencyKey.value = "";
  if (fileInputRef.value) {
    fileInputRef.value.value = "";
  }
}

function onClearFileClick() {
  clearSelectedFile();
  void handoffFocus(`#${props.idPrefix}-choose-file`);
}

function onFileSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const file = input?.files?.[0] ?? null;
  successMessage.value = "";
  errorMessage.value = "";
  pendingIdempotencyKey.value = "";

  if (!file) {
    selectedFile.value = null;
    return;
  }

  if (file.size > MAX_UPLOAD_BYTES) {
    selectedFile.value = null;
    if (input) input.value = "";
    errorMessage.value = `File '${file.name}' (${formatFileSize(file.size)}) exceeds the 10 MB synchronous upload limit.`;
    return;
  }

  selectedFile.value = file;
}

watch(
  [
    selectedTemplateIds,
    changeReason,
    replaceForms,
    amendmentClassification,
    transitionPolicy,
  ],
  () => {
    pendingIdempotencyKey.value = "";
  }
);

async function loadTemplates() {
  if (typeof kernelApi.listProtocolTemplates !== "function") return;
  try {
    templates.value = await kernelApi.listProtocolTemplates();
  } catch {
    // Keep file upload usable even if template catalog cannot be fetched.
  }
}

async function loadSelectedSummary(versionRef: string) {
  if (!versionRef || typeof kernelApi.getProtocolDesignSummary !== "function") {
    return;
  }
  if (draftSummary.value?.version?.ref === versionRef) {
    return;
  }
  const req = ++summaryRequest;
  summaryLoading.value = true;
  try {
    const loaded = await kernelApi.getProtocolDesignSummary(versionRef);
    if (req === summaryRequest) {
      draftSummary.value = loaded;
    }
    if (typeof kernelApi.getScheduleOfActivities === "function") {
      try {
        const soa = await kernelApi.getScheduleOfActivities(versionRef);
        if (req === summaryRequest && Array.isArray(soa.columns)) {
          draftScheduleVisits.value = soa.columns.map((col) => ({
            id: col.activity_id,
            name: col.name,
            studyDay:
              col.study_day !== null && col.study_day !== undefined
                ? Number(col.study_day)
                : null,
          }));
        }
      } catch {
        if (req === summaryRequest) {
          draftScheduleVisits.value = [];
        }
      }
    }
  } catch {
    if (req === summaryRequest) {
      draftSummary.value = null;
    }
  } finally {
    if (req === summaryRequest) {
      summaryLoading.value = false;
    }
  }
}

watch(
  () => props.selectedVersionRef,
  (versionRef) => {
    if (versionRef) {
      loadSelectedSummary(versionRef);
    }
  },
  { immediate: true }
);

async function submitImport() {
  if (typeof kernelApi.importProtocolDraft !== "function") {
    errorMessage.value = "Protocol import service is unavailable.";
    return;
  }
  const reason = changeReason.value.trim();
  if (!reason) {
    errorMessage.value = "Enter a reason for change before importing.";
    return;
  }
  if (!selectedFile.value && selectedTemplateIds.value.length === 0) {
    errorMessage.value =
      "Choose an ODM XML or USDM JSON file or select at least one CDISC Library eCRF template.";
    return;
  }

  if (!pendingIdempotencyKey.value) {
    pendingIdempotencyKey.value = makeIdempotencyKey();
  }

  importing.value = true;
  errorMessage.value = "";
  successMessage.value = "";

  try {
    const templateAssignments = buildTemplateAssignmentsPayload();
    const summary = await kernelApi.importProtocolDraft({
      studyRef: props.studyRef || props.studyCode,
      file: selectedFile.value,
      templateIds: selectedTemplateIds.value,
      templateAssignments:
        templateAssignments.length > 0 ? templateAssignments : undefined,
      replaceForms: replaceForms.value,
      amendmentClassification: amendmentClassification.value || null,
      transitionPolicy: transitionPolicy.value || null,
      reason,
      idempotencyKey: pendingIdempotencyKey.value,
    });

    draftSummary.value = summary;
    const notesCount = Number(summary.import_notes?.length ?? 0);
    const notesSuffix =
      notesCount > 0
        ? ` with ${notesCount} import ${notesCount === 1 ? "note" : "notes"}`
        : "";
    const formCountNum = Number(summary.form_count);
    const itemCountNum = Number(summary.item_count);
    successMessage.value = `Imported Draft version ${summary.version.number} (${formCountNum} ${formCountNum === 1 ? "form" : "forms"}, ${itemCountNum} ${itemCountNum === 1 ? "item" : "items"})${notesSuffix}.`;

    clearSelectedFile();
    selectedTemplateIds.value = [];
    templateAssignmentModes.value = {};
    templateCustomVisits.value = {};
    replaceForms.value = false;
    pendingIdempotencyKey.value = "";
    emit("imported", summary);
    void handoffFocus([
      `#${props.idPrefix}-summary-title`,
      `#${props.idPrefix}-choose-file`,
    ]);
  } catch (err) {
    errorMessage.value = describeKernelError(err);
  } finally {
    importing.value = false;
  }
}

onMounted(() => {
  loadTemplates();
});

defineExpose({
  openFilePicker,
  fileInputRef,
});
</script>

<style scoped>
.protocol-import-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.import-card,
.draft-summary-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.import-card-header h3 {
  margin: 0 0 4px;
  font-size: 0.95rem;
}

.import-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.banner {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.banner-progress {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.file-chooser-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px dashed var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.hidden-file-input {
  display: none;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

.file-chooser-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.btn-link {
  background: transparent;
  border: none;
  color: var(--semantic-color-interactive-primary);
  cursor: pointer;
  padding: 4px 8px;
  font-size: 0.85rem;
}

.file-meta,
.file-hint {
  margin: 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}

.template-picker {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.template-picker-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.field-label {
  font-size: 0.85rem;
  font-weight: 600;
}

.text-input {
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font: inherit;
  font-size: 0.85rem;
  width: 100%;
  box-sizing: border-box;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
}

@media (pointer: coarse) {
  .text-input {
    font-size: 1rem;
  }
}

.template-search {
  max-width: 22rem;
}

.template-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 220px), 1fr));
  gap: 6px;
  max-height: 180px;
  overflow-y: auto;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.template-option {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
  padding: 4px 6px;
  border-radius: 4px;
  font-size: 0.8rem;
  cursor: pointer;
}

.template-domain {
  display: inline-block;
  padding: 1px 5px;
  border-radius: 4px;
  background: var(--semantic-color-border);
  color: var(--semantic-color-text-body);
  font-size: var(--font-size-2xs);
  font-weight: 600;
}

.template-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.template-repeating-badge {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 999px;
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  font-size: var(--font-size-2xs);
  font-weight: 600;
  white-space: nowrap;
}

.template-assignments-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
}

.template-assignments-header {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.template-assignment-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 6px;
  border-top: 1px solid var(--semantic-color-border);
}

.template-assignment-main {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.template-assignment-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.84rem;
}

.template-assignment-select {
  max-width: 26rem;
}

.template-visit-checkboxes {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.template-visit-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.8rem;
  cursor: pointer;
}

.visit-chip-day {
  font-size: 0.75rem;
  color: var(--neutral-dark);
  font-variant-numeric: tabular-nums;
}

.import-fields-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
  gap: 12px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.checkbox-group {
  justify-content: flex-end;
}

.checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  cursor: pointer;
  padding: 8px 0;
}

.import-submit-row {
  display: flex;
  justify-content: flex-start;
}

.draft-summary-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
}

.summary-eyebrow {
  font-size: 0.78rem;
  color: var(--neutral-dark);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.status-pill {
  display: inline-block;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text-dark);
  font-weight: 600;
}

.summary-title {
  margin: 4px 0 0;
  font-size: 1rem;
}

.summary-counts {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--neutral-dark);
}

.governance-note {
  margin: 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}

.amendment-summary-box {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  font-size: 0.82rem;
}

.amendment-summary-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.amendment-badges {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 6px;
}

.amendment-badge {
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
  font-weight: 600;
  font-size: 0.76rem;
}

.amendment-rationale {
  margin: 0;
  color: var(--semantic-color-text-body);
}

.amendment-elements {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.amendment-elements-label {
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.amendment-element-code {
  padding: 1px 5px;
  border-radius: 4px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  font-size: 0.75rem;
}

.import-notes-box {
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
}

.notes-heading {
  margin: 0 0 6px;
  font-size: 0.85rem;
  color: var(--semantic-color-warning-text);
}

.notes-list {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.82rem;
  color: var(--semantic-color-warning-text-dark);
}

.note-item {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.note-source {
  background: var(--semantic-color-warning-bg-badge);
  padding: 1px 4px;
  border-radius: 3px;
}

.draft-forms-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.draft-form-card {
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.draft-form-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 8px;
}

.draft-form-name {
  margin: 0;
  font-size: 0.92rem;
}

.draft-form-meta {
  font-size: 0.78rem;
  color: var(--neutral-dark);
}

.draft-items-list {
  margin: 0;
  padding-left: 20px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.84rem;
}

.draft-item-row {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  padding: 3px 0;
  border-bottom: 1px solid var(--semantic-color-border);
}

.draft-item-row:last-child {
  border-bottom: none;
}

.draft-item-badges {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  font-size: 0.75rem;
}

.badge-mandatory {
  padding: 1px 6px;
  border-radius: 999px;
  background: var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
}

.badge-mandatory.required {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  font-weight: 600;
}

.badge-kind {
  color: var(--neutral-dark);
}

.draft-item-id {
  color: var(--neutral-dark);
  font-size: var(--font-size-2xs);
}
</style>
