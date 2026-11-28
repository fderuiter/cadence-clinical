<template>
  <div
    v-if="readiness && readiness.blockers.length > 0"
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
        <h3 :id="group.headingId" class="readiness-group-title">
          {{ group.title }}
        </h3>
        <span
          :class="[
            'readiness-group-pill',
            'tabular-nums',
            group.blockers.length > 0
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

      <LockBlockerList
        v-if="group.blockers.length > 0"
        :blockers="group.blockers"
        list-class="readiness-blockers"
        badge-test-id-prefix="blocker-kind-"
      />
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import type { KernelLockReadiness } from "../../api/kernelApi";
import {
  normalizeLockBlockerKind,
  toReadinessCount,
} from "../../composables/useSignedKernelLock";
import LockBlockerList from "./LockBlockerList.vue";

const props = defineProps<{
  readiness: KernelLockReadiness | null;
}>();

const COHORT_AND_SCHEDULE_KINDS = new Set([
  "ScreeningEnrollment",
  "UnopenedVisit",
]);

const readinessGroups = computed(() => {
  const allBlockers = props.readiness?.blockers ?? [];
  const cohortBlockers = allBlockers.filter((blocker) =>
    COHORT_AND_SCHEDULE_KINDS.has(normalizeLockBlockerKind(blocker.kind))
  );
  const dataBlockers = allBlockers.filter(
    (blocker) =>
      !COHORT_AND_SCHEDULE_KINDS.has(normalizeLockBlockerKind(blocker.kind))
  );
  const openQueries = toReadinessCount(props.readiness?.open_query_count);

  return [
    {
      id: "cohort",
      testId: "readiness-group-cohort-schedule",
      headingId: "readiness-cohort-heading",
      title: "Cohort & Schedule Gates",
      pillText:
        cohortBlockers.length > 0
          ? `${cohortBlockers.length} ${cohortBlockers.length === 1 ? "issue" : "issues"} blocking`
          : "All clear",
      passSummary:
        cohortBlockers.length === 0
          ? "0 unresolved screening enrollments · scheduled visit windows clear"
          : null,
      passMeta: null,
      blockers: cohortBlockers,
    },
    {
      id: "data",
      testId: "readiness-group-data-review",
      headingId: "readiness-data-heading",
      title: "Data, Queries & Review Gates",
      pillText: dataBlockers.length > 0 ? "Attention needed" : "All clear",
      passSummary:
        openQueries === 0
          ? "0 open queries · synchronous kernel projection"
          : null,
      passMeta: openQueries === 0 ? "Clean sync" : null,
      blockers: dataBlockers,
    },
  ];
});
</script>

<style scoped src="./locksSection.css"></style>
<style scoped src="./canvasBanners.css"></style>
