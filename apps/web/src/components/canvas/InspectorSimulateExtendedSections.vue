<template>
  <div class="simulation-extended">
    <template v-if="!isPartMasked('operational_impact')">
      <h4 class="simulation-subheading">
        Operational site &amp; participant impact
      </h4>
      <div
        v-if="simulationResult.operational_impact"
        id="kernel-simulate-operational"
        class="sim-operational"
      >
        <dl class="sim-metric-grid">
          <div class="sim-metric">
            <dt>Active sites</dt>
            <dd id="sim-op-active-sites" class="numeric">
              {{ simulationResult.operational_impact.active_site_count }}
            </dd>
          </div>
          <div class="sim-metric">
            <dt>Pending sites</dt>
            <dd id="sim-op-pending-sites" class="numeric">
              {{ simulationResult.operational_impact.pending_site_count }}
            </dd>
          </div>
          <div class="sim-metric">
            <dt>Total participants</dt>
            <dd id="sim-op-total-participants" class="numeric">
              {{ simulationResult.operational_impact.total_participant_count }}
            </dd>
          </div>
          <div class="sim-metric">
            <dt>Affected participants</dt>
            <dd id="sim-op-affected-participants" class="numeric">
              {{
                simulationResult.operational_impact.affected_participant_count
              }}
            </dd>
          </div>
          <div class="sim-metric">
            <dt>Re-consent required</dt>
            <dd id="sim-op-reconsent-participants" class="numeric">
              {{
                simulationResult.operational_impact.reconsent_participant_count
              }}
            </dd>
          </div>
        </dl>

        <p
          v-if="
            simulationResult.operational_impact.hidden?.more_exist ||
            simulationResult.operational_impact.hidden?.reasons?.length
          "
          id="kernel-simulate-operational-hidden"
          class="simulation-note"
          role="status"
        >
          Additional participant records exist outside your visible scope ({{
            formatHiddenReasons(
              simulationResult.operational_impact.hidden.reasons
            )
          }}).
        </p>

        <ul
          v-if="
            simulationResult.operational_impact.participant_impacts &&
            simulationResult.operational_impact.participant_impacts.length
          "
          id="kernel-simulate-participant-impacts"
          class="simulation-list"
        >
          <li
            v-for="(item, idx) in simulationResult.operational_impact
              .participant_impacts"
            :key="`${item.enrollment}-${idx}`"
            class="simulation-item"
          >
            <span
              v-if="isMaskedRefString(item.enrollment)"
              class="noderef-masked"
            >
              {{ formatMaskedRefString(item.enrollment) }}
            </span>
            <NodeRefChip
              v-else-if="parseNodeRef(item.enrollment)"
              :node-ref="item.enrollment"
              :fallback-title="item.subject_code || null"
            />
            <code v-else>{{ item.subject_code || item.enrollment }}</code>
            <span class="simulation-meta">
              · {{ formatCommandTitle(item.status) }} ·
              {{ formatCommandTitle(item.cohort) }}
            </span>
            <span v-if="item.requires_reconsent" class="sim-reconsent-tag">
              Re-consent required
            </span>
            <ul
              v-if="item.schedule_shifts && item.schedule_shifts.length"
              class="sim-shift-list"
            >
              <li
                v-for="shift in item.schedule_shifts"
                :key="shift.activity_id"
                class="numeric"
              >
                <span class="simulation-change-kind">{{ shift.change }}</span>
                <NodeRefChip
                  v-if="
                    shift.protocol_element &&
                    parseNodeRef(shift.protocol_element)
                  "
                  :node-ref="shift.protocol_element"
                  :fallback-title="shift.activity_id"
                />
                <code v-else>{{ shift.activity_id }}</code>
                <span v-if="formatShiftDates(shift)" class="simulation-meta">
                  ({{ formatShiftDates(shift) }})
                </span>
              </li>
            </ul>
          </li>
        </ul>
      </div>
      <p v-else id="kernel-simulate-operational-empty" class="simulation-note">
        No operational site or participant impact recorded.
      </p>
    </template>

    <template v-if="!isPartMasked('projection_changes')">
      <h4 class="simulation-subheading">Dataset projection changes</h4>
      <div
        v-if="
          simulationResult.projection_changes &&
          simulationResult.projection_changes.length
        "
        id="kernel-simulate-projections"
        class="sim-stack"
      >
        <div
          v-for="ds in simulationResult.projection_changes"
          :key="ds.dataset"
          class="sim-card"
          :data-dataset="ds.dataset"
        >
          <div class="sim-card-header">
            <code class="sim-dataset-code">{{ ds.dataset }}</code>
            <span class="simulation-meta numeric">
              +{{ ds.added_count }} added · -{{ ds.removed_count }} removed ·
              ~{{ ds.changed_count }}
              changed
            </span>
          </div>
          <ul
            v-if="ds.samples && ds.samples.length"
            class="simulation-list sim-samples"
          >
            <li
              v-for="sample in ds.samples"
              :key="sample.record_key"
              class="simulation-item"
            >
              <span class="simulation-change-kind">{{ sample.change }}</span>
              <code>{{ sample.record_key }}</code>
            </li>
          </ul>
        </div>
      </div>
      <p v-else id="kernel-simulate-projections-empty" class="simulation-note">
        No projected dataset changes detected.
      </p>
    </template>

    <template v-if="!isPartMasked('validation_delta')">
      <h4 class="simulation-subheading">Validation rule delta</h4>
      <div
        v-if="simulationResult.validation_delta"
        id="kernel-simulate-validation"
        class="sim-stack"
      >
        <p
          id="kernel-simulate-validation-summary"
          class="simulation-meta numeric"
        >
          Opening:
          <strong id="sim-val-opening-count">{{
            simulationResult.validation_delta.opening.length
          }}</strong>
          · Resolving:
          <strong id="sim-val-resolving-count">{{
            simulationResult.validation_delta.resolving.length
          }}</strong>
          · Unchanged:
          <strong id="sim-val-unchanged-count">{{
            simulationResult.validation_delta.unchanged_count
          }}</strong>
        </p>

        <div
          v-for="group in validationFindingGroups"
          :id="group.id"
          :key="group.key"
          class="sim-stack"
        >
          <span class="sim-delta-label">{{ group.label }}</span>
          <ul class="simulation-list">
            <li
              v-for="(finding, idx) in group.items"
              :key="`${group.key}-${finding.rule_id}-${finding.subject}-${idx}`"
              class="simulation-item"
            >
              <span class="sim-severity-badge">{{ finding.severity }}</span>
              <code>{{ finding.rule_id }}</code>
              <span class="simulation-meta">({{ finding.dataset }})</span>
              <template v-if="finding.subject">
                {{ " · " }}
                <span
                  v-if="isMaskedRefString(finding.subject)"
                  class="noderef-masked"
                >
                  {{ formatMaskedRefString(finding.subject) }}
                </span>
                <NodeRefChip
                  v-else-if="parseNodeRef(finding.subject)"
                  :node-ref="finding.subject"
                />
                <code v-else>{{ finding.subject }}</code>
              </template>
              <div class="simulation-meta">{{ finding.explanation }}</div>
            </li>
          </ul>
        </div>
      </div>
      <p v-else id="kernel-simulate-validation-empty" class="simulation-note">
        No validation rule changes evaluated.
      </p>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import type { KernelSimulationResult } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  formatCommandTitle,
  formatMaskedNode,
  maskReasonWords,
} from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  simulationResult: KernelSimulationResult;
}>();

const validationFindingGroups = computed(() => {
  const delta = props.simulationResult.validation_delta;
  if (!delta) return [];
  const groups = [];
  if (delta.opening.length) {
    groups.push({
      id: "kernel-simulate-validation-opening",
      key: "open",
      label: "Opening findings",
      items: delta.opening,
    });
  }
  if (delta.resolving.length) {
    groups.push({
      id: "kernel-simulate-validation-resolving",
      key: "res",
      label: "Resolving findings",
      items: delta.resolving,
    });
  }
  return groups;
});

function isPartMasked(part: string): boolean {
  return Boolean(
    props.simulationResult.masked?.some((entry) => entry.part === part)
  );
}

function isMaskedRefString(raw: string | null | undefined): boolean {
  return Boolean(raw && raw.startsWith("masked:"));
}

function formatMaskedRefString(raw: string): string {
  const parts = raw.split(":");
  const kind = parts[1] || "record";
  const reason = parts[2] || "NotPermitted";
  return formatMaskedNode(kind, reason);
}

function formatHiddenReasons(reasons: string[] | undefined): string {
  if (!reasons || reasons.length === 0) return "restricted";
  return reasons.map((r) => maskReasonWords(r)).join(", ");
}

function formatShiftDates(shift: {
  before?: { target_date?: string } | null;
  after?: { target_date?: string } | null;
}): string {
  if (shift.before?.target_date && shift.after?.target_date) {
    return `${shift.before.target_date} -> ${shift.after.target_date}`;
  }
  if (shift.after?.target_date) return shift.after.target_date;
  if (shift.before?.target_date) return `was ${shift.before.target_date}`;
  return "";
}
</script>

<style scoped>
.simulation-extended,
.sim-operational,
.sim-stack {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.simulation-subheading {
  margin: 4px 0 0;
  font-size: 0.85rem;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.sim-metric-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px;
  margin: 0;
}

.sim-metric {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
}

.sim-metric dt {
  font-size: 0.75rem;
  color: var(--neutral-dark);
}

.sim-metric dd {
  margin: 2px 0 0;
  font-size: 0.9rem;
  font-weight: 600;
}

.simulation-list,
.sim-shift-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding-left: 18px;
  font-size: 0.84rem;
}

.sim-shift-list {
  margin-top: 4px;
  font-size: 0.8rem;
}

.simulation-item {
  overflow-wrap: anywhere;
}

.simulation-item code,
.sim-dataset-code {
  font-family: var(--semantic-font-mono);
}

.simulation-change-kind {
  font-weight: 600;
  margin-right: 4px;
}

.simulation-meta,
.simulation-note {
  margin: 0;
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.sim-reconsent-tag,
.sim-severity-badge {
  display: inline-block;
  margin-left: 4px;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--border);
}

.sim-severity-badge {
  margin-left: 0;
  margin-right: 4px;
}

.sim-card {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
}

.sim-card-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 6px;
}

.sim-samples {
  margin-top: 4px;
}

.sim-delta-label {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.noderef-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}
</style>
