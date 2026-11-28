<script setup>
import {
  computed,
  onBeforeUnmount,
  onMounted,
  ref,
  useId,
  useSlots,
  watch,
} from "vue";
import {
  AlertTriangle,
  CheckCircle2,
  FileDown,
  Loader2,
  Lock,
  ShieldCheck,
  SlidersHorizontal,
  Snowflake,
} from "lucide-vue-next";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import { clinicalDrawerHostProps } from "./sharedProps.js";

const props = defineProps({
  id: {
    type: String,
    default: undefined,
  },
  status: {
    type: String,
    default: "unlocked",
    validator: (val) =>
      ["unlocked", "frozen", "locked", "ready", "blocked", "loading"].includes(
        val
      ),
  },
  statusLabel: {
    type: String,
    default: "",
  },
  statusTestId: {
    type: String,
    default: undefined,
  },
  summary: {
    type: String,
    default: "",
  },
  summaryTestId: {
    type: String,
    default: undefined,
  },
  gates: {
    type: Array,
    default: () => [],
  },
  gatesTestId: {
    type: String,
    default: undefined,
  },
  blockerCount: {
    type: Number,
    default: 0,
  },
  showBlockerAction: {
    type: Boolean,
    default: undefined,
  },
  blockerActionLabel: {
    type: String,
    default: "",
  },
  blockerActionId: {
    type: String,
    default: undefined,
  },
  blockerActionTestId: {
    type: String,
    default: undefined,
  },
  blockerDrawerTitle: {
    type: String,
    default: "Lock readiness blockers",
  },
  blockerDrawerSubtitle: {
    type: String,
    default: "",
  },
  showManageAction: {
    type: Boolean,
    default: false,
  },
  manageActionLabel: {
    type: String,
    default: "Manage lock",
  },
  manageActionId: {
    type: String,
    default: undefined,
  },
  manageActionTestId: {
    type: String,
    default: undefined,
  },
  manageDrawerTitle: {
    type: String,
    default: "Lock governance",
  },
  manageDrawerSubtitle: {
    type: String,
    default: "",
  },
  showExportAction: {
    type: Boolean,
    default: false,
  },
  exportActionLabel: {
    type: String,
    default: "Export casebook",
  },
  exportActionId: {
    type: String,
    default: undefined,
  },
  exportActionTestId: {
    type: String,
    default: undefined,
  },
  exportDrawerTitle: {
    type: String,
    default: "Export casebook",
  },
  exportDrawerSubtitle: {
    type: String,
    default: "",
  },
  modelValue: {
    type: String,
    default: undefined,
  },
  activeSheet: {
    type: String,
    default: undefined,
  },
  ...clinicalDrawerHostProps,
  drawerSize: {
    type: String,
    default: "md",
  },
  drawerDismissible: {
    type: Boolean,
    default: true,
  },
  closeLabel: {
    type: String,
    default: "Close governance drawer",
  },
  teleport: {
    type: [Boolean, String],
    default: "body",
  },
  ariaLabel: {
    type: String,
    default: "Governance status",
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:activeSheet",
  "open-sheet",
  "close",
]);

const slots = useSlots();
const instanceId = useId();
const internalSheet = ref(null);
const blockerTriggerRef = ref(null);
const manageTriggerRef = ref(null);
const exportTriggerRef = ref(null);
const customTriggerEl = ref(null);
const lastOpenedSheet = ref(null);

const currentSheet = computed(() => {
  if (props.modelValue !== undefined) return props.modelValue || null;
  if (props.activeSheet !== undefined) return props.activeSheet || null;
  return internalSheet.value;
});

const isDrawerOpen = computed(() => Boolean(currentSheet.value));

const computedDrawerId = computed(
  () => props.drawerId || `clinical-governance-drawer-${instanceId}`
);

const statusIconComponent = computed(() => {
  switch (props.status) {
    case "locked":
      return Lock;
    case "frozen":
      return Snowflake;
    case "ready":
      return CheckCircle2;
    case "blocked":
      return AlertTriangle;
    case "loading":
      return Loader2;
    default:
      return ShieldCheck;
  }
});

const badgeVariantClass = computed(() => {
  switch (props.status) {
    case "locked":
      return "governance-badge--locked";
    case "frozen":
      return "governance-badge--frozen";
    case "ready":
      return "governance-badge--ready";
    case "blocked":
      return "governance-badge--blocked";
    default:
      return "governance-badge--neutral";
  }
});

const resolvedStatusLabel = computed(() => {
  if (props.statusLabel) return props.statusLabel;
  switch (props.status) {
    case "locked":
      return "Locked";
    case "frozen":
      return "Frozen";
    case "ready":
      return "Ready for Locked";
    case "blocked":
      return "Blocked for Locked";
    case "loading":
      return "Checking readiness…";
    default:
      return "Unlocked";
  }
});

const shouldShowBlockerAction = computed(() => {
  if (props.showBlockerAction !== undefined) return props.showBlockerAction;
  return props.blockerCount > 0;
});

const resolvedBlockerActionLabel = computed(() => {
  if (props.blockerActionLabel) return props.blockerActionLabel;
  if (props.blockerCount > 0) {
    return `Review blockers (${props.blockerCount})`;
  }
  return "Review blockers";
});

const resolvedDrawerTitle = computed(() => {
  if (props.drawerTitle) return props.drawerTitle;
  if (currentSheet.value === "export") return props.exportDrawerTitle;
  if (currentSheet.value === "locks") return props.manageDrawerTitle;
  if (currentSheet.value === "blockers") return props.blockerDrawerTitle;
  return props.blockerDrawerTitle || "Governance details";
});

const resolvedDrawerSubtitle = computed(() => {
  if (props.drawerSubtitle) return props.drawerSubtitle;
  if (currentSheet.value === "export") return props.exportDrawerSubtitle;
  if (currentSheet.value === "locks") return props.manageDrawerSubtitle;
  if (currentSheet.value === "blockers") return props.blockerDrawerSubtitle;
  return "";
});

watch(
  currentSheet,
  (val) => {
    if (val && val !== lastOpenedSheet.value) {
      customTriggerEl.value = null;
      lastOpenedSheet.value = val;
    }
  },
  { flush: "sync", immediate: true }
);

function resolveExplicitRestoreTarget() {
  return customTriggerEl.value || null;
}

function resolveRestoreTarget() {
  if (customTriggerEl.value) return customTriggerEl.value;
  if (lastOpenedSheet.value === "export" && exportTriggerRef.value) {
    return exportTriggerRef.value;
  }
  if (lastOpenedSheet.value === "locks" && manageTriggerRef.value) {
    return manageTriggerRef.value;
  }
  if (lastOpenedSheet.value === "blockers" && blockerTriggerRef.value) {
    return blockerTriggerRef.value;
  }
  return (
    blockerTriggerRef.value ||
    manageTriggerRef.value ||
    exportTriggerRef.value ||
    null
  );
}

function setSheet(nextSheet, triggerEventOrEl) {
  const normalized = nextSheet || null;
  if (normalized) {
    lastOpenedSheet.value = normalized;
    const candidateEl =
      triggerEventOrEl && typeof triggerEventOrEl === "object"
        ? triggerEventOrEl.currentTarget || triggerEventOrEl
        : null;
    if (candidateEl && typeof candidateEl.focus === "function") {
      customTriggerEl.value = candidateEl;
    } else {
      customTriggerEl.value = null;
    }
  }
  internalSheet.value = normalized;
  emit("update:modelValue", normalized);
  emit("update:activeSheet", normalized);
  if (normalized) {
    emit("open-sheet", normalized);
  } else {
    emit("close");
  }
}

function openBlockers(event) {
  setSheet("blockers", blockerTriggerRef.value || event);
}

function openLocks(event) {
  setSheet("locks", manageTriggerRef.value || event);
}

function openExport(event) {
  setSheet("export", exportTriggerRef.value || event);
}

function closeDrawer() {
  if (!props.drawerDismissible) return;
  setSheet(null);
}

onMounted(() => {
  if (typeof window !== "undefined") {
    window.addEventListener("cadence:close-governance-drawers", closeDrawer);
  }
});

onBeforeUnmount(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("cadence:close-governance-drawers", closeDrawer);
  }
});

const hasSummaryContent = computed(
  () =>
    Boolean(props.summary) ||
    props.gates.length > 0 ||
    Boolean(slots.summary) ||
    Boolean(slots.gates)
);

defineExpose({
  openSheet: setSheet,
  openBlockers,
  openLocks,
  openExport,
  closeDrawer,
  currentSheet,
});
</script>

<template>
  <div
    :id="id"
    :class="['clinical-governance-banner-root', `status-${status}`]"
    role="region"
    :aria-label="ariaLabel"
  >
    <div class="clinical-governance-banner">
      <div class="clinical-governance-banner__lead">
        <slot
          name="status"
          :status="status"
          :status-label="resolvedStatusLabel"
        >
          <span
            :data-testid="statusTestId"
            :class="[
              'clinical-badge',
              'clinical-governance-banner__badge',
              badgeVariantClass,
            ]"
          >
            <component
              :is="statusIconComponent"
              :size="16"
              :stroke-width="1.75"
              aria-hidden="true"
              focusable="false"
              :class="[
                'clinical-governance-banner__icon',
                { 'is-spinning': status === 'loading' },
              ]"
            />
            <span>{{ resolvedStatusLabel }}</span>
          </span>
        </slot>

        <div
          v-if="hasSummaryContent"
          class="clinical-governance-banner__summary-wrap"
        >
          <slot name="summary">
            <span
              v-if="summary"
              :data-testid="summaryTestId"
              class="clinical-governance-banner__summary tabular-nums"
            >
              {{ summary }}
            </span>
          </slot>

          <slot name="gates" :gates="gates">
            <span
              v-if="gates.length > 0"
              :data-testid="gatesTestId"
              class="clinical-governance-banner__gates tabular-nums"
            >
              <span
                v-for="(gate, idx) in gates"
                :key="gate.key ?? idx"
                :data-testid="gate.testId"
                :class="[
                  'clinical-governance-banner__gate',
                  Number(gate.count) > 0
                    ? 'clinical-governance-banner__gate--active'
                    : 'clinical-governance-banner__gate--clear',
                ]"
              >
                <template v-if="idx > 0"> · </template>{{ gate.label }}
              </span>
            </span>
          </slot>
        </div>
      </div>

      <div class="clinical-governance-banner__actions">
        <button
          v-if="shouldShowBlockerAction"
          :id="blockerActionId"
          ref="blockerTriggerRef"
          :data-testid="blockerActionTestId"
          type="button"
          class="clinical-governance-banner__btn clinical-governance-banner__btn--blockers tabular-nums"
          aria-haspopup="dialog"
          :aria-expanded="currentSheet === 'blockers' ? 'true' : 'false'"
          :aria-controls="computedDrawerId"
          @click="openBlockers"
        >
          <AlertTriangle
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
            focusable="false"
          />
          <span>{{ resolvedBlockerActionLabel }}</span>
        </button>

        <button
          v-if="showManageAction"
          :id="manageActionId"
          ref="manageTriggerRef"
          :data-testid="manageActionTestId"
          type="button"
          class="clinical-governance-banner__btn tabular-nums"
          aria-haspopup="dialog"
          :aria-expanded="currentSheet === 'locks' ? 'true' : 'false'"
          :aria-controls="computedDrawerId"
          @click="openLocks"
        >
          <SlidersHorizontal
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
            focusable="false"
          />
          <span>{{ manageActionLabel }}</span>
        </button>

        <button
          v-if="showExportAction"
          :id="exportActionId"
          ref="exportTriggerRef"
          :data-testid="exportActionTestId"
          type="button"
          class="clinical-governance-banner__btn tabular-nums"
          aria-haspopup="dialog"
          :aria-expanded="currentSheet === 'export' ? 'true' : 'false'"
          :aria-controls="computedDrawerId"
          @click="openExport"
        >
          <FileDown
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
            focusable="false"
          />
          <span>{{ exportActionLabel }}</span>
        </button>

        <slot
          name="actions"
          :active-sheet="currentSheet"
          :open-sheet="setSheet"
          :open-blockers="openBlockers"
          :open-locks="openLocks"
          :open-export="openExport"
          :close-drawer="closeDrawer"
        />
      </div>
    </div>

    <slot name="outcome" />

    <ClinicalDrawer
      :id="computedDrawerId"
      :overlay-id="drawerOverlayId"
      :is-open="isDrawerOpen"
      :title="resolvedDrawerTitle"
      :subtitle="resolvedDrawerSubtitle"
      :size="drawerSize"
      :dismissible="drawerDismissible"
      :close-label="closeLabel"
      :teleport="teleport"
      :mobile-bottom-sheet="mobileBottomSheet"
      :restore-focus="resolveExplicitRestoreTarget"
      :fallback-focus="resolveRestoreTarget"
      @close="closeDrawer"
    >
      <slot
        v-if="currentSheet === 'export' && $slots.export"
        name="export"
        :close="closeDrawer"
      />
      <slot
        v-else-if="currentSheet === 'locks' && $slots.locks"
        name="locks"
        :close="closeDrawer"
      />
      <slot
        v-else-if="currentSheet === 'blockers' && $slots.blockers"
        name="blockers"
        :close="closeDrawer"
      />
      <slot
        v-else
        name="drawer"
        :active-sheet="currentSheet"
        :close="closeDrawer"
      />
    </ClinicalDrawer>
  </div>
</template>

<style scoped>
.clinical-governance-banner-root {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  font-variant-numeric: tabular-nums;
}

.clinical-governance-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 6px 10px;
  min-height: 48px;
  padding: 2px 10px;
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm, 6px);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.status-blocked .clinical-governance-banner {
  border-color: var(--semantic-color-error-border);
}

.status-ready .clinical-governance-banner {
  border-color: var(--semantic-color-success-border);
}

.status-frozen .clinical-governance-banner {
  border-color: var(--semantic-color-info-border);
}

.clinical-governance-banner__lead {
  display: flex;
  flex: 1 1 220px;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 8px;
  min-width: 0;
  max-width: 100%;
}

.clinical-governance-banner__badge {
  display: inline-flex;
  flex-shrink: 1;
  min-width: 0;
  max-width: 100%;
  align-items: center;
  gap: 5px;
  padding: 3px 8px;
  border-radius: var(--radius-xs, 4px);
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1.25;
  white-space: normal;
  overflow-wrap: anywhere;
  border: 1px solid var(--semantic-badge-draft-border);
  background: var(--semantic-badge-draft-bg);
  color: var(--semantic-badge-draft-text);
  font-variant-numeric: tabular-nums;
}

.governance-badge--locked {
  border-color: var(--semantic-badge-locked-border);
  background: var(--semantic-badge-locked-bg);
  color: var(--semantic-badge-locked-text);
}

.governance-badge--frozen {
  border-color: var(--semantic-badge-frozen-border);
  background: var(--semantic-badge-frozen-bg);
  color: var(--semantic-badge-frozen-text);
}

.governance-badge--ready {
  border-color: var(--semantic-badge-verified-border);
  background: var(--semantic-badge-verified-bg);
  color: var(--semantic-badge-verified-text);
}

.governance-badge--blocked {
  border-color: var(--semantic-badge-error-border);
  background: var(--semantic-badge-error-bg);
  color: var(--semantic-badge-error-text);
}

.clinical-governance-banner__icon {
  flex-shrink: 0;
}

.clinical-governance-banner__summary-wrap {
  display: flex;
  flex: 1 1 160px;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px 8px;
  min-width: 0;
  max-width: 100%;
  font-size: 0.8125rem;
  line-height: 1.3;
  color: var(--semantic-color-text);
  overflow-wrap: anywhere;
}

.clinical-governance-banner__summary-wrap > :deep(*) {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.clinical-governance-banner__summary,
.clinical-governance-banner__gates {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
  font-variant-numeric: tabular-nums;
}

.clinical-governance-banner__gate--active {
  font-weight: 600;
  color: var(--semantic-color-text);
}

.clinical-governance-banner__gate--clear {
  color: var(--semantic-color-text-muted);
}

.clinical-governance-banner__actions {
  display: flex;
  flex: 0 1 auto;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
}

.clinical-governance-banner__actions > :deep(*) {
  min-width: 0;
  max-width: 100%;
}

.clinical-governance-banner__btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 5px;
  min-height: 44px;
  min-width: min(44px, 100%);
  max-width: 100%;
  padding: 4px 10px;
  border-radius: var(--radius-xs, 4px);
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: inherit;
  font-size: 0.8125rem;
  font-weight: 600;
  line-height: 1.2;
  white-space: normal;
  overflow-wrap: anywhere;
  cursor: pointer;
  box-sizing: border-box;
  font-variant-numeric: tabular-nums;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.clinical-governance-banner__btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-muted);
}

.clinical-governance-banner__btn--blockers {
  border-color: var(--semantic-color-error-border);
}

@media (min-width: 48rem) {
  .clinical-governance-banner {
    flex-wrap: nowrap;
    min-height: 48px;
    padding: 2px 10px;
  }

  .clinical-governance-banner__lead {
    flex: 1 1 0%;
    flex-wrap: nowrap;
    overflow: hidden;
  }

  .clinical-governance-banner__badge {
    flex-shrink: 0;
    white-space: nowrap;
  }

  .clinical-governance-banner__summary-wrap {
    flex: 1 1 0%;
    flex-wrap: nowrap;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
  }

  .clinical-governance-banner__summary-wrap > :deep(*),
  .clinical-governance-banner__summary,
  .clinical-governance-banner__gates {
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
  }

  .clinical-governance-banner__actions {
    flex-shrink: 0;
    flex-wrap: nowrap;
  }

  .clinical-governance-banner__btn {
    min-width: 44px;
    white-space: nowrap;
  }
}

@media (max-width: 480px) {
  .clinical-governance-banner {
    padding: 6px 8px;
  }

  .clinical-governance-banner__actions {
    width: 100%;
  }

  .clinical-governance-banner__btn {
    flex: 1 1 auto;
  }
}

@keyframes governance-spin {
  to {
    transform: rotate(360deg);
  }
}

.is-spinning {
  animation: governance-spin 1s linear infinite;
}

@media (prefers-reduced-motion: reduce) {
  .clinical-governance-banner__btn,
  .is-spinning {
    animation: none;
    transition: none;
  }
}
</style>
