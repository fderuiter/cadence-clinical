<template>
  <dl class="ecrf-form-summary" data-testid="ecrf-form-summary">
    <dt>Form</dt>
    <dd data-testid="ecrf-form-id">{{ form.form_id }}</dd>
    <dt>Collects</dt>
    <dd>{{ form.collection_spec_id }}</dd>
    <dt>Visit</dt>
    <dd>{{ form.planned_activity_id ?? "Unscheduled" }}</dd>
    <template v-if="form.visit_timing">
      <dt>Visit timing</dt>
      <dd
        class="ecrf-visit-timing-cell"
        data-testid="ecrf-form-visit-timing"
        :data-visit-timing="form.visit_timing"
      >
        <span
          class="clinical-badge"
          :class="
            form.visit_timing === 'InWindow'
              ? 'clinical-badge--verified'
              : 'clinical-badge--warning'
          "
        >
          {{ visitTimingBadgeText }}
        </span>
        <span
          v-if="form.deviation_category"
          id="ecrf-visit-deviation-badge"
          class="clinical-badge clinical-badge--error"
          data-testid="ecrf-form-visit-deviation"
        >
          {{ form.deviation_category }}
        </span>
        <span v-if="form.target_date" class="ecrf-visit-window-range">
          Target {{ form.target_date
          }}<template
            v-if="
              form.window_start &&
              form.window_end &&
              (form.window_start !== form.target_date ||
                form.window_end !== form.target_date)
            "
          >
            ({{ form.window_start }} – {{ form.window_end }})</template
          >
        </span>
      </dd>
    </template>
    <dt>Collected</dt>
    <dd class="ecrf-form-collected-at">{{ form.collected_at }}</dd>
    <dt>Version</dt>
    <dd data-testid="ecrf-form-version">{{ form.version }}</dd>
    <dt>Status</dt>
    <dd data-testid="ecrf-form-status">
      <span v-if="form.complete" class="ecrf-form-complete">Complete</span>
      <span v-else class="ecrf-form-incomplete">
        <template v-if="form.missing_required.length > 0">
          Incomplete: {{ form.missing_required.length }} required
          {{ form.missing_required.length === 1 ? "item" : "items" }}
          without a value ({{ form.missing_required.join(", ") }})<template
            v-if="nonMissingReadinessBlockers.length > 0"
            >; {{ nonMissingReadinessBlockers.join("; ") }}</template
          >
        </template>
        <template v-else-if="nonMissingReadinessBlockers.length > 0">
          Incomplete: {{ nonMissingReadinessBlockers.join("; ") }}
        </template>
        <template v-else>Incomplete</template>
      </span>
    </dd>
  </dl>

  <div
    v-if="locked"
    class="ecrf-form-locks"
    data-testid="ecrf-form-locks"
    role="status"
  >
    <strong>Locked.</strong> Values cannot be changed while a lock is in force.
    <span data-testid="ecrf-lock-scope-note">
      <template v-if="assertionsLocked">
        Source verifications, verification withdrawals, and query changes are
        also blocked while a Locked assertion is in force.
      </template>
      <template v-else>
        Source verification and queries remain available while Frozen.
      </template>
    </span>
    <ul>
      <li v-for="held in form.locks" :key="held.lock_id">
        {{ held.kind }} on
        <NodeRefChip
          v-if="normalizeLookupNodeRef(String(held.scope))"
          :node-ref="normalizeLookupNodeRef(String(held.scope))!"
        />
        <template v-else>{{ held.scope }}</template>
        by
        <NodeRefChip
          v-if="normalizeLookupNodeRef(String(held.locked_by))"
          :node-ref="normalizeLookupNodeRef(String(held.locked_by))!"
        />
        <template v-else>{{ held.locked_by }}</template>
        at {{ held.locked_at }}: {{ held.reason }}
      </li>
    </ul>
  </div>

  <ScopeLockControls
    :study-ref="`study:${form.study_id}`"
    :scope-ref="`collection_instance:${form.form_id}`"
    scope-label="form"
    :direct-locks="directFormLocks"
    :covering-locks="ancestorFormLocks"
    id-prefix="ecrf-lock"
    @changed="emit('lock-changed')"
  />

  <div
    v-if="
      canVerifySource &&
      (sdv.unverifiedItems.length > 0 || sdv.queriedUnverifiedCount > 0)
    "
    class="ecrf-bulk-sdv"
    data-testid="ecrf-bulk-sdv"
  >
    <div class="ecrf-form-row">
      <strong
        >Source verification ({{ sdv.unverifiedItems.length }} unverified
        {{ sdv.unverifiedItems.length === 1 ? "item" : "items" }})</strong
      >
      <button
        id="ecrf-sdv-select-all"
        class="btn btn-secondary"
        type="button"
        :disabled="
          sdv.sdvBusy || assertionsLocked || sdv.unverifiedItems.length === 0
        "
        @click="sdv.selectAllUnverifiedSdv()"
      >
        Select all unverified
      </button>
      <button
        v-if="sdv.selectedSdvItems.length > 0"
        id="ecrf-sdv-clear-selection"
        class="btn btn-secondary"
        type="button"
        :disabled="sdv.sdvBusy"
        @click="sdv.clearSdvSelection()"
      >
        Clear selection
      </button>
    </div>
    <p
      v-if="sdv.queriedUnverifiedCount > 0"
      class="ecrf-form-hint"
      data-testid="ecrf-sdv-queried-note"
    >
      {{ sdv.queriedUnverifiedCount }}
      {{ sdv.queriedUnverifiedCount === 1 ? "item" : "items" }} with an open
      query skipped until queries close.
    </p>
    <p
      v-if="assertionsLocked"
      class="ecrf-form-hint"
      data-testid="ecrf-sdv-locked-note"
    >
      This scope is Locked, so source verifications cannot be recorded or
      withdrawn until the lock is released.
    </p>
    <div v-else-if="sdv.selectedSdvItems.length > 0" class="ecrf-form-row">
      <label for="ecrf-bulk-sdv-reason"
        >Verification reason ({{ sdv.selectedSdvItems.length }} selected)</label
      >
      <input
        id="ecrf-bulk-sdv-reason"
        :value="sdv.bulkSdvReason"
        type="text"
        autocomplete="off"
        placeholder="Verified against source medical record"
        :disabled="sdv.sdvBusy"
        @input="sdv.setBulkSdvReason(($event.target as HTMLInputElement).value)"
      />
      <button
        id="ecrf-bulk-sdv-submit"
        class="btn btn-primary"
        type="button"
        :disabled="sdv.sdvBusy || !sdv.bulkSdvReason.trim()"
        @click="sdv.submitBulkSdv()"
      >
        {{
          sdv.sdvBusy
            ? "Verifying…"
            : `Verify selected (${sdv.selectedSdvItems.length})`
        }}
      </button>
    </div>
    <ul
      v-if="sdv.bulkSdvResults.length > 0"
      class="ecrf-bulk-sdv-results"
      data-testid="ecrf-bulk-sdv-results"
      aria-live="polite"
    >
      <li
        v-for="res in sdv.bulkSdvResults"
        :key="res.itemId"
        :data-sdv-result="res.itemId"
        :data-ok="String(res.ok)"
        :class="
          res.ok ? 'ecrf-bulk-sdv-result--ok' : 'ecrf-bulk-sdv-result--failed'
        "
      >
        <strong>{{ res.itemId }}:</strong> {{ res.message }}
      </li>
    </ul>
  </div>

  <div
    v-if="sdv.sdvOutcome"
    data-testid="ecrf-sdv-outcome"
    :class="[
      'ecrf-form-outcome',
      sdv.sdvOutcome.kind === 'done'
        ? 'ecrf-form-outcome--saved'
        : 'ecrf-form-outcome--failed',
    ]"
    aria-live="polite"
    :role="sdv.sdvOutcome.kind === 'done' ? 'status' : 'alert'"
  >
    {{ sdv.sdvOutcome.message }}
    <button
      v-if="sdv.sdvOutcome.kind === 'stale'"
      id="ecrf-sdv-reload"
      class="btn btn-secondary"
      type="button"
      @click="emit('reload-stale')"
    >
      Reload form
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, type UnwrapNestedRefs } from "vue";
import { toSignedFormDayOffset, type EcrfForm } from "../../api/ecrfFormsApi";
import {
  toCoveringScopeLocks,
  toDirectScopeLocks,
} from "../../canvas/kernelLocks";
import { normalizeLookupNodeRef } from "../../canvas/nodeResolver";
import type { useEcrfSdv } from "../../composables/useEcrfSdv";
import NodeRefChip from "../canvas/NodeRefChip.vue";
import ScopeLockControls, {
  type CoveringScopeLockItem,
  type DirectScopeLockItem,
} from "../canvas/ScopeLockControls.vue";

const props = defineProps<{
  form: EcrfForm;
  locked: boolean;
  assertionsLocked: boolean;
  canVerifySource?: boolean;
  sdv: UnwrapNestedRefs<ReturnType<typeof useEcrfSdv>>;
}>();

const emit = defineEmits<{
  (e: "lock-changed"): void;
  (e: "reload-stale"): void;
}>();

const directFormLocks = computed<DirectScopeLockItem[]>(() =>
  toDirectScopeLocks(
    props.form.locks,
    `collection_instance:${props.form.form_id}`
  )
);

const ancestorFormLocks = computed<CoveringScopeLockItem[]>(() =>
  toCoveringScopeLocks(
    props.form.locks,
    `collection_instance:${props.form.form_id}`
  )
);

const nonMissingReadinessBlockers = computed(() =>
  (props.form.readiness_blockers ?? []).filter(
    (blocker) => !/required items? without a value/i.test(blocker)
  )
);

const visitTimingBadgeText = computed(() => {
  const timing = props.form.visit_timing;
  if (!timing) return "";
  const outside = toSignedFormDayOffset(props.form.days_outside_window);
  if (timing === "Early" && outside && outside > 0) {
    return `Early (-${outside}d)`;
  }
  if (timing === "Late" && outside && outside > 0) {
    return `Late (+${outside}d)`;
  }
  return timing;
});
</script>

<style scoped>
.ecrf-form-summary {
  display: grid;
  grid-template-columns: max-content minmax(0, 1fr);
  gap: 4px 12px;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}
.ecrf-form-summary dt {
  font-weight: 600;
}
.ecrf-form-summary dd {
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
  font-variant-numeric: tabular-nums;
}
.ecrf-visit-timing-cell {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.ecrf-visit-window-range {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
[data-testid="ecrf-form-id"],
.ecrf-form-summary dd:nth-of-type(2),
.ecrf-form-summary dd:nth-of-type(3),
.ecrf-form-summary dd:nth-of-type(4):not(.ecrf-visit-timing-cell),
.ecrf-form-collected-at,
[data-testid="ecrf-form-version"],
.ecrf-bulk-sdv-results strong {
  font-family: var(--semantic-font-mono);
}
.ecrf-form-locks,
.ecrf-bulk-sdv {
  font-variant-numeric: tabular-nums;
}
.ecrf-form-complete {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}
.ecrf-form-incomplete {
  color: var(--semantic-color-warning-text);
}
.ecrf-form-locks {
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
}
.ecrf-form-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-bulk-sdv {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
}
.ecrf-bulk-sdv-results {
  margin: 6px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.ecrf-bulk-sdv-result--ok {
  color: var(--semantic-color-success-dark);
}
.ecrf-bulk-sdv-result--failed {
  color: var(--semantic-color-error-text-dark);
}
</style>

<style scoped src="./ecrfFormBanners.css"></style>
