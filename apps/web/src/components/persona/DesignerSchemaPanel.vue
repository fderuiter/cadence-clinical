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
        Ultimate CRF Builder: Protocol Ingestion &amp; Review
      </div>
      <p
        style="font-size: 0.85rem; color: var(--semantic-color-text-secondary)"
      >
        Upload a clinical protocol document (PDF/DOCX) to automatically generate
        candidate SoA visits and form fields with trace citations and confidence
        levels. Accept, edit, or reject each item before promoting reviewed
        candidates into a formal study draft.
      </p>

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
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          ref="fileInputRef"
          type="file"
          accept=".pdf,.docx"
          style="display: none"
          @change="$emit('trigger-document-upload', $event)"
        />
        <button
          class="btn"
          type="button"
          :disabled="store.ingestionLoading"
          @click="$emit('trigger-file-select')"
        >
          {{
            store.ingestionLoading
              ? "Processing Document..."
              : "📁 Select Protocol PDF/DOCX"
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

      <!-- Ingestion Error Display -->
      <div
        v-if="store.ingestionError"
        style="
          color: var(--semantic-color-error);
          font-size: 0.85rem;
          margin-top: 8px;
        "
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
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label style="font-size: 0.75rem"
                  >Modify Candidate Name/Label</label
                >
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
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
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label style="font-size: 0.75rem"
                  >Change Reason Justification (Mandatory)</label
                >
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
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
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label
                  style="
                    font-size: 0.75rem;
                    color: var(--semantic-color-error);
                    font-weight: bold;
                  "
                  >Provide Rejection Reason (Mandatory)</label
                >
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
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
                  class="btn btn-sm accept-btn"
                  style="padding: 2px 8px; font-size: 0.75rem"
                  @click="$emit('accept-item', item.id)"
                >
                  ✔️ Accept
                </button>
                <button
                  class="btn btn-sm edit-btn"
                  style="padding: 2px 8px; font-size: 0.75rem"
                  @click="$emit('start-edit-item', item)"
                >
                  ✏️ Edit
                </button>
                <button
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
import { ref } from "vue";

const fileInputRef = ref(null);

defineProps({
  store: { type: Object, required: true },
  selectedFileName: { type: String, required: true },
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

defineEmits([
  "update:editItemValue",
  "update:editItemReason",
  "update:rejectItemReason",
  "update:promoteChangeReason",
  "trigger-file-select",
  "trigger-document-upload",
  "accept-item",
  "start-edit-item",
  "cancel-edit-item",
  "save-edit-item",
  "start-reject-item",
  "cancel-reject-item",
  "confirm-reject-item",
  "promote-candidate",
]);

defineExpose({
  fileInputRef,
});
</script>
