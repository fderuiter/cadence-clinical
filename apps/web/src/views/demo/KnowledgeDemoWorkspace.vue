<template>
  <div class="knowledge-demo-modals">
    <!-- Modal 1: Create New Article Modal -->
    <ClinicalModal
      v-model="isCreateArticleModalOpen"
      :teleport="false"
      :show-footer="false"
      size="lg"
      dialog-class="modal-card modal-lg"
      aria-labelledby="create-art-modal-title"
    >
      <template #header>
        <h3 id="create-art-modal-title">➕ Author New Knowledge SOP Article</h3>
      </template>

      <form @submit.prevent="handleCreateArticle">
        <div class="form-group">
          <label for="new-art-title">SOP Title *</label>
          <input
            id="new-art-title"
            v-model="newArticleForm.title"
            type="text"
            class="form-control"
            placeholder="e.g. SOP-ECRF-301: Adverse Event Severity Grading"
            required
            @input="autoGenerateSlug"
          />
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="new-art-slug">Document Slug *</label>
            <input
              id="new-art-slug"
              v-model="newArticleForm.slug"
              type="text"
              class="form-control"
              placeholder="sop-ecrf-301-adverse-events"
              required
            />
          </div>
          <div class="form-group col">
            <label for="new-art-category">Category *</label>
            <select
              id="new-art-category"
              v-model="newArticleForm.category_id"
              class="form-control"
              required
            >
              <option
                v-for="cat in visibleCategories"
                :key="cat.id"
                :value="cat.id"
              >
                {{ cat.name }}
              </option>
            </select>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="new-art-version">Initial Version Label</label>
            <input
              id="new-art-version"
              v-model="newArticleForm.version_label"
              type="text"
              class="form-control"
              placeholder="0.1"
            />
          </div>
          <div class="form-group col">
            <label for="new-art-tags">Tags (comma-separated)</label>
            <input
              id="new-art-tags"
              v-model="newArticleForm.tagsInput"
              type="text"
              class="form-control"
              placeholder="ecrf, safety, adverse-events"
            />
          </div>
        </div>

        <div class="form-group">
          <label for="new-art-body">Markdown SOP Body *</label>
          <textarea
            id="new-art-body"
            v-model="newArticleForm.body_markdown"
            class="form-control code-textarea"
            rows="8"
            placeholder="## Purpose & Scope&#10;Describe procedural guidance, regulatory backing (21 CFR Part 11), and responsibilities..."
            required
          />
        </div>

        <div class="form-group">
          <label for="new-art-reason">GxP Reason for Change *</label>
          <input
            id="new-art-reason"
            v-model="newArticleForm.reason_for_change"
            type="text"
            class="form-control"
            placeholder="Initial authoring of standard operating procedure"
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isCreateArticleModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Save Working Draft
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 2: Edit Working Draft Modal -->
    <ClinicalModal
      v-model="isEditDraftModalOpen"
      :teleport="false"
      :show-footer="false"
      size="lg"
      dialog-class="modal-card modal-lg"
      aria-labelledby="edit-draft-modal-title"
    >
      <template #header>
        <h3 id="edit-draft-modal-title">✏️ Edit Working Draft SOP</h3>
      </template>

      <form @submit.prevent="handleSaveDraft">
        <div class="form-group">
          <label for="edit-art-title">SOP Title *</label>
          <input
            id="edit-art-title"
            v-model="editDraftForm.title"
            type="text"
            class="form-control"
            required
          />
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="edit-art-category">Category</label>
            <select
              id="edit-art-category"
              v-model="editDraftForm.category_id"
              class="form-control"
            >
              <option
                v-for="cat in visibleCategories"
                :key="cat.id"
                :value="cat.id"
              >
                {{ cat.name }}
              </option>
            </select>
          </div>
          <div class="form-group col">
            <label for="edit-art-tags">Tags</label>
            <input
              id="edit-art-tags"
              v-model="editDraftForm.tagsInput"
              type="text"
              class="form-control"
            />
          </div>
        </div>

        <div class="form-group">
          <div class="textarea-label-row">
            <label for="edit-art-body">Markdown SOP Content *</label>
            <button
              type="button"
              class="btn-text-toggle"
              @click="isPreviewMode = !isPreviewMode"
            >
              {{
                isPreviewMode ? "👁️ Show Raw Markdown" : "🔍 Show Live Preview"
              }}
            </button>
          </div>

          <!-- eslint-disable vue/no-v-html -->
          <div
            v-if="isPreviewMode"
            class="preview-container prose-content"
            v-html="renderPreviewHtml(editDraftForm.body_markdown)"
          />
          <!-- eslint-enable vue/no-v-html -->
          <textarea
            v-else
            id="edit-art-body"
            v-model="editDraftForm.body_markdown"
            class="form-control code-textarea"
            rows="10"
            required
          />
        </div>

        <div class="form-group">
          <label for="edit-art-reason">GxP Reason for Change *</label>
          <input
            id="edit-art-reason"
            v-model="editDraftForm.reason_for_change"
            type="text"
            class="form-control"
            placeholder="Updated procedural guidelines and regulatory notes"
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isEditDraftModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Save Draft Changes
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 3: Part 11 Action Justification / Reason Modal -->
    <ClinicalModal
      v-model="isReasonModalOpen"
      :teleport="false"
      :show-footer="false"
      dialog-class="modal-card"
      aria-labelledby="reason-modal-title"
    >
      <template #header>
        <h3 id="reason-modal-title">
          {{ actionModalTitle }}
        </h3>
      </template>

      <form @submit.prevent="executeActionWithReason">
        <p class="reason-modal-intro">
          {{ actionModalIntro }}
        </p>

        <div v-if="actionModalType === 'publish'" class="form-group">
          <label for="action-version-label">Published Version Label *</label>
          <input
            id="action-version-label"
            v-model="actionVersionLabel"
            type="text"
            class="form-control"
            placeholder="e.g. 1.0 or 2.0"
            required
          />
        </div>

        <div class="form-group">
          <label for="action-reason-input"
            >21 CFR Part 11 Reason for Change *</label
          >
          <textarea
            id="action-reason-input"
            v-model="actionReason"
            class="form-control"
            rows="3"
            placeholder="Mandatory regulatory reason for executing this document state change..."
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isReasonModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn" :class="actionButtonClass">
            {{ actionButtonLabel }}
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 4: Create Category Modal -->
    <ClinicalModal
      v-model="isCreateCategoryModalOpen"
      :teleport="false"
      :show-footer="false"
      dialog-class="modal-card"
      aria-labelledby="create-cat-modal-title"
    >
      <template #header>
        <h3 id="create-cat-modal-title">📁 Create Knowledge Category</h3>
      </template>

      <form @submit.prevent="handleCreateCategory">
        <div class="form-group">
          <label for="new-cat-name">Category Name *</label>
          <input
            id="new-cat-name"
            v-model="newCategoryForm.name"
            type="text"
            class="form-control"
            placeholder="e.g. Safety & Pharmacovigilance"
            required
            @input="autoGenerateCategorySlug"
          />
        </div>

        <div class="form-group">
          <label for="new-cat-slug">Category Slug *</label>
          <input
            id="new-cat-slug"
            v-model="newCategoryForm.slug"
            type="text"
            class="form-control"
            placeholder="safety-pharmacovigilance"
            required
          />
        </div>

        <div class="form-group">
          <label for="new-cat-desc">Description</label>
          <input
            id="new-cat-desc"
            v-model="newCategoryForm.description"
            type="text"
            class="form-control"
            placeholder="Guidelines for safety event intake, triage, and expedited reporting."
          />
        </div>

        <fieldset class="form-group form-fieldset">
          <legend class="form-label">
            Persona Visibility (All by default if empty)
          </legend>
          <div class="persona-checkbox-grid">
            <label
              v-for="p in PERSONA_PRESETS"
              :key="p.key"
              class="checkbox-label"
            >
              <input
                v-model="newCategoryForm.persona_visibility"
                type="checkbox"
                :value="p.key"
              />
              <span>{{ p.label }}</span>
            </label>
          </div>
        </fieldset>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isCreateCategoryModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">Create Category</button>
        </div>
      </form>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { ClinicalModal } from "ui";
import {
  compileSopMarkdownToHtml,
  knowledgeService,
} from "../../api/knowledge.js";
import { PERSONA_PRESETS } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";

const props = defineProps({
  visibleCategories: {
    type: Array,
    default: () => [],
  },
  selectedArticle: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits([
  "article-created",
  "article-updated",
  "category-created",
]);

const toastStore = useToastStore();

const isCreateArticleModalOpen = ref(false);
const isEditDraftModalOpen = ref(false);
const isReasonModalOpen = ref(false);
const isCreateCategoryModalOpen = ref(false);
const isPreviewMode = ref(false);

const newArticleForm = ref({
  title: "",
  slug: "",
  category_id: "",
  version_label: "0.1",
  tagsInput: "",
  body_markdown: "",
  reason_for_change: "Initial SOP document draft creation",
});

const editDraftForm = ref({
  title: "",
  category_id: "",
  tagsInput: "",
  body_markdown: "",
  reason_for_change: "",
});

const newCategoryForm = ref({
  name: "",
  slug: "",
  description: "",
  persona_visibility: [],
});

const actionModalType = ref("");
const actionModalTitle = ref("");
const actionModalIntro = ref("");
const actionReason = ref("");
const actionVersionLabel = ref("");
const actionButtonLabel = ref("Confirm");
const actionButtonClass = ref("btn-primary");

function getTagList(tags) {
  if (!tags) return [];
  if (Array.isArray(tags)) return tags;
  if (typeof tags === "string") {
    return tags
      .split(",")
      .map((t) => t.trim())
      .filter(Boolean);
  }
  return [];
}

function renderPreviewHtml(markdown) {
  if (!markdown) return "<p>No content to preview.</p>";
  return compileSopMarkdownToHtml(markdown);
}

function openCreateArticleModal() {
  const defaultCat = props.visibleCategories[0]?.id || "cat-clinical-ops";
  newArticleForm.value = {
    title: "",
    slug: "",
    category_id: defaultCat,
    version_label: "0.1",
    tagsInput: "",
    body_markdown:
      "## Purpose & Scope\n\n### Responsibilities\n1. \n\n### Procedure\n1. ",
    reason_for_change: "Initial authoring of standard operating procedure",
  };
  isCreateArticleModalOpen.value = true;
}

function autoGenerateSlug() {
  if (newArticleForm.value.title) {
    newArticleForm.value.slug = newArticleForm.value.title
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");
  }
}

function autoGenerateCategorySlug() {
  if (newCategoryForm.value.name) {
    newCategoryForm.value.slug = newCategoryForm.value.name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");
  }
}

async function handleCreateArticle() {
  try {
    if (!newArticleForm.value.slug && newArticleForm.value.title) {
      autoGenerateSlug();
    }
    const tags = newArticleForm.value.tagsInput
      ? newArticleForm.value.tagsInput
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean)
      : [];
    const payload = {
      title: newArticleForm.value.title,
      slug:
        newArticleForm.value.slug ||
        newArticleForm.value.title.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
      category_id:
        newArticleForm.value.category_id ||
        props.visibleCategories[0]?.id ||
        "cat-clinical-ops",
      version_label: newArticleForm.value.version_label || "0.1",
      tags,
      body_markdown: newArticleForm.value.body_markdown,
      reason_for_change: newArticleForm.value.reason_for_change,
    };
    const created = await knowledgeService.createArticle(payload);
    emit("article-created", created);
    isCreateArticleModalOpen.value = false;
  } catch (err) {
    toastStore.apiError("Could not create article", err);
  }
}

function openEditDraftModal() {
  if (!props.selectedArticle) return;
  const art = props.selectedArticle;
  editDraftForm.value = {
    title: art.title,
    category_id: art.category_id,
    tagsInput: getTagList(art.tags).join(", "),
    body_markdown: art.body_markdown || "",
    reason_for_change: `Updated draft for ${art.title}`,
  };
  isPreviewMode.value = false;
  isEditDraftModalOpen.value = true;
}

async function handleSaveDraft() {
  if (!props.selectedArticle) return;
  try {
    const tags = editDraftForm.value.tagsInput
      ? editDraftForm.value.tagsInput
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean)
      : [];
    const payload = {
      title: editDraftForm.value.title,
      category_id: editDraftForm.value.category_id,
      tags,
      body_markdown: editDraftForm.value.body_markdown,
      reason_for_change: editDraftForm.value.reason_for_change,
    };
    const updated = await knowledgeService.updateArticleDraft(
      props.selectedArticle.id,
      payload
    );
    emit("article-updated", updated);
    isEditDraftModalOpen.value = false;
  } catch (err) {
    toastStore.apiError("Could not save draft", err);
  }
}

function promptSubmitReview() {
  actionModalType.value = "submit_review";
  actionModalTitle.value = "📨 Submit Draft for Four-Eyes Review";
  actionModalIntro.value =
    "Submitting this working draft will transition the document to IN_REVIEW status. An independent peer reviewer must review and approve this revision.";
  actionReason.value =
    "Draft completed; submitted for formal quality & regulatory review.";
  actionButtonLabel.value = "Submit for Review";
  actionButtonClass.value = "btn-primary";
  isReasonModalOpen.value = true;
}

function promptApproveArticle() {
  actionModalType.value = "approve";
  actionModalTitle.value = "✓ Approve Article Revision";
  actionModalIntro.value =
    "Approving this article locks the working version snapshot as permanently immutable per 21 CFR Part 11 four-eyes principles.";
  actionReason.value =
    "Verified procedural accuracy and regulatory compliance.";
  actionButtonLabel.value = "Approve & Lock Version";
  actionButtonClass.value = "btn-success";
  isReasonModalOpen.value = true;
}

function promptRejectArticle() {
  actionModalType.value = "reject";
  actionModalTitle.value = "✕ Reject Article Revision";
  actionModalIntro.value =
    "Rejecting this draft returns it to REJECTED status. Please provide clear clinical or regulatory justification for rejection.";
  actionReason.value =
    "Protocol amendment alignment requires updates before approval.";
  actionButtonLabel.value = "Reject Draft";
  actionButtonClass.value = "btn-danger";
  isReasonModalOpen.value = true;
}

function promptPublishArticle() {
  actionModalType.value = "publish";
  actionModalTitle.value = "🚀 Publish Approved SOP Live";
  actionModalIntro.value =
    "Publishing will immediately make this document active across the platform and automatically supersede any previous published version.";
  actionReason.value =
    "Formal regulatory release for clinical site operations.";
  actionVersionLabel.value = props.selectedArticle?.version_label || "1.0";
  actionButtonLabel.value = "Publish Live";
  actionButtonClass.value = "btn-primary";
  isReasonModalOpen.value = true;
}

function promptArchiveArticle() {
  actionModalType.value = "archive";
  actionModalTitle.value = "📦 Archive Knowledge Article";
  actionModalIntro.value =
    "Archiving will deprecate this SOP document and remove it from active clinical workflows while retaining 21 CFR Part 11 audit records.";
  actionReason.value =
    "Replaced by updated master protocol and standard operating procedures.";
  actionButtonLabel.value = "Archive SOP";
  actionButtonClass.value = "btn-danger";
  isReasonModalOpen.value = true;
}

async function executeActionWithReason() {
  if (!props.selectedArticle) return;
  const artId = props.selectedArticle.id;
  const reason = actionReason.value;

  try {
    let updated = null;
    if (actionModalType.value === "submit_review") {
      updated = await knowledgeService.submitArticleReview(artId, {
        reason_for_change: reason,
      });
    } else if (actionModalType.value === "approve") {
      updated = await knowledgeService.approveArticle(artId, {
        reason_for_change: reason,
      });
    } else if (actionModalType.value === "reject") {
      updated = await knowledgeService.rejectArticle(artId, {
        reason_for_change: reason,
      });
    } else if (actionModalType.value === "publish") {
      updated = await knowledgeService.publishArticle(artId, {
        reason_for_change: reason,
        version_label: actionVersionLabel.value,
      });
    } else if (actionModalType.value === "archive") {
      updated = await knowledgeService.transitionArticle(artId, {
        target_status: "ARCHIVED",
        reason_for_change: reason,
      });
    }

    if (updated) {
      emit("article-updated", updated);
    }

    isReasonModalOpen.value = false;
  } catch (err) {
    toastStore.apiError("Could not update article status", err);
  }
}

function openCreateCategoryModal() {
  newCategoryForm.value = {
    name: "",
    slug: "",
    description: "",
    persona_visibility: [],
  };
  isCreateCategoryModalOpen.value = true;
}

async function handleCreateCategory() {
  try {
    if (!newCategoryForm.value.slug && newCategoryForm.value.name) {
      autoGenerateCategorySlug();
    }
    const payload = {
      name: newCategoryForm.value.name,
      slug:
        newCategoryForm.value.slug ||
        newCategoryForm.value.name.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
      description: newCategoryForm.value.description,
      persona_visibility: newCategoryForm.value.persona_visibility,
    };
    const created = await knowledgeService.createCategory(payload);
    emit("category-created", created);
    isCreateCategoryModalOpen.value = false;
  } catch (err) {
    toastStore.apiError("Could not create category", err);
  }
}

defineExpose({
  openCreateArticleModal,
  autoGenerateSlug,
  autoGenerateCategorySlug,
  handleCreateArticle,
  openEditDraftModal,
  handleSaveDraft,
  promptSubmitReview,
  promptApproveArticle,
  promptRejectArticle,
  promptPublishArticle,
  promptArchiveArticle,
  executeActionWithReason,
  openCreateCategoryModal,
  handleCreateCategory,
  renderPreviewHtml,
});
</script>

<style scoped>
:deep(.modal-card) {
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
  width: 100%;
  max-width: 580px;
  display: flex;
  flex-direction: column;
  max-height: 90vh;
  overflow: hidden;
}

:deep(.modal-lg) {
  max-width: 820px;
}

.modal-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 14px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 12px;
}

.form-group label,
.form-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--primary);
}

.form-row {
  display: flex;
  gap: 12px;
}

.form-row .col {
  flex: 1;
}

.form-control {
  width: 100%;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.85rem;
  box-sizing: border-box;
}

.form-control:focus-visible {
  border-color: var(--accent);
}

.code-textarea {
  font-family: monospace;
  font-size: 0.85rem;
  line-height: 1.4;
}

.textarea-label-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.btn-text-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: 4px;
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  box-sizing: border-box;
}

.preview-container {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
  min-height: 200px;
  max-height: 300px;
  overflow-y: auto;
  background-color: var(--semantic-color-surface-muted);
}

.persona-checkbox-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-top: 4px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  cursor: pointer;
}

.reason-modal-intro {
  font-size: 0.85rem;
  color: var(--neutral-dark);
  margin-bottom: 10px;
}

.form-fieldset {
  border: none;
  padding: 0;
  margin: 0;
  min-width: 0;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--primary);
  border-color: var(--border);
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}
</style>
