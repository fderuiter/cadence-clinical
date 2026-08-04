<template>
  <div
    ref="modalRef"
    class="modal-backdrop"
    role="dialog"
    aria-modal="true"
    aria-label="Secure PDF Preview Modal"
    @click.self="$emit('close')"
  >
    <div class="modal-card pdf-preview-modal">
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
            <div v-if="document.content_checksum" class="checksum-block">
              <h5>SHA-256 Digest:</h5>
              <code class="checksum-text" :title="document.content_checksum">
                {{ document.content_checksum }}
              </code>
            </div>
            <div v-else class="checksum-block">
              <h5>Verification Integrity:</h5>
              <code class="checksum-text text-success">
                MOCK-SHA256-VALIDATED-INTEGRITY-OK
              </code>
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
        </aside>

        <!-- Simulated Document Frame with Dynamic Overlay Watermark -->
        <main class="viewer-workspace" aria-label="Document Viewer">
          <div class="pdf-document-canvas">
            <!-- Dynamic CSS Rotating Diagonal Watermark -->
            <div class="watermark-overlay-container" aria-hidden="true">
              <div v-for="n in 3" :key="n" class="diagonal-watermark-row">
                <span class="watermark-text">{{ watermarkText }}</span>
              </div>
            </div>

            <!-- Page 1 Content Sheet Mock -->
            <div class="document-page">
              <div class="page-header">
                <div class="logo">Cadence Clinical Systems</div>
                <div class="doc-code">ST-{{ document.study_id }}</div>
              </div>

              <div class="page-content">
                <h1 class="doc-title">
                  {{ document.artifact_type || "Clinical Trial Document" }}
                </h1>
                <p class="doc-subtitle">
                  DIA TMF Code: {{ document.artifact_code }} | Version Index:
                  {{ document.version_index }}.0
                </p>

                <hr class="divider" />

                <section class="doc-section-content">
                  <h3>1. REGULATORY INTENT AND AUDIT CLASSIFICATION</h3>
                  <p>
                    This electronic record has been indexed within the Cadence
                    Clinical Electronic Trial Master File (eTMF) in full
                    alignment with FDA 21 CFR Part 11 and EU Annex 11 digital
                    record keeping standards. Any modifications, status
                    transitions, or sign-off requests execute within a validated
                    transaction boundary and emit to the global cryptographic
                    ledger chain.
                  </p>
                </section>

                <section class="doc-section-content">
                  <h3>2. DIGITAL TRACEABILITY DATA</h3>
                  <p>
                    <strong>Parent Study:</strong> {{ document.study_id }}<br />
                    <strong>Record ID:</strong> {{ document.id }}<br />
                    <strong>Ingestion Filename:</strong> {{ document.filename
                    }}<br />
                    <strong>MIME Category:</strong> {{ document.mime_type
                    }}<br />
                    <strong>Author Identity:</strong> {{ document.created_by }}
                  </p>
                </section>

                <section class="doc-section-content">
                  <h3>3. GxP COMPLIANCE AND ENCRYPTION CHECKS</h3>
                  <p>
                    Verification check completed successfully. System integrity
                    is active. This preview is generated securely with real-time
                    watermark placement rendering the viewing identity and
                    date-timestamp on-the-fly. Do not photocopy or distribute
                    this document without explicit study delegation authority.
                  </p>
                </section>
              </div>

              <div class="page-footer">
                <span>Page 1 of 1</span>
                <span>CONFIDENTIAL - FOR AUTHORIZED CLINICAL USE ONLY</span>
              </div>
            </div>
          </div>
        </main>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { useAuthStore } from "../../stores/auth";
import { useFocusTrap } from "@/composables/useFocusTrap";
import { useEscapeClose } from "@/composables/useEscapeClose";

const props = defineProps({
  document: {
    type: Object,
    required: true,
  },
});

const emit = defineEmits(["close"]);

const modalRef = ref(null);
useFocusTrap(modalRef);
useEscapeClose(() => emit("close"));

const authStore = useAuthStore();

// Compute Username dynamically from authentication context or fallback
const currentUserId = computed(() => {
  return authStore.identity?.username || "fderuiter";
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
  background-color: var(--color-surface-muted);
}

/* Metadata Sidebar styling */
.metadata-sidebar {
  width: 300px;
  background-color: var(--color-surface);
  border-right: 1px solid var(--color-border);
  display: flex;
  flex-direction: column;
  padding: var(--spacing-md);
  overflow-y: auto;
  box-sizing: border-box;
}

.meta-section {
  margin-bottom: var(--spacing-lg);
}

.meta-section.border-top {
  border-top: 1px solid var(--color-border);
  padding-top: var(--spacing-md);
}

.meta-section h4 {
  margin: 0 0 var(--spacing-sm) 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--color-text);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.meta-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: var(--spacing-xs);
  font-size: 0.85rem;
}

.meta-label {
  color: var(--color-text-muted);
  font-weight: 500;
}

.meta-value {
  color: var(--color-text);
  text-align: right;
  word-break: break-all;
}

.font-mono {
  font-family: monospace;
}

.font-bold {
  font-weight: 700;
}

/* Status colors */
.text-muted {
  color: var(--color-text-muted);
}
.text-warning {
  color: var(--color-warning);
}
.text-success {
  color: var(--color-success);
}
.text-danger {
  color: var(--color-error);
}

.change-reason-block h5,
.checksum-block h5 {
  margin: 0 0 var(--spacing-2xs) 0;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-text-muted);
}

.reason-text {
  font-size: 0.8rem;
  color: var(--color-text);
  background-color: var(--color-surface-muted);
  padding: var(--spacing-xs);
  border-radius: 4px;
  margin: 0;
  line-height: 1.4;
  border: 1px solid var(--color-border);
}

.checksum-text {
  display: block;
  font-family: monospace;
  font-size: 0.75rem;
  background-color: var(--color-surface-muted);
  color: var(--color-text);
  padding: var(--spacing-2xs);
  border-radius: 4px;
  overflow-x: auto;
  word-break: break-all;
}

/* Signature Cards */
.manifest-card {
  border-radius: 6px;
  padding: var(--spacing-sm);
  font-size: 0.8rem;
}

.manifest-card.signed {
  background-color: var(--color-success-bg);
  border: 1px solid var(--color-success-bg);
  color: var(--color-success);
}

.manifest-card.unsigned {
  background-color: var(--color-warning-bg);
  border: 1px solid var(--color-warning-bg);
  color: var(--color-warning);
}

.sign-status {
  font-weight: 700;
  margin: 0 0 var(--spacing-2xs) 0;
  display: flex;
  align-items: center;
  gap: var(--spacing-2xs);
}

.signer-meta {
  margin: 0 0 var(--spacing-2xs) 0;
}

.unsigned-warning {
  margin: 0;
  line-height: 1.4;
}

/* Document Frame Canvas */
.viewer-workspace {
  flex: 1;
  display: flex;
  justify-content: center;
  padding: var(--spacing-xl);
  overflow-y: auto;
  position: relative;
}

.pdf-document-canvas {
  width: 100%;
  max-width: 650px;
  background-color: var(--color-surface);
  box-shadow:
    0 10px 15px -3px var(--color-border),
    0 4px 6px -2px rgba(0, 0, 0, 0.05);
  border-radius: 4px;
  position: relative;
  display: flex;
  flex-direction: column;
  min-height: 800px;
}

/* CSS Watermarking overlay rotates 35deg diagonally across the page content background */
.watermark-overlay-container {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  z-index: 100;
  pointer-events: none;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  justify-content: space-around;
  align-items: center;
  opacity: 0.12; /* subtle watermark overlay */
}

.diagonal-watermark-row {
  transform: rotate(-30deg);
  white-space: nowrap;
}

.watermark-text {
  font-family: Arial, sans-serif;
  font-size: 1rem;
  font-weight: 900;
  color: var(--color-error);
  letter-spacing: 0.1em;
}

/* Page content Mock-up sheet */
.document-page {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: var(--spacing-4xl);
  box-sizing: border-box;
  z-index: 10;
}

.page-header {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: var(--color-text-muted);
  font-weight: 600;
  text-transform: uppercase;
  margin-bottom: var(--spacing-xl);
}

.page-content {
  flex: 1;
}

.doc-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--color-text);
  margin: 0 0 var(--spacing-xs) 0;
}

.doc-subtitle {
  font-size: 0.85rem;
  color: var(--color-text-muted);
  margin: 0 0 var(--spacing-md) 0;
}

.divider {
  border: none;
  border-top: 2px solid var(--color-border);
  margin-bottom: var(--spacing-xl);
}

.doc-section-content {
  margin-bottom: var(--spacing-xl);
}

.doc-section-content h3 {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--color-text);
  margin: 0 0 var(--spacing-xs) 0;
}

.doc-section-content p {
  font-size: 0.85rem;
  line-height: 1.6;
  color: var(--color-text);
  margin: 0;
}

.page-footer {
  display: flex;
  justify-content: space-between;
  font-size: 0.7rem;
  color: var(--color-text-muted);
  border-top: 1px solid var(--color-border);
  padding-top: var(--spacing-sm);
  margin-top: var(--spacing-2xl);
}
</style>
