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
          v-for="chip in getLockBlockerChips(blocker)"
          :key="chip.key"
          :node-ref="chip.ref"
          :fallback-title="chip.fallback"
          :to="blockerLocation(blocker)"
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
        <component
          :is="routerLinkComponent"
          v-if="
            openLinkTestIdPrefix &&
            hasRouter &&
            routerLinkComponent &&
            blockerLocation(blocker)
          "
          class="lock-blocker-workspace-link"
          :data-testid="`${openLinkTestIdPrefix}${idx}`"
          :to="blockerLocation(blocker)!"
          @click="onWorkspaceNavigate"
        >
          {{ resolveLockBlockerActionLabel(blocker) }}
        </component>
      </template>
    </li>
  </ul>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance } from "vue";
import type { KernelLockBlocker } from "../../api/kernelApi";
import {
  getLockBlockerChips,
  resolveLockBlockerActionLabel,
  resolveLockBlockerLocation,
} from "../../canvas/lockBlockerRoutes";
import {
  formatLockBlockerDetail,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
} from "../../composables/useSignedKernelLock";
import NodeRefChip from "./NodeRefChip.vue";

const props = withDefaults(
  defineProps<{
    blockers: KernelLockBlocker[];
    studyCode?: string | null;
    listTestId?: string;
    listClass?: string;
    badgeTestIdPrefix?: string;
    openLinkTestIdPrefix?: string;
  }>(),
  {
    studyCode: null,
    listTestId: undefined,
    listClass: "",
    badgeTestIdPrefix: undefined,
    openLinkTestIdPrefix: undefined,
  }
);

const emit = defineEmits<{
  (e: "navigate"): void;
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

function blockerLocation(blocker: KernelLockBlocker) {
  return resolveLockBlockerLocation(
    props.studyCode?.trim() || routeStudyCode.value,
    blocker
  );
}

function onWorkspaceNavigate() {
  void Promise.resolve().then(() => {
    emit("navigate");
  });
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

.lock-blocker-workspace-link {
  color: inherit;
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 2px;
  overflow-wrap: anywhere;
}
</style>
