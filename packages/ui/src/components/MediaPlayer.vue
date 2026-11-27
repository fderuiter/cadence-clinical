<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import {
  Pause,
  Play,
  RotateCw,
  Volume2,
  VolumeX,
  ZoomIn,
  ZoomOut,
} from "lucide-vue-next";

const props = defineProps({
  src: {
    type: String,
    required: true,
  },
  mimeType: {
    type: String,
    default: "application/pdf",
  },
  title: {
    type: String,
    default: "Clinical Media Artifact",
  },
  isWatermarked: {
    type: Boolean,
    default: false,
  },
  watermarkText: {
    type: String,
    default: "CONFIDENTIAL - CLINICAL TRIAL REVIEW ONLY",
  },
  autoplay: {
    type: Boolean,
    default: false,
  },
  initialZoom: {
    type: Number,
    default: 1,
  },
});

const emit = defineEmits(["play", "pause", "ended", "zoom-change", "error"]);

// Media category evaluation
const mediaCategory = computed(() => {
  const mime = (props.mimeType || "").toLowerCase();
  const src = (props.src || "").toLowerCase();

  if (mime.includes("pdf") || src.endsWith(".pdf")) {
    return "pdf";
  }
  if (mime.startsWith("image/") || /\.(png|jpe?g|gif|webp|svg)$/.test(src)) {
    return "image";
  }
  if (mime.startsWith("audio/") || /\.(mp3|wav|ogg|aac|flac)$/.test(src)) {
    return "audio";
  }
  if (mime.startsWith("video/") || /\.(mp4|webm|ogv)$/.test(src)) {
    return "video";
  }
  return "unsupported";
});

// Image viewer state (Pan & Zoom)
const zoomLevel = ref(props.initialZoom || 1);
const rotation = ref(0);
const panOffset = ref({ x: 0, y: 0 });
const isDragging = ref(false);
const dragStart = ref({ x: 0, y: 0 });
const imageViewportRef = ref(null);

const zoomIn = () => {
  zoomLevel.value = Math.min(zoomLevel.value + 0.25, 4.0);
  emit("zoom-change", zoomLevel.value);
};

const zoomOut = () => {
  zoomLevel.value = Math.max(zoomLevel.value - 0.25, 0.5);
  emit("zoom-change", zoomLevel.value);
};

const rotateClockwise = () => {
  rotation.value = (rotation.value + 90) % 360;
};

const resetTransform = () => {
  zoomLevel.value = 1;
  rotation.value = 0;
  panOffset.value = { x: 0, y: 0 };
  emit("zoom-change", 1);
};

const handleMouseDown = (e) => {
  if (mediaCategory.value !== "image") return;
  isDragging.value = true;
  dragStart.value = {
    x: e.clientX - panOffset.value.x,
    y: e.clientY - panOffset.value.y,
  };
};

const handleMouseMove = (e) => {
  if (!isDragging.value) return;
  panOffset.value = {
    x: e.clientX - dragStart.value.x,
    y: e.clientY - dragStart.value.y,
  };
};

const handleMouseUp = () => {
  isDragging.value = false;
};

const panBy = (dx, dy) => {
  panOffset.value = {
    x: panOffset.value.x + dx,
    y: panOffset.value.y + dy,
  };
};

watch(imageViewportRef, (el, prevEl) => {
  if (prevEl && typeof prevEl.removeEventListener === "function") {
    prevEl.removeEventListener("mousedown", handleMouseDown);
    prevEl.removeEventListener("mousemove", handleMouseMove);
  }
  if (el && typeof el.addEventListener === "function") {
    el.addEventListener("mousedown", handleMouseDown);
    el.addEventListener("mousemove", handleMouseMove);
  }
});

// Audio & Video player states
const audioRef = ref(null);
const videoRef = ref(null);
const isPlaying = ref(false);
const currentTime = ref(0);
const duration = ref(0);
const isMuted = ref(false);
const playbackRate = ref(1);

const togglePlayPause = () => {
  const mediaEl =
    mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (!mediaEl) return;

  if (isPlaying.value) {
    mediaEl.pause();
    isPlaying.value = false;
    emit("pause");
  } else {
    mediaEl.play();
    isPlaying.value = true;
    emit("play");
  }
};

const onTimeUpdate = (e) => {
  currentTime.value = e.target.currentTime;
};

const onLoadedMetadata = (e) => {
  duration.value = e.target.duration || 0;
};

const onEnded = () => {
  isPlaying.value = false;
  emit("ended");
};

const seekMedia = (e) => {
  const val = parseFloat(e.target.value);
  currentTime.value = val;
  const mediaEl =
    mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (mediaEl) {
    mediaEl.currentTime = val;
  }
};

const setPlaybackRate = (rate) => {
  playbackRate.value = rate;
  const mediaEl =
    mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (mediaEl) {
    mediaEl.playbackRate = rate;
  }
};

const toggleMute = () => {
  isMuted.value = !isMuted.value;
  const mediaEl =
    mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (mediaEl) {
    mediaEl.muted = isMuted.value;
  }
};

const formatTime = (secs) => {
  if (!secs || isNaN(secs)) return "00:00";
  const m = Math.floor(secs / 60);
  const s = Math.floor(secs % 60);
  return `${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
};

// PDF state
const pdfPage = ref(1);
const pdfTotalPages = ref(1);
const pdfCanvasRef = ref(null);
const pdfFrameSrc = ref("");
let ownedPdfObjectUrl = "";
let pdfFetchAbortController = null;
let isComponentMounted = false;

function canUseBlobUrls() {
  return (
    typeof Blob !== "undefined" &&
    typeof URL !== "undefined" &&
    typeof URL.createObjectURL === "function"
  );
}

function revokeOwnedPdfUrl() {
  if (
    ownedPdfObjectUrl &&
    typeof URL !== "undefined" &&
    typeof URL.revokeObjectURL === "function"
  ) {
    URL.revokeObjectURL(ownedPdfObjectUrl);
  }
  ownedPdfObjectUrl = "";
}

function abortPendingPdfFetch() {
  if (pdfFetchAbortController) {
    pdfFetchAbortController.abort();
    pdfFetchAbortController = null;
  }
}

function decodeDataUriToPdfBlob(dataUri) {
  const commaIdx = dataUri.indexOf(",");
  if (commaIdx < 0) {
    return null;
  }
  const metadata = dataUri.slice(5, commaIdx);
  const payload = dataUri.slice(commaIdx + 1);
  const isBase64 = /;base64/i.test(metadata);

  if (isBase64) {
    const binary = atob(payload);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return new Blob([bytes], { type: "application/pdf" });
  }

  const decoded = decodeURIComponent(payload);
  return new Blob([decoded], { type: "application/pdf" });
}

async function syncPdfBlobSource() {
  abortPendingPdfFetch();
  revokeOwnedPdfUrl();

  const rawSrc = props.src || "";
  if (!rawSrc || mediaCategory.value !== "pdf") {
    pdfFrameSrc.value = "";
    return;
  }

  if (rawSrc.startsWith("blob:")) {
    pdfFrameSrc.value = rawSrc;
    return;
  }

  if (rawSrc.startsWith("data:")) {
    if (!canUseBlobUrls()) {
      pdfFrameSrc.value = "";
      return;
    }
    try {
      const pdfBlob = decodeDataUriToPdfBlob(rawSrc);
      if (pdfBlob) {
        ownedPdfObjectUrl = URL.createObjectURL(pdfBlob);
        pdfFrameSrc.value = ownedPdfObjectUrl;
      } else {
        pdfFrameSrc.value = "";
      }
    } catch (err) {
      pdfFrameSrc.value = "";
      emit("error", err);
    }
    return;
  }

  if (
    !isComponentMounted ||
    !canUseBlobUrls() ||
    typeof window === "undefined" ||
    typeof fetch !== "function"
  ) {
    pdfFrameSrc.value = "";
    return;
  }

  const controller =
    typeof AbortController !== "undefined" ? new AbortController() : null;
  pdfFetchAbortController = controller;

  try {
    const resolvedHref = window.location?.href
      ? new URL(rawSrc, window.location.href).href
      : rawSrc;
    const response = await fetch(
      resolvedHref,
      controller ? { signal: controller.signal } : undefined
    );
    if (!response.ok) {
      throw new Error(`Failed to load PDF preview (${response.status})`);
    }
    const buffer = await response.arrayBuffer();
    if (pdfFetchAbortController !== controller) {
      return;
    }
    pdfFetchAbortController = null;
    const pdfBlob = new Blob([buffer], { type: "application/pdf" });
    ownedPdfObjectUrl = URL.createObjectURL(pdfBlob);
    pdfFrameSrc.value = ownedPdfObjectUrl;
  } catch (err) {
    if (err?.name === "AbortError") {
      return;
    }
    pdfFrameSrc.value = "";
    emit("error", err);
  }
}

const prevPdfPage = () => {
  if (pdfPage.value > 1) {
    pdfPage.value--;
  }
};

const nextPdfPage = () => {
  if (pdfPage.value < pdfTotalPages.value) {
    pdfPage.value++;
  }
};

// Synchronously initialize data:/blob: PDF URLs for immediate render
if (
  mediaCategory.value === "pdf" &&
  (props.src?.startsWith("blob:") || props.src?.startsWith("data:"))
) {
  void syncPdfBlobSource();
}

// Reset state when src or category changes
watch(
  () => [props.src, mediaCategory.value],
  ([newSrc], [oldSrc] = []) => {
    if (newSrc !== oldSrc) {
      resetTransform();
      isPlaying.value = false;
      currentTime.value = 0;
    }
    void syncPdfBlobSource();
  }
);

onMounted(() => {
  isComponentMounted = true;
  window.addEventListener("mouseup", handleMouseUp);
  if (mediaCategory.value === "pdf" && !pdfFrameSrc.value) {
    void syncPdfBlobSource();
  }
});

onUnmounted(() => {
  isComponentMounted = false;
  window.removeEventListener("mouseup", handleMouseUp);
  abortPendingPdfFetch();
  revokeOwnedPdfUrl();
});
</script>

<template>
  <div
    class="clinical-media-player"
    role="region"
    :aria-label="title"
    tabindex="0"
  >
    <!-- Header Bar -->
    <div class="player-header">
      <div class="media-title-wrapper">
        <span class="media-badge" :class="`badge-${mediaCategory}`">
          {{ mediaCategory.toUpperCase() }}
        </span>
        <h3 class="media-title">
          {{ title }}
        </h3>
      </div>

      <!-- Controls Toolbar -->
      <div class="header-controls">
        <template v-if="mediaCategory === 'image' || mediaCategory === 'pdf'">
          <button
            type="button"
            class="control-btn"
            aria-label="Zoom Out"
            title="Zoom Out"
            @click="zoomOut"
          >
            <ZoomOut
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
          </button>
          <span class="zoom-indicator">{{ Math.round(zoomLevel * 100) }}%</span>
          <button
            type="button"
            class="control-btn"
            aria-label="Zoom In"
            title="Zoom In"
            @click="zoomIn"
          >
            <ZoomIn
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
          </button>
          <button
            v-if="mediaCategory === 'image'"
            type="button"
            class="control-btn"
            aria-label="Rotate 90 Degrees"
            title="Rotate Clockwise"
            @click="rotateClockwise"
          >
            <RotateCw
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
          </button>
          <button
            v-if="mediaCategory === 'image'"
            type="button"
            class="control-btn"
            aria-label="Pan image left"
            title="Pan Left"
            @click="panBy(-24, 0)"
          >
            <span aria-hidden="true">&larr;</span>
          </button>
          <button
            v-if="mediaCategory === 'image'"
            type="button"
            class="control-btn"
            aria-label="Pan image right"
            title="Pan Right"
            @click="panBy(24, 0)"
          >
            <span aria-hidden="true">&rarr;</span>
          </button>
          <button
            type="button"
            class="control-btn"
            aria-label="Reset View"
            title="Reset Zoom & Pan"
            @click="resetTransform"
          >
            Reset
          </button>
        </template>
      </div>
    </div>

    <!-- Media Viewport Container -->
    <div
      class="player-viewport"
      role="region"
      aria-label="Media preview viewport"
    >
      <!-- Watermark Overlay (GxP Compliance Policy) -->
      <div
        v-if="isWatermarked"
        class="watermark-overlay"
        aria-hidden="true"
        data-testid="watermark-overlay"
      >
        <div v-for="n in 16" :key="n" class="watermark-label">
          {{ watermarkText }}
        </div>
      </div>

      <!-- 1. PDF Document Viewer -->
      <div
        v-if="mediaCategory === 'pdf'"
        class="pdf-viewer-container"
        data-testid="pdf-viewer"
      >
        <iframe
          :src="pdfFrameSrc || undefined"
          class="pdf-frame"
          :title="title"
          data-testid="pdf-iframe"
        ></iframe>
        <canvas
          ref="pdfCanvasRef"
          class="pdf-canvas"
          role="img"
          aria-label="Rendered PDF document"
          data-testid="pdf-canvas"
        ></canvas>
        <div class="pdf-bottom-bar">
          <button
            type="button"
            class="control-btn"
            :disabled="pdfPage <= 1"
            aria-label="Previous page"
            @click="prevPdfPage"
          >
            ‹
          </button>
          <span class="page-counter"
            >Page {{ pdfPage }} of {{ pdfTotalPages }}</span
          >
          <button
            type="button"
            class="control-btn"
            :disabled="pdfPage >= pdfTotalPages"
            aria-label="Next page"
            @click="nextPdfPage"
          >
            ›
          </button>
        </div>
      </div>

      <!-- 2. Pan & Zoom Image Viewer -->
      <div
        v-else-if="mediaCategory === 'image'"
        ref="imageViewportRef"
        class="image-viewer-container"
        data-testid="image-viewer"
      >
        <img
          :src="src"
          :alt="title"
          class="pannable-image"
          data-testid="pannable-image"
          :style="{
            transform: `translate(${panOffset.x}px, ${panOffset.y}px) scale(${zoomLevel}) rotate(${rotation}deg)`,
            cursor:
              zoomLevel > 1 ? (isDragging ? 'grabbing' : 'grab') : 'default',
          }"
          draggable="false"
        />
      </div>

      <!-- 3 & 4. Audio Waveform & Direct Video Playback -->
      <div
        v-else-if="mediaCategory === 'audio' || mediaCategory === 'video'"
        :class="
          mediaCategory === 'audio'
            ? 'audio-viewer-container'
            : 'video-viewer-container'
        "
        :data-testid="`${mediaCategory}-viewer`"
      >
        <template v-if="mediaCategory === 'audio'">
          <audio
            ref="audioRef"
            :src="src"
            :autoplay="autoplay"
            @timeupdate="onTimeUpdate"
            @loadedmetadata="onLoadedMetadata"
            @ended="onEnded"
          >
            <track kind="captions" />
          </audio>

          <div class="waveform-box">
            <div class="waveform-bars" :class="{ 'is-playing': isPlaying }">
              <span
                v-for="bar in 32"
                :key="bar"
                class="waveform-bar"
                :style="{ height: `${20 + ((bar * 17) % 65)}%` }"
              ></span>
            </div>
          </div>
        </template>

        <video
          v-else
          ref="videoRef"
          :src="src"
          :autoplay="autoplay"
          class="video-element"
          data-testid="video-element"
          playsinline
          @timeupdate="onTimeUpdate"
          @loadedmetadata="onLoadedMetadata"
          @ended="onEnded"
        >
          <track kind="captions" />
        </video>

        <div
          :class="
            mediaCategory === 'audio'
              ? 'audio-controls-bar'
              : 'video-controls-bar'
          "
        >
          <button
            type="button"
            class="play-toggle-btn"
            :aria-label="
              isPlaying ? `Pause ${mediaCategory}` : `Play ${mediaCategory}`
            "
            :data-testid="`${mediaCategory}-play-btn`"
            @click="togglePlayPause"
          >
            <Pause
              v-if="isPlaying"
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
            <Play
              v-else
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
          </button>

          <span class="time-display">{{ formatTime(currentTime) }}</span>
          <input
            type="range"
            class="timeline-slider"
            min="0"
            :max="duration || 100"
            step="0.1"
            :value="currentTime"
            :aria-label="`${mediaCategory === 'audio' ? 'Audio' : 'Video'} playback seek slider`"
            :data-testid="`${mediaCategory}-timeline`"
            @input="seekMedia"
          />
          <span class="time-display">{{ formatTime(duration) }}</span>

          <!-- Playback Speed -->
          <div v-if="mediaCategory === 'video'" class="speed-selector">
            <button
              v-for="rate in [0.5, 1, 1.5, 2]"
              :key="rate"
              type="button"
              class="speed-btn"
              :class="{ active: playbackRate === rate }"
              :aria-label="`Set speed to ${rate}x`"
              @click="setPlaybackRate(rate)"
            >
              {{ rate }}x
            </button>
          </div>

          <button
            type="button"
            class="control-btn"
            :aria-label="isMuted ? 'Unmute' : 'Mute'"
            @click="toggleMute"
          >
            <VolumeX
              v-if="isMuted"
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
            <Volume2
              v-else
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              class="clinical-icon"
            />
          </button>
        </div>
      </div>

      <!-- 5. Unsupported Media Fallback -->
      <div v-else class="unsupported-viewer" data-testid="unsupported-viewer">
        <p class="unsupported-msg">
          Unsupported media format ({{ mimeType || "unknown" }}).
        </p>
        <a :href="src" download class="btn btn-secondary">Download Raw File</a>
      </div>
    </div>
  </div>
</template>

<style scoped>
.clinical-media-player {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: var(--shadow-sm);
  font-family: inherit;
  width: 100%;
}

.player-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.media-title-wrapper {
  display: flex;
  align-items: center;
  gap: 8px;
}

.media-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-pdf {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.badge-image {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.badge-audio {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}

.badge-video {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.media-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.header-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.control-btn {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  padding: 4px 8px;
  font-size: 13px;
  cursor: pointer;
  color: var(--semantic-color-text);
  transition: background 0.15s ease;
}

.control-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.control-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.zoom-indicator {
  font-size: 12px;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  min-width: 42px;
  text-align: center;
}

.player-viewport {
  position: relative;
  min-height: 380px;
  max-height: 650px;
  background: var(--semantic-color-surface-inverse);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

/* Watermark Overlay */
.watermark-overlay {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  pointer-events: none;
  z-index: 100;
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  grid-template-rows: repeat(4, 1fr);
  align-items: center;
  justify-items: center;
  overflow: hidden;
  opacity: 0.18;
  user-select: none;
}

.watermark-label {
  transform: rotate(-35deg);
  font-size: 14px;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
  white-space: nowrap;
  letter-spacing: 2px;
}

/* PDF Viewer */
.pdf-viewer-container {
  width: 100%;
  height: 520px;
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface-header);
}

.pdf-frame {
  width: 100%;
  flex: 1;
  border: none;
}

.pdf-canvas {
  display: none;
}

.pdf-bottom-bar {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 12px;
  padding: 8px;
  background: var(--semantic-color-border);
  border-top: 1px solid var(--semantic-color-border-muted);
}

.page-counter {
  font-size: 13px;
  font-weight: 500;
  color: var(--semantic-color-text-body);
}

/* Image Viewer */
.image-viewer-container {
  width: 100%;
  height: 480px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.pannable-image {
  max-width: 90%;
  max-height: 90%;
  object-fit: contain;
  transition: transform 0.08s ease-out;
  user-select: none;
}

/* Audio Waveform Viewer */
.audio-viewer-container {
  width: 100%;
  padding: 32px 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
  background: var(--semantic-color-surface-elevated);
}

.waveform-box {
  width: 100%;
  height: 80px;
  background: var(--semantic-color-surface-inverse);
  border-radius: 6px;
  padding: 12px;
  display: flex;
  align-items: center;
}

.waveform-bars {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 100%;
  gap: 3px;
}

.waveform-bar {
  flex: 1;
  background: var(--semantic-color-accent-inverse);
  border-radius: 2px;
  transition: height 0.15s ease;
}

.waveform-bars.is-playing .waveform-bar {
  animation: pulse-wave 1s infinite alternate;
}

@keyframes pulse-wave {
  0% {
    filter: brightness(1);
  }
  100% {
    filter: brightness(1.6);
  }
}

.audio-controls-bar,
.video-controls-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
}

.play-toggle-btn {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: none;
  font-size: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background 0.15s ease;
}

.play-toggle-btn:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}

.time-display {
  font-size: 12px;
  font-family: var(--semantic-font-mono);
  color: var(--semantic-color-text-inverse-muted);
  min-width: 42px;
  font-variant-numeric: tabular-nums;
}

.timeline-slider {
  flex: 1;
  accent-color: var(--semantic-color-primary);
  cursor: pointer;
}

/* Video Viewer */
.video-viewer-container {
  width: 100%;
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-black);
}

.video-element {
  width: 100%;
  max-height: 450px;
  background: var(--semantic-color-black);
}

.video-controls-bar {
  padding: 10px 16px;
  background: var(--semantic-color-backdrop-strong);
}

.speed-selector {
  display: flex;
  gap: 4px;
}

.speed-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background: transparent;
  border: 1px solid var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse-muted);
  font-size: var(--font-size-2xs);
  border-radius: 4px;
  padding: 2px 6px;
  cursor: pointer;
  box-sizing: border-box;
}

.speed-btn.active {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--semantic-color-primary);
}

/* Fallback */
.unsupported-viewer {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  color: var(--semantic-color-text-inverse);
  padding: 40px;
}

.unsupported-msg {
  font-size: 14px;
  color: var(--semantic-color-text-inverse-muted);
}

.btn {
  padding: 8px 16px;
  font-size: 13px;
  font-weight: 500;
  border-radius: 6px;
  text-decoration: none;
  cursor: pointer;
  border: 1px solid transparent;
}

.btn-secondary {
  background: var(--semantic-color-text-inverse);
  color: var(--semantic-color-surface-inverse);
}
</style>
