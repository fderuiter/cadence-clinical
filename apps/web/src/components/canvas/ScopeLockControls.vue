<template>
  <div
    v-if="showControls"
    :id="`${prefix}-panel`"
    class="scope-lock-controls"
    :data-testid="`${prefix}-controls`"
  >
    <div
      v-if="canFreeze || canLock || releasableLocks.length > 0"
      class="scope-lock-row"
    >
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

      <button
        v-if="canFreeze"
        :id="`${prefix}-freeze`"
        type="button"
        class="btn btn-secondary"
        :disabled="busy || !reason.trim()"
        @click="openApply('Frozen')"
      >
        Freeze {{ scopeLabel }}
      </button>

      <button
        v-if="canLock"
        :id="`${prefix}-lock`"
        type="button"
        class="btn btn-primary"
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
        class="btn btn-secondary"
        :disabled="busy || !reason.trim()"
        @click="openRelease(held.lockRef, held.kind)"
      >
        Unlock {{ scopeLabel }} ({{ held.kind }})
      </button>
    </div>

    <div
      v-if="showReadinessPanel && readiness"
      :data-testid="`${prefix}-readiness`"
      :class="[
        'scope-lock-readiness',
        readiness.ready_for_lock
          ? 'scope-lock-readiness--ready'
          : 'scope-lock-readiness--blocked',
      ]"
    >
      <span
        class="scope-lock-readiness-summary"
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
      <ul
        v-if="!readiness.ready_for_lock && readiness.blockers.length > 0"
        class="scope-lock-blocker-list"
        :data-testid="`${prefix}-readiness-blockers`"
      >
        <li
          v-for="(blocker, idx) in readiness.blockers"
          :key="`${blocker.kind}-${blocker.target_ref ?? blocker.finding_ref ?? blocker.query_ref ?? idx}`"
          class="scope-lock-blocker-item"
          :data-blocker-kind="normalizeLockBlockerKind(blocker.kind)"
        >
          <span class="scope-lock-blocker-kind">
            {{
              formatLockBlockerKindLabel(
                blocker.kind,
                Boolean(blocker.signature_superseded)
              )
            }}
          </span>
          <template v-if="blocker.masked">
            <span>Masked ({{ blocker.mask ?? "Restricted" }})</span>
            <strong v-if="formatMaskedBlockerOwner(blocker)"
              >{{ formatMaskedBlockerOwner(blocker) }}:</strong
            >
            <span v-if="blocker.next_action">— {{ blocker.next_action }}</span>
          </template>
          <template v-else>
            <strong v-if="blocker.owner">{{ blocker.owner }}:</strong>
            <NodeRefChip
              v-if="blocker.target_ref"
              :node-ref="blocker.target_ref"
              :fallback-title="formatLockBlockerFallbackTitle(blocker)"
              :to="blockerLocation(blocker)"
              include-kind
            />
            <NodeRefChip
              v-if="
                blocker.query_ref && blocker.query_ref !== blocker.target_ref
              "
              :node-ref="blocker.query_ref"
              :to="blockerLocation(blocker)"
              include-kind
            />
            <NodeRefChip
              v-if="
                blocker.finding_ref &&
                blocker.finding_ref !== blocker.target_ref
              "
              :node-ref="blocker.finding_ref"
              :fallback-title="blocker.rule_id ?? undefined"
              :to="blockerLocation(blocker)"
              include-kind
            />
            <span v-if="formatLockBlockerDetail(blocker)">{{
              formatLockBlockerDetail(blocker)
            }}</span>
            <span v-if="blocker.next_action">— {{ blocker.next_action }}</span>
            <component
              :is="routerLinkComponent"
              v-if="
                hasRouter && routerLinkComponent && blockerLocation(blocker)
              "
              class="scope-lock-blocker-link"
              :data-testid="`${prefix}-blocker-open-${idx}`"
              :to="blockerLocation(blocker)!"
            >
              {{ resolveLockBlockerActionLabel(blocker) }}
            </component>
          </template>
        </li>
      </ul>
    </div>

    <p
      v-if="outcome"
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

    <SignatureCaptureModal
      v-if="modalOp"
      v-bind="signatureModalProps"
      @cancel="closeModal"
      @success="closeModal"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import {
  type KernelLock,
  type KernelLockBlocker,
  type KernelLockKind,
} from "../../api/kernelApi";
import type {
  CoveringScopeLockItem,
  DirectScopeLockItem,
} from "../../canvas/kernelLocks";
import {
  resolveLockBlockerActionLabel,
  resolveLockBlockerLocation,
} from "../../canvas/lockBlockerRoutes";
import {
  formatLockBlockerDetail,
  formatLockBlockerFallbackTitle,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
  useSignedKernelLock,
} from "../../composables/useSignedKernelLock";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
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
  }>(),
  {
    studyCode: null,
    directLocks: () => [],
    coveringLocks: () => [],
    idPrefix: "scope-lock",
  }
);

const emit = defineEmits<{
  changed: [lock: KernelLock];
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkComponent = instance?.appContext.components.RouterLink ?? null;
const routeStudyCode = computed(() => {
  const raw = (
    instance?.appContext.config.globalProperties as {
      $route?: { params?: Record<string, unknown> };
    }
  )?.$route?.params?.studyCode;
  return typeof raw === "string" ? raw.trim() : "";
});

const prefix = computed(() => props.idPrefix);
const reason = ref("");

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

const showReadinessPanel = computed(
  () =>
    !hasDirectLocked.value &&
    readiness.value !== null &&
    (canApply.value ||
      (!readiness.value.ready_for_lock && readiness.value.blockers.length > 0))
);

const showControls = computed(
  () =>
    canFreeze.value ||
    canLock.value ||
    releasableLocks.value.length > 0 ||
    showReadinessPanel.value ||
    outcome.value !== null
);

const resolvedStudyCode = computed(
  () =>
    props.studyCode?.trim() ||
    readiness.value?.study_code?.trim() ||
    routeStudyCode.value
);

function blockerLocation(blocker: Partial<KernelLockBlocker>) {
  return resolveLockBlockerLocation(resolvedStudyCode.value, blocker);
}

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
  margin-top: 8px;
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
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
  flex: 1 1 220px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.scope-lock-readiness {
  margin: 8px 0 0;
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.82rem;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-readiness--ready {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.scope-lock-readiness--blocked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.scope-lock-blocker-list {
  margin: 6px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
}

.scope-lock-blocker-list li {
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.scope-lock-readiness-summary {
  font-variant-numeric: tabular-nums;
}

.scope-lock-blocker-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.scope-lock-blocker-kind {
  display: inline-flex;
  align-items: center;
  padding: 1px 6px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 600;
}

.scope-lock-blocker-link {
  color: var(--semantic-color-interactive-primary);
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 2px;
}

@media (pointer: coarse), (max-width: 1024px) {
  .scope-lock-input {
    font-size: 1rem;
  }
}

.scope-lock-outcome {
  margin: 8px 0 0;
  font-size: 0.85rem;
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
