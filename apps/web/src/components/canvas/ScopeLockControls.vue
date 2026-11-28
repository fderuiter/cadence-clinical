<template>
  <div
    v-if="showControls"
    :id="`${prefix}-panel`"
    class="scope-lock-controls"
    :data-testid="`${prefix}-controls`"
  >
    <ClinicalGovernanceBanner
      :id="`${prefix}-banner`"
      v-model="activeSheet"
      :status="bannerStatus"
      :status-label="bannerStatusLabel"
      :status-test-id="`${prefix}-status`"
      :blocker-count="activeBlockerCount"
      :show-blocker-action="activeBlockerCount > 0"
      :blocker-action-id="`${prefix}-review-blockers`"
      :blocker-action-test-id="`${prefix}-review-blockers`"
      :blocker-drawer-title="`${capitalizedScopeLabel} lock blockers`"
      :blocker-drawer-subtitle="scopeRef"
      :show-manage-action="showManageButton"
      :manage-action-label="manageActionLabel"
      :manage-action-id="`${prefix}-manage`"
      :manage-action-test-id="`${prefix}-manage`"
      :manage-drawer-title="`${capitalizedScopeLabel} lock governance`"
      :manage-drawer-subtitle="scopeRef"
      :show-export-action="showExportAction"
      :export-action-label="exportActionLabel"
      :export-action-id="exportActionId"
      :export-action-test-id="exportActionTestId"
      :export-drawer-title="exportDrawerTitle"
      :export-drawer-subtitle="exportDrawerSubtitle"
      :teleport="false"
      :aria-label="`${capitalizedScopeLabel} lock governance`"
    >
      <template #summary>
        <slot name="banner-summary" />
        <span
          v-if="showReadinessSummary && readiness"
          :data-testid="`${prefix}-readiness`"
          class="scope-lock-readiness-inline"
        >
          <span
            class="scope-lock-readiness-summary tabular-nums"
            :data-testid="`${prefix}-readiness-summary`"
          >
            <template v-if="readiness.ready_for_lock">
              Ready for Locked ({{
                readinessGates.map((gate) => gate.label).join(" · ")
              }}).
            </template>
            <template v-else-if="canApply">
              Locked blocked:
              {{ readinessGates.map((gate) => gate.label).join(", ") }}
              (Freeze {{ scopeLabel }} remains available).
            </template>
            <template v-else>
              Closeout blockers on this {{ scopeLabel }}:
              {{ readinessGates.map((gate) => gate.label).join(", ") }}.
            </template>
          </span>
        </span>
        <span
          v-else-if="!hasAnyLock && !readiness && showExportAction"
          class="scope-lock-readiness-summary"
        >
          Unlocked · casebook export ready
        </span>
      </template>

      <template v-if="$slots.export" #export="{ close }">
        <slot name="export" :close="close" />
      </template>

      <template #drawer="{ close }">
        <div class="scope-lock-drawer-body">
          <slot name="held-locks">
            <div
              v-if="directLocks.length > 0 || coveringLocks.length > 0"
              class="scope-lock-held-section"
            >
              <h3 class="scope-lock-drawer-heading">Active locks in force</h3>
              <ul class="scope-lock-held-list tabular-nums">
                <li v-for="held in directLocks" :key="`direct-${held.lockRef}`">
                  <strong>{{ held.kind }}</strong> on {{ scopeLabel }}
                </li>
                <li
                  v-for="(cov, cIdx) in coveringLocks"
                  :key="`cov-${cIdx}-${cov.scope}`"
                >
                  <strong>{{ cov.kind }}</strong> on
                  <NodeRefChip
                    v-if="normalizeLookupNodeRef(cov.scope)"
                    :node-ref="normalizeLookupNodeRef(cov.scope)!"
                    include-kind
                  />
                  <template v-else>{{ cov.scope || "parent scope" }}</template>
                </li>
              </ul>
            </div>
          </slot>

          <div v-if="hasLockMutationActions" class="scope-lock-form-section">
            <label :for="`${prefix}-reason`" class="scope-lock-label">
              Reason for {{ scopeLabel }} lock change
            </label>
            <input
              :id="`${prefix}-reason`"
              v-model="reason"
              type="text"
              class="scope-lock-input"
              autocomplete="off"
              placeholder="Source review complete / addendum reopened"
              :disabled="busy"
            />
            <div class="scope-lock-drawer-actions">
              <button
                v-if="canFreeze"
                :id="`${prefix}-freeze`"
                type="button"
                class="btn btn-secondary scope-lock-action-btn"
                :disabled="busy || !reason.trim()"
                @click="openApply('Frozen')"
              >
                Freeze {{ scopeLabel }}
              </button>

              <button
                v-if="canLock"
                :id="`${prefix}-lock`"
                type="button"
                class="btn btn-primary scope-lock-action-btn"
                :disabled="busy || !reason.trim() || isLockBlockedByReadiness"
                @click="openApply('Locked')"
              >
                Lock {{ scopeLabel }}
              </button>

              <button
                v-for="held in releasableLocks"
                :key="held.lockRef"
                :data-testid="`${prefix}-unlock-${held.kind.toLowerCase()}`"
                type="button"
                class="btn btn-secondary scope-lock-action-btn"
                :disabled="busy || !reason.trim()"
                @click="openRelease(held.lockRef, held.kind)"
              >
                Unlock {{ scopeLabel }} ({{ held.kind }})
              </button>
            </div>
          </div>

          <div
            v-if="showReadinessSummary && readiness"
            :class="[
              'scope-lock-readiness',
              readiness.ready_for_lock
                ? 'scope-lock-readiness--ready'
                : 'scope-lock-readiness--blocked',
            ]"
          >
            <p class="scope-lock-drawer-readiness-text tabular-nums">
              <template v-if="readiness.ready_for_lock">
                Ready for Locked ({{
                  readinessGates.map((gate) => gate.label).join(" · ")
                }}).
              </template>
              <template v-else-if="canApply">
                Locked blocked:
                {{ readinessGates.map((gate) => gate.label).join(", ") }}
                (Freeze {{ scopeLabel }} remains available).
              </template>
              <template v-else>
                Closeout blockers on this {{ scopeLabel }}:
                {{ readinessGates.map((gate) => gate.label).join(", ") }}.
              </template>
            </p>
            <LockBlockerList
              v-if="!readiness.ready_for_lock && readiness.blockers.length > 0"
              :blockers="readiness.blockers"
              :study-code="resolvedStudyCode"
              list-class="scope-lock-blocker-list"
              :list-test-id="`${prefix}-readiness-blockers`"
              :open-link-test-id-prefix="`${prefix}-blocker-open-`"
              @navigate="close"
            />
          </div>
        </div>
      </template>

      <template v-if="outcome" #outcome>
        <p
          :data-testid="`${prefix}-outcome`"
          :class="[
            'scope-lock-outcome',
            outcome.kind === 'ok'
              ? 'scope-lock-outcome--ok'
              : 'scope-lock-outcome--error',
          ]"
          :role="outcome.kind === 'ok' ? 'status' : 'alert'"
          aria-live="polite"
        >
          {{ outcome.message }}
        </p>
      </template>
    </ClinicalGovernanceBanner>

    <SignatureCaptureModal
      v-if="modalOp"
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, useSlots, watch } from "vue";
import { ClinicalGovernanceBanner } from "ui";
import { type KernelLock, type KernelLockKind } from "../../api/kernelApi";
import type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../../canvas/kernelLocks";
import { normalizeLookupNodeRef } from "../../canvas/nodeResolver";
import { useSignedKernelLock } from "../../composables/useSignedKernelLock";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import LockBlockerList from "./LockBlockerList.vue";
import NodeRefChip from "./NodeRefChip.vue";

export type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../../canvas/kernelLocks";

const props = withDefaults(
  defineProps<{
    studyCode?: string | null;
    studyRef: string | null;
    scopeRef: string;
    scopeLabel: string;
    directLocks?: DirectScopeLockItem[];
    coveringLocks?: CoveringScopeLockItem[];
    idPrefix?: string;
    forceVisible?: boolean;
    showExportAction?: boolean;
    exportActionLabel?: string;
    exportActionId?: string;
    exportActionTestId?: string;
    exportDrawerTitle?: string;
    exportDrawerSubtitle?: string;
  }>(),
  {
    studyCode: null,
    directLocks: () => [],
    coveringLocks: () => [],
    idPrefix: "scope-lock",
    forceVisible: false,
    showExportAction: false,
    exportActionLabel: "Export casebook",
    exportActionId: undefined,
    exportActionTestId: undefined,
    exportDrawerTitle: "Export casebook",
    exportDrawerSubtitle: "",
  }
);

const emit = defineEmits<{
  changed: [lock: KernelLock];
}>();

const instance = getCurrentInstance();
const routeStudyCode = computed(() => {
  const raw = (
    instance?.appContext.config.globalProperties as {
      $route?: { params?: Record<string, unknown> };
    }
  )?.$route?.params?.studyCode;
  return typeof raw === "string" ? raw.trim() : "";
});

const slots = useSlots();
const prefix = computed(() => props.idPrefix);
const reason = ref("");
const activeSheet = ref<string | null>(null);

const capitalizedScopeLabel = computed(() => {
  if (!props.scopeLabel) return "Scope";
  return props.scopeLabel.charAt(0).toUpperCase() + props.scopeLabel.slice(1);
});

function handleLockChanged(updated: KernelLock) {
  reason.value = "";
  emit("changed", updated);
}

const {
  canApply,
  canRelease,
  readiness,
  readinessGates,
  isLockBlockedByReadiness,
  busy,
  outcome,
  modalOp,
  signatureModalProps,
  reloadReadiness,
  openApply: openSignedApply,
  openRelease: openSignedRelease,
  closeModal,
} = useSignedKernelLock({
  studyRef: () => props.studyRef,
  scopeRef: () => props.scopeRef,
  onApplied: handleLockChanged,
  onReleased: handleLockChanged,
});

const hasDirectLocked = computed(() =>
  props.directLocks.some((l) => l.kind === "Locked")
);
const hasDirectFrozenOrLocked = computed(() =>
  props.directLocks.some((l) => l.kind === "Frozen" || l.kind === "Locked")
);
const coveredByLocked = computed(() =>
  props.coveringLocks.some((l) => l.kind === "Locked")
);
const coveredByFrozenOrLocked = computed(() =>
  props.coveringLocks.some((l) => l.kind === "Frozen" || l.kind === "Locked")
);
const hasAnyLocked = computed(
  () => hasDirectLocked.value || coveredByLocked.value
);
const hasAnyLock = computed(
  () => hasDirectFrozenOrLocked.value || coveredByFrozenOrLocked.value
);

const canFreeze = computed(
  () =>
    canApply.value &&
    !hasDirectFrozenOrLocked.value &&
    !coveredByFrozenOrLocked.value
);
const canLock = computed(
  () => canApply.value && !hasDirectLocked.value && !coveredByLocked.value
);
const releasableLocks = computed(() =>
  canRelease.value ? props.directLocks : []
);

const hasLockMutationActions = computed(
  () => canFreeze.value || canLock.value || releasableLocks.value.length > 0
);

const showReadinessSummary = computed(
  () =>
    !hasDirectLocked.value &&
    readiness.value !== null &&
    (canApply.value ||
      (!readiness.value.ready_for_lock && readiness.value.blockers.length > 0))
);

const activeBlockerCount = computed(() => {
  if (hasDirectLocked.value || !readiness.value) return 0;
  if (readiness.value.ready_for_lock) return 0;
  return readiness.value.blockers.length;
});

const bannerStatus = computed(() => {
  if (hasAnyLocked.value) return "locked";
  if (hasAnyLock.value) return "frozen";
  if (showReadinessSummary.value && readiness.value) {
    return readiness.value.ready_for_lock ? "ready" : "blocked";
  }
  return "unlocked";
});

const bannerStatusLabel = computed(() => {
  if (hasAnyLocked.value) return "Locked";
  if (hasAnyLock.value) return "Frozen";
  if (showReadinessSummary.value && readiness.value) {
    return readiness.value.ready_for_lock ? "Ready" : "Blocked";
  }
  return "Unlocked";
});

const showManageButton = computed(() => {
  if (hasLockMutationActions.value) return true;
  return (
    activeBlockerCount.value === 0 &&
    (hasAnyLock.value || Boolean(slots["held-locks"]))
  );
});

const manageActionLabel = computed(() => {
  if (hasLockMutationActions.value) {
    if (
      !canFreeze.value &&
      !canLock.value &&
      releasableLocks.value.length > 0
    ) {
      return `Unlock ${props.scopeLabel}`;
    }
    return "Manage lock";
  }
  return "Inspect lock";
});

const showControls = computed(
  () =>
    props.forceVisible ||
    props.showExportAction ||
    hasAnyLock.value ||
    hasLockMutationActions.value ||
    showReadinessSummary.value ||
    outcome.value !== null
);

const resolvedStudyCode = computed(
  () =>
    props.studyCode?.trim() ||
    readiness.value?.study_code?.trim() ||
    routeStudyCode.value
);

watch(
  () => props.directLocks,
  () => {
    void reloadReadiness();
  }
);

function openApply(lockKind: KernelLockKind) {
  openSignedApply({
    scopeRef: props.scopeRef,
    kind: lockKind,
    reason: reason.value,
    targetLabel: () => props.scopeLabel,
  });
}

function openRelease(lockRef: string, lockKind: string) {
  openSignedRelease({
    lockRef,
    lockKind,
    reason: reason.value,
    targetLabel: () => props.scopeLabel,
  });
}
</script>

<style scoped>
.scope-lock-controls {
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-readiness-inline {
  min-width: 0;
  max-width: 100%;
}

.scope-lock-readiness-summary {
  font-variant-numeric: tabular-nums;
}

.scope-lock-drawer-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  max-width: 100%;
}

.scope-lock-drawer-heading {
  margin: 0 0 6px;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.scope-lock-held-list {
  margin: 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.85rem;
}

.scope-lock-form-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.scope-lock-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.scope-lock-input {
  width: 100%;
  min-width: 0;
  max-width: 100%;
  min-height: 44px;
  box-sizing: border-box;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.875rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.scope-lock-drawer-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.scope-lock-action-btn {
  min-height: 44px;
}

.scope-lock-readiness {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.84rem;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-drawer-readiness-text {
  margin: 0;
}

.scope-lock-readiness--ready {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.scope-lock-readiness--blocked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

@media (pointer: coarse), (max-width: 1024px) {
  .scope-lock-input {
    font-size: 1rem;
  }
}

.scope-lock-outcome {
  margin: 0;
  font-size: 0.84rem;
  padding: 6px 10px;
  border-radius: 4px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-outcome--ok {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.scope-lock-outcome--error {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>
