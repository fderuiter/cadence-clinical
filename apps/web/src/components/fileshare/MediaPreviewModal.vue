<template>
  <div class="modal-backdrop" role="presentation" @click.self="$emit('close')">
    <div
      ref="modalRef"
      class="modal-card media-preview-modal"
      role="dialog"
      aria-modal="true"
      :aria-label="'Secure Media Preview: ' + (fileRecord?.filename || 'File')"
      tabindex="-1"
      @contextmenu.prevent
    >
      <!-- Header -->
      <div class="modal-header">
        <div class="header-details">
          <h3>
            <span class="secure-icon" aria-hidden="true">🔒</span>
            Secure Regulated Viewer — {{ fileRecord?.filename }}
          </h3>
          <p class="gxp-tracking-subtitle">
            System ID: <code>{{ fileRecord?.id }}</code> | Version:
            <strong>v{{ fileRecord?.version_index || 1 }}.0</strong> | MIME:
            <code>{{
              fileRecord?.mime_type || "application/octet-stream"
            }}</code>
          </p>
        </div>
        <div class="header-actions">
          <div
            v-if="fileRecord?.conversion_status === 'READY'"
            class="approval-badge rendition-pill"
            data-testid="rendition-badge"
            title="Automatic PDF Rendition generated for regulated preview"
          >
            <span class="badge-icon">📄</span>
            <span class="badge-label">PDF Rendition</span>
          </div>
          <div
            v-else-if="fileRecord?.conversion_status === 'PENDING'"
            class="approval-badge pending-pill"
            data-testid="converting-badge"
            title="Converting Office document to PDF rendition in background"
          >
            <span class="badge-icon">🔄</span>
            <span class="badge-label">Converting...</span>
          </div>
          <div
            v-if="isApproved"
            class="approval-badge approved-pill"
            data-testid="approved-badge"
          >
            <span class="badge-icon">✅</span>
            <span class="badge-label">Approved</span>
            <span v-if="fileRecord?.approved_by" class="badge-signer font-mono"
              >by {{ fileRecord.approved_by }}</span
            >
          </div>
          <button
            v-if="canApprove && !isApproved"
            class="btn btn-sm btn-success btn-approve"
            data-testid="approve-doc-btn"
            title="Approve this document with 21 CFR Part 11 Electronic Signature"
            @click="openReAuthModal"
          >
            ✍️ Approve this document
          </button>
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
                >{{ fileRecord?.filename }}</span
              >
            </div>
            <div class="meta-row">
              <span class="meta-label">Study ID:</span>
              <span class="meta-value font-mono">{{
                fileRecord?.study_id
              }}</span>
            </div>
            <div v-if="fileRecord?.site_id" class="meta-row">
              <span class="meta-label">Site ID:</span>
              <span class="meta-value font-mono">{{
                fileRecord?.site_id
              }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">File Size:</span>
              <span class="meta-value">{{
                formatFileSize(fileRecord?.size_bytes)
              }}</span>
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
              <span class="meta-value font-mono">{{
                fileRecord?.uploaded_by || fileRecord?.created_by
              }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">Uploaded At:</span>
              <span class="meta-value">{{
                formatDate(fileRecord?.uploaded_at || fileRecord?.created_at)
              }}</span>
            </div>
          </div>

          <!-- 21 CFR Part 11 Audit Record -->
          <div class="meta-section border-top">
            <h4>21 CFR Part 11 Audit Trail</h4>
            <div class="meta-row">
              <span class="meta-label">Audit Status:</span>
              <span
                class="meta-value font-bold"
                :class="auditErrorState ? 'text-danger' : 'text-success'"
                data-testid="audit-status-badge"
              >
                {{ auditErrorState ? "Audit Interrupted" : "Audit Active" }}
              </span>
            </div>
            <div class="change-reason-block">
              <h5>Change Justification:</h5>
              <p class="reason-text">
                {{
                  fileRecord?.reason_for_change ||
                  "Standard regulated file ingestion."
                }}
              </p>
            </div>
            <div class="checksum-block">
              <h5>SHA-256 Digest:</h5>
              <code class="checksum-text" :title="fileRecord?.checksum_sha256">
                {{ fileRecord?.checksum_sha256 || emptySha256Digest }}
              </code>
            </div>
          </div>

          <!-- Active Access Permissions -->
          <div class="meta-section border-top">
            <h4>Security Context</h4>
            <div class="manifest-card signed">
              <p class="sign-status">🛡️ PROTECTED VIEW ENFORCED</p>
              <p class="signer-meta">
                Viewer: <strong>{{ currentUserId }}</strong>
              </p>
              <p class="signer-meta">
                Role Context:
                <span>{{ activeUserRoles.join(", ") || "Authenticated" }}</span>
              </p>
              <p class="signer-meta">
                Download:
                <strong>{{
                  canDownload ? "Permitted" : "Restricted (View-Only)"
                }}</strong>
              </p>
            </div>
          </div>

          <!-- 21 CFR Part 11 Electronic Signature Approval Manifestation -->
          <div
            v-if="isApproved"
            class="meta-section border-top"
            data-testid="approval-details-section"
          >
            <h4>Document Approval</h4>
            <div class="manifest-card approved">
              <p class="sign-status text-success font-bold">✅ Approved</p>
              <p v-if="fileRecord?.approved_by" class="signer-meta">
                Signer:
                <strong class="font-mono">{{ fileRecord.approved_by }}</strong>
              </p>
              <p v-if="fileRecord?.approved_at" class="signer-meta">
                Approved At:
                <span>{{ formatDate(fileRecord.approved_at) }}</span>
              </p>
              <p v-if="fileRecord?.approval_reason" class="signer-meta">
                Meaning: <em>{{ fileRecord.approval_reason }}</em>
              </p>
            </div>
          </div>
        </aside>

        <!-- Viewer Workspace -->
        <div
          class="viewer-workspace"
          role="region"
          aria-label="Media Canvas Workspace"
        >
          <!-- Watermark Overlay (Pointer events none, subtle rotating diagonal text) -->
          <div class="watermark-overlay-container" aria-hidden="true">
            <div v-for="n in 4" :key="n" class="diagonal-watermark-row">
              <span class="watermark-text">{{ watermarkText }}</span>
            </div>
          </div>

          <!-- Media Container with Type Switching -->
          <div class="media-canvas-frame">
            <!-- Video Player with HLS, Quality Selector & Transcode Polling -->
            <div
              v-if="mediaType === 'video'"
              class="media-player-container video-container"
            >
              <!-- 1. Transcode Pending / In-Progress Placeholder -->
              <div
                v-if="
                  transcodeStatus === 'PENDING' ||
                  transcodeStatus === 'IN_PROGRESS'
                "
                class="video-processing-card"
                role="status"
                aria-live="polite"
              >
                <div class="spinner-circle" aria-hidden="true" />
                <h4 class="processing-title">
                  Processing video — check back soon
                </h4>
                <p class="processing-subtitle">
                  Generating HLS adaptive bitrate streams for secure clinical
                  inspection.
                </p>
                <p class="polling-status font-mono">
                  Status: <strong>{{ transcodeStatus }}</strong> (Attempt
                  {{ pollCount }}/{{ maxPollAttempts }})
                </p>
              </div>

              <!-- 2. Transcode Timeout State -->
              <div
                v-else-if="transcodeStatus === 'TIMEOUT'"
                class="video-processing-card timeout-card"
                role="alert"
              >
                <span class="processing-icon warning-icon" aria-hidden="true"
                  >⏱️</span
                >
                <h4 class="processing-title">
                  Video processing is taking longer than expected — try again
                  later
                </h4>
                <p class="processing-subtitle">
                  The transcode task is still in progress. Please check again
                  shortly or reopen the viewer later.
                </p>
                <button
                  class="btn btn-sm btn-outline-light"
                  @click="retryTranscodeCheck"
                >
                  🔄 Check Again
                </button>
              </div>

              <!-- 3. Transcode Failed State -->
              <div
                v-else-if="transcodeStatus === 'FAILED'"
                class="video-processing-card error-card"
                role="alert"
              >
                <span class="processing-icon error-icon" aria-hidden="true"
                  >⚠️</span
                >
                <h4 class="processing-title">Video Transcoding Failed</h4>
                <p class="processing-subtitle font-mono">
                  {{
                    transcodeError ||
                    "An unexpected error occurred during HLS stream processing."
                  }}
                </p>
                <button
                  class="btn btn-sm btn-outline-light"
                  @click="retryTranscodeCheck"
                >
                  🔄 Retry
                </button>
              </div>

              <!-- 4. Video Player Ready State -->
              <div v-else class="video-player-wrapper">
                <!-- Compliance Warning Overlay for Audit Failure -->
                <div
                  v-if="auditErrorState"
                  class="audit-compliance-overlay"
                  role="alert"
                  data-testid="audit-error-overlay"
                >
                  <div class="audit-alert-card">
                    <span class="alert-icon" aria-hidden="true">⚠️</span>
                    <h4 class="alert-title">Audit Telemetry Interrupted</h4>
                    <p class="alert-message">
                      21 CFR Part 11 compliance requires verified audit trail
                      transmission. Video playback is paused until telemetry is
                      restored.
                    </p>
                    <button
                      class="btn btn-sm btn-primary btn-retry-audit"
                      data-testid="retry-audit-btn"
                      @click="retryAuditConnection"
                    >
                      🔄 Retry Audit Connection
                    </button>
                  </div>
                </div>

                <video
                  ref="videoPlayerRef"
                  controls
                  controlslist="nodownload"
                  class="video-element"
                  preload="metadata"
                  @playing="onVideoPlaying"
                  @play="onVideoPlay"
                  @timeupdate="onVideoTimeUpdate"
                  @ended="onVideoEnded"
                >
                  <track kind="captions" />
                  Your browser does not support HTML5 video playback.
                </video>

                <!-- Quality selector overlay -->
                <div
                  v-if="qualityLevels.length > 0"
                  class="video-quality-overlay"
                >
                  <label for="quality-select" class="quality-label"
                    >Quality:</label
                  >
                  <select
                    id="quality-select"
                    class="quality-select"
                    :value="selectedQuality"
                    aria-label="Select video streaming quality"
                    @change="onQualityChange"
                  >
                    <option
                      v-for="lvl in qualityLevels"
                      :key="lvl.index"
                      :value="lvl.index"
                    >
                      {{ lvl.label }}
                    </option>
                  </select>
                </div>
              </div>

              <div class="media-meta-badge">
                🎬 Regulated Video Stream ({{ fileRecord?.filename }})
                <span
                  v-if="selectedQualityLabel && transcodeStatus === 'READY'"
                  class="quality-badge font-mono"
                  >[{{ selectedQualityLabel }}]</span
                >
              </div>
            </div>

            <!-- Audio Player -->
            <div
              v-else-if="mediaType === 'audio'"
              class="media-player-container audio-container"
            >
              <div class="audio-player-card">
                <div class="audio-icon-display">🎙️</div>
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
                  <track kind="captions" />
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
              />
            </div>

            <!-- PDF / Document Page Canvas View -->
            <div v-else class="media-player-container pdf-container">
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
                    Study Scope: {{ fileRecord?.study_id }} | Site Scope:
                    {{ fileRecord?.site_id || "All Sites" }} | Version: v{{
                      fileRecord?.version_index || 1
                    }}.0
                  </p>
                  <hr class="doc-divider" />

                  <section class="doc-text-section">
                    <h4>1. ELECTRONIC RECORD INTEGRITY & CLASSIFICATION</h4>
                    <p>
                      This electronic record has been indexed within the Cadence
                      Clinical Digital Data Flow and Fileshare platform in full
                      conformance with FDA 21 CFR Part 11 and EU Annex 11
                      standards. Cryptographic checksums and immutable audit
                      metadata have been recorded in the central compliance
                      ledger.
                    </p>
                  </section>

                  <section class="doc-text-section">
                    <h4>2. CLINICAL METADATA SUMMARY</h4>
                    <p>
                      <strong>Parent Study:</strong> {{ fileRecord?.study_id
                      }}<br />
                      <strong>Site Code:</strong>
                      {{ fileRecord?.site_id || "Global Study Level" }}<br />
                      <strong>Originator:</strong>
                      {{ fileRecord?.uploaded_by || fileRecord?.created_by
                      }}<br />
                      <strong>MIME Specification:</strong>
                      {{ fileRecord?.mime_type }}<br />
                      <strong>Reason for Ingestion:</strong>
                      {{ fileRecord?.reason_for_change }}
                    </p>
                  </section>

                  <section class="doc-text-section">
                    <h4>3. REGULATORY PREVIEW WATERMARK NOTICE</h4>
                    <p>
                      This document is being inspected in a protected viewer
                      session with real-time watermark placement rendering the
                      viewing identity and timestamp dynamically. Unauthorized
                      reproduction, screen capture, or distribution is
                      prohibited.
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
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Electronic Signature Re-Authentication Modal -->
    <ReAuthModal
      :is-open="isReAuthOpen"
      :filename="fileRecord?.filename"
      :username="currentUserId"
      :busy="isApproving"
      :error-message="approvalError"
      @cancel="isReAuthOpen = false"
      @close="isReAuthOpen = false"
      @confirm="handleApproveConfirm"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from "vue";
import Hls from "hls.js";
import { useFocusTrap } from "ui";
import { fileshareService } from "../../api/fileshare";
import { useAuthStore } from "../../stores/auth";
import ReAuthModal from "../shared/ReAuthModal.vue";

const props = defineProps({
  fileRecord: {
    type: Object,
    required: true,
  },
  permissions: {
    type: Object,
    default: () => ({ canDownload: true, canComment: true, canApprove: false }),
  },
  isGuestView: {
    type: Boolean,
    default: false,
  },
  guestToken: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["close", "download", "approved"]);

const modalRef = ref(null);
const videoPlayerRef = ref(null);

useFocusTrap(modalRef, {
  onEscape: () => emit("close"),
});

const authStore = useAuthStore();

// Unique session ID for video playback audit telemetry
const sessionId = ref(
  typeof crypto !== "undefined" && crypto.randomUUID
    ? crypto.randomUUID()
    : `sess-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
);

// Video HLS playback and transcoding state
const streamUrl = ref("");
const transcodeStatus = ref(props.fileRecord?.transcode_status || "READY");
const transcodeError = ref("");
const pollCount = ref(0);
const maxPollAttempts = 10;
const isPolling = ref(false);
const pollTimer = ref(null);
const qualityLevels = ref([]);
const selectedQuality = ref(-1);
const hlsInstance = ref(null);
const emptySha256Digest =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // pragma: allowlist secret
const reportedCheckpoints = ref(new Set());
const pendingAuditEvents = ref(new Set());
const auditErrorState = ref(null);

const currentUserId = computed(() => {
  if (props.isGuestView) return "guest_external_reviewer";
  return authStore.identity?.username || "authenticated_user";
});

const activeUserRoles = computed(() => {
  return authStore.normalizedRoles || ["user"];
});

const isApproved = computed(() => {
  const status = (props.fileRecord?.approval_status || "").toUpperCase();
  return status === "APPROVED";
});

const canApprove = computed(() => {
  return Boolean(props.permissions?.canApprove);
});

const isReAuthOpen = ref(false);
const isApproving = ref(false);
const approvalError = ref("");

function openReAuthModal() {
  approvalError.value = "";
  isReAuthOpen.value = true;
}

async function handleApproveConfirm({ credential, signing_reason }) {
  isApproving.value = true;
  approvalError.value = "";
  try {
    const updated = await fileshareService.approveFile(props.fileRecord.id, {
      credential,
      signing_reason,
    });
    isReAuthOpen.value = false;
    emit(
      "approved",
      updated || {
        ...props.fileRecord,
        approval_status: "APPROVED",
        approved_by: updated?.approved_by || currentUserId.value,
        approved_at: updated?.approved_at || new Date().toISOString(),
        approval_reason: updated?.approval_reason || signing_reason,
        signature_manifestation: updated?.signature_manifestation,
      }
    );
  } catch (err) {
    console.error("Document approval failed:", err);
    approvalError.value =
      err.response?.data?.detail ||
      err.message ||
      "Failed to approve document.";
  } finally {
    isApproving.value = false;
  }
}

const canDownload = computed(() => {
  if (props.permissions && props.permissions.canDownload !== undefined) {
    return props.permissions.canDownload;
  }
  return true;
});

const mediaType = computed(() => {
  const mime = (props.fileRecord?.mime_type || "").toLowerCase();
  const filename = (props.fileRecord?.filename || "").toLowerCase();

  if (
    mime.startsWith("video/") ||
    filename.endsWith(".mp4") ||
    filename.endsWith(".webm") ||
    filename.endsWith(".mov")
  ) {
    return "video";
  }
  if (
    mime.startsWith("audio/") ||
    filename.endsWith(".mp3") ||
    filename.endsWith(".wav") ||
    filename.endsWith(".m4a") ||
    filename.endsWith(".ogg")
  ) {
    return "audio";
  }
  if (
    mime.startsWith("image/") ||
    filename.endsWith(".png") ||
    filename.endsWith(".jpg") ||
    filename.endsWith(".jpeg") ||
    filename.endsWith(".webp") ||
    filename.endsWith(".svg") ||
    filename.endsWith(".gif")
  ) {
    return "image";
  }
  return "pdf";
});

const mediaSourceUrl = computed(() => {
  return props.fileRecord?.download_url || props.fileRecord?.object_url || "";
});

const selectedQualityLabel = computed(() => {
  const current = qualityLevels.value.find(
    (l) => l.index === selectedQuality.value
  );
  return current ? current.label : "Auto";
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
  if (
    (e.ctrlKey || e.metaKey) &&
    (e.key === "s" || e.key === "S" || e.key === "p" || e.key === "P")
  ) {
    e.preventDefault();
    console.warn("Protected view: browser save/print operation intercepted.");
  }
}

/**
 * Send Part 11 Video Playback Checkpoint Telemetry
 */
async function sendPlaybackAudit(eventType) {
  if (!props.fileRecord?.id) return;
  if (reportedCheckpoints.value.has(eventType)) return;
  if (pendingAuditEvents.value.has(eventType)) return;

  pendingAuditEvents.value.add(eventType);

  const payload = {
    file_record_id: props.fileRecord.id,
    event: eventType,
    source: props.isGuestView ? "guest_link" : "internal",
    session_id: sessionId.value,
    guest_token:
      props.guestToken ||
      (props.isGuestView ? props.fileRecord?.guest_token : null) ||
      null,
    reason_for_change: `Video playback checkpoint ${eventType} telemetry`,
  };

  const backoffs = [0, 2000, 4000, 8000];
  let lastError = null;

  for (let attempt = 0; attempt < backoffs.length; attempt++) {
    const delay = backoffs[attempt];
    if (delay > 0) {
      await new Promise((resolve) => setTimeout(resolve, delay));
    }
    try {
      await fileshareService.recordPlaybackAudit(payload);
      reportedCheckpoints.value.add(eventType);
      pendingAuditEvents.value.delete(eventType);
      auditErrorState.value = null;
      return;
    } catch (err) {
      lastError = err;
    }
  }

  // All retries failed
  pendingAuditEvents.value.delete(eventType);
  auditErrorState.value = {
    eventType,
    payload,
    errorMessage:
      lastError?.response?.data?.detail ||
      lastError?.message ||
      `Audit telemetry transmission failed for ${eventType}`,
  };

  if (videoPlayerRef.value) {
    videoPlayerRef.value.pause();
  }
}

async function retryAuditConnection() {
  if (!auditErrorState.value) return;
  const eventType = auditErrorState.value.eventType;
  auditErrorState.value = null;
  await sendPlaybackAudit(eventType);
  if (!auditErrorState.value && videoPlayerRef.value) {
    videoPlayerRef.value.play().catch(() => {});
  }
}

function onVideoPlay() {
  sendPlaybackAudit("PLAY_START");
}

function onVideoPlaying() {
  sendPlaybackAudit("PLAY_START");
}

function onVideoTimeUpdate(e) {
  const video = e.target;
  if (!video || !video.duration || isNaN(video.duration) || video.duration <= 0)
    return;
  const progress = (video.currentTime / video.duration) * 100;

  if (progress >= 25 && !reportedCheckpoints.value.has("PLAY_25")) {
    sendPlaybackAudit("PLAY_25");
  }
  if (progress >= 50 && !reportedCheckpoints.value.has("PLAY_50")) {
    sendPlaybackAudit("PLAY_50");
  }
  if (progress >= 75 && !reportedCheckpoints.value.has("PLAY_75")) {
    sendPlaybackAudit("PLAY_75");
  }
}

function onVideoEnded() {
  sendPlaybackAudit("PLAY_END");
}

/**
 * Configure HLS or native playback on the video element
 */
async function setupVideoPlayer(manifestUrl) {
  if (!manifestUrl) return;
  await nextTick();
  const video = videoPlayerRef.value;
  if (!video) return;

  if (hlsInstance.value) {
    hlsInstance.value.destroy();
    hlsInstance.value = null;
  }

  if (Hls && Hls.isSupported && Hls.isSupported()) {
    const hls = new Hls({
      enableWorker: true,
      lowLatencyMode: true,
    });
    hlsInstance.value = hls;

    hls.loadSource(manifestUrl);
    hls.attachMedia(video);

    hls.on(Hls.Events.MANIFEST_PARSED, (_event, data) => {
      if (data && data.levels && data.levels.length > 0) {
        qualityLevels.value = [
          { index: -1, label: "Auto" },
          ...data.levels.map((lvl, index) => ({
            index,
            label: lvl.height
              ? `${lvl.height}p`
              : `${Math.round((lvl.bitrate || 0) / 1000)}k`,
          })),
        ];
      } else {
        qualityLevels.value = [{ index: -1, label: "Auto" }];
      }
      selectedQuality.value = -1;
    });

    hls.on(Hls.Events.ERROR, (_event, data) => {
      if (data.fatal) {
        switch (data.type) {
          case Hls.ErrorTypes.NETWORK_ERROR:
            hls.startLoad();
            break;
          case Hls.ErrorTypes.MEDIA_ERROR:
            hls.recoverMediaError();
            break;
          default:
            hls.destroy();
            hlsInstance.value = null;
            break;
        }
      }
    });
  } else if (
    video.canPlayType &&
    video.canPlayType("application/vnd.apple.mpegurl")
  ) {
    video.src = manifestUrl;
    qualityLevels.value = [{ index: -1, label: "Auto (Native)" }];
    selectedQuality.value = -1;
  } else {
    video.src = manifestUrl;
    qualityLevels.value = [];
  }
}

function onQualityChange(e) {
  const levelIndex = parseInt(e.target.value, 10);
  selectedQuality.value = levelIndex;
  if (hlsInstance.value) {
    hlsInstance.value.currentLevel = levelIndex;
  }
}

/**
 * Fetch short-lived stream URL or initiate polling if transcode pending
 */
async function fetchStreamUrl() {
  if (!props.fileRecord?.id) return;
  try {
    const res = await fileshareService.getStreamUrl(props.fileRecord.id);
    if (res && res.transcode_status === "READY" && res.stream_url) {
      transcodeStatus.value = "READY";
      streamUrl.value = res.stream_url;
      clearPolling();
      await setupVideoPlayer(res.stream_url);
    } else if (
      res &&
      (res.transcode_status === "PENDING" ||
        res.transcode_status === "IN_PROGRESS")
    ) {
      transcodeStatus.value = res.transcode_status;
      startPolling();
    } else if (res && res.transcode_status === "FAILED") {
      transcodeStatus.value = "FAILED";
      transcodeError.value = res.message || "Video transcoding failed.";
      clearPolling();
    } else {
      transcodeStatus.value = "READY";
      const fallbackUrl = mediaSourceUrl.value;
      streamUrl.value = fallbackUrl;
      clearPolling();
      if (fallbackUrl) {
        await setupVideoPlayer(fallbackUrl);
      }
    }
  } catch (err) {
    if (
      err.status === 409 ||
      (err.data && err.data.detail && err.data.detail.includes("failed"))
    ) {
      transcodeStatus.value = "FAILED";
      transcodeError.value = err.data?.detail || "Video transcoding failed.";
      clearPolling();
    } else if (err.status === 400) {
      transcodeStatus.value = "READY";
      const fallbackUrl = mediaSourceUrl.value;
      streamUrl.value = fallbackUrl;
      clearPolling();
      if (fallbackUrl) {
        await setupVideoPlayer(fallbackUrl);
      }
    } else {
      console.warn(
        "Failed to retrieve stream URL, polling transcode readiness:",
        err
      );
      transcodeStatus.value = "PENDING";
      startPolling();
    }
  }
}

function startPolling() {
  if (pollTimer.value) return;
  isPolling.value = true;
  pollTimer.value = setInterval(async () => {
    pollCount.value += 1;
    if (pollCount.value >= maxPollAttempts) {
      clearPolling();
      transcodeStatus.value = "TIMEOUT";
      return;
    }
    try {
      const res = await fileshareService.getStreamUrl(props.fileRecord.id);
      if (res && res.transcode_status === "READY" && res.stream_url) {
        transcodeStatus.value = "READY";
        streamUrl.value = res.stream_url;
        clearPolling();
        await setupVideoPlayer(res.stream_url);
      } else if (res && res.transcode_status === "FAILED") {
        transcodeStatus.value = "FAILED";
        transcodeError.value = res.message || "Video transcoding failed.";
        clearPolling();
      } else if (res && res.transcode_status) {
        transcodeStatus.value = res.transcode_status;
      }
    } catch (err) {
      if (err.status === 409) {
        transcodeStatus.value = "FAILED";
        transcodeError.value = err.data?.detail || "Video transcoding failed.";
        clearPolling();
      }
    }
  }, 10000);
}

function clearPolling() {
  if (pollTimer.value) {
    clearInterval(pollTimer.value);
    pollTimer.value = null;
  }
  isPolling.value = false;
}

function retryTranscodeCheck() {
  pollCount.value = 0;
  transcodeError.value = "";
  transcodeStatus.value = "PENDING";
  fetchStreamUrl();
}

onMounted(() => {
  document.addEventListener("keydown", handleProtectedKeydown);
  if (mediaType.value === "video") {
    // If pre-marked as pending or no direct ready manifest, fetch/poll stream url
    if (
      props.fileRecord?.transcode_status === "PENDING" ||
      props.fileRecord?.transcode_status === "IN_PROGRESS"
    ) {
      transcodeStatus.value = props.fileRecord.transcode_status;
      startPolling();
    }
    fetchStreamUrl();
  }
});

onUnmounted(() => {
  document.removeEventListener("keydown", handleProtectedKeydown);
  clearPolling();
  if (hlsInstance.value) {
    hlsInstance.value.destroy();
    hlsInstance.value = null;
  }
});

watch(
  () => props.fileRecord?.id,
  (newId, oldId) => {
    if (newId !== oldId) {
      reportedCheckpoints.value.clear();
      pendingAuditEvents.value.clear();
      auditErrorState.value = null;
      clearPolling();
      if (hlsInstance.value) {
        hlsInstance.value.destroy();
        hlsInstance.value = null;
      }
      if (mediaType.value === "video") {
        fetchStreamUrl();
      }
    }
  }
);

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
  if (s.includes("approved") || s.includes("active") || s.includes("verified"))
    return "text-success";
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
  background-color: var(--semantic-color-surface);
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
  background-color: var(--semantic-color-surface-header);
}

/* Sidebar styling */
.metadata-sidebar {
  width: 320px;
  background-color: var(--semantic-color-surface);
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
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 16px;
}

.meta-section h4 {
  margin: 0 0 12px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
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
  color: var(--semantic-color-text-secondary);
  font-weight: 500;
}

.meta-value {
  color: var(--semantic-color-text);
  text-align: right;
  word-break: break-all;
  max-width: 180px;
}

.change-reason-block h5,
.checksum-block h5 {
  margin: 0 0 6px 0;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.reason-text {
  font-size: 0.8rem;
  color: var(--semantic-color-text-dark);
  background-color: var(--semantic-color-surface-muted);
  padding: 8px;
  border-radius: 4px;
  margin: 0;
  line-height: 1.4;
  border: 1px solid var(--semantic-color-border);
}

.checksum-text {
  display: block;
  font-family: monospace;
  font-size: 0.72rem;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
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
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.manifest-card.approved {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.sign-status {
  font-weight: 700;
  margin: 0 0 6px 0;
}

.signer-meta {
  margin: 0 0 4px 0;
}

.approval-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border-radius: 9999px;
  font-size: 0.8rem;
  font-weight: 600;
}

.approved-pill {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.rendition-pill {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
}

.pending-pill {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
  border: 1px solid var(--semantic-color-warning-border);
}

.badge-signer {
  font-size: 0.75rem;
  color: var(--semantic-color-success-dark);
}

.btn-approve {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-success);
  padding: 6px 12px;
  font-size: 0.82rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease;
}

.btn-approve:hover:not(:disabled) {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
}

.btn-approve:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.text-success {
  color: var(--semantic-color-success);
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
.watermark-text {
  font-family: Arial, sans-serif;
  font-size: 1rem;
  font-weight: 900;
  color: var(--semantic-color-error);
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
  background-color: var(--semantic-color-black);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.video-player-wrapper {
  position: relative;
  width: 100%;
  background-color: var(--semantic-color-black);
  display: flex;
  justify-content: center;
  align-items: center;
}

.video-element {
  width: 100%;
  max-height: 480px;
  background-color: var(--semantic-color-black);
}

.video-quality-overlay {
  position: absolute;
  top: 12px;
  right: 12px;
  z-index: 20;
  background: rgba(15, 23, 42, 0.85);
  backdrop-filter: blur(4px);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 6px;
  padding: 4px 8px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.quality-label {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.quality-select {
  background: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 0.75rem;
  font-weight: 500;
  outline: none;
  cursor: pointer;
}

.quality-select:focus {
  border-color: var(--primary);
}

.quality-badge {
  margin-left: 8px;
  color: var(--semantic-color-primary);
  font-weight: 600;
}

/* Video Processing / Transcoding Card States */
.video-processing-card {
  width: 100%;
  min-height: 320px;
  background-color: var(--semantic-color-surface-inverse);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px;
  box-sizing: border-box;
  text-align: center;
  color: var(--semantic-color-text-inverse);
}

.spinner-circle {
  width: 44px;
  height: 44px;
  border: 3px solid rgba(255, 255, 255, 0.15);
  border-top-color: var(--primary);
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 20px;
}

@keyframes spin {
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
}

.processing-icon {
  font-size: 2.5rem;
  margin-bottom: 12px;
}

.processing-title {
  margin: 0 0 8px 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.processing-subtitle {
  margin: 0 0 16px 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  max-width: 420px;
  line-height: 1.5;
}

.polling-status {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.btn-outline-light {
  background-color: transparent;
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  padding: 6px 12px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.btn-outline-light:hover {
  background-color: var(--semantic-color-surface-elevated);
  border-color: var(--semantic-color-border-inverse);
}

.media-meta-badge {
  width: 100%;
  padding: 8px 12px;
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  font-size: 0.75rem;
  text-align: center;
}

/* Audio specific */
.audio-container {
  background-color: var(--semantic-color-surface);
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
  background-color: var(--semantic-color-info-bg);
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
  background-color: var(--semantic-color-surface);
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
  background-color: var(--semantic-color-surface);
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
  color: var(--semantic-color-text-secondary);
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
  color: var(--semantic-color-text);
}

.doc-display-subtitle {
  margin: 0 0 16px 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.doc-divider {
  border: none;
  border-top: 2px solid var(--semantic-color-border);
  margin-bottom: 20px;
}

.doc-text-section {
  margin-bottom: 20px;
}

.doc-text-section h4 {
  margin: 0 0 8px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.doc-text-section p {
  margin: 0;
  font-size: 0.85rem;
  line-height: 1.6;
  color: var(--semantic-color-text-body);
}

.doc-sheet-footer {
  display: flex;
  justify-content: space-between;
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 12px;
  margin-top: 24px;
}

/* GxP Audit Compliance Warning Overlay */
.audit-compliance-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 50;
  background: rgba(15, 23, 42, 0.92);
  backdrop-filter: blur(6px);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  box-sizing: border-box;
}

.audit-alert-card {
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-error);
  border-radius: 8px;
  padding: 24px;
  max-width: 440px;
  text-align: center;
  color: var(--semantic-color-text-inverse);
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5);
}

.audit-alert-card .alert-icon {
  font-size: 2rem;
  display: block;
  margin-bottom: 12px;
}

.audit-alert-card .alert-title {
  margin: 0 0 8px 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.audit-alert-card .alert-message {
  margin: 0 0 16px 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.5;
}

.btn-retry-audit {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  border: none;
  border-radius: 6px;
  padding: 8px 16px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.btn-retry-audit:hover {
  background-color: var(--semantic-color-error);
}
</style>
