<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";

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

// Audio & Video player states
const audioRef = ref(null);
const videoRef = ref(null);
const isPlaying = ref(false);
const currentTime = ref(0);
const duration = ref(0);
const volume = ref(1);
const isMuted = ref(false);
const playbackRate = ref(1);

const togglePlayPause = () => {
  const mediaEl = mediaCategory.value === "video" ? videoRef.value : audioRef.value;
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
  const mediaEl = mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (mediaEl) {
    mediaEl.currentTime = val;
  }
};

const setPlaybackRate = (rate) => {
  playbackRate.value = rate;
  const mediaEl = mediaCategory.value === "video" ? videoRef.value : audioRef.value;
  if (mediaEl) {
    mediaEl.playbackRate = rate;
  }
};

const toggleMute = () => {
  isMuted.value = !isMuted.value;
  const mediaEl = mediaCategory.value === "video" ? videoRef.value : audioRef.value;
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

// Reset state when src or category changes
watch(
  () => props.src,
  () => {
    resetTransform();
    isPlaying.value = false;
    currentTime.value = 0;
  }
);

onMounted(() => {
  window.addEventListener("mouseup", handleMouseUp);
});

onUnmounted(() => {
  window.removeEventListener("mouseup", handleMouseUp);
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
        <h3 class="media-title">{{ title }}</h3>
      </div>

      <!-- Controls Toolbar -->
      <div class="header-controls">
        <template v-if="mediaCategory === 'image' || mediaCategory === 'pdf'">
          <button
            class="control-btn"
            aria-label="Zoom Out"
            title="Zoom Out"
            @click="zoomOut"
          >
            🔍−
          </button>
          <span class="zoom-indicator">{{ Math.round(zoomLevel * 100) }}%</span>
          <button
            class="control-btn"
            aria-label="Zoom In"
            title="Zoom In"
            @click="zoomIn"
          >
            🔍+
          </button>
          <button
            v-if="mediaCategory === 'image'"
            class="control-btn"
            aria-label="Rotate 90 Degrees"
            title="Rotate Clockwise"
            @click="rotateClockwise"
          >
            🔄
          </button>
          <button
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
      @mousedown="handleMouseDown"
      @mousemove="handleMouseMove"
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
          :src="src"
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
            class="control-btn"
            :disabled="pdfPage <= 1"
            aria-label="Previous page"
            @click="prevPdfPage"
          >
            ‹
          </button>
          <span class="page-counter">Page {{ pdfPage }} of {{ pdfTotalPages }}</span>
          <button
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
            cursor: zoomLevel > 1 ? (isDragging ? 'grabbing' : 'grab') : 'default',
          }"
          draggable="false"
        />
      </div>

      <!-- 3. Audio Waveform Preview Player -->
      <div
        v-else-if="mediaCategory === 'audio'"
        class="audio-viewer-container"
        data-testid="audio-viewer"
      >
        <audio
          ref="audioRef"
          :src="src"
          :autoplay="autoplay"
          @timeupdate="onTimeUpdate"
          @loadedmetadata="onLoadedMetadata"
          @ended="onEnded"
        ></audio>

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

        <div class="audio-controls-bar">
          <button
            class="play-toggle-btn"
            :aria-label="isPlaying ? 'Pause audio' : 'Play audio'"
            data-testid="audio-play-btn"
            @click="togglePlayPause"
          >
            {{ isPlaying ? "⏸" : "▶" }}
          </button>

          <span class="time-display">{{ formatTime(currentTime) }}</span>
          <input
            type="range"
            class="timeline-slider"
            min="0"
            :max="duration || 100"
            step="0.1"
            :value="currentTime"
            aria-label="Audio playback seek slider"
            data-testid="audio-timeline"
            @input="seekMedia"
          />
          <span class="time-display">{{ formatTime(duration) }}</span>

          <button
            class="control-btn"
            :aria-label="isMuted ? 'Unmute' : 'Mute'"
            @click="toggleMute"
          >
            {{ isMuted ? "🔇" : "🔊" }}
          </button>
        </div>
      </div>

      <!-- 4. Direct Video Playback (MP4 / WebM) -->
      <div
        v-else-if="mediaCategory === 'video'"
        class="video-viewer-container"
        data-testid="video-viewer"
      >
        <video
          ref="videoRef"
          :src="src"
          :autoplay="autoplay"
          class="video-element"
          data-testid="video-element"
          playsinline
          @timeupdate="onTimeUpdate"
          @loadedmetadata="onLoadedMetadata"
          @ended="onEnded"
        ></video>

        <div class="video-controls-bar">
          <button
            class="play-toggle-btn"
            :aria-label="isPlaying ? 'Pause video' : 'Play video'"
            data-testid="video-play-btn"
            @click="togglePlayPause"
          >
            {{ isPlaying ? "⏸" : "▶" }}
          </button>

          <span class="time-display">{{ formatTime(currentTime) }}</span>
          <input
            type="range"
            class="timeline-slider"
            min="0"
            :max="duration || 100"
            step="0.1"
            :value="currentTime"
            aria-label="Video playback seek slider"
            data-testid="video-timeline"
            @input="seekMedia"
          />
          <span class="time-display">{{ formatTime(duration) }}</span>

          <!-- Playback Speed -->
          <div class="speed-selector">
            <button
              v-for="rate in [0.5, 1, 1.5, 2]"
              :key="rate"
              class="speed-btn"
              :class="{ active: playbackRate === rate }"
              :aria-label="`Set speed to ${rate}x`"
              @click="setPlaybackRate(rate)"
            >
              {{ rate }}x
            </button>
          </div>

          <button
            class="control-btn"
            :aria-label="isMuted ? 'Unmute' : 'Mute'"
            @click="toggleMute"
          >
            {{ isMuted ? "🔇" : "🔊" }}
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
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);
  font-family: inherit;
  width: 100%;
}

.player-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.media-title-wrapper {
  display: flex;
  align-items: center;
  gap: 8px;
}

.media-badge {
  font-size: 11px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-pdf {
  background: #fee2e2;
  color: #991b1b;
}

.badge-image {
  background: #e0e7ff;
  color: #3730a3;
}

.badge-audio {
  background: #fef3c7;
  color: #92400e;
}

.badge-video {
  background: #dcfce7;
  color: #166534;
}

.media-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.header-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.control-btn {
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #cbd5e1);
  border-radius: 4px;
  padding: 4px 8px;
  font-size: 13px;
  cursor: pointer;
  color: var(--color-text, #0f172a);
  transition: background 0.15s ease;
}

.control-btn:hover:not(:disabled) {
  background: #f1f5f9;
}

.control-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.zoom-indicator {
  font-size: 12px;
  font-weight: 600;
  color: var(--color-text-muted, #64748b);
  min-width: 42px;
  text-align: center;
}

.player-viewport {
  position: relative;
  min-height: 380px;
  max-height: 650px;
  background: #0f172a;
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
  color: #ffffff;
  white-space: nowrap;
  letter-spacing: 2px;
}

/* PDF Viewer */
.pdf-viewer-container {
  width: 100%;
  height: 520px;
  display: flex;
  flex-direction: column;
  background: #f1f5f9;
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
  background: #e2e8f0;
  border-top: 1px solid #cbd5e1;
}

.page-counter {
  font-size: 13px;
  font-weight: 500;
  color: #334155;
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
  background: #1e293b;
}

.waveform-box {
  width: 100%;
  height: 80px;
  background: #0f172a;
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
  background: #0284c7;
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
  background: var(--color-primary, #026597);
  color: #ffffff;
  border: none;
  font-size: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background 0.15s ease;
}

.play-toggle-btn:hover {
  background: var(--color-primary-dark, #014d76);
}

.time-display {
  font-size: 12px;
  font-family: monospace;
  color: #e2e8f0;
  min-width: 42px;
}

.timeline-slider {
  flex: 1;
  accent-color: var(--color-primary, #026597);
  cursor: pointer;
}

/* Video Viewer */
.video-viewer-container {
  width: 100%;
  display: flex;
  flex-direction: column;
  background: #000000;
}

.video-element {
  width: 100%;
  max-height: 450px;
  background: #000000;
}

.video-controls-bar {
  padding: 10px 16px;
  background: rgba(15, 23, 42, 0.95);
}

.speed-selector {
  display: flex;
  gap: 4px;
}

.speed-btn {
  background: transparent;
  border: 1px solid #475569;
  color: #cbd5e1;
  font-size: 11px;
  border-radius: 4px;
  padding: 2px 6px;
  cursor: pointer;
}

.speed-btn.active {
  background: var(--color-primary, #026597);
  color: #ffffff;
  border-color: var(--color-primary, #026597);
}

/* Fallback */
.unsupported-viewer {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
  color: #ffffff;
  padding: 40px;
}

.unsupported-msg {
  font-size: 14px;
  color: #cbd5e1;
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
  background: #ffffff;
  color: #0f172a;
}
</style>
