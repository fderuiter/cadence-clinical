<template>
  <section
    v-if="readinessLoading || readiness"
    class="readiness-card"
    :class="{
      'readiness-panel--ready': readiness?.ready_for_lock,
      'readiness-panel--blocked': readiness && !readiness.ready_for_lock,
    }"
    data-testid="study-locks-readiness"
    aria-labelledby="study-closeout-readiness-heading"
  >
    <div class="readiness-card-header">
      <div>
        <h3 id="study-closeout-readiness-heading">
          Closeout readiness (visible to all study roles)
        </h3>
        <p class="readiness-card-subtitle">
          Role-scoped gating assertions required prior to irreversible database
          lock
        </p>
      </div>

      <div
        v-if="readiness && readiness.blockers.length > 0"
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
    </div>

    <p
      v-if="readinessLoading"
      class="status-line readiness-card-status"
      role="status"
    >
      Checking lock readiness…
    </p>
    <template v-else-if="readiness">
      <div
        class="readiness-summary"
        :class="{
          'readiness-summary--ready': readiness.ready_for_lock,
          'readiness-summary--blocked': !readiness.ready_for_lock,
        }"
      >
        <span
          class="clinical-badge readiness-badge"
          :class="
            readiness.ready_for_lock
              ? 'clinical-badge--verified readiness-badge--ready'
              : 'clinical-badge--error readiness-badge--blocked'
          "
          data-testid="study-locks-readiness-status"
        >
          <ClinicalIcon
            :name="readiness.ready_for_lock ? 'check-circle' : 'alert-triangle'"
            :size="16"
          />
          {{
            readiness.ready_for_lock
              ? "Ready for Locked"
              : canApply
                ? "Blocked for Locked · Frozen available"
                : "Blocked for Locked"
          }}
        </span>
        <span
          class="readiness-counts"
          data-testid="study-locks-readiness-counts"
        >
          <span
            v-for="(gate, gIdx) in readinessGates"
            :key="gate.key"
            :data-testid="`readiness-gate-${gate.key}`"
            :class="[
              'readiness-gate-chip',
              gate.count > 0
                ? 'readiness-gate-chip--active'
                : 'readiness-gate-chip--clear',
            ]"
          >
            <template v-if="gIdx > 0"> · </template>{{ gate.label }}
          </span>
        </span>
      </div>

      <p
        v-if="readiness.blockers.length > 0 && filteredBlockers.length === 0"
        class="status-line readiness-card-status"
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
                  v-for="chip in getBlockerChips(blocker)"
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
            >
              {{ resolveLockBlockerActionLabel(blocker) }}
            </component>
          </div>
        </li>
      </ul>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, getCurrentInstance, ref, watch } from "vue";
import { ClinicalIcon } from "ui";
import type { KernelLockReadiness } from "../../api/kernelApi";
import {
  countLockBlockersForRoleFilter,
  LOCK_BLOCKER_ROLE_FILTERS,
  matchesLockBlockerRoleFilter,
  resolveLockBlockerActionLabel,
  resolveLockBlockerLocation,
  type LockBlockerRoleFilter,
} from "../../canvas/lockBlockerRoutes";
import {
  formatLockBlockerDetail,
  formatLockBlockerFallbackTitle,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
  type useSignedKernelLock,
} from "../../composables/useSignedKernelLock";
import NodeRefChip from "./NodeRefChip.vue";

const props = withDefaults(
  defineProps<{
    canApply?: boolean;
    studyCode: string;
    applyScopeRef: string;
    readiness: KernelLockReadiness | null;
    readinessLoading: boolean;
    readinessGates: ReturnType<
      typeof useSignedKernelLock
    >["readinessGates"]["value"];
  }>(),
  { canApply: true }
);

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

function getBlockerChips(
  blocker: NonNullable<KernelLockReadiness["blockers"]>[number]
) {
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

<style scoped src="./locksSection.css"></style>
<style scoped src="./canvasBanners.css"></style>
