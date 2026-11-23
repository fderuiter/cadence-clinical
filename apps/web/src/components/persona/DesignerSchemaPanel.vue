<template>
  <div class="designer-schema-panel">
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
        Protocol Draft Import: ODM v1.3.2 XML &amp; USDM v4 JSON
      </div>
      <p
        style="font-size: 0.85rem; color: var(--semantic-color-text-secondary)"
      >
        Upload an ODM v1.3.2 XML or USDM v4 JSON file (up to 10 MB) or pick
        bundled CDISC Library eCRF templates to create or revise a
        <strong>Draft</strong> protocol version with a signed reason for change.
      </p>

      <div
        v-if="importingDraft || store.ingestionLoading"
        id="schema-import-progress"
        role="status"
        style="
          background-color: var(--semantic-color-info-bg);
          border: 1px solid var(--semantic-color-info-border);
          border-radius: 6px;
          padding: 10px 12px;
          color: var(--semantic-color-info);
          font-size: 0.85rem;
        "
      >
        Importing protocol definition and validating forms and items…
      </div>

      <div
        v-if="importSuccessMessage"
        id="schema-import-success"
        role="status"
        style="
          background-color: var(--semantic-color-success-bg-soft);
          border: 1px solid var(--semantic-color-success-border-soft);
          border-radius: 6px;
          padding: 10px 12px;
          color: var(--semantic-color-success-dark);
          font-size: 0.85rem;
        "
      >
        {{ importSuccessMessage }}
      </div>

      <div
        v-if="importErrorMessage || store.ingestionError"
        id="schema-import-error"
        role="alert"
        style="
          background-color: var(--semantic-color-error-bg-soft);
          border: 1px solid var(--semantic-color-error-border-soft);
          border-radius: 6px;
          padding: 10px 12px;
          color: var(--semantic-color-error-text-dark);
          font-size: 0.85rem;
        "
      >
        {{ importErrorMessage || `Error: ${store.ingestionError}` }}
      </div>

      <div class="form-group">
        <label
          for="designer-import-reason"
          style="font-size: 0.85rem; font-weight: 600"
          >Reason for change (required)</label
        >
        <input
          id="designer-import-reason"
          :value="importChangeReason"
          type="text"
          placeholder="e.g. Import initial ODM v1.3.2 or USDM v4 protocol draft"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            font-size: 0.85rem;
            box-sizing: border-box;
          "
          @input="$emit('update:importChangeReason', $event.target.value)"
        />
      </div>

      <!-- Upload File Section -->
      <div
        style="
          border: 1px dashed var(--border);
          border-radius: 8px;
          padding: 16px;
          text-align: center;
          background-color: var(--semantic-color-surface-muted);
        "
      >
        <label
          for="designer-schema-file-input"
          style="
            position: absolute;
            width: 1px;
            height: 1px;
            overflow: hidden;
            clip: rect(0 0 0 0);
          "
          >Select ODM XML or USDM JSON file</label
        >
        <input
          id="designer-schema-file-input"
          ref="fileInputRef"
          type="file"
          accept=".xml,.json,application/xml,text/xml,application/json"
          style="display: none"
          @change="$emit('trigger-document-upload', $event)"
        />
        <button
          id="btn-select-protocol-file"
          class="btn"
          type="button"
          :disabled="importingDraft || store.ingestionLoading"
          @click="onChooseFileClick"
        >
          {{
            importingDraft || store.ingestionLoading
              ? "Importing Protocol…"
              : "📁 Select ODM XML or USDM JSON"
          }}
        </button>
        <div
          v-if="selectedFileName"
          style="
            margin-top: 8px;
            font-size: 0.8rem;
            color: var(--semantic-color-text-secondary);
          "
        >
          Selected:
          <strong class="selected-file-name">{{ selectedFileName }}</strong>
        </div>
      </div>

      <!-- Bundled CDISC Library eCRF Templates -->
      <div
        id="designer-cdisc-templates"
        style="
          display: flex;
          flex-direction: column;
          gap: 8px;
          border: 1px solid var(--border);
          border-radius: 8px;
          padding: 12px;
          background-color: var(--semantic-color-surface-subtle);
        "
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px;
          "
        >
          <span style="font-size: 0.85rem; font-weight: 600">
            CDISC Library eCRF Templates
            <template
              v-if="availableTemplates && availableTemplates.length > 0"
            >
              ({{ selectedTemplateIds.length }} selected)
            </template>
          </span>
          <button
            v-if="!availableTemplates || availableTemplates.length === 0"
            id="btn-load-cdisc-templates"
            type="button"
            class="btn btn-secondary"
            :disabled="importingDraft"
            @click="$emit('load-templates')"
          >
            Browse CDISC Library templates
          </button>
          <button
            v-else
            id="btn-import-cdisc-templates"
            type="button"
            class="btn btn-primary"
            :disabled="
              importingDraft ||
              selectedTemplateIds.length === 0 ||
              !importChangeReason.trim()
            "
            @click="$emit('import-selected-templates')"
          >
            Add selected eCRF templates to draft
          </button>
        </div>
        <div
          v-if="availableTemplates && availableTemplates.length > 0"
          style="
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
            gap: 6px;
            max-height: 160px;
            overflow-y: auto;
          "
        >
          <label
            v-for="tpl in availableTemplates"
            :key="tpl.id"
            :for="`designer-tpl-${tpl.id}`"
            style="
              display: flex;
              align-items: center;
              gap: 6px;
              font-size: 0.8rem;
              cursor: pointer;
            "
          >
            <input
              :id="`designer-tpl-${tpl.id}`"
              type="checkbox"
              :checked="selectedTemplateIds.includes(tpl.id)"
              @change="toggleTemplate(tpl.id, $event.target.checked)"
            />
            <span>
              <strong>{{ tpl.category }}</strong> · {{ tpl.form_name }}
            </span>
          </label>
        </div>
      </div>

      <!-- Imported Draft Summary & Import Notes -->
      <div
        v-if="importedDraftSummary"
        id="designer-imported-draft-summary"
        style="
          display: flex;
          flex-direction: column;
          gap: 10px;
          border: 1px solid var(--border);
          border-radius: 8px;
          padding: 12px;
        "
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 8px;
          "
        >
          <strong style="font-size: 0.92rem">
            {{ importedDraftSummary.title }} · Version
            {{ importedDraftSummary.version.number }} ({{
              importedDraftSummary.version.status
            }})
          </strong>
          <span
            style="
              font-size: 0.82rem;
              color: var(--semantic-color-text-secondary);
            "
          >
            {{ importedDraftSummary.form_count }} forms ·
            {{ importedDraftSummary.item_count }} items
          </span>
        </div>

        <div
          v-if="
            importedDraftSummary.import_notes &&
            importedDraftSummary.import_notes.length > 0
          "
          id="designer-import-notes"
          style="
            background-color: var(--semantic-color-warning-bg-soft);
            border: 1px solid var(--semantic-color-warning-border);
            border-radius: 6px;
            padding: 8px 10px;
            font-size: 0.8rem;
            color: var(--semantic-color-warning-text-dark);
          "
        >
          <strong>
            Import notes ({{ importedDraftSummary.import_notes.length }}):
          </strong>
          <ul style="margin: 4px 0 0; padding-left: 18px">
            <li
              v-for="(note, idx) in importedDraftSummary.import_notes"
              :key="`${note.source}-${idx}`"
            >
              <code>{{ note.source }}</code> — {{ note.message }}
            </li>
          </ul>
        </div>

        <div
          v-for="form in importedDraftSummary.forms"
          :key="form.id"
          style="
            border: 1px solid var(--border);
            border-radius: 6px;
            padding: 8px 10px;
            background-color: var(--semantic-color-surface-subtle);
          "
        >
          <div style="font-weight: 600; font-size: 0.85rem">
            {{ form.name }} ({{ form.item_count }} items)
          </div>
          <ol style="margin: 6px 0 0; padding-left: 18px; font-size: 0.8rem">
            <li v-for="item in form.items" :key="item.id">
              {{ item.prompt }} —
              <strong>{{ item.required ? "Mandatory" : "Optional" }}</strong>
            </li>
          </ol>
        </div>
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
                    color: var(--semantic-color-text-secondary);
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
                  :class="['badge', getConfidenceClass(item.confidence_level)]"
                  style="font-size: 0.7rem"
                  class="item-confidence"
                >
                  {{ (item.confidence * 100).toFixed(0) }}% ({{
                    item.confidence_level
                  }})
                </span>
                <span
                  style="
                    font-size: 0.75rem;
                    color: var(--semantic-color-text-secondary);
                  "
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
                background-color: var(--semantic-color-surface-muted);
                padding: 8px;
                border-radius: 6px;
              "
              class="item-edit-section"
            >
              <div class="form-group">
                <label
                  :for="`designer-edit-item-value-${item.id}`"
                  style="font-size: 0.75rem"
                  >Modify Candidate Name/Label</label
                >
                <input
                  :id="`designer-edit-item-value-${item.id}`"
                  :value="editItemValue"
                  type="text"
                  style="
                    width: 100%;
                    padding: 6px;
                    border: 1px solid var(--border);
                    border-radius: 4px;
                    font-size: 0.8rem;
                  "
                  class="edit-item-input"
                  @input="$emit('update:editItemValue', $event.target.value)"
                />
              </div>
              <div class="form-group">
                <label
                  :for="`designer-edit-item-reason-${item.id}`"
                  style="font-size: 0.75rem"
                  >Change Reason Justification (Mandatory)</label
                >
                <input
                  :id="`designer-edit-item-reason-${item.id}`"
                  :value="editItemReason"
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
                  @input="$emit('update:editItemReason', $event.target.value)"
                />
              </div>
              <div style="display: flex; justify-content: flex-end; gap: 6px">
                <button class="btn btn-sm" @click="$emit('cancel-edit-item')">
                  Cancel
                </button>
                <button
                  class="btn btn-primary btn-sm save-edit-btn"
                  @click="$emit('save-edit-item', item.id)"
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
                background-color: var(--semantic-color-error-bg-soft);
                padding: 8px;
                border-radius: 6px;
              "
              class="item-reject-section"
            >
              <div class="form-group">
                <label
                  :for="`designer-reject-item-reason-${item.id}`"
                  style="
                    font-size: 0.75rem;
                    color: var(--semantic-color-error);
                    font-weight: bold;
                  "
                  >Provide Rejection Reason (Mandatory)</label
                >
                <input
                  :id="`designer-reject-item-reason-${item.id}`"
                  :value="rejectItemReason"
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
                  @input="$emit('update:rejectItemReason', $event.target.value)"
                />
              </div>
              <div style="display: flex; justify-content: flex-end; gap: 6px">
                <button class="btn btn-sm" @click="$emit('cancel-reject-item')">
                  Cancel
                </button>
                <button
                  class="btn btn-primary btn-sm confirm-reject-btn"
                  style="background-color: var(--semantic-color-error)"
                  @click="$emit('confirm-reject-item', item.id)"
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
              <div style="color: var(--semantic-color-text-secondary)">
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
                  :id="`btn-accept-candidate-${item.id}`"
                  class="btn btn-sm accept-btn"
                  style="padding: 2px 8px; font-size: 0.75rem"
                  @click="$emit('accept-item', item.id)"
                >
                  ✔️ Accept
                </button>
                <button
                  :id="`btn-edit-candidate-${item.id}`"
                  class="btn btn-sm edit-btn"
                  style="padding: 2px 8px; font-size: 0.75rem"
                  @click="$emit('start-edit-item', item)"
                >
                  ✏️ Edit
                </button>
                <button
                  :id="`btn-reject-candidate-${item.id}`"
                  class="btn btn-sm reject-btn"
                  style="
                    padding: 2px 8px;
                    font-size: 0.75rem;
                    background-color: var(--semantic-color-error-border-soft);
                    color: var(--semantic-color-error-text-dark);
                  "
                  @click="$emit('start-reject-item', item.id)"
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
              :value="promoteChangeReason"
              type="text"
              placeholder="Enter justification to promote reviewed draft into formal protocol..."
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              class="promote-change-reason"
              @input="$emit('update:promoteChangeReason', $event.target.value)"
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
              style="
                font-size: 0.8rem;
                color: var(--semantic-color-text-secondary);
              "
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
              @click="$emit('promote-candidate')"
            >
              🚀 Promote Reviewed Candidate
            </button>
          </div>
        </div>
        <div
          v-else
          id="designer-candidate-promoted-banner"
          tabindex="-1"
          style="
            background-color: var(--semantic-color-success-bg-soft);
            border: 1px solid var(--semantic-color-success-border-soft);
            border-radius: 8px;
            padding: 12px;
            color: var(--semantic-color-success-dark);
            font-size: 0.85rem;
            text-align: center;
          "
        >
          This candidate has already been promoted.
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, ref, watch } from "vue";
import { handoffFocus } from "ui";

const fileInputRef = ref(null);

const props = defineProps({
  store: { type: Object, required: true },
  selectedFileName: { type: String, required: true },
  importChangeReason: {
    type: String,
    default: "Import protocol draft into study",
  },
  selectedTemplateIds: { type: Array, default: () => [] },
  availableTemplates: { type: Array, default: () => [] },
  importingDraft: { type: Boolean, default: false },
  importSuccessMessage: { type: String, default: "" },
  importErrorMessage: { type: String, default: "" },
  importedDraftSummary: { type: Object, default: null },
  editingItemId: { type: String, default: null },
  editItemValue: { type: String, default: "" },
  editItemReason: { type: String, default: "" },
  rejectingItemId: { type: String, default: null },
  rejectItemReason: { type: String, default: "" },
  promoteChangeReason: { type: String, default: "" },
  unreviewedCount: { type: Number, required: true },
  getConfidenceClass: { type: Function, required: true },
  getStatusClass: { type: Function, required: true },
});

const emit = defineEmits([
  "update:importChangeReason",
  "update:selectedTemplateIds",
  "update:editItemValue",
  "update:editItemReason",
  "update:rejectItemReason",
  "update:promoteChangeReason",
  "register-file-input",
  "load-templates",
  "trigger-file-select",
  "trigger-document-upload",
  "import-selected-templates",
  "accept-item",
  "start-edit-item",
  "cancel-edit-item",
  "save-edit-item",
  "start-reject-item",
  "cancel-reject-item",
  "confirm-reject-item",
  "promote-candidate",
]);

function onChooseFileClick() {
  emit("trigger-file-select");
  if (fileInputRef.value && typeof fileInputRef.value.click === "function") {
    fileInputRef.value.click();
  }
}

function toggleTemplate(templateId, checked) {
  const current = Array.isArray(props.selectedTemplateIds)
    ? [...props.selectedTemplateIds]
    : [];
  const next = checked
    ? Array.from(new Set([...current, templateId]))
    : current.filter((id) => id !== templateId);
  emit("update:selectedTemplateIds", next);
}

onMounted(() => {
  emit("register-file-input", fileInputRef.value);
});

watch(fileInputRef, (el) => {
  emit("register-file-input", el);
});

watch(
  () =>
    Array.isArray(props.availableTemplates)
      ? props.availableTemplates.length
      : 0,
  (nextLen, prevLen) => {
    if (nextLen > 0 && !prevLen) {
      void handoffFocus([
        "#btn-import-cdisc-templates",
        () =>
          document.querySelector(
            '#designer-cdisc-templates input[type="checkbox"]'
          ),
        "#designer-import-reason",
      ]);
    }
  }
);

watch(
  () => props.editingItemId,
  (nextId, prevId) => {
    if (nextId) {
      void handoffFocus(`#designer-edit-item-value-${nextId}`, {
        scrollIfOffscreen: true,
      });
    } else if (prevId) {
      void handoffFocus([
        `#btn-edit-candidate-${prevId}`,
        "#promote-change-reason",
        "#designer-import-reason",
      ]);
    }
  }
);

watch(
  () => props.rejectingItemId,
  (nextId, prevId) => {
    if (nextId) {
      void handoffFocus(`#designer-reject-item-reason-${nextId}`, {
        scrollIfOffscreen: true,
      });
    } else if (prevId) {
      void handoffFocus([
        `#btn-reject-candidate-${prevId}`,
        `#btn-edit-candidate-${prevId}`,
        "#promote-change-reason",
        "#designer-import-reason",
      ]);
    }
  }
);

watch(
  () => props.store?.candidateDraft?.status,
  (nextStatus, prevStatus) => {
    if (nextStatus === "PROMOTED" && prevStatus && prevStatus !== "PROMOTED") {
      void handoffFocus([
        "#designer-candidate-promoted-banner",
        "#designer-import-reason",
      ]);
    }
  }
);

defineExpose({
  fileInputRef,
});
</script>
