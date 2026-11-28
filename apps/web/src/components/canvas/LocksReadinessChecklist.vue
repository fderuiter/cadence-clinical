<template>
  <div
    v-if="readinessLoading || readiness"
    class="readiness-panel"
    :class="{
      'readiness-panel--ready': readiness?.ready_for_lock,
      'readiness-panel--blocked': readiness && !readiness.ready_for_lock,
    }"
    data-testid="study-locks-readiness"
  >
    <p v-if="readinessLoading" class="status-line" role="status">
      Checking lock readiness…
    </p>
    <template v-else-if="readiness">
      <div class="readiness-summary">
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
              : "Blocked for Locked · Frozen available"
          }}
        </span>
        <span
          class="readiness-counts tabular-nums"
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

      <div
        class="readiness-groups"
        data-testid="study-locks-readiness-blockers"
      >
        <section
          v-for="group in readinessGroups"
          :key="group.id"
          class="readiness-group-card"
          :data-testid="group.testId"
          :aria-labelledby="group.headingId"
        >
          <header class="readiness-group-header">
            <h4 :id="group.headingId" class="readiness-group-title">
              {{ group.title }}
            </h4>
            <span
              :class="[
                'readiness-group-pill',
                'tabular-nums',
                group.entries.length > 0
                  ? 'readiness-group-pill--warn'
                  : 'readiness-group-pill--ok',
              ]"
            >
              {{ group.pillText }}
            </span>
          </header>

          <div v-if="group.passSummary" class="readiness-pass-row tabular-nums">
            <span class="clinical-badge clinical-badge--verified">Passed</span>
            <span class="readiness-pass-text">
              {{ group.passSummary }}
            </span>
            <span v-if="group.passMeta" class="readiness-pass-meta">
              {{ group.passMeta }}
            </span>
          </div>

          <ul v-if="group.entries.length > 0" class="readiness-blockers">
            <li
              v-for="entry in group.entries"
              :key="entry.key"
              class="readiness-blocker-item"
              :data-blocker-kind="entry.normalizedKind"
            >
              <span
                class="clinical-badge blocker-kind-badge"
                :data-testid="`blocker-kind-${entry.normalizedKind}`"
              >
                {{
                  formatLockBlockerKindLabel(
                    entry.blocker.kind,
                    Boolean(entry.blocker.signature_superseded)
                  )
                }}
              </span>
              <template v-if="entry.blocker.masked">
                <span class="masked-pill">
                  Masked ({{ entry.blocker.mask ?? "Restricted" }})
                </span>
                <strong
                  v-if="formatMaskedBlockerOwner(entry.blocker)"
                  class="blocker-owner"
                >
                  {{ formatMaskedBlockerOwner(entry.blocker) }}:
                </strong>
                <span v-if="entry.blocker.next_action" class="blocker-action">
                  {{ entry.blocker.next_action }}
                </span>
              </template>
              <template v-else>
                <strong v-if="entry.blocker.owner" class="blocker-owner">
                  {{ entry.blocker.owner }}:
                </strong>
                <NodeRefChip
                  v-for="chip in getBlockerChips(entry.blocker)"
                  :key="chip.key"
                  :node-ref="chip.ref"
                  :fallback-title="chip.fallback"
                  include-kind
                />
                <span
                  v-if="formatLockBlockerDetail(entry.blocker)"
                  class="blocker-detail"
                >
                  {{ formatLockBlockerDetail(entry.blocker) }}
                </span>
                <span v-if="entry.blocker.next_action" class="blocker-action">
                  — {{ entry.blocker.next_action }}
                </span>
              </template>
            </li>
          </ul>
        </section>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { ClinicalIcon } from "ui";
import type { KernelLockReadiness } from "../../api/kernelApi";
import {
  formatLockBlockerDetail,
  formatLockBlockerFallbackTitle,
  formatLockBlockerKindLabel,
  formatMaskedBlockerOwner,
  normalizeLockBlockerKind,
  toReadinessCount,
  type useSignedKernelLock,
} from "../../composables/useSignedKernelLock";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  readiness: KernelLockReadiness | null;
  readinessLoading: boolean;
  readinessGates: ReturnType<
    typeof useSignedKernelLock
  >["readinessGates"]["value"];
}>();

const COHORT_AND_SCHEDULE_KINDS = new Set([
  "ScreeningEnrollment",
  "UnopenedVisit",
]);

const indexedBlockers = computed(() =>
  (props.readiness?.blockers ?? []).map((blocker, idx) => {
    const normalizedKind = normalizeLockBlockerKind(blocker.kind);
    return {
      key: `${blocker.kind}-${blocker.target_ref ?? blocker.finding_ref ?? blocker.query_ref ?? idx}`,
      normalizedKind,
      blocker,
    };
  })
);

const readinessGroups = computed(() => {
  const cohortEntries = indexedBlockers.value.filter((entry) =>
    COHORT_AND_SCHEDULE_KINDS.has(entry.normalizedKind)
  );
  const dataEntries = indexedBlockers.value.filter(
    (entry) => !COHORT_AND_SCHEDULE_KINDS.has(entry.normalizedKind)
  );
  const openQueries = toReadinessCount(props.readiness?.open_query_count);

  return [
    {
      id: "cohort",
      testId: "readiness-group-cohort-schedule",
      headingId: "readiness-cohort-heading",
      title: "Cohort & Schedule Gates",
      pillText:
        cohortEntries.length > 0
          ? `${cohortEntries.length} ${cohortEntries.length === 1 ? "issue" : "issues"} blocking`
          : "All clear",
      passSummary:
        cohortEntries.length === 0
          ? "0 unresolved screening enrollments · scheduled visit windows clear"
          : null,
      passMeta: null,
      entries: cohortEntries,
    },
    {
      id: "data",
      testId: "readiness-group-data-review",
      headingId: "readiness-data-heading",
      title: "Data, Queries & Review Gates",
      pillText: dataEntries.length > 0 ? "Attention needed" : "All clear",
      passSummary:
        openQueries === 0
          ? "0 open queries · synchronous kernel projection"
          : null,
      passMeta: openQueries === 0 ? "Clean sync" : null,
      entries: dataEntries,
    },
  ];
});

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
