<template>
  <section
    id="kernel-simulate"
    class="sdv"
    aria-labelledby="kernel-simulate-title"
  >
    <h3 id="kernel-simulate-title" tabindex="-1">Schedule gap check</h3>
    <InspectorCandidateRunner
      :kernel-ref="kernelRef"
      :protocol-version="protocolVersion"
      :disabled="simulationLoading"
      @run-candidate="emit('run-candidate', $event)"
    />
    <p v-if="simulationLoading" class="inspector-empty">
      Checking schedule matrix and visit windows…
    </p>
    <CanvasErrorAlert
      v-else-if="simulationError"
      message="Schedule check couldn't be completed."
      :detail="simulationError"
      summary-label="Technical details"
      detail-id="kernel-simulate-error"
      @retry="emit('retry')"
      @explain="emit('explain')"
    />
    <div v-else-if="simulationResult" class="simulation-details">
      <div
        v-if="simulationResult.masked && simulationResult.masked.length"
        id="kernel-simulate-masked"
        class="simulation-masked"
        role="status"
      >
        <h4 class="simulation-subheading">Withheld sections</h4>
        <ul class="simulation-list">
          <li
            v-for="maskedPart in simulationResult.masked"
            :key="maskedPart.part"
            class="simulation-item"
          >
            {{ formatSimulationPart(maskedPart.part) }} withheld ({{
              maskReasonWords(maskedPart.reason)
            }})
          </li>
        </ul>
      </div>

      <template v-if="!isPartMasked('structural_diff')">
        <h4 class="simulation-subheading">Protocol element changes</h4>
        <ul
          v-if="
            simulationResult.structural_diff &&
            simulationResult.structural_diff.length
          "
          id="kernel-simulate-diff"
          class="simulation-list"
        >
          <li
            v-for="change in simulationResult.structural_diff"
            :key="change.element_id"
            class="simulation-item"
            :data-change="change.change"
          >
            <span class="simulation-change-kind">{{ change.change }}</span>
            {{
              change.instance_type
                ? plainSimulationWords(change.instance_type)
                : "element"
            }}
            <NodeRefChip
              v-if="
                change.protocol_element && parseNodeRef(change.protocol_element)
              "
              :node-ref="change.protocol_element"
              :fallback-title="change.element_id"
            />
            <code v-else>{{ change.element_id }}</code>
            <span
              v-if="change.properties && change.properties.length"
              class="simulation-meta"
            >
              ({{ change.properties.map(formatFieldPath).join(", ") }})
            </span>
          </li>
        </ul>
        <p v-else id="kernel-simulate-diff-empty" class="simulation-note">
          No protocol element changes detected.
        </p>
      </template>

      <template v-if="!isPartMasked('schedule_changes')">
        <h4 class="simulation-subheading">Visit schedule changes</h4>
        <ul
          v-if="
            simulationResult.schedule_changes &&
            simulationResult.schedule_changes.length
          "
          id="kernel-simulate-schedule"
          class="simulation-list"
        >
          <li
            v-for="change in simulationResult.schedule_changes"
            :key="change.activity_id"
            class="simulation-item"
            :data-change="change.change"
          >
            <span class="simulation-change-kind">{{ change.change }}</span>
            <NodeRefChip
              v-if="
                change.protocol_element && parseNodeRef(change.protocol_element)
              "
              :node-ref="change.protocol_element"
              :fallback-title="change.activity_id"
            /><code v-else>{{ change.activity_id }}</code
            >{{ " "
            }}<span class="simulation-meta numeric">
              {{ formatScheduleChange(change) }}
            </span>
          </li>
        </ul>
        <p v-else id="kernel-simulate-schedule-empty" class="simulation-note">
          No timed visit schedule changes detected.
        </p>
      </template>

      <template v-if="!isPartMasked('dependents')">
        <h4 class="simulation-subheading">Downstream impact</h4>
        <div
          v-if="
            simulationResult.dependents &&
            simulationResult.dependents.groups &&
            simulationResult.dependents.groups.length
          "
          id="kernel-simulate-dependents"
          class="simulation-dependents"
        >
          <p
            id="kernel-simulate-dependents-total"
            class="simulation-total numeric"
          >
            {{ simulationResult.dependents.truncated ? "At least " : ""
            }}{{ simulationResult.dependents.total }} dependent
            {{
              Number(simulationResult.dependents.total) === 1
                ? "object"
                : "objects"
            }}
          </p>
          <ul class="simulation-list">
            <li
              v-for="group in simulationResult.dependents.groups"
              :key="group.kind"
              class="simulation-group"
              :data-kind="group.kind"
            >
              <details>
                <summary>
                  {{ plainSimulationWords(group.kind) }}:
                  <span class="numeric">{{ group.count }}</span>
                </summary>
                <ul class="simulation-objects">
                  <li v-for="object in group.objects" :key="object.handle">
                    <span v-if="isMasked(object)" class="noderef-masked">
                      {{ formatMaskedNode(object.kind, object.mask) }}
                    </span>
                    <NodeRefChip
                      v-else-if="object.ref"
                      :node-ref="object.ref"
                    />
                    <template v-else>{{ object.handle }}</template>
                  </li>
                </ul>
              </details>
            </li>
          </ul>
        </div>
        <p v-else id="kernel-simulate-dependents-empty" class="simulation-note">
          No downstream records affected.
        </p>
      </template>

      <template v-if="!isPartMasked('matrix_gaps')">
        <h4 class="simulation-subheading">Schedule findings</h4>
        <div
          v-if="
            simulationResult.matrix_gaps && simulationResult.matrix_gaps.length
          "
          id="kernel-simulate-gaps"
          class="gaps-list"
        >
          <div
            v-for="(gap, idx) in simulationResult.matrix_gaps"
            :key="idx"
            class="gap-card reasoning-modal-s-2"
          >
            <div class="gap-card-title">
              <span class="gap-badge">{{ gap.severity }}</span>
              {{ formatCommandTitle(String(gap.kind || "Finding")) }}
              <span v-if="gap.json_pointer_path">
                ({{ formatFieldPath(String(gap.json_pointer_path)) }})
              </span>
            </div>
            <div
              v-if="gap.element_id || gap.protocol_element"
              class="gap-card-element"
            >
              Element:
              <NodeRefChip
                v-if="
                  gap.protocol_element && parseNodeRef(gap.protocol_element)
                "
                :node-ref="gap.protocol_element"
                :fallback-title="gap.element_id || 'Element'"
              />
              <code v-else>{{ gap.element_id }}</code>
            </div>
            <div class="gap-card-description">
              {{ gap.description }}
            </div>
            <div v-if="gap.recommendation" class="gap-card-recommendation">
              <em>Recommended next step: {{ gap.recommendation }}</em>
            </div>
          </div>
        </div>
        <p v-else id="kernel-simulate-gaps-empty" class="simulation-success">
          No schedule matrix gaps detected.
        </p>
      </template>

      <InspectorSimulateExtendedSections
        :simulation-result="simulationResult"
      />

      <details
        v-if="simulationResult.input_hash || simulationResult.result_hash"
        id="kernel-simulate-hashes"
        class="inspector-ref-details"
      >
        <summary class="inspector-ref-summary">Verification checksums</summary>
        <div class="reasoning-modal-s-1">
          <div v-if="simulationResult.input_hash">
            <strong>Input digest:</strong>
            {{ simulationResult.input_hash }}
          </div>
          <div v-if="simulationResult.result_hash">
            <strong>Result digest:</strong>
            {{ simulationResult.result_hash }}
          </div>
        </div>
      </details>

      <SimulationGovernanceActions
        v-if="protocolVersion && !hasParticipant && studyCode"
        :study-code="studyCode"
        :protocol-version="protocolVersion"
        :simulation-result="simulationResult"
      />
    </div>
  </section>
</template>

<script setup lang="ts">
import {
  isMasked,
  type KernelProtocolVersion,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  formatCommandTitle,
  formatFieldPath,
  formatMaskedNode,
  maskReasonWords,
} from "../../canvas/nodeResolver";
import { defineLazyOverlay } from "../../utils/lazyOverlay";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";
import InspectorCandidateRunner from "./InspectorCandidateRunner.vue";
import InspectorSimulateExtendedSections from "./InspectorSimulateExtendedSections.vue";
import NodeRefChip from "./NodeRefChip.vue";
import {
  formatScheduleChange,
  formatSimulationPart,
  plainSimulationWords,
  type CandidateSimulationOverride,
} from "./useInspectorSimulation";

const SimulationGovernanceActions = defineLazyOverlay(
  () => import("./SimulationGovernanceActions.vue")
);

const props = defineProps<{
  studyCode?: string;
  kernelRef?: string;
  protocolVersion?: KernelProtocolVersion | null;
  hasParticipant?: boolean;
  simulationResult: KernelSimulationResult | null;
  simulationLoading: boolean;
  simulationError: string;
}>();

const emit = defineEmits<{
  (e: "retry"): void;
  (e: "run-candidate", override: CandidateSimulationOverride): void;
  (e: "explain"): void;
}>();

function isPartMasked(part: string): boolean {
  return Boolean(
    props.simulationResult?.masked?.some((entry) => entry.part === part)
  );
}
</script>

<style scoped>
.inspector-empty {
  margin: 0;
  color: var(--neutral-dark);
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.reasoning-modal-s-1 {
  background: var(--neutral-light);
  padding: 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-family: var(--semantic-font-mono);
}

.reasoning-modal-s-2 {
  border-left: 3px solid var(--warning);
  padding: 6px 8px;
  margin-bottom: 8px;
  background: var(--semantic-color-warning-bg-soft);
}

.gap-card-title {
  font-weight: 600;
  font-size: 0.85rem;
}

.gap-card-element {
  font-size: 0.78rem;
  margin-top: 2px;
  color: var(--semantic-color-text-secondary);
}

.gap-card-element code {
  font-family: var(--semantic-font-mono);
}

.gap-card-description {
  font-size: 0.8rem;
  margin-top: 2px;
}

.gap-card-recommendation {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.simulation-details {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.simulation-subheading {
  margin: 4px 0 0;
  font-size: 0.85rem;
}

.simulation-list,
.simulation-objects {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding-left: 18px;
  font-size: 0.85rem;
}

.simulation-item {
  overflow-wrap: anywhere;
}

.simulation-change-kind {
  font-weight: 600;
  margin-right: 4px;
}

.simulation-meta,
.simulation-note {
  margin: 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}

.simulation-total {
  margin: 0;
  font-size: 0.85rem;
  font-weight: 600;
}

.simulation-dependents {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.simulation-group summary {
  min-height: var(--semantic-control-target-min, 24px);
  padding-block: 4px;
  cursor: pointer;
  box-sizing: border-box;
}

.simulation-group .simulation-objects {
  margin-top: 4px;
}

.simulation-masked {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px 8px;
  border-left: 3px solid var(--border);
  background: var(--neutral-light);
}

.simulation-success {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-success);
}
</style>
