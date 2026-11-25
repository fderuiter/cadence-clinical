<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    :class="{ 'inspector--collapsed': isCollapsed }"
    :data-collapsed="isCollapsed ? 'true' : 'false'"
    aria-labelledby="canvas-inspector-title"
  >
    <div class="inspector-rail">
      <button
        id="btn-toggle-inspector"
        type="button"
        class="inspector-toggle"
        :aria-expanded="!isCollapsed"
        aria-controls="canvas-inspector-body"
        :aria-label="isCollapsed ? 'Expand inspector' : 'Collapse inspector'"
        :title="isCollapsed ? 'Expand inspector' : 'Collapse inspector'"
        @click="toggleCollapsed"
      >
        <span aria-hidden="true">{{ isCollapsed ? "◂" : "▸" }}</span>
      </button>
    </div>

    <div
      id="canvas-inspector-body"
      v-show="!isCollapsed"
      class="inspector-body"
    >
      <template v-if="activeInspectorHeader && activeInspectorRender">
        <header class="inspector-header">
          <span class="inspector-kind">{{
            activeInspectorHeader.kindLabel
          }}</span>
          <h2
            id="canvas-inspector-title"
            class="inspector-title"
            :class="{
              'inspector-title--code': activeInspectorHeader.isCodeTitle,
            }"
            tabindex="-1"
          >
            {{ activeInspectorHeader.title }}
          </h2>
          <details
            v-if="activeInspectorHeader.activeNodeRef"
            class="inspector-ref-details"
          >
            <summary class="inspector-ref-summary">Record reference</summary>
            <div class="inspector-ref-row">
              <code id="canvas-inspector-ref" class="inspector-ref">{{
                activeInspectorHeader.activeNodeRef
              }}</code>
              <button
                id="btn-copy-inspector-ref"
                type="button"
                class="btn-copy-ref"
                @click="copyInspectorRef"
              >
                {{ copiedRef ? "Copied" : "Copy" }}
              </button>
            </div>
          </details>
        </header>

        <div ref="toolbarRef" class="verbs" role="toolbar" aria-label="Verbs">
          <button
            id="verb-details"
            type="button"
            class="verb"
            :class="{ 'verb-current': currentVerb === 'details' }"
            :aria-pressed="currentVerb === 'details'"
            :tabindex="getTabIndex('verb-details')"
            @click="choose('details')"
          >
            Details
          </button>
          <template v-for="verb in verbs" :key="verb.id">
            <button
              v-if="verb.live"
              :id="`verb-${verb.id}`"
              type="button"
              class="verb"
              :class="{ 'verb-current': currentVerb === verb.id }"
              :aria-pressed="currentVerb === verb.id"
              :tabindex="getTabIndex(`verb-${verb.id}`)"
              @click="choose(verb.id)"
            >
              {{ verb.label }}
            </button>
            <button
              v-else
              :id="`verb-${verb.id}`"
              type="button"
              class="verb"
              aria-disabled="true"
              :tabindex="getTabIndex(`verb-${verb.id}`)"
              :aria-describedby="
                shownVerb === verb.id ? 'verb-note' : undefined
              "
              @click="shownVerb = verb.id"
            >
              {{ verb.label }}
            </button>
          </template>
        </div>
        <p v-if="shownVerbNote" id="verb-note" class="verb-note" role="status">
          {{ shownVerbNote }}
        </p>

        <AssistPanel
          v-if="currentVerb === 'assist' && kernelRef && aiEnabled"
          :study-code="studyCode"
          :node-ref="kernelRef"
          :prefill="assistPrompt"
          @act="choose('act')"
        />

        <KernelActPanel
          v-else-if="currentVerb === 'act' && kernelRef"
          :node-ref="kernelRef"
          @explain="explainCommand"
          @open-explain="openExplain()"
        />

        <KernelLineagePanel
          v-else-if="
            (currentVerb === 'trace' || currentVerb === 'impact') && kernelRef
          "
          :node-ref="kernelRef"
          :mode="lineageMode"
          @explain="openExplain()"
        />

        <section
          v-else-if="currentVerb === 'explain' && kernelRef"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">
            {{
              explained
                ? `Why: ${formatCommandTitle(explained.command)}`
                : "Explain"
            }}
          </h3>
          <KernelExplanation
            v-if="explained"
            :explanation="explained.decision.explanation"
          />
          <p v-else id="kernel-explain-empty" class="inspector-empty">
            Choose Explain beside an action under Act to see why it is permitted
            or blocked.
          </p>
        </section>

        <section
          v-else-if="currentVerb === 'simulate' && kernelRef"
          id="kernel-simulate"
          class="sdv"
          aria-labelledby="kernel-simulate-title"
        >
          <h3 id="kernel-simulate-title" tabindex="-1">Schedule gap check</h3>
          <p v-if="simulationLoading" class="inspector-empty">
            Checking schedule matrix and visit windows…
          </p>
          <CanvasErrorAlert
            v-else-if="simulationError"
            message="Schedule check couldn't be completed."
            :detail="simulationError"
            summary-label="Technical details"
            detail-id="kernel-simulate-error"
            @retry="runSimulation"
            @explain="openExplain()"
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

            <template v-if="!isSimulationPartMasked('structural_diff')">
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
                  <span class="simulation-change-kind">{{
                    change.change
                  }}</span>
                  {{
                    change.instance_type
                      ? plainWords(change.instance_type)
                      : "element"
                  }}
                  <code>{{ change.element_id }}</code>
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

            <template v-if="!isSimulationPartMasked('schedule_changes')">
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
                  <span class="simulation-change-kind">{{
                    change.change
                  }}</span>
                  <code>{{ change.activity_id }}</code>
                  <span class="simulation-meta numeric">
                    {{ formatScheduleChange(change) }}
                  </span>
                </li>
              </ul>
              <p
                v-else
                id="kernel-simulate-schedule-empty"
                class="simulation-note"
              >
                No timed visit schedule changes detected.
              </p>
            </template>

            <template v-if="!isSimulationPartMasked('dependents')">
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
                        {{ plainWords(group.kind) }}:
                        <span class="numeric">{{ group.count }}</span>
                      </summary>
                      <ul class="simulation-objects">
                        <li
                          v-for="object in group.objects"
                          :key="object.handle"
                        >
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
              <p
                v-else
                id="kernel-simulate-dependents-empty"
                class="simulation-note"
              >
                No downstream records affected.
              </p>
            </template>

            <template v-if="!isSimulationPartMasked('matrix_gaps')">
              <h4 class="simulation-subheading">Schedule findings</h4>
              <div
                v-if="
                  simulationResult.matrix_gaps &&
                  simulationResult.matrix_gaps.length
                "
                id="kernel-simulate-gaps"
                class="gaps-list"
              >
                <div
                  v-for="(gap, idx) in simulationResult.matrix_gaps"
                  :key="idx"
                  class="gap-card reasoning-modal-s-2"
                >
                  <div style="font-weight: 600; font-size: 0.85rem">
                    <span class="gap-badge">{{ gap.severity }}</span>
                    {{ formatCommandTitle(String(gap.kind || "Finding")) }}
                    <span v-if="gap.json_pointer_path">
                      ({{ formatFieldPath(String(gap.json_pointer_path)) }})
                    </span>
                  </div>
                  <div style="font-size: 0.8rem; margin-top: 2px">
                    {{ gap.description }}
                  </div>
                  <div
                    v-if="gap.recommendation"
                    style="
                      font-size: 0.75rem;
                      color: var(--semantic-color-text-secondary);
                      margin-top: 2px;
                    "
                  >
                    <em>Recommended next step: {{ gap.recommendation }}</em>
                  </div>
                </div>
              </div>
              <p
                v-else
                id="kernel-simulate-gaps-empty"
                class="simulation-success"
              >
                No schedule matrix gaps detected.
              </p>
            </template>
            <details
              v-if="simulationResult.input_hash || simulationResult.result_hash"
              id="kernel-simulate-hashes"
              class="inspector-ref-details"
            >
              <summary class="inspector-ref-summary">
                Verification checksums
              </summary>
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
          </div>
        </section>

        <component
          :is="activeInspectorRender.component"
          v-else
          :key="activeInspectorRender.key"
          v-bind="activeInspectorRender.props"
        />
      </template>

      <template v-else-if="protocolVersionError">
        <h2 id="canvas-inspector-title" class="inspector-title" tabindex="-1">
          Protocol version
        </h2>
        <CanvasErrorAlert
          id="inspector-version-error"
          message="This protocol version couldn't be loaded. Check that it exists and is visible to your account."
          :detail="protocolVersionError"
          @retry="() => props.onRetryProtocolVersion?.()"
          @explain="openProtocolVersionExplain"
        />
        <section
          v-if="forceExplainOpen"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">Explain</h3>
          <KernelExplanation
            v-if="versionErrorCanI.decision.value?.explanation"
            :explanation="versionErrorCanI.decision.value.explanation"
          />
          <p v-else id="kernel-explain-empty" class="inspector-empty">
            Choose Retry above or verify your account's study access to inspect
            available protocol actions.
          </p>
        </section>
      </template>

      <template v-else-if="missing">
        <h2 id="canvas-inspector-title" class="inspector-title">
          Participant not loaded
        </h2>
        <p class="inspector-empty">
          This participant isn't among those loaded for {{ studyCode }}. Find it
          in Participants to see its details.
        </p>
      </template>

      <template v-else>
        <h2 id="canvas-inspector-title" class="inspector-title">Inspector</h2>
        <p class="inspector-empty">
          Select a participant or study record to see its details here.
        </p>
        <section
          v-if="forceExplainOpen"
          id="kernel-explain"
          class="sdv"
          tabindex="-1"
          aria-labelledby="kernel-explain-title"
        >
          <h3 id="kernel-explain-title">Explain</h3>
          <p id="kernel-explain-empty" class="inspector-empty">
            Select a study record to inspect why its actions are permitted or
            blocked.
          </p>
        </section>
        <AssistPanel
          v-if="assistPrompt && aiEnabled"
          :study-code="studyCode"
          :prefill="assistPrompt"
        />
      </template>
    </div>
  </aside>
</template>

<script setup lang="ts">
import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
} from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  useStudyCanvasStore,
  type CanvasParticipant,
} from "../../stores/studyCanvas";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";
import KernelActPanel from "./KernelActPanel.vue";
import KernelExplanation from "./KernelExplanation.vue";
import KernelLineagePanel from "./KernelLineagePanel.vue";
import NodeRefChip from "./NodeRefChip.vue";
import {
  isMasked,
  kernelApi,
  type KernelCommand,
  type KernelNode,
  type KernelPlannedVisit,
  type KernelProtocolVersion,
  type KernelScheduleChange,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import { resolveActiveInspector } from "../../canvas/inspectors/registry";
import type { CanvasInspectorContext } from "../../canvas/inspectors/types";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  formatCommandTitle,
  formatFieldPath,
  formatMaskedNode,
  getNodeDisplay,
  maskReasonWords,
} from "../../canvas/nodeResolver";
import { useAiAssistance } from "../../ai/aiAssistance";
import { useKernelCanI } from "../../composables/useKernelCanI";
import { useRovingToolbar } from "../../composables/useRovingToolbar";
import AssistPanel from "../ai/AssistPanel.vue";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): resolves object-specific header
 * metadata and Details panes via glob-discovered `*.inspector.ts` descriptors while keeping the universal
 * Trace, Impact, Explain, Simulate, Act, and Assist verb toolbar shared across every study object.
 */
const props = defineProps<{
  studyCode: string;
  /** The selected participant, when the canvas has loaded it. */
  participant: CanvasParticipant | null;
  /** The canonical node reference of the selection. */
  nodeRef: string;
  /** True when a participant is selected but the canvas has not loaded it. */
  missing: boolean;
  /** The selected protocol version, as the kernel returned it (ADR-2540). */
  protocolVersion?: KernelProtocolVersion | null;
  /** Why the selected protocol version couldn't be read. */
  protocolVersionError?: string;
  /** Canonical NodeRef when another study object (site, enrollment, grant, etc.) is selected. */
  genericNodeRef?: string;
  /** Resolved node summary for a generic selection, when loaded. */
  genericNode?: KernelNode | null;
  /** True while resolving a generic node selection. */
  genericNodeLoading?: boolean;
  /** Where the participant's record opens; absent when it is already open. */
  recordLocation?: RouteLocationRaw | null;
  /** Where the participant's forms workspace opens inside Clinical Canvas; absent when already open. */
  dataEntryLocation?: RouteLocationRaw | null;
  /** Context handoff location to the standalone /ecrf view forwarding study, participant, enrollment, form, and visit. */
  handoffLocation?: RouteLocationRaw | null;
  /** Whether to offer changing the participant's state (Execution enforces who may, #4943). */
  canManageParticipants?: boolean;
  /** Whether the caller holds emergency unblinding authority (SubjectAuthority.CanUnblind). */
  canUnblindParticipants?: boolean;
  /** A question from the palette's Ask entry: opens Assist with it (ADR-2635). */
  assistPrompt?: string;
  onRetryProtocolVersion?: () => unknown | Promise<unknown>;
}>();

const emit = defineEmits<{
  (e: "update:collapsed", collapsed: boolean): void;
}>();

const { enabled: aiEnabled } = useAiAssistance();
const canvas = useStudyCanvasStore();
const versionErrorCanI = useKernelCanI();
const forceExplainOpen = ref(false);

const SIMULATION_PART_LABELS: Record<string, string> = {
  structural_diff: "Protocol element changes",
  schedule_changes: "Visit schedule changes",
  dependents: "Downstream impact",
  matrix_gaps: "Schedule findings",
};

function plainWords(name: string): string {
  return name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
}

function formatSimulationPart(part: string): string {
  return SIMULATION_PART_LABELS[part] ?? formatCommandTitle(part);
}

function isSimulationPartMasked(part: string): boolean {
  return Boolean(
    simulationResult.value?.masked?.some((entry) => entry.part === part)
  );
}

function formatPlannedVisit(visit: KernelPlannedVisit): string {
  const window =
    visit.window_before || visit.window_after
      ? ` (-${visit.window_before}/+${visit.window_after}d)`
      : "";
  return `Day ${visit.study_day}${window} in ${visit.epoch_id}`;
}

function formatScheduleChange(change: KernelScheduleChange): string {
  if (change.before && change.after) {
    return `${formatPlannedVisit(change.before)} -> ${formatPlannedVisit(change.after)}`;
  }
  if (change.after) {
    return formatPlannedVisit(change.after);
  }
  if (change.before) {
    return `was ${formatPlannedVisit(change.before)}`;
  }
  return "";
}
const genericDisplay = computed(() => {
  if (!props.genericNodeRef || props.missing) return null;
  return getNodeDisplay(props.genericNodeRef, props.genericNode?.title ?? null);
});

const inspectorContext = computed<CanvasInspectorContext>(() => ({
  studyCode: props.studyCode,
  nodeRef: props.nodeRef,
  participant: props.participant,
  missing: props.missing,
  protocolVersion: props.protocolVersion ?? null,
  protocolVersionError: props.protocolVersionError ?? "",
  genericNodeRef: props.genericNodeRef ?? "",
  genericNode: props.genericNode ?? null,
  genericNodeLoading: Boolean(props.genericNodeLoading),
  genericDisplay: genericDisplay.value,
  recordLocation: props.recordLocation ?? null,
  dataEntryLocation: props.dataEntryLocation ?? null,
  handoffLocation: props.handoffLocation ?? null,
  canManageParticipants: Boolean(props.canManageParticipants),
  canUnblindParticipants: props.canUnblindParticipants,
}));

const activeInspectorDescriptor = computed(() =>
  resolveActiveInspector(inspectorContext.value)
);

const activeInspectorHeader = computed(() =>
  activeInspectorDescriptor.value?.resolveHeader(inspectorContext.value)
);

const activeInspectorRender = computed(() =>
  activeInspectorDescriptor.value?.resolveDetailsRender(inspectorContext.value)
);

const copiedRef = ref(false);
async function copyInspectorRef() {
  const refText = activeInspectorHeader.value?.activeNodeRef;
  if (!refText) return;
  try {
    await globalThis.navigator?.clipboard?.writeText?.(refText);
    copiedRef.value = true;
    setTimeout(() => {
      copiedRef.value = false;
    }, 1500);
  } catch {
    copiedRef.value = false;
  }
}

const hasActiveContent = computed(() =>
  Boolean(
    activeInspectorDescriptor.value ||
    props.protocolVersionError ||
    props.missing ||
    forceExplainOpen.value ||
    (props.assistPrompt && aiEnabled.value)
  )
);

const manualCollapsed = ref<boolean | null>(null);
const isCollapsed = computed(
  () => manualCollapsed.value ?? !hasActiveContent.value
);

function toggleCollapsed() {
  manualCollapsed.value = !isCollapsed.value;
}

function expand() {
  manualCollapsed.value = false;
}

const NO_RECORD_REF =
  "this selection doesn't have a linked study reference yet.";

const kernelRef = computed(() => activeInspectorHeader.value?.kernelRef ?? "");
const canSimulate = computed(() =>
  Boolean(activeInspectorHeader.value?.canSimulate)
);

type VerbId =
  "details" | "trace" | "impact" | "act" | "explain" | "simulate" | "assist";
const currentVerb = ref<VerbId>("details");

const lineageMode = computed(() => {
  if (currentVerb.value === "trace") return "trace";
  return activeInspectorHeader.value?.impactLineageMode ?? "impact";
});

interface Verb {
  id: "trace" | "impact" | "explain" | "simulate" | "act" | "assist";
  label: string;
  live: boolean;
  note: string;
}

const verbs = computed<Verb[]>(() => [
  {
    id: "trace",
    label: "Trace",
    live: Boolean(kernelRef.value),
    note: `Trace shows upstream source records, when available — ${NO_RECORD_REF}`,
  },
  {
    id: "impact",
    label: "Impact",
    live: Boolean(kernelRef.value),
    note: `Impact shows downstream dependent records, when available — ${NO_RECORD_REF}`,
  },
  {
    id: "explain",
    label: "Explain",
    live: Boolean(kernelRef.value),
    note: `Explain shows why an action is permitted or blocked, when available — ${NO_RECORD_REF}`,
  },
  {
    id: "simulate",
    label: "Simulate",
    live: canSimulate.value,
    note: "Participant simulation isn't available in this workspace yet; select a protocol version to check its schedule.",
  },
  {
    id: "act",
    label: "Act",
    live: Boolean(kernelRef.value),
    note: `Act previews available actions and eligibility, when available — ${NO_RECORD_REF}`,
  },
  ...(aiEnabled.value
    ? [
        {
          id: "assist" as const,
          label: "Assist",
          live: Boolean(kernelRef.value),
          note: `Assist reviews related proposals for this record, when available — ${NO_RECORD_REF}`,
        },
      ]
    : []),
]);

const explained = ref<KernelCommand | null>(null);
const shownVerb = ref<string | null>(null);
const toolbarRef = ref<HTMLElement | null>(null);
const toolbarItemIds = computed(() => [
  "verb-details",
  ...verbs.value.map((verb) => `verb-${verb.id}`),
]);
const { getTabIndex, setActiveId } = useRovingToolbar(toolbarRef, {
  itemIds: toolbarItemIds,
});

const simulationResult = ref<KernelSimulationResult | null>(null);
const simulationLoading = ref(false);
const simulationError = ref("");
let simulationRequest = 0;

async function runSimulation() {
  if (!canSimulate.value || !kernelRef.value) return;
  const request = ++simulationRequest;
  simulationLoading.value = true;
  simulationError.value = "";
  try {
    const result = await kernelApi.simulate({
      storedVersionRef: kernelRef.value,
    });
    if (request !== simulationRequest) return;
    simulationResult.value = result;
    if (result.dependents?.groups?.length) {
      ensureNodesResolved(
        result.dependents.groups.flatMap((group) =>
          group.objects.filter((obj) => !isMasked(obj)).map((obj) => obj.ref)
        )
      );
    }
  } catch (e: unknown) {
    if (request !== simulationRequest) return;
    simulationError.value = e instanceof Error ? e.message : String(e);
  } finally {
    if (request === simulationRequest) {
      simulationLoading.value = false;
    }
  }
}

watch(currentVerb, (verb) => {
  if (
    verb === "simulate" &&
    canSimulate.value &&
    !simulationResult.value &&
    !simulationLoading.value
  ) {
    void runSimulation();
  }
});
const shownVerbNote = computed(
  () => verbs.value.find((v) => !v.live && v.id === shownVerb.value)?.note ?? ""
);

function choose(verb: VerbId) {
  currentVerb.value = verb;
  shownVerb.value = null;
  setActiveId(`verb-${verb}`);
}

function focusExplainSection() {
  void nextTick(() => {
    if (typeof document === "undefined") return;
    document.getElementById("kernel-explain")?.focus();
  });
}

function explainCommand(command: KernelCommand) {
  explained.value = command;
  expand();
  choose("explain");
  focusExplainSection();
}

function openProtocolVersionExplain() {
  forceExplainOpen.value = true;
  expand();
  if (parseNodeRef(props.nodeRef)) {
    void versionErrorCanI.probe("protocol.read", props.nodeRef);
  }
  focusExplainSection();
}

function openExplain(options?: {
  fromShortcut?: boolean;
  targetNodeRef?: string | null;
}): boolean {
  const fromShortcut = Boolean(options?.fromShortcut);
  if (kernelRef.value || explained.value) {
    expand();
    if (kernelRef.value) {
      choose("explain");
    }
    focusExplainSection();
    return true;
  }
  if (fromShortcut) {
    return false;
  }
  if (props.protocolVersionError) {
    openProtocolVersionExplain();
    return true;
  }
  if (options?.targetNodeRef) {
    return false;
  }
  forceExplainOpen.value = true;
  expand();
  focusExplainSection();
  return true;
}

let unregisterExplain: (() => void) | null = null;
onMounted(() => {
  unregisterExplain = canvas.registerExplainCoordinator((options) =>
    openExplain(options)
  );
});
onBeforeUnmount(() => {
  unregisterExplain?.();
  unregisterExplain = null;
});

defineExpose({ expand, openExplain });

watch(
  () => [props.nodeRef, props.protocolVersion?.ref, props.genericNodeRef],
  () => {
    simulationRequest++;
    shownVerb.value = null;
    currentVerb.value = "details";
    setActiveId("verb-details");
    explained.value = null;
    forceExplainOpen.value = false;
    versionErrorCanI.reset();
    copiedRef.value = false;
    simulationResult.value = null;
    simulationLoading.value = false;
    simulationError.value = "";
  }
);

watch(
  () => [
    props.assistPrompt,
    props.nodeRef,
    props.protocolVersion?.ref,
    props.genericNodeRef,
  ],
  () => {
    if (props.assistPrompt && aiEnabled.value && kernelRef.value) {
      choose("assist");
    }
  },
  { immediate: true }
);

watch(
  () => [
    props.nodeRef,
    props.participant?.id ?? "",
    props.protocolVersion?.ref ?? "",
    props.protocolVersionError ?? "",
    props.genericNodeRef ?? "",
    props.missing,
    props.assistPrompt ?? "",
    aiEnabled.value,
  ],
  () => {
    manualCollapsed.value = null;
  }
);

watch(
  isCollapsed,
  (collapsed) => {
    emit("update:collapsed", collapsed);
  },
  { immediate: true }
);

watch(aiEnabled, (on) => {
  if (!on && currentVerb.value === "assist") choose("details");
});
</script>

<style scoped>
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

.inspector {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 16px 16px;
  min-width: 0;
}

.inspector.inspector--collapsed {
  padding: 8px 6px;
  align-items: center;
}

.inspector-rail {
  display: flex;
  justify-content: flex-end;
  width: 100%;
}

.inspector.inspector--collapsed .inspector-rail {
  justify-content: center;
}

.inspector-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  min-height: 28px;
  padding: 2px 6px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  font-size: 0.85rem;
  line-height: 1;
  cursor: pointer;
}

.inspector-toggle:hover {
  background: var(--neutral-light);
  color: var(--primary);
}

.inspector-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.inspector-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.inspector-kind {
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  overflow-wrap: anywhere;
}

.inspector-title {
  margin: 0;
  font-size: 1.15rem;
  overflow-wrap: anywhere;
}

.inspector-title--code {
  font-family: var(--semantic-font-mono);
}

.inspector-ref,
:deep(.inspector-ref) {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}

.inspector-ref-details,
:deep(.inspector-ref-details) {
  margin-top: 2px;
  font-size: 0.78rem;
  color: var(--neutral-dark);
  min-width: 0;
  max-width: 100%;
}

.inspector-ref-summary,
:deep(.inspector-ref-summary) {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  max-width: 100%;
  min-width: 0;
  overflow-wrap: anywhere;
  cursor: pointer;
  user-select: none;
}

:deep(.inspector-details-pane) {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

:deep(.details) {
  display: grid;
  grid-template-columns: minmax(0, max-content) minmax(0, 1fr);
  gap: 6px 12px;
  margin: 0;
  min-width: 0;
  max-width: 100%;
}

:deep(.details dt) {
  color: var(--neutral-dark);
  min-width: 0;
  overflow-wrap: anywhere;
}

:deep(.details dd) {
  margin: 0;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

@media (max-width: 640px) {
  :deep(.details) {
    grid-template-columns: minmax(0, 1fr);
  }
}

.numeric,
:deep(.numeric) {
  font-variant-numeric: tabular-nums;
}

:deep(.inspector-empty) {
  margin: 0;
  color: var(--neutral-dark);
}

.inspector-ref-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  margin-top: 4px;
}

.btn-copy-ref {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: transparent;
  color: inherit;
  font-size: var(--font-size-2xs);
  cursor: pointer;
  box-sizing: border-box;
}

.verbs {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.verb {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 4px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: inherit;
  font-size: 0.85rem;
  cursor: pointer;
  box-sizing: border-box;
}

.verb[aria-disabled="true"] {
  color: var(--neutral-dark);
  border-style: dashed;
}

.verb-current {
  background: var(--accent-bg);
  border-color: var(--accent);
}

.verb-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.inspector-empty {
  margin: 0;
  color: var(--neutral-dark);
}

.sdv {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}

.sdv h3 {
  margin: 0;
  font-size: 0.95rem;
}
</style>
