<template>
  <div class="mpr-viewer-container">
    <!-- Viewport Identity Banner -->
    <div v-if="viewportLabel || timepoint" class="viewport-identity-banner">
      <div class="banner-title-group">
        <span v-if="viewportLabel" class="viewport-name-tag">{{
          viewportLabel
        }}</span>
        <span v-if="timepoint" class="timepoint-tag">{{
          formatTimepointLabel(timepoint)
        }}</span>
      </div>
      <div
        class="sync-status-badge"
        :class="{ synced: isSynced, unlinked: !isSynced }"
      >
        <span class="status-icon">{{
          isSynced ? "Open link" : "Unlocked"
        }}</span>
        <span class="status-text">{{
          isSynced ? "Crosshairs & Scroll Linked" : "Independent Control"
        }}</span>
      </div>
    </div>

    <!-- Viewer Toolbar -->
    <div class="mpr-toolbar">
      <div class="toolbar-group">
        <label for="layout-mode-select" class="toolbar-label">Layout:</label>
        <select
          id="layout-mode-select"
          v-model="layoutMode"
          class="toolbar-select"
          aria-label="Viewport Layout Mode"
        >
          <option value="mpr-tri-view">MPR Tri-View (Axial / Sag / Cor)</option>
          <option value="axial-only">Axial View Only</option>
          <option value="sagittal-only">Sagittal View Only</option>
          <option value="coronal-only">Coronal View Only</option>
        </select>
      </div>

      <div class="toolbar-group">
        <label for="window-preset-select" class="toolbar-label"
          >W/L Preset:</label
        >
        <select
          id="window-preset-select"
          v-model="selectedPreset"
          class="toolbar-select"
          aria-label="Window Level Preset"
          @change="applyWindowPreset"
        >
          <option value="SOFT_TISSUE">Soft Tissue (W:350 L:40)</option>
          <option value="LUNG">Lung (W:1500 L:-600)</option>
          <option value="BONE">Bone (W:2000 L:400)</option>
          <option value="BRAIN">Brain (W:80 L:40)</option>
          <option value="LIVER">Liver (W:150 L:30)</option>
          <option value="CUSTOM">Custom</option>
        </select>
      </div>

      <div class="toolbar-group">
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: isCrosshairEnabled }"
          title="Toggle Synchronized MPR Crosshairs"
          @click="isCrosshairEnabled = !isCrosshairEnabled"
        >
          <span class="btn-icon"
            ><ClinicalIcon name="trending-up" :size="16"
          /></span>
          Crosshairs
        </button>

        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: isInverted }"
          title="Invert Grayscale Color Map"
          @click="isInverted = !isInverted"
        >
          <span class="btn-icon"
            ><ClinicalIcon name="file-text" :size="16"
          /></span>
          Invert
        </button>

        <button
          type="button"
          class="toolbar-btn"
          title="Reset Zoom & Pan"
          @click="resetView"
        >
          <span class="btn-icon"
            ><ClinicalIcon name="refresh" :size="16"
          /></span>
          Reset
        </button>
      </div>

      <div class="toolbar-group cine-group">
        <button
          type="button"
          class="toolbar-btn"
          :class="{ active: isCinePlaying }"
          :title="isCinePlaying ? 'Pause Cine Playback' : 'Play Cine Loop'"
          @click="toggleCine"
        >
          <span class="btn-icon"
            ><ClinicalIcon :name="isCinePlaying ? 'pause' : 'play'" :size="16"
          /></span>
          Cine
        </button>
        <span class="cine-speed-label">{{ cineFps }} fps</span>
      </div>
    </div>

    <!-- Viewports Canvas Grid -->
    <div class="viewports-grid" :class="layoutMode">
      <!-- Axial Viewport -->
      <div
        v-if="layoutMode === 'mpr-tri-view' || layoutMode === 'axial-only'"
        class="viewport-card axial-card"
      >
        <div class="viewport-header">
          <span class="plane-badge axial-badge"
            >AXIAL (Z: {{ axialSlice + 1 }} / {{ maxSlices }})</span
          >
          <span class="slice-loc-label"
            >Loc: {{ (axialSlice * 2.5).toFixed(1) }} mm</span
          >
        </div>
        <div class="canvas-wrapper">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <canvas
            ref="axialCanvasRef"
            class="mpr-canvas"
            width="380"
            height="380"
            @mousedown="startPanOrMeasure($event, 'AXIAL')"
            @mousemove="onCanvasMouseMove($event, 'AXIAL')"
            @mouseup="stopPanOrMeasure"
            @touchstart.prevent="startPanOrMeasure($event, 'AXIAL')"
            @touchmove.prevent="onCanvasMouseMove($event, 'AXIAL')"
            @touchend.prevent="stopPanOrMeasure"
            @wheel.prevent="onWheelScroll($event, 'AXIAL')"
          />
        </div>
        <div class="slice-slider-box">
          <input
            v-model.number="axialSlice"
            type="range"
            min="0"
            :max="maxSlices - 1"
            class="slice-slider"
            aria-label="Axial Slice Navigator"
          />
        </div>
      </div>

      <!-- Sagittal Viewport -->
      <div
        v-if="layoutMode === 'mpr-tri-view' || layoutMode === 'sagittal-only'"
        class="viewport-card sagittal-card"
      >
        <div class="viewport-header">
          <span class="plane-badge sagittal-badge"
            >SAGITTAL (X: {{ sagittalSlice + 1 }} / 512)</span
          >
          <span class="slice-loc-label">Reconstructed</span>
        </div>
        <div class="canvas-wrapper">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <canvas
            ref="sagittalCanvasRef"
            class="mpr-canvas"
            width="380"
            height="380"
            @mousedown="startPanOrMeasure($event, 'SAGITTAL')"
            @mousemove="onCanvasMouseMove($event, 'SAGITTAL')"
            @mouseup="stopPanOrMeasure"
            @touchstart.prevent="startPanOrMeasure($event, 'SAGITTAL')"
            @touchmove.prevent="onCanvasMouseMove($event, 'SAGITTAL')"
            @touchend.prevent="stopPanOrMeasure"
            @wheel.prevent="onWheelScroll($event, 'SAGITTAL')"
          />
        </div>
        <div class="slice-slider-box">
          <input
            v-model.number="sagittalSlice"
            type="range"
            min="0"
            max="511"
            class="slice-slider"
            aria-label="Sagittal Slice Navigator"
          />
        </div>
      </div>

      <!-- Coronal Viewport -->
      <div
        v-if="layoutMode === 'mpr-tri-view' || layoutMode === 'coronal-only'"
        class="viewport-card coronal-card"
      >
        <div class="viewport-header">
          <span class="plane-badge coronal-badge"
            >CORONAL (Y: {{ coronalSlice + 1 }} / 512)</span
          >
          <span class="slice-loc-label">Reconstructed</span>
        </div>
        <div class="canvas-wrapper">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <canvas
            ref="coronalCanvasRef"
            class="mpr-canvas"
            width="380"
            height="380"
            @mousedown="startPanOrMeasure($event, 'CORONAL')"
            @mousemove="onCanvasMouseMove($event, 'CORONAL')"
            @mouseup="stopPanOrMeasure"
            @touchstart.prevent="startPanOrMeasure($event, 'CORONAL')"
            @touchmove.prevent="onCanvasMouseMove($event, 'CORONAL')"
            @touchend.prevent="stopPanOrMeasure"
            @wheel.prevent="onWheelScroll($event, 'CORONAL')"
          />
        </div>
        <div class="slice-slider-box">
          <input
            v-model.number="coronalSlice"
            type="range"
            min="0"
            max="511"
            class="slice-slider"
            aria-label="Coronal Slice Navigator"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { ref, onMounted, onUnmounted, watch } from "vue";
import {
  observeActiveThemeChanges,
  resolveThemeToken,
} from "../../utils/themeColorResolver";

const props = defineProps({
  seriesUid: {
    type: String,
    default: "",
  },
  activeAnnotations: {
    type: Array,
    default: () => [],
  },
  activeTool: {
    type: String,
    default: "POINTER",
  },
  timepoint: {
    type: String,
    default: "",
  },
  viewportLabel: {
    type: String,
    default: "",
  },
  isSynced: {
    type: Boolean,
    default: false,
  },
  syncedState: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits([
  "caliperCreated",
  "coordinateSelected",
  "update:syncedState",
  "syncStateChange",
  "update:isSynced",
]);

const layoutMode = ref("mpr-tri-view");
const selectedPreset = ref("SOFT_TISSUE");
const isCrosshairEnabled = ref(true);
const isInverted = ref(false);
const isCinePlaying = ref(false);
const cineFps = ref(15);

const maxSlices = ref(48);
const axialSlice = ref(24);
const sagittalSlice = ref(256);
const coronalSlice = ref(256);

const axialCanvasRef = ref(null);
const sagittalCanvasRef = ref(null);
const coronalCanvasRef = ref(null);

let cineInterval = null;
const isDrawing = ref(false);
const drawStart = ref(null);
let isUpdatingFromProp = false;

const PRESET_MAP = {
  SOFT_TISSUE: { center: 40, width: 350 },
  LUNG: { center: -600, width: 1500 },
  BONE: { center: 400, width: 2000 },
  BRAIN: { center: 40, width: 80 },
  LIVER: { center: 30, width: 150 },
};

function formatTimepointLabel(tp) {
  if (!tp) return "";
  const map = {
    BASELINE: "Baseline Screening",
    CYCLE_2_DAY_1: "Cycle 2 Day 1",
    CYCLE_4_DAY_1: "Cycle 4 Day 1",
    END_OF_TREATMENT: "End of Treatment",
  };
  return map[tp] || tp;
}

function notifySyncStateChange() {
  if (props.isSynced && !isUpdatingFromProp) {
    const currentState = {
      axialSlice: axialSlice.value,
      sagittalSlice: sagittalSlice.value,
      coronalSlice: coronalSlice.value,
      selectedPreset: selectedPreset.value,
    };
    emit("update:syncedState", currentState);
    emit("syncStateChange", currentState);
  }
}

watch(
  () => props.syncedState,
  (newSync) => {
    if (props.isSynced && newSync) {
      isUpdatingFromProp = true;
      if (
        typeof newSync.axialSlice === "number" &&
        newSync.axialSlice !== axialSlice.value
      ) {
        axialSlice.value = Math.max(
          0,
          Math.min(maxSlices.value - 1, newSync.axialSlice)
        );
      }
      if (
        typeof newSync.sagittalSlice === "number" &&
        newSync.sagittalSlice !== sagittalSlice.value
      ) {
        sagittalSlice.value = Math.max(0, Math.min(511, newSync.sagittalSlice));
      }
      if (
        typeof newSync.coronalSlice === "number" &&
        newSync.coronalSlice !== coronalSlice.value
      ) {
        coronalSlice.value = Math.max(0, Math.min(511, newSync.coronalSlice));
      }
      if (
        newSync.selectedPreset &&
        newSync.selectedPreset !== selectedPreset.value
      ) {
        selectedPreset.value = newSync.selectedPreset;
      }
      isUpdatingFromProp = false;
    }
  },
  { deep: true, immediate: true }
);

function applyWindowPreset() {
  if (selectedPreset.value !== "CUSTOM" && PRESET_MAP[selectedPreset.value]) {
    renderAllViewports();
  }
  notifySyncStateChange();
}

function resetView() {
  axialSlice.value = Math.floor(maxSlices.value / 2);
  sagittalSlice.value = 256;
  coronalSlice.value = 256;
  renderAllViewports();
  notifySyncStateChange();
}

function toggleCine() {
  isCinePlaying.value = !isCinePlaying.value;
  if (isCinePlaying.value) {
    cineInterval = setInterval(() => {
      axialSlice.value = (axialSlice.value + 1) % maxSlices.value;
      notifySyncStateChange();
    }, 1000 / cineFps.value);
  } else if (cineInterval) {
    clearInterval(cineInterval);
    cineInterval = null;
  }
}

function onWheelScroll(event, plane) {
  const delta = Math.sign(event.deltaY);
  if (plane === "AXIAL") {
    axialSlice.value = Math.max(
      0,
      Math.min(maxSlices.value - 1, axialSlice.value + delta)
    );
  } else if (plane === "SAGITTAL") {
    sagittalSlice.value = Math.max(
      0,
      Math.min(511, sagittalSlice.value + delta * 4)
    );
  } else if (plane === "CORONAL") {
    coronalSlice.value = Math.max(
      0,
      Math.min(511, coronalSlice.value + delta * 4)
    );
  }
  notifySyncStateChange();
}

function getCanvasCoordinates(event, canvas) {
  if (!canvas) return { x: 0, y: 0 };
  const rect = canvas.getBoundingClientRect();

  let clientX = event.clientX;
  let clientY = event.clientY;

  if (event.touches && event.touches.length > 0) {
    clientX = event.touches[0].clientX;
    clientY = event.touches[0].clientY;
  } else if (event.changedTouches && event.changedTouches.length > 0) {
    clientX = event.changedTouches[0].clientX;
    clientY = event.changedTouches[0].clientY;
  }

  const scaleX = rect.width > 0 ? canvas.width / rect.width : 1;
  const scaleY = rect.height > 0 ? canvas.height / rect.height : 1;

  const x = (clientX - rect.left) * scaleX;
  const y = (clientY - rect.top) * scaleY;

  return {
    x: Math.max(0, Math.min(canvas.width, x)),
    y: Math.max(0, Math.min(canvas.height, y)),
  };
}

function startPanOrMeasure(event, plane) {
  const canvas = event.target;
  const { x, y } = getCanvasCoordinates(event, canvas);

  isDrawing.value = true;
  drawStart.value = { x, y, plane };

  if (props.activeTool === "POINTER") {
    // Crosshair sync
    if (plane === "AXIAL") {
      sagittalSlice.value = Math.floor((x / canvas.width) * 512);
      coronalSlice.value = Math.floor((y / canvas.height) * 512);
      notifySyncStateChange();
    }
  }
}

function onCanvasMouseMove(event, plane) {
  if (!isDrawing.value) return;
  const canvas = event.target;
  const { x: currentX, y: currentY } = getCanvasCoordinates(event, canvas);

  if (props.activeTool === "POINTER") {
    if (plane === "AXIAL") {
      sagittalSlice.value = Math.max(
        0,
        Math.min(511, Math.floor((currentX / canvas.width) * 512))
      );
      coronalSlice.value = Math.max(
        0,
        Math.min(511, Math.floor((currentY / canvas.height) * 512))
      );
      notifySyncStateChange();
    }
  }
}

function stopPanOrMeasure(event) {
  if (!isDrawing.value) return;
  isDrawing.value = false;

  if (drawStart.value && props.activeTool.startsWith("CALIPER_")) {
    const canvas = event.target;
    const { x: endX, y: endY } = getCanvasCoordinates(event, canvas);

    const dx = endX - drawStart.value.x;
    const dy = endY - drawStart.value.y;
    const pixelDistance = Math.sqrt(dx * dx + dy * dy);
    // Assuming 0.75 mm/pixel scaling
    const lengthMm = Number((pixelDistance * 0.75).toFixed(2));

    if (lengthMm > 2.0) {
      emit("caliperCreated", {
        plane: drawStart.value.plane,
        tool: props.activeTool,
        lengthMm,
        points: [
          { x: drawStart.value.x, y: drawStart.value.y },
          { x: endX, y: endY },
        ],
        sliceIndex: axialSlice.value,
        timepoint: props.timepoint || "",
        viewportLabel: props.viewportLabel || "",
      });
    }
  }
  drawStart.value = null;
  renderAllViewports();
}

function renderViewport(canvas, plane, sliceVal) {
  if (!canvas || !canvas.getContext) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;
  const w = canvas.width;
  const h = canvas.height;

  const inverseTextColor = resolveThemeToken(
    "--semantic-color-text-inverse",
    canvas
  );
  const sunkenSurfaceColor = resolveThemeToken(
    "--semantic-color-surface-sunken",
    canvas
  );
  const inverseSurfaceColor = resolveThemeToken(
    "--semantic-color-surface-inverse",
    canvas
  );
  const elevatedSurfaceColor = resolveThemeToken(
    "--semantic-color-surface-elevated",
    canvas
  );
  const controlBorderColor = resolveThemeToken(
    "--color-border-control",
    canvas
  );
  const inverseMutedTextColor = resolveThemeToken(
    "--semantic-color-text-inverse-muted",
    canvas
  );
  const imagingHighlightColor = resolveThemeToken(
    "--semantic-color-imaging-highlight",
    canvas
  );
  const subtleTextColor = resolveThemeToken(
    "--semantic-color-text-subtle",
    canvas
  );
  const inverseBorderColor = resolveThemeToken(
    "--semantic-color-border-inverse",
    canvas
  );
  const accentOverlayColor = resolveThemeToken(
    "--semantic-color-accent-inverse",
    canvas
  );
  const manualContourColor = resolveThemeToken(
    "--semantic-color-emerald-inverse-border",
    canvas
  );
  const perpendicularCaliperColor = resolveThemeToken(
    "--semantic-badge-queried-border",
    canvas
  );
  const newLesionColor = resolveThemeToken("--semantic-color-error", canvas);

  // Background
  ctx.fillStyle = isInverted.value ? inverseTextColor : sunkenSurfaceColor;
  ctx.fillRect(0, 0, w, h);

  // Draw simulated anatomical structure (e.g. Thorax / Liver / Lesion)
  const centerX = w / 2;
  const centerY = h / 2;
  const grad = ctx.createRadialGradient(
    centerX,
    centerY,
    20,
    centerX,
    centerY,
    w * 0.45
  );

  if (isInverted.value) {
    grad.addColorStop(0, elevatedSurfaceColor);
    grad.addColorStop(0.4, controlBorderColor);
    grad.addColorStop(0.8, inverseMutedTextColor);
    grad.addColorStop(1, imagingHighlightColor);
  } else {
    grad.addColorStop(0, imagingHighlightColor);
    grad.addColorStop(0.3, subtleTextColor);
    grad.addColorStop(0.7, inverseBorderColor);
    grad.addColorStop(1, inverseSurfaceColor);
  }

  ctx.fillStyle = grad;
  ctx.beginPath();
  if (plane === "AXIAL") {
    ctx.ellipse(centerX, centerY, w * 0.4, h * 0.35, 0, 0, Math.PI * 2);
  } else if (plane === "SAGITTAL") {
    ctx.ellipse(centerX, centerY, w * 0.35, h * 0.42, 0, 0, Math.PI * 2);
  } else {
    ctx.ellipse(centerX, centerY, w * 0.42, h * 0.4, 0, 0, Math.PI * 2);
  }
  ctx.fill();

  // Draw simulated target tumor lesion in center-right quadrant
  ctx.fillStyle = isInverted.value ? inverseSurfaceColor : inverseTextColor;
  ctx.beginPath();
  const tumorX = centerX + 40;
  const tumorY = centerY - 20;
  const tumorRadius = 22 + Math.sin(sliceVal * 0.15) * 6;
  ctx.arc(tumorX, tumorY, Math.max(8, tumorRadius), 0, Math.PI * 2);
  ctx.fill();

  // Render crosshairs
  if (isCrosshairEnabled.value) {
    ctx.save();
    ctx.globalAlpha = 0.65;
    ctx.lineWidth = 1;
    ctx.strokeStyle = accentOverlayColor;
    ctx.setLineDash([4, 4]);

    const crossX = (sagittalSlice.value / 512) * w;
    const crossY = (coronalSlice.value / 512) * h;

    ctx.beginPath();
    ctx.moveTo(crossX, 0);
    ctx.lineTo(crossX, h);
    ctx.moveTo(0, crossY);
    ctx.lineTo(w, crossY);
    ctx.stroke();
    ctx.setLineDash([]);
    ctx.restore();
  }

  // Render active annotations for this slice
  props.activeAnnotations.forEach((annot, idx) => {
    if (annot.points && annot.points.length > 2) {
      // Polygon contour overlay (AI auto-segment contour or manual polygon)
      const isAiContour =
        annot.measurement_type === "AI_AUTO_CONTOUR" || annot.is_ai_assisted;
      const contourColor = isAiContour
        ? accentOverlayColor
        : manualContourColor;
      ctx.lineWidth = 2;
      ctx.strokeStyle = contourColor;

      ctx.beginPath();
      ctx.moveTo(annot.points[0].x, annot.points[0].y);
      for (let i = 1; i < annot.points.length; i++) {
        ctx.lineTo(annot.points[i].x, annot.points[i].y);
      }
      ctx.closePath();
      ctx.save();
      ctx.globalAlpha = isAiContour ? 0.22 : 0.15;
      ctx.fillStyle = contourColor;
      ctx.fill();
      ctx.restore();
      ctx.stroke();

      // Fitted caliper lines
      if (annot.fittedCalipers && annot.fittedCalipers.length > 0) {
        annot.fittedCalipers.forEach((cal) => {
          const sp = cal.startPoint || cal.StartPoint;
          const ep = cal.endPoint || cal.EndPoint;
          const type = cal.caliperType || cal.CaliperType;
          if (sp && ep) {
            ctx.lineWidth = 2;
            ctx.strokeStyle =
              type === "PERPENDICULAR_DIAMETER"
                ? perpendicularCaliperColor
                : accentOverlayColor;
            if (type === "PERPENDICULAR_DIAMETER") {
              ctx.setLineDash([4, 3]);
            }
            ctx.beginPath();
            ctx.moveTo(sp.x, sp.y);
            ctx.lineTo(ep.x, ep.y);
            ctx.stroke();
            ctx.setLineDash([]);
          }
        });
      } else if (annot.points.length >= 2) {
        const midIdx = Math.floor(annot.points.length / 2);
        ctx.lineWidth = 2;
        ctx.strokeStyle = accentOverlayColor;
        ctx.beginPath();
        ctx.moveTo(annot.points[0].x, annot.points[0].y);
        ctx.lineTo(annot.points[midIdx].x, annot.points[midIdx].y);
        ctx.stroke();
      }

      // Contour overlay label
      ctx.fillStyle = accentOverlayColor;
      ctx.font = "bold 11px sans-serif";
      const labelText = `${annot.lesion_id || "TL" + (idx + 1)}: ${annot.longest_diameter_mm}mm (AI)`;
      ctx.fillText(labelText, annot.points[0].x, annot.points[0].y - 8);
    } else if (annot.points && annot.points.length === 2) {
      ctx.lineWidth = 2;
      ctx.strokeStyle =
        annot.lesion_type === "NEW_LESION"
          ? newLesionColor
          : manualContourColor;
      ctx.beginPath();
      ctx.moveTo(annot.points[0].x, annot.points[0].y);
      ctx.lineTo(annot.points[1].x, annot.points[1].y);
      ctx.stroke();

      // Caliper end ticks
      ctx.fillStyle = inverseTextColor;
      ctx.beginPath();
      ctx.arc(annot.points[0].x, annot.points[0].y, 3, 0, Math.PI * 2);
      ctx.arc(annot.points[1].x, annot.points[1].y, 3, 0, Math.PI * 2);
      ctx.fill();

      // Label
      ctx.fillStyle = accentOverlayColor;
      ctx.font = "bold 11px sans-serif";
      const midX = (annot.points[0].x + annot.points[1].x) / 2;
      const midY = (annot.points[0].y + annot.points[1].y) / 2 - 8;
      ctx.fillText(
        `${annot.lesion_id || "L" + (idx + 1)}: ${annot.longest_diameter_mm}mm`,
        midX,
        midY
      );
    }
  });
}

function renderAllViewports() {
  renderViewport(axialCanvasRef.value, "AXIAL", axialSlice.value);
  renderViewport(sagittalCanvasRef.value, "SAGITTAL", sagittalSlice.value);
  renderViewport(coronalCanvasRef.value, "CORONAL", coronalSlice.value);
}

watch(
  [
    axialSlice,
    sagittalSlice,
    coronalSlice,
    isInverted,
    isCrosshairEnabled,
    () => props.activeAnnotations,
  ],
  () => {
    renderAllViewports();
  }
);

let stopThemeObserver = null;

onMounted(() => {
  renderAllViewports();
  stopThemeObserver = observeActiveThemeChanges(() => {
    renderAllViewports();
  });
});

onUnmounted(() => {
  if (cineInterval) {
    clearInterval(cineInterval);
  }
  if (stopThemeObserver) {
    stopThemeObserver();
    stopThemeObserver = null;
  }
});
</script>

<style scoped>
.mpr-viewer-container {
  display: flex;
  flex-direction: column;
  background-color: var(--semantic-color-surface-sunken);
  border-radius: var(--semantic-border-radius-md, 8px);
  border: 1px solid var(--semantic-color-border);
  overflow: hidden;
  height: 100%;
}

.viewport-identity-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 12px;
  background-color: var(--semantic-color-surface-sunken);
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.banner-title-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.viewport-name-tag {
  font-size: 0.82rem;
  font-weight: 700;
  color: var(--semantic-color-accent-inverse);
}

.timepoint-tag {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-accent-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  padding: 2px 6px;
  border-radius: 4px;
}

.sync-status-badge {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
}

.sync-status-badge.synced {
  background-color: var(--semantic-color-emerald-inverse-bg);
  color: var(--semantic-color-emerald-inverse-text);
  border: 1px solid var(--semantic-color-emerald-inverse-border);
}

.sync-status-badge.unlinked {
  background-color: var(--semantic-badge-warning-bg);
  color: var(--semantic-badge-warning-text);
  border: 1px solid var(--semantic-badge-warning-border);
}

.mpr-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  background-color: var(--semantic-color-surface-inverse);
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.toolbar-group {
  display: flex;
  align-items: center;
  gap: 6px;
}

.toolbar-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

.toolbar-select {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 4px 8px;
  font-size: 0.8rem;
  box-sizing: border-box;
}

.toolbar-btn {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  box-sizing: border-box;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease;
}

.toolbar-btn:hover {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

.toolbar-btn.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-accent-inverse);
  color: var(--semantic-color-text-on-primary);
}

.cine-speed-label {
  font-size: 0.75rem;
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.viewports-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 12px;
  padding: 12px;
  flex: 1;
  overflow: auto;
}

.viewports-grid.axial-only,
.viewports-grid.sagittal-only,
.viewports-grid.coronal-only {
  grid-template-columns: 1fr;
}

.viewport-card {
  display: flex;
  flex-direction: column;
  background-color: var(--semantic-color-surface-sunken);
  border: 1px solid var(--semantic-color-text-dark);
  border-radius: 6px;
  overflow: hidden;
}

.viewport-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 6px 10px;
  background-color: var(--semantic-color-surface-inverse);
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.plane-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
  letter-spacing: 0.03em;
}

.axial-badge {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-accent-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
}

.sagittal-badge {
  background-color: var(--semantic-color-emerald-inverse-bg);
  color: var(--semantic-color-emerald-inverse-text);
  border: 1px solid var(--semantic-color-emerald-inverse-border);
}

.coronal-badge {
  background-color: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border: 1px solid var(--semantic-badge-queried-border);
}

.slice-loc-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
}

.canvas-wrapper {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 6px;
  background-color: var(--semantic-color-surface-sunken);
  flex: 1;
}

.mpr-canvas {
  width: 100%;
  max-width: 380px;
  aspect-ratio: 1 / 1;
  background-color: var(--semantic-color-black);
  cursor: crosshair;
  border-radius: 4px;
}

.slice-slider-box {
  padding: 6px 12px;
  background-color: var(--semantic-color-surface-inverse);
  border-top: 1px solid var(--semantic-color-text-dark);
  display: flex;
  align-items: center;
}

.slice-slider {
  width: 100%;
  accent-color: var(--semantic-color-primary);
  cursor: pointer;
}

@media (max-width: 640px) {
  .mpr-toolbar {
    padding: 6px 8px;
    gap: 6px;
  }

  .toolbar-group {
    flex-wrap: wrap;
    width: 100%;
  }

  .toolbar-select,
  .toolbar-btn {
    flex: 1 1 auto;
    min-height: 36px;
  }

  .viewports-grid {
    grid-template-columns: 1fr !important;
    padding: 6px;
    gap: 8px;
  }

  .viewport-card {
    width: 100%;
    box-sizing: border-box;
  }

  .viewport-header {
    padding: 6px 8px;
    flex-wrap: wrap;
    gap: 4px;
  }

  .plane-badge {
    font-size: var(--font-size-2xs);
    white-space: nowrap;
  }

  .canvas-wrapper {
    padding: 4px;
  }

  .mpr-canvas {
    width: 100%;
    max-width: 100%;
    height: auto;
    aspect-ratio: 1 / 1;
  }

  .slice-slider-box {
    padding: 8px;
    min-height: 44px;
  }

  .slice-slider {
    width: 100%;
    height: 28px;
    box-sizing: border-box;
  }
}
</style>
