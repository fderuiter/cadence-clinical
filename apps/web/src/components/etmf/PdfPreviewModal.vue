<template>
  <div class="modal-backdrop" role="presentation" @click.self="$emit('close')">
    <div
      ref="modalRef"
      class="modal-card pdf-preview-modal"
      role="dialog"
      aria-modal="true"
      aria-label="Secure PDF Preview Modal"
      tabindex="-1"
    >
      <div class="modal-header">
        <div class="header-details">
          <h3>
            <span class="secure-icon" aria-hidden="true">🔒</span> Secure
            Regulated Viewer -
            {{ document.filename }}
          </h3>
          <p class="gxp-tracking-subtitle">
            System ID: <code>{{ document.id }}</code> | Version:
            <strong>v{{ document.version_index }}.0</strong>
          </p>
        </div>
        <button
          class="close-modal-btn"
          aria-label="Close modal"
          @click="$emit('close')"
        >
          ×
        </button>
      </div>

      <div class="modal-body-layout">
        <!-- Sidebar with GxP Provenance Ledger Metadata -->
        <aside class="metadata-sidebar" aria-label="Document Metadata">
          <div class="meta-section">
            <h4>Record Details</h4>
            <div v-if="document.artifact_type" class="meta-row">
              <span class="meta-label">Artifact Type:</span>
              <span class="meta-value">{{ document.artifact_type }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">DIA Code:</span>
              <span class="meta-value font-mono">{{
                document.artifact_code
              }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Status:</span>
              <span
                class="meta-value font-bold"
                :class="getStatusClass(document.status)"
              >
                {{ document.status }}
              </span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Owner ID:</span>
              <span class="meta-value">{{ document.created_by }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Uploaded At:</span>
              <span class="meta-value">{{
                formatDate(document.created_at)
              }}</span>
            </div>
            <div v-if="document.site_id" class="meta-row">
              <span class="meta-label">Site Scope:</span>
              <span class="meta-value font-mono">{{ document.site_id }}</span>
            </div>
          </div>

          <div class="meta-section border-top">
            <h4>21 CFR Part 11 Audit Trail</h4>
            <div class="change-reason-block">
              <h5>Change Justification:</h5>
              <p class="reason-text">
                {{
                  document.reason_for_change ||
                  "No change justification provided."
                }}
              </p>
            </div>
            <div v-if="effectiveSha256" class="checksum-block">
              <h5>
                {{
                  verifiedSha256
                    ? "Verified SHA-256 Digest:"
                    : "SHA-256 Digest:"
                }}
              </h5>
              <code
                id="pdf-preview-sha256"
                class="checksum-text"
                :class="{ 'text-success': Boolean(verifiedSha256) }"
                :title="effectiveSha256"
              >
                {{ effectiveSha256 }}
              </code>
            </div>
            <div v-else class="checksum-block">
              <h5>Verification Integrity:</h5>
              <span
                id="pdf-preview-unverified"
                class="checksum-unverified text-muted"
              >
                Unverified — no SHA-256 digest recorded
              </span>
            </div>
          </div>

          <!-- Electronic Signature Manifestation -->
          <div class="meta-section border-top manifestation-section">
            <h4>Digital Signature Manifestation</h4>
            <div
              v-if="document.signer && document.signing_timestamp"
              class="manifest-card signed"
            >
              <p class="sign-status">📝 ELECTRONICALLY SIGNED</p>
              <p class="signer-meta">
                Signer: <strong>{{ document.signer }}</strong>
              </p>
              <p class="signer-meta">
                Reason:
                <strong>{{
                  document.signature_manifestation?.signing_reason || "APPROVED"
                }}</strong>
              </p>
              <p class="signer-meta">
                Date: <span>{{ formatDate(document.signing_timestamp) }}</span>
              </p>
            </div>
            <div v-else class="manifest-card unsigned">
              <p class="sign-status">⚠️ UNSIGNED RECORD</p>
              <p class="unsigned-warning">
                This document is a working electronic draft and has not been
                locked by signature manifestation.
              </p>
            </div>
          </div>

          <!-- Redaction / Compliance Manifest -->
          <div
            v-if="document.is_redacted"
            class="meta-section border-top redaction-manifest-section"
          >
            <h4>Redaction Manifest</h4>
            <div class="manifest-card redacted">
              <p class="redact-status">🛡️ HIPAA/GDPR COMPLIANT</p>
              <p
                v-if="document.redaction_manifest_json?.operator_name"
                class="redact-meta"
              >
                Operator:
                <strong>{{
                  document.redaction_manifest_json.operator_name
                }}</strong>
              </p>
              <p v-else-if="document.created_by" class="redact-meta">
                Operator: <strong>{{ document.created_by }}</strong>
              </p>
              <p
                v-if="document.redaction_manifest_json?.reason"
                class="redact-meta"
              >
                Reason:
                <strong>{{ document.redaction_manifest_json.reason }}</strong>
              </p>
              <p v-else-if="document.reason_for_change" class="redact-meta">
                Reason: <strong>{{ document.reason_for_change }}</strong>
              </p>
              <div
                v-if="document.redaction_manifest_json?.redacted_items_count"
                class="redacted-items-list"
              >
                <span class="meta-label-items">Redacted Items:</span>
                <ul class="redact-ul">
                  <li
                    v-for="(count, category) in document.redaction_manifest_json
                      .redacted_items_count"
                    :key="category"
                  >
                    {{ category }}: <strong>{{ count }}</strong>
                  </li>
                </ul>
              </div>
              <div
                v-if="document.redaction_manifest_json?.signature"
                class="signature-block"
              >
                <span class="meta-label-items">HMAC Signature:</span>
                <code
                  class="signature-text-code"
                  :title="document.redaction_manifest_json.signature"
                >
                  {{ document.redaction_manifest_json.signature }}
                </code>
              </div>
            </div>
          </div>
        </aside>

        <!-- Stored Document Stream with Dynamic GxP Overlay Watermark -->
        <div
          class="viewer-workspace"
          role="region"
          aria-label="Document Viewer"
        >
          <div class="pdf-document-canvas">
            <!-- Dynamic CSS Rotating Diagonal Watermark -->
            <div class="watermark-overlay-container" aria-hidden="true">
              <div v-for="n in 3" :key="n" class="diagonal-watermark-row">
                <span class="watermark-text">{{ watermarkText }}</span>
              </div>
            </div>

            <div
              v-if="isLoadingContent"
              id="pdf-preview-loading"
              class="stream-status-box"
              role="status"
              aria-live="polite"
            >
              <p class="stream-status-title">
                Streaming regulated document from object storage...
              </p>
              <p class="stream-status-meta">
                Verifying SHA-256 digest for
                <code>{{ document.filename }}</code>
              </p>
            </div>

            <div
              v-else-if="contentError"
              id="pdf-preview-error"
              class="stream-status-box stream-error-box"
              role="alert"
            >
              <p class="stream-status-title">
                Unable to render stored document stream
              </p>
              <p class="stream-status-meta">{{ contentError }}</p>
              <button
                type="button"
                class="btn-retry-stream"
                @click="loadDocumentStream"
              >
                Retry Stream
              </button>
            </div>

            <iframe
              v-else-if="isPdfStream && blobUrl"
              id="pdf-preview-iframe"
              :src="blobUrl"
              class="pdf-stream-frame"
              :title="`Stored PDF preview for ${document.filename}`"
            />

            <div
              v-else-if="decodedTextPreview !== null"
              id="pdf-preview-text"
              class="text-stream-sheet"
            >
              <pre class="text-stream-content">{{ decodedTextPreview }}</pre>
            </div>

            <div
              v-else-if="blobUrl"
              id="pdf-preview-binary-download"
              class="stream-status-box"
            >
              <p class="stream-status-title">
                Binary artifact ready ({{ streamMimeType }})
              </p>
              <a
                :href="blobUrl"
                :download="document.filename"
                class="btn-retry-stream"
              >
                Download Stored Artifact
              </a>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onBeforeUnmount } from "vue";
import { useFocusTrap } from "ui";
import { useAuthStore } from "../../stores/auth";
import { useEtmfStore } from "../../stores/etmf";

const props = defineProps({
  document: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(["close"]);

const modalRef = ref(null);
useFocusTrap(modalRef, {
  onEscape: () => emit("close"),
});

const authStore = useAuthStore();
const etmfStore = useEtmfStore();

const isLoadingContent = ref(false);
const contentError = ref("");
const blobUrl = ref("");
const streamMimeType = ref("");
const verifiedSha256 = ref("");
const decodedTextPreview = ref(null);

function revokeCurrentBlobUrl() {
  if (
    blobUrl.value &&
    typeof URL !== "undefined" &&
    typeof URL.revokeObjectURL === "function"
  ) {
    URL.revokeObjectURL(blobUrl.value);
  }
  blobUrl.value = "";
}

const effectiveSha256 = computed(() => {
  return (
    verifiedSha256.value ||
    props.document?.content_checksum ||
    props.document?.contentChecksum ||
    ""
  );
});

const isPdfStream = computed(() => {
  const mime = (
    streamMimeType.value ||
    props.document?.mime_type ||
    ""
  ).toLowerCase();
  return (
    mime.includes("pdf") ||
    String(props.document?.filename || "")
      .toLowerCase()
      .endsWith(".pdf")
  );
});

async function loadDocumentStream() {
  revokeCurrentBlobUrl();
  contentError.value = "";
  verifiedSha256.value = "";
  decodedTextPreview.value = null;

  if (!props.document?.id) {
    contentError.value = "Document record ID is missing.";
    return;
  }

  isLoadingContent.value = true;
  try {
    const result = await etmfStore.fetchDocumentContent(props.document);
    const mime =
      result?.mimeType || props.document?.mime_type || "application/pdf";
    streamMimeType.value = mime;
    const blob = result?.blob;
    if (!blob) {
      throw new Error("Empty document stream returned by storage service.");
    }

    if (result?.sha256) {
      verifiedSha256.value = result.sha256;
    } else if (
      typeof blob.arrayBuffer === "function" &&
      globalThis.crypto?.subtle?.digest
    ) {
      const digestBuffer = await globalThis.crypto.subtle.digest(
        "SHA-256",
        await blob.arrayBuffer()
      );
      verifiedSha256.value = Array.from(new Uint8Array(digestBuffer))
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
    }

    if (
      mime.startsWith("text/") ||
      mime.includes("json") ||
      mime.includes("xml") ||
      mime.includes("csv")
    ) {
      decodedTextPreview.value =
        typeof blob.text === "function" ? await blob.text() : "";
    } else if (
      typeof URL !== "undefined" &&
      typeof URL.createObjectURL === "function"
    ) {
      blobUrl.value = URL.createObjectURL(blob);
    }
  } catch (err) {
    contentError.value =
      err?.message || "Failed to stream document bytes from object storage.";
  } finally {
    isLoadingContent.value = false;
  }
}

watch(
  () => props.document?.id,
  () => {
    loadDocumentStream();
  },
  { immediate: true }
);

onBeforeUnmount(() => {
  revokeCurrentBlobUrl();
});

// Signed-in username from the Keycloak token
const currentUserId = computed(() => {
  return authStore.identity?.username || "";
});

// Compute current timestamp string
const viewTimestamp = computed(() => {
  return new Date().toISOString().replace("T", " ").substring(0, 19);
});

// Compute Audit Classification based on status
const auditClassification = computed(() => {
  const status = (props.document.status || "").toLowerCase();
  if (status.includes("approved") || status.includes("active")) {
    return "FINAL APPROVED";
  }
  if (status.includes("archive")) {
    return "ARCHIVED / REPLACED";
  }
  if (status.includes("review") || status.includes("pending")) {
    return "CONFIDENTIAL / UNDER REVIEW";
  }
  return "DRAFT - NOT FOR REGULATORY USE";
});

// Standard GxP compliant Watermark Text
const watermarkText = computed(() => {
  return `SECURE PREVIEW | USER: ${currentUserId.value} | VIEW TIME: ${viewTimestamp.value} | CLASS: ${auditClassification.value}`;
});

function getStatusClass(status) {
  const s = (status || "").toLowerCase();
  if (s.includes("draft")) return "text-muted";
  if (s.includes("review") || s.includes("pending")) return "text-warning";
  if (s.includes("approved") || s.includes("active")) return "text-success";
  if (s.includes("archive")) return "text-danger";
  return "";
}

function formatDate(dateStr) {
  if (!dateStr) return "N/A";
  try {
    const d = new Date(dateStr);
    return d.toLocaleString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  } catch {
    return dateStr;
  }
}
</script>

<style scoped>
.pdf-preview-modal {
  max-width: 1000px !important;
  width: 95% !important;
  height: 85vh;
}

.modal-body-layout {
  display: flex;
  flex: 1;
  overflow: hidden;
  background-color: var(--semantic-color-surface-header);
}

/* Metadata Sidebar styling */
.metadata-sidebar {
  width: 300px;
  background-color: var(--semantic-color-surface);
  border-right: 1px solid var(--semantic-color-border-control);
  display: flex;
  flex-direction: column;
  padding: 16px;
  overflow-y: auto;
  box-sizing: border-box;
}

.meta-section {
  margin-bottom: 20px;
}

.meta-section.border-top {
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 16px;
}

.meta-section h4 {
  margin: 0 0 12px 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.meta-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 8px;
  font-size: 0.85rem;
}

.meta-label {
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.meta-value {
  color: var(--semantic-color-text);
  text-align: right;
  word-break: break-all;
}

/* Status colors */
.text-muted {
  color: var(--semantic-color-text-secondary);
}
.text-warning {
  color: var(--semantic-color-warning);
}
.text-success {
  color: var(--semantic-color-success);
}
.text-danger {
  color: var(--semantic-color-error);
}

.change-reason-block h5,
.checksum-block h5 {
  margin: 0 0 6px;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.reason-text {
  font-size: 0.8rem;
  color: var(--semantic-color-text-dark);
  background-color: var(--semantic-color-surface-muted);
  margin: 0;
  padding: 8px;
  line-height: 1.4;
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-xs);
}

.checksum-text {
  display: block;
  padding: 6px;
  font-family: var(--font-mono, monospace);
  font-size: 0.75rem;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
  border-radius: var(--radius-xs);
  overflow-x: auto;
  word-break: break-all;
}

/* Signature Cards */
.manifest-card {
  padding: 12px;
  border-radius: var(--radius-md, 6px);
  font-size: 0.8rem;
}

.manifest-card.signed {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.manifest-card.unsigned {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.sign-status {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0 0 6px;
  font-weight: 700;
}

.signer-meta {
  margin: 0 0 4px;
}

.unsigned-warning {
  margin: 0;
  line-height: 1.4;
}

/* Document Frame Canvas */
.viewer-workspace {
  position: relative;
  flex: 1;
  display: flex;
  justify-content: center;
  padding: 24px;
  overflow-y: auto;
}

.pdf-document-canvas {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  max-width: 650px;
  min-height: 800px;
  background-color: var(--semantic-color-surface);
  border-radius: var(--radius-sm, 4px);
  box-shadow:
    0 10px 15px -3px rgba(0, 0, 0, 0.1),
    0 4px 6px -2px rgba(0, 0, 0, 0.05);
}

.watermark-overlay-container {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 20;
  display: flex;
  flex-direction: column;
  justify-content: space-around;
  align-items: center;
  overflow: hidden;
  opacity: 0.18;
}

.diagonal-watermark-row {
  transform: rotate(-28deg);
  white-space: nowrap;
}

/* CSS Watermarking overlay rotates diagonally across the stored document stream */
.watermark-text {
  font-family: var(--font-sans, Arial, sans-serif);
  font-size: 1rem;
  font-weight: 900;
  color: var(--semantic-color-error);
  letter-spacing: 0.1em;
}

.pdf-stream-frame {
  width: 100%;
  flex: 1;
  min-height: 760px;
  border: none;
  border-radius: var(--radius-sm, 4px);
  z-index: 10;
}

.text-stream-sheet {
  flex: 1;
  padding: 28px;
  overflow: auto;
  z-index: 10;
}

.text-stream-content {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: var(--font-mono, monospace);
  font-size: 0.82rem;
  line-height: 1.5;
  color: var(--semantic-color-text-dark);
}

.stream-status-box {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 36px;
  text-align: center;
  z-index: 10;
}

.stream-error-box {
  color: var(--semantic-color-error);
}

.stream-status-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
}

.stream-status-meta {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}

.btn-retry-stream {
  margin-top: 8px;
  padding: 6px 14px;
  font-size: 0.8rem;
  font-weight: 600;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-interactive-primary);
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  cursor: pointer;
  text-decoration: none;
}

.checksum-unverified {
  display: block;
  font-size: 0.78rem;
  font-style: italic;
}
.manifest-card.redacted {
  background-color: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}
.redact-status {
  font-weight: 700;
  margin: 0 0 6px 0;
  display: flex;
  align-items: center;
  gap: 4px;
}
.redact-meta {
  margin: 0 0 4px 0;
  font-size: 0.8rem;
}
.redacted-items-list {
  margin-top: 8px;
  font-size: 0.8rem;
}
.meta-label-items {
  color: var(--semantic-color-info);
  font-weight: 600;
}
.redact-ul {
  margin: 4px 0 0 16px;
  padding: 0;
}
.signature-block {
  margin-top: 8px;
  font-size: 0.8rem;
}
.signature-text-code {
  display: block;
  font-family: monospace;
  font-size: 0.75rem;
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  padding: 6px;
  border-radius: 4px;
  overflow-x: auto;
  word-break: break-all;
}
</style>
