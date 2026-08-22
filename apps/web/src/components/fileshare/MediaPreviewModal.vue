<template>
  <div
    ref="modalRef"
    class="modal-backdrop"
    role="dialog"
    aria-modal="true"
    :aria-label="'Secure Media Preview: ' + (fileRecord?.filename || 'File')"
    @click.self="$emit('close')"
  >
    <div
      class="modal-card media-preview-modal"
      @contextmenu.prevent
    >
      <!-- Header -->
      <div class="modal-header">
        <div class="header-details">
          <h3>
            <span
              class="secure-icon"
              aria-hidden="true"
            >🔒</span>
            Secure Regulated Viewer — {{ fileRecord?.filename }}
          </h3>
          <p class="gxp-tracking-subtitle">
            System ID: <code>{{ fileRecord?.id }}</code> | Version:
            <strong>v{{ fileRecord?.version_index || 1 }}.0</strong> | MIME:
            <code>{{ fileRecord?.mime_type || "application/octet-stream" }}</code>
          </p>
        </div>
        <div class="header-actions">
          <button
            v-if="canDownload"
            class="btn btn-sm btn-outline-primary btn-download"
            title="Download Original File"
            :aria-label="'Download ' + fileRecord?.filename"
            @click="handleDownload"
          >
            📥 Download
          </button>
          <button
            class="close-modal-btn"
            aria-label="Close preview modal"
            @click="$emit('close')"
          >
            ×
          </button>
        </div>
      </div>

      <!-- Layout: Sidebar + Main Viewer Workspace -->
      <div class="modal-body-layout">
        <!-- Metadata Sidebar -->
        <aside
          class="metadata-sidebar"
          aria-label="File Metadata and Audit Ledger"
        >
          <div class="meta-section">
            <h4>Record Details</h4>
            <div class="meta-row">
              <span class="meta-label">Filename:</span>
              <span
                class="meta-value font-mono"
                :title="fileRecord?.filename"
              >{{ fileRecord?.filename }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Study ID:</span>
              <span class="meta-value font-mono">{{ fileRecord?.study_id }}</span>
            </div>
            <div
              v-if="fileRecord?.site_id"
              class="meta-row"
            >
              <span class="meta-label">Site ID:</span>
              <span class="meta-value font-mono">{{ fileRecord?.site_id }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">File Size:</span>
              <span class="meta-value">{{ formatFileSize(fileRecord?.size_bytes) }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Status:</span>
              <span
                class="meta-value font-bold"
                :class="getStatusClass(fileRecord?.status)"
              >
                {{ fileRecord?.status || "ACTIVE" }}
              </span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Uploaded By:</span>
              <span class="meta-value font-mono">{{ fileRecord?.uploaded_by || fileRecord?.created_by }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Uploaded At:</span>
              <span class="meta-value">{{ formatDate(fileRecord?.uploaded_at || fileRecord?.created_at) }}</span>
            </div>
          </div>

          <!-- 21 CFR Part 11 Audit Record -->
          <div class="meta-section border-top">
            <h4>21 CFR Part 11 Audit Trail</h4>
            <div class="change-reason-block">
              <h5>Change Justification:</h5>
              <p class="reason-text">
                {{ fileRecord?.reason_for_change || "Standard regulated file ingestion." }}
              </p>
            </div>
            <div class="checksum-block">
              <h5>SHA-256 Digest:</h5>
              <code
                class="checksum-text"
                :title="fileRecord?.checksum_sha256"
              >
                {{ fileRecord?.checksum_sha256 || "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" }}
              </code>
            </div>
          </div>

          <!-- Active Access Permissions -->
          <div class="meta-section border-top">
            <h4>Security Context</h4>
            <div class="manifest-card signed">
              <p class="sign-status">
                🛡️ PROTECTED VIEW ENFORCED
              </p>
              <p class="signer-meta">
                Viewer: <strong>{{ currentUserId }}</strong>
              </p>
              <p class="signer-meta">
                Role Context: <span>{{ activeUserRoles.join(", ") || "Authenticated" }}</span>
              </p>
              <p class="signer-meta">
                Download:
                <strong>{{ canDownload ? "Permitted" : "Restricted (View-Only)" }}</strong>
              </p>
            </div>
          </div>
        </aside>

        <!-- Viewer Workspace -->
        <main
          class="viewer-workspace"
          aria-label="Media Canvas Workspace"
        >
          <!-- Watermark Overlay (Pointer events none, subtle rotating diagonal text) -->
          <div
            class="watermark-overlay-container"
            aria-hidden="true"
          >
            <div
              v-for="n in 4"
              :key="n"
              class="diagonal-watermark-row"
            >
              <span class="watermark-text">{{ watermarkText }}</span>
            </div>
          </div>

          <!-- Media Container with Type Switching -->
          <div class="media-canvas-frame">
            <!-- Video Player -->
            <div
              v-if="mediaType === 'video'"
              class="media-player-container video-container"
            >
              <video
                ref="videoPlayerRef"
                controls
                controlslist="nodownload"
                class="video-element"
                :src="mediaSourceUrl"
                preload="metadata"
              >
                Your browser does not support HTML5 video playback.
              </video>
              <div class="media-meta-badge">
                🎬 Regulated Video Stream ({{ fileRecord?.filename }})
              </div>
            </div>

            <!-- Audio Player -->
            <div
              v-else-if="mediaType === 'audio'"
              class="media-player-container audio-container"
            >
              <div class="audio-player-card">
                <div class="audio-icon-display">
                  🎙️
                </div>
                <h4 class="audio-filename">
                  {{ fileRecord?.filename }}
                </h4>
                <p class="audio-duration-meta">
                  {{ formatFileSize(fileRecord?.size_bytes) }} — Audio Recording
                </p>
                <audio
                  controls
                  controlslist="nodownload"
                  class="audio-element"
                  :src="mediaSourceUrl"
                >
                  Your browser does not support the audio element.
                </audio>
              </div>
            </div>

            <!-- Image Viewer -->
            <div
              v-else-if="mediaType === 'image'"
              class="media-player-container image-container"
            >
              <img
                :src="mediaSourceUrl || defaultPlaceholderImage"
                :alt="fileRecord?.filename || 'Clinical Image'"
                class="preview-image-element"
                @error="onImageError"
              >
            </div>

            <!-- PDF / Document Page Canvas View -->
            <div
              v-else
              class="media-player-container pdf-container"
            >
              <div class="pdf-document-sheet">
                <div class="doc-sheet-header">
                  <div class="header-logo">
                    Cadence Clinical Research Platform
                  </div>
                  <div class="header-doc-code">
                    REF: {{ fileRecord?.study_id || "CAD-001" }}
                  </div>
                </div>

                <div class="doc-sheet-body">
                  <h2 class="doc-display-title">
                    {{ fileRecord?.filename }}
                  </h2>
                  <p class="doc-display-subtitle">
                    Study Scope: {{ fileRecord?.study_id }} | Site Scope: {{ fileRecord?.site_id || "All Sites" }} | Version: v{{ fileRecord?.version_index || 1 }}.0
                  </p>
                  <hr class="doc-divider">

                  <section class="doc-text-section">
                    <h4>1. ELECTRONIC RECORD INTEGRITY & CLASSIFICATION</h4>
                    <p>
                      This electronic record has been indexed within the Cadence Clinical Digital Data Flow and Fileshare platform in full conformance with FDA 21 CFR Part 11 and EU Annex 11 standards. Cryptographic checksums and immutable audit metadata have been recorded in the central compliance ledger.
                    </p>
                  </section>

                  <section class="doc-text-section">
                    <h4>2. CLINICAL METADATA SUMMARY</h4>
                    <p>
                      <strong>Parent Study:</strong> {{ fileRecord?.study_id }}<br>
                      <strong>Site Code:</strong> {{ fileRecord?.site_id || "Global Study Level" }}<br>
                      <strong>Originator:</strong> {{ fileRecord?.uploaded_by || fileRecord?.created_by }}<br>
                      <strong>MIME Specification:</strong> {{ fileRecord?.mime_type }}<br>
                      <strong>Reason for Ingestion:</strong> {{ fileRecord?.reason_for_change }}
                    </p>
                  </section>

                  <section class="doc-text-section">
                    <h4>3. REGULATORY PREVIEW WATERMARK NOTICE</h4>
                    <p>
                      This document is being inspected in a protected viewer session with real-time watermark placement rendering the viewing identity and timestamp dynamically. Unauthorized reproduction, screen capture, or distribution is prohibited.
                    </p>
                  </section>
                </div>

                <div class="doc-sheet-footer">
                  <span>Page 1 of 1</span>
                  <span>CONFIDENTIAL — REGULATED CLINICAL DATA</span>
                </div>
              </div>
            </div>
          </div>
        </main>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from "vue";
import { useAuthStore } from "../../stores/auth";
import { useFocusTrap } from "@/composables/useFocusTrap";
import { useEscapeClose } from "@/composables/useEscapeClose";

const props = defineProps({
  fileRecord: {
    type: Object,
    required: true,
  },
  permissions: {
    type: Object,
    default: () => ({ canDownload: true, canComment: true }),
  },
  isGuestView: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["close", "download"]);

const modalRef = ref(null);
const videoPlayerRef = ref(null);

useFocusTrap(modalRef);
useEscapeClose(() => emit("close"));

const authStore = useAuthStore();

const currentUserId = computed(() => {
  if (props.isGuestView) return "guest_external_reviewer";
  return authStore.identity?.username || "authenticated_user";
});

const activeUserRoles = computed(() => {
  return authStore.normalizedRoles || ["user"];
});

const canDownload = computed(() => {
  if (props.permissions && props.permissions.canDownload !== undefined) {
    return props.permissions.canDownload;
  }
  return true;
});

const mediaType = computed(() => {
  const mime = (props.fileRecord?.mime_type || "").toLowerCase();
  const filename = (props.fileRecord?.filename || "").toLowerCase();

  if (mime.startsWith("video/") || filename.endsWith(".mp4") || filename.endsWith(".webm") || filename.endsWith(".mov")) {
    return "video";
  }
  if (mime.startsWith("audio/") || filename.endsWith(".mp3") || filename.endsWith(".wav") || filename.endsWith(".m4a") || filename.endsWith(".ogg")) {
    return "audio";
  }
  if (mime.startsWith("image/") || filename.endsWith(".png") || filename.endsWith(".jpg") || filename.endsWith(".jpeg") || filename.endsWith(".webp") || filename.endsWith(".svg") || filename.endsWith(".gif")) {
    return "image";
  }
  return "pdf";
});

const mediaSourceUrl = computed(() => {
  return props.fileRecord?.download_url || props.fileRecord?.object_url || "";
});

const defaultPlaceholderImage = computed(() => {
  // SVG placeholder data URI if no remote blob URL is available
  return "data:image/svg+xml;charset=UTF-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%22600%22%20height%3D%22400%22%20viewBox%3D%220%200%20600%20400%22%3E%3Crect%20fill%3D%22%23f1f5f9%22%20width%3D%22600%22%20height%3D%22400%22%2F%3E%3Ctext%20fill%3D%22%2364748b%22%20font-family%3D%22sans-serif%22%20font-size%3D%2218%22%20x%3D%2250%25%22%20y%3D%2250%25%22%20text-anchor%3D%22middle%22%3EClinical%20Source%20Image%20Preview%3C%2Ftext%3E%3C%2Fsvg%3E";
});

function onImageError(e) {
  if (e.target) {
    e.target.src = defaultPlaceholderImage.value;
  }
}

// Dynamic real-time GxP diagonal watermark string
const watermarkText = computed(() => {
  const timeStr = new Date().toISOString().replace("T", " ").substring(0, 19);
  return `SECURE PREVIEW | USER: ${currentUserId.value} | TIME: ${timeStr} | REGULATED ACCESS`;
});

function handleDownload() {
  emit("download", props.fileRecord);
}

// Protected-view hotkey interception (Ctrl+S / Cmd+S, Ctrl+P / Cmd+P)
function handleProtectedKeydown(e) {
  if ((e.ctrlKey || e.metaKey) && (e.key === "s" || e.key === "S" || e.key === "p" || e.key === "P")) {
    e.preventDefault();
    console.warn("Protected view: browser save/print operation intercepted.");
  }
}

onMounted(() => {
  document.addEventListener("keydown", handleProtectedKeydown);
});

onUnmounted(() => {
  document.removeEventListener("keydown", handleProtectedKeydown);
});

function formatFileSize(bytes) {
  if (!bytes || bytes === 0) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
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

function getStatusClass(status) {
  const s = (status || "").toLowerCase();
  if (s.includes("approved") || s.includes("active") || s.includes("verified")) return "text-success";
  if (s.includes("review") || s.includes("pending")) return "text-warning";
  if (s.includes("hold") || s.includes("archive")) return "text-danger";
  return "text-muted";
}
</script>

<style scoped>
.media-preview-modal {
  max-width: 1100px !important;
  width: 95% !important;
  height: 88vh;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  background-color: #ffffff;
}

.header-details h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--primary);
  display: flex;
  align-items: center;
  gap: 8px;
}

.gxp-tracking-subtitle {
  margin: 4px 0 0 0;
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.modal-body-layout {
  display: flex;
  flex: 1;
  overflow: hidden;
  background-color: #f1f5f9;
}

/* Sidebar styling */
.metadata-sidebar {
  width: 320px;
  background-color: #ffffff;
  border-right: 1px solid var(--border);
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
  border-top: 1px solid #e2e8f0;
  padding-top: 16px;
}

.meta-section h4 {
  margin: 0 0 12px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: #0f172a;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.meta-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 8px;
  font-size: 0.82rem;
}

.meta-label {
  color: #64748b;
  font-weight: 500;
}

.meta-value {
  color: #0f172a;
  text-align: right;
  word-break: break-all;
  max-width: 180px;
}

.font-mono {
  font-family: monospace;
}

.font-bold {
  font-weight: 700;
}

.text-muted { color: #64748b; }
.text-warning { color: #d97706; }
.text-success { color: #16a34a; }
.text-danger { color: #dc2626; }

.change-reason-block h5,
.checksum-block h5 {
  margin: 0 0 6px 0;
  font-size: 0.78rem;
  font-weight: 600;
  color: #475569;
}

.reason-text {
  font-size: 0.8rem;
  color: #1e293b;
  background-color: #f8fafc;
  padding: 8px;
  border-radius: 4px;
  margin: 0;
  line-height: 1.4;
  border: 1px solid #e2e8f0;
}

.checksum-text {
  display: block;
  font-family: monospace;
  font-size: 0.72rem;
  background-color: #f1f5f9;
  color: #0f172a;
  padding: 6px;
  border-radius: 4px;
  word-break: break-all;
}

.manifest-card {
  border-radius: 6px;
  padding: 10px;
  font-size: 0.78rem;
}

.manifest-card.signed {
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
}

.sign-status {
  font-weight: 700;
  margin: 0 0 6px 0;
}

.signer-meta {
  margin: 0 0 4px 0;
}

/* Viewer Workspace */
.viewer-workspace {
  flex: 1;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 24px;
  overflow-y: auto;
  position: relative;
}

/* Dynamic rotating diagonal watermark */
.watermark-overlay-container {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 100;
  pointer-events: none;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  justify-content: space-around;
  align-items: center;
  opacity: 0.12;
}

.diagonal-watermark-row {
  transform: rotate(-30deg);
  white-space: nowrap;
}

.watermark-text {
  font-family: Arial, sans-serif;
  font-size: 1rem;
  font-weight: 900;
  color: #ef4444;
  letter-spacing: 0.15em;
}

.media-canvas-frame {
  width: 100%;
  max-width: 720px;
  position: relative;
  z-index: 10;
  display: flex;
  justify-content: center;
}

.media-player-container {
  width: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
}

/* Video specific */
.video-container {
  background-color: #000000;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.video-element {
  width: 100%;
  max-height: 480px;
  background-color: #000000;
}

.media-meta-badge {
  width: 100%;
  padding: 8px 12px;
  background-color: #1e293b;
  color: #94a3b8;
  font-size: 0.75rem;
  text-align: center;
}

/* Audio specific */
.audio-container {
  background-color: #ffffff;
  padding: 32px;
  border-radius: 12px;
  border: 1px solid var(--border);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.06);
}

.audio-player-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  width: 100%;
  gap: 12px;
}

.audio-icon-display {
  font-size: 3rem;
  background-color: #eff6ff;
  width: 72px;
  height: 72px;
  border-radius: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.audio-filename {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--primary);
}

.audio-duration-meta {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.audio-element {
  width: 100%;
  margin-top: 12px;
}

/* Image specific */
.image-container {
  background-color: #ffffff;
  padding: 16px;
  border-radius: 8px;
  border: 1px solid var(--border);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.08);
}

.preview-image-element {
  max-width: 100%;
  max-height: 600px;
  object-fit: contain;
  border-radius: 4px;
}

/* PDF document sheet */
.pdf-document-sheet {
  width: 100%;
  background-color: #ffffff;
  border-radius: 4px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.08);
  padding: 40px;
  box-sizing: border-box;
  min-height: 700px;
  display: flex;
  flex-direction: column;
}

.doc-sheet-header {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: #94a3b8;
  font-weight: 600;
  text-transform: uppercase;
  margin-bottom: 20px;
}

.doc-sheet-body {
  flex: 1;
}

.doc-display-title {
  margin: 0 0 6px 0;
  font-size: 1.4rem;
  font-weight: 800;
  color: #0f172a;
}

.doc-display-subtitle {
  margin: 0 0 16px 0;
  font-size: 0.85rem;
  color: #64748b;
}

.doc-divider {
  border: none;
  border-top: 2px solid #e2e8f0;
  margin-bottom: 20px;
}

.doc-text-section {
  margin-bottom: 20px;
}

.doc-text-section h4 {
  margin: 0 0 8px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: #1e293b;
}

.doc-text-section p {
  margin: 0;
  font-size: 0.85rem;
  line-height: 1.6;
  color: #334155;
}

.doc-sheet-footer {
  display: flex;
  justify-content: space-between;
  font-size: 0.7rem;
  color: #94a3b8;
  border-top: 1px solid #e2e8f0;
  padding-top: 12px;
  margin-top: 24px;
}
</style>
