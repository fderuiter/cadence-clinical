<template>
  <ul
    v-if="blockers.length > 0"
    :class="['lock-blocker-list', listClass]"
    :data-testid="listTestId"
  >
    <li
      v-for="(blocker, idx) in blockers"
      :key="`${blocker.kind}-${blocker.target_ref ?? blocker.finding_ref ?? blocker.query_ref ?? idx}`"
      class="lock-blocker-item"
      :data-blocker-kind="normalizeLockBlockerKind(blocker.kind)"
    >
      <span
        class="clinical-badge lock-blocker-kind-badge"
        :data-testid="
          badgeTestIdPrefix
            ? `${badgeTestIdPrefix}${normalizeLockBlockerKind(blocker.kind)}`
            : undefined
        "
      >
        {{
          formatLockBlockerKindLabel(
            blocker.kind,
            Boolean(blocker.signature_superseded)
          )
        }}
      </span>
      <template v-if="blocker.masked">
        <span class="lock-blocker-masked">
          Masked ({{ blocker.mask ?? "Restricted" }})
        </span>
        <strong
          v-if="formatMaskedBlockerOwner(blocker)"
          class="lock-blocker-owner"
        >
          {{ formatMaskedBlockerOwner(blocker) }}:
        </strong>
        <span v-if="blocker.next_action" class="lock-blocker-action">
          — {{ blocker.next_action }}
        </span>
      </template>
      <template v-else>
        <strong v-if="blocker.owner" class="lock-blocker-owner">
          {{ blocker.owner }}:
        </strong>
        <NodeRefChip
          v-for="chip in getBlockerChips(blocker)"
          :key="chip.key"
          :node-ref="chip.ref"
          :fallback-title="chip.fallback"
          include-kind
        />
        <span
          v-if="formatLockBlockerDetail(blocker)"
          class="lock-blocker-detail"
        >
          {{ formatLockBlockerDetail(blocker) }}
        </span>
        <span v-if="blocker.next_action" class="lock-blocker-action">
          — {{ blocker.next_action }}
        </span>
      </template>
    </li>
  </ul>
</template>

<script setup lang="ts">
import type { KernelLockBlocker } from "../../api/kernelApi";
import {
  formatLockBlockerDetail,
  formatLockBlockerFallbackTitle,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
} from "../../composables/useSignedKernelLock";
import NodeRefChip from "./NodeRefChip.vue";

withDefaults(
  defineProps<{
    blockers: KernelLockBlocker[];
    listTestId?: string;
    listClass?: string;
    badgeTestIdPrefix?: string;
  }>(),
  {
    listTestId: undefined,
    listClass: "",
    badgeTestIdPrefix: undefined,
  }
);

function getBlockerChips(blocker: KernelLockBlocker) {
  const chips: Array<{ key: string; ref: string; fallback?: string }> = [];
  if (blocker.target_ref) {
    chips.push({
      key: `t-${blocker.target_ref}`,
      ref: blocker.target_ref,
      fallback: formatLockBlockerFallbackTitle(blocker),
    });
  }
  if (blocker.query_ref && blocker.query_ref !== blocker.target_ref) {
    chips.push({ key: `q-${blocker.query_ref}`, ref: blocker.query_ref });
  }
  if (blocker.finding_ref && blocker.finding_ref !== blocker.target_ref) {
    chips.push({
      key: `f-${blocker.finding_ref}`,
      ref: blocker.finding_ref,
      fallback: blocker.rule_id ?? undefined,
    });
  }
  return chips;
}
</script>

<style scoped>
.lock-blocker-list {
  margin: 8px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  overflow-wrap: anywhere;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}

.lock-blocker-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.lock-blocker-kind-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 7px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
  font-size: 0.75rem;
  font-weight: 600;
}

.lock-blocker-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.lock-blocker-owner {
  color: var(--semantic-color-text-dark);
}

.lock-blocker-action {
  color: var(--semantic-color-text-body);
}
</style>
