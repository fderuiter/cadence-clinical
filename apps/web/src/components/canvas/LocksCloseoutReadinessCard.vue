<template>
  <div v-if="readiness" class="readiness-drawer-content">
    <div
      v-if="readiness.blockers.length > 0"
      class="readiness-role-filters"
      data-testid="study-locks-blocker-filters"
      role="group"
      aria-label="Filter closeout blockers by role"
    >
      <button
        v-for="opt in LOCK_BLOCKER_ROLE_FILTERS"
        :key="opt.id"
        type="button"
        class="readiness-filter-pill tabular-nums"
        :class="{
          'readiness-filter-pill--active': roleFilter === opt.id,
        }"
        :data-testid="`blocker-role-filter-${opt.id}`"
        :aria-pressed="roleFilter === opt.id"
        @click="roleFilter = opt.id"
      >
        {{ opt.label }} ({{
          countLockBlockersForRoleFilter(readiness.blockers, opt.id)
        }})
      </button>
    </div>

    <p
      v-if="readiness.blockers.length > 0 && filteredBlockers.length === 0"
      class="status-line readiness-drawer-empty"
      data-testid="study-locks-no-filtered-blockers"
      role="status"
    >
      No blockers match this role filter.
    </p>

    <ul
      v-else-if="filteredBlockers.length > 0"
      class="readiness-blockers"
      data-testid="study-locks-readiness-blockers"
    >
      <li
        v-for="(blocker, bIdx) in filteredBlockers"
        :key="`${blocker.kind}-${blocker.target_ref ?? blocker.finding_ref ?? blocker.query_ref ?? bIdx}`"
        class="readiness-blocker-item"
        :data-blocker-kind="normalizeLockBlockerKind(blocker.kind)"
      >
        <div class="readiness-blocker-main">
          <div class="readiness-blocker-headline">
            <span
              class="clinical-badge blocker-kind-badge"
              :class="blockerBadgeModifier(blocker.kind)"
              :data-testid="`blocker-kind-${normalizeLockBlockerKind(blocker.kind)}`"
            >
              {{
                formatLockBlockerKindLabel(
                  blocker.kind,
                  Boolean(blocker.signature_superseded)
                )
              }}
            </span>
            <template v-if="blocker.masked">
              <span class="masked-pill">
                Masked ({{ blocker.mask ?? "Restricted" }})
              </span>
              <strong
                v-if="formatMaskedBlockerOwner(blocker)"
                class="blocker-owner"
              >
                {{ formatMaskedBlockerOwner(blocker) }}:
              </strong>
              <span v-if="blocker.next_action" class="blocker-action">
                {{ blocker.next_action }}
              </span>
            </template>
            <template v-else>
              <strong v-if="blocker.owner" class="blocker-owner">
                {{ blocker.owner }}:
              </strong>
              <span v-if="blocker.subject_code" class="blocker-subject-pill">
                {{ blocker.subject_code }}
              </span>
              <NodeRefChip
                v-for="chip in getLockBlockerChips(blocker)"
                :key="chip.key"
                :node-ref="chip.ref"
                :fallback-title="chip.fallback"
                :to="blockerLocation(blocker)"
                include-kind
              />
              <span
                v-if="
                  blocker.collection_title &&
                  blocker.target_kind === 'observation'
                "
                class="blocker-collection"
              >
                {{ blocker.collection_title }}
              </span>
            </template>
          </div>
          <p
            v-if="
              !blocker.masked &&
              (formatLockBlockerDetail(blocker) || blocker.next_action)
            "
            class="readiness-blocker-subline tabular-nums"
          >
            <span
              v-if="formatLockBlockerDetail(blocker)"
              class="blocker-detail"
            >
              {{ formatLockBlockerDetail(blocker) }}
            </span>
            <span v-if="blocker.next_action" class="blocker-action">
              <template v-if="formatLockBlockerDetail(blocker)"> — </template
              >{{ blocker.next_action }}
            </span>
          </p>
        </div>
        <div
          v-if="
            !blocker.masked &&
            hasRouter &&
            routerLinkComponent &&
            blockerLocation(blocker)
          "
          class="readiness-blocker-cta"
        >
          <component
            :is="routerLinkComponent"
            class="btn btn-secondary blocker-workspace-link"
            :data-testid="`blocker-open-${bIdx}`"
            :to="blockerLocation(blocker)!"
            @click="onWorkspaceNavigate"
          >
            {{ resolveLockBlockerActionLabel(blocker) }}
          </component>
        </div>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import type { KernelLockReadiness } from "../../api/kernelApi";
import {
  countLockBlockersForRoleFilter,
  getLockBlockerChips,
  LOCK_BLOCKER_ROLE_FILTERS,
  matchesLockBlockerRoleFilter,
  resolveLockBlockerActionLabel,
  resolveLockBlockerLocation,
  type LockBlockerRoleFilter,
} from "../../canvas/lockBlockerRoutes";
import {
  formatLockBlockerDetail,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
} from "../../composables/useSignedKernelLock";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  studyCode: string;
  applyScopeRef: string;
  readiness: KernelLockReadiness | null;
}>();

const emit = defineEmits<{
  (e: "navigate"): void;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkComponent = instance?.appContext.components.RouterLink ?? null;

const roleFilter = ref<LockBlockerRoleFilter>("all");

watch(
  () => props.applyScopeRef,
  () => {
    roleFilter.value = "all";
  }
);

function onWorkspaceNavigate() {
  void Promise.resolve().then(() => {
    emit("navigate");
  });
}

const filteredBlockers = computed(() => {
  const all = props.readiness?.blockers ?? [];
  if (roleFilter.value === "all") return all;
  return all.filter((blocker) =>
    matchesLockBlockerRoleFilter(blocker, roleFilter.value)
  );
});

function blockerLocation(
  blocker: NonNullable<KernelLockReadiness["blockers"]>[number]
) {
  return resolveLockBlockerLocation(
    props.studyCode || props.readiness?.study_code,
    blocker
  );
}

function blockerBadgeModifier(kind: string | null | undefined): string {
  switch (normalizeLockBlockerKind(kind)) {
    case "OpenQuery":
      return "clinical-badge--queried";
    case "UnverifiedValue":
      return "clinical-badge--regulatory";
    case "UnreviewedValue":
      return "clinical-badge--in-progress";
    case "UnsignedForm":
      return "clinical-badge--signed";
    case "ErrorFinding":
      return "clinical-badge--error";
    default:
      return "clinical-badge--warning";
  }
}
</script>

<style scoped src="./locksSection.css"></style>
<style scoped src="./canvasBanners.css"></style>
