<template>
  <div class="amendment-summary">
    <div id="kernel-amendment-operational" class="amendment-operational">
      <h4 class="lineage-subheading">
        Operational site &amp; participant counts
      </h4>
      <dl class="amendment-metrics">
        <div class="amendment-metric">
          <dt>Active sites</dt>
          <dd id="amendment-active-sites" class="numeric">
            {{ amendment.active_site_count ?? 0 }}
          </dd>
        </div>
        <div class="amendment-metric">
          <dt>Pending sites</dt>
          <dd id="amendment-pending-sites" class="numeric">
            {{ amendment.pending_site_count ?? 0 }}
          </dd>
        </div>
        <div class="amendment-metric">
          <dt>Total participants</dt>
          <dd id="amendment-total-participants" class="numeric">
            {{ amendment.total_participant_count ?? 0 }}
          </dd>
        </div>
        <div class="amendment-metric">
          <dt>Affected participants</dt>
          <dd id="amendment-affected-participants" class="numeric">
            {{ amendment.affected_participant_count ?? 0 }}
          </dd>
        </div>
        <div class="amendment-metric">
          <dt>Re-consent required</dt>
          <dd id="amendment-reconsent-participants" class="numeric">
            {{ amendment.reconsent_participant_count ?? 0 }}
          </dd>
        </div>
        <div class="amendment-metric">
          <dt>Hidden outside scope</dt>
          <dd id="amendment-hidden-summary" class="numeric">
            {{ formatHiddenSummary(amendment.hidden) }}
          </dd>
        </div>
      </dl>
    </div>

    <p
      v-if="!amendment.changed_elements.length"
      id="kernel-amendment-unchanged"
      class="lineage-note"
      role="status"
    >
      No protocol elements were changed in this amendment.
    </p>
    <ul v-else id="kernel-amendment-changes" class="lineage-list">
      <li
        v-for="change in amendment.changed_elements"
        :key="change.element_id"
        class="amendment-change"
        :data-change="change.change"
      >
        <span class="amendment-change-kind">{{ change.change }}</span>
        {{ change.instance_type ? words(change.instance_type) : "element" }}
        <NodeRefChip
          v-if="
            change.protocol_element && parseNodeRef(change.protocol_element)
          "
          :node-ref="change.protocol_element"
          :fallback-title="change.element_id"
        />
        <code v-else>{{ change.element_id }}</code>
        <span
          v-if="change.properties.length"
          class="lineage-note amendment-properties"
        >
          ({{ change.properties.map(formatFieldPath).join(", ") }})
        </span>
      </li>
    </ul>

    <div
      v-if="amendment.schedule"
      id="kernel-amendment-schedule"
      class="amendment-schedule"
    >
      <h4 class="lineage-subheading">Schedule and eligibility preview</h4>
      <p id="kernel-amendment-schedule-flags" class="lineage-note">
        {{
          amendment.schedule.changes_schedule
            ? "Changes on-study visit schedule"
            : "On-study visit schedule unchanged"
        }}
        ·
        {{
          amendment.schedule.changes_eligibility
            ? "Changes entry eligibility"
            : "Entry eligibility unchanged"
        }}
      </p>
      <ul
        v-if="amendment.schedule.directives.length"
        id="kernel-amendment-directives"
        class="lineage-list"
      >
        <li
          v-for="(directive, dIdx) in amendment.schedule.directives"
          :key="`${directive.kind}-${directive.element_id}-${dIdx}`"
          class="amendment-change"
          :data-directive-kind="directive.kind"
        >
          <span class="amendment-change-kind">{{
            formatCommandTitle(directive.kind)
          }}</span>
          <NodeRefChip
            v-if="
              directive.protocol_element &&
              parseNodeRef(directive.protocol_element)
            "
            :node-ref="directive.protocol_element"
            :fallback-title="directive.element_id"
          />
          <code v-else>{{ directive.element_id }}</code>
          <span
            v-if="formatDirectiveDetail(directive)"
            class="lineage-note amendment-properties numeric"
          >
            ({{ formatDirectiveDetail(directive) }})
          </span>
          <span
            v-if="
              directive.impacted_concepts && directive.impacted_concepts.length
            "
            class="amendment-concepts"
          >
            <NodeRefChip
              v-for="conceptRef in directive.impacted_concepts"
              :key="conceptRef"
              :node-ref="conceptRef"
            />
          </span>
        </li>
      </ul>
      <p v-else id="kernel-amendment-directives-empty" class="lineage-note">
        No schedule or eligibility directives recorded.
      </p>
      <p
        v-if="
          amendment.schedule.undeclared && amendment.schedule.undeclared.length
        "
        id="kernel-amendment-undeclared"
        class="amendment-undeclared"
        role="status"
      >
        Undeclared impacted elements:
        <template
          v-for="(elementId, uIdx) in amendment.schedule.undeclared"
          :key="elementId"
        >
          <template v-if="uIdx > 0">, </template>
          <NodeRefChip
            v-if="resolveElementNodeRef(elementId)"
            :node-ref="resolveElementNodeRef(elementId)!"
            :fallback-title="elementId"
          />
          <code v-else>{{ elementId }}</code>
        </template>
      </p>
    </div>

    <h4 class="lineage-subheading">What depends on the version it amends</h4>
  </div>
</template>

<script setup lang="ts">
import type {
  KernelAmendmentImpact,
  KernelSoaDirective,
  KernelSoaVisit,
} from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  formatCommandTitle,
  formatFieldPath,
  maskReasonWords,
} from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  amendment: KernelAmendmentImpact;
}>();

function words(name: string): string {
  return name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
}

function formatHiddenSummary(
  hidden:
    | {
        more_exist?: boolean;
        reasons?: ReadonlyArray<string | number> | null;
      }
    | null
    | undefined
): string {
  if (!hidden?.more_exist) {
    return "None";
  }
  const reasons = (hidden.reasons ?? []).map((r) => maskReasonWords(String(r)));
  return reasons.length ? `Yes (${reasons.join(", ")})` : "Yes";
}

function resolveElementNodeRef(elementId: string): string | null {
  const candidate =
    props.amendment.schedule?.protocol_elements?.[elementId] ??
    props.amendment.protocol_elements?.[elementId] ??
    null;
  return candidate && parseNodeRef(candidate) ? candidate : null;
}

function formatSoaVisit(visit: KernelSoaVisit): string {
  const dayLabel =
    visit.study_day !== null && visit.study_day !== undefined
      ? `Day ${visit.study_day}`
      : "Event-driven";
  const windowLabel =
    visit.window_before || visit.window_after
      ? ` (-${visit.window_before}/+${visit.window_after}d)`
      : "";
  const locationLabel = visit.locations?.length
    ? `, ${visit.locations.join("/")}`
    : "";
  return `${dayLabel}${windowLabel} in ${visit.epoch_id}${locationLabel}`;
}

function formatDirectiveDetail(directive: KernelSoaDirective): string {
  if (directive.before && directive.after) {
    return `${formatSoaVisit(directive.before)} -> ${formatSoaVisit(directive.after)}`;
  }
  if (directive.after) {
    return formatSoaVisit(directive.after);
  }
  if (directive.before) {
    return `was ${formatSoaVisit(directive.before)}`;
  }
  if (
    directive.frequency_before !== null &&
    directive.frequency_before !== undefined &&
    directive.frequency_after !== null &&
    directive.frequency_after !== undefined
  ) {
    const parts = [
      `${directive.frequency_before} -> ${directive.frequency_after} visits`,
    ];
    if (directive.collected_at_added?.length) {
      parts.push(`+${directive.collected_at_added.join(", ")}`);
    }
    if (directive.collected_at_removed?.length) {
      parts.push(`-${directive.collected_at_removed.join(", ")}`);
    }
    return parts.join("; ");
  }
  return "";
}
</script>

<style scoped>
.amendment-summary,
.amendment-operational,
.amendment-schedule {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.lineage-subheading {
  margin: 4px 0 0;
  font-size: 0.85rem;
}

.amendment-metrics {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px;
  margin: 0;
}

.amendment-metric {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
}

.amendment-metric dt {
  font-size: 0.75rem;
  color: var(--neutral-dark);
}

.amendment-metric dd {
  margin: 2px 0 0;
  font-size: 0.88rem;
  font-weight: 600;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.lineage-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding-left: 18px;
  font-size: 0.85rem;
}

.amendment-change {
  overflow-wrap: anywhere;
}

.amendment-change code {
  font-family: var(--semantic-font-mono);
}

.amendment-change-kind {
  font-weight: 600;
  margin-right: 4px;
}

.amendment-concepts {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-left: 4px;
  vertical-align: middle;
}

.amendment-undeclared {
  margin: 0;
  padding: 6px 8px;
  border-left: 3px solid var(--warning);
  background: var(--semantic-color-warning-bg-soft);
  font-size: 0.82rem;
}

.lineage-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}
</style>
