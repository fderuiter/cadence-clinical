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
      <template v-if="hasSelection">
        <header class="inspector-header">
          <span class="inspector-kind">{{ headerKind }}</span>
          <h2 id="canvas-inspector-title" class="inspector-title">
            {{ headerTitle }}
          </h2>
          <details v-if="activeNodeRef" class="inspector-ref-details">
            <summary class="inspector-ref-summary">Record reference</summary>
            <div class="inspector-ref-row">
              <code id="canvas-inspector-ref" class="inspector-ref">{{
                activeNodeRef
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

        <div class="verbs" role="toolbar" aria-label="Verbs">
          <button
            id="verb-details"
            type="button"
            class="verb"
            :class="{ 'verb-current': currentVerb === 'details' }"
            :aria-pressed="currentVerb === 'details'"
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
        />

        <KernelLineagePanel
          v-else-if="
            (currentVerb === 'trace' || currentVerb === 'impact') && kernelRef
          "
          :node-ref="kernelRef"
          :mode="lineageMode"
        />

        <section
          v-else-if="currentVerb === 'explain' && kernelRef"
          id="kernel-explain"
          class="sdv"
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
          <h3 id="kernel-simulate-title">Schedule gap check</h3>
          <p v-if="simulationLoading" class="inspector-empty">
            Checking schedule matrix and visit windows…
          </p>
          <div v-else-if="simulationError" class="inspector-empty" role="alert">
            <p class="sdv-status">Schedule check couldn't be completed.</p>
            <details class="inspector-ref-details">
              <summary class="inspector-ref-summary">Technical details</summary>
              <code id="kernel-simulate-error" class="inspector-ref">{{
                simulationError
              }}</code>
            </details>
          </div>
          <div v-else-if="simulationResult" class="simulation-details">
            <h4 style="margin-top: 4px; margin-bottom: 6px">
              Schedule findings
            </h4>
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
              style="font-size: 0.85rem; color: var(--semantic-color-success)"
            >
              ✓ No schedule matrix gaps detected.
            </p>
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

        <dl v-else-if="protocolVersion" id="inspector-version" class="details">
          <dt>Status</dt>
          <dd id="inspector-version-status">
            {{ protocolVersion.status }}
          </dd>
          <dt>Amends</dt>
          <dd>
            <NodeRefChip
              v-if="protocolVersion.parent_version"
              :node-ref="protocolVersion.parent_version"
            />
            <template v-else
              >Nothing: this is the study's first version</template
            >
          </dd>
          <dt>Amendment intent</dt>
          <dd>{{ protocolVersion.amendment_intent || "None recorded" }}</dd>
          <template v-if="protocolVersion.approval_attestation">
            <dt>Approved by</dt>
            <dd>
              <NodeRefChip :node-ref="protocolVersion.approval_attestation" />
            </dd>
          </template>
          <template v-if="protocolVersion.withdrawal_reason">
            <dt>Withdrawn because</dt>
            <dd>{{ protocolVersion.withdrawal_reason }}</dd>
          </template>
          <dt>Verification</dt>
          <dd>
            <details
              v-if="protocolVersion.content_hash"
              class="inspector-ref-details"
            >
              <summary class="inspector-ref-summary">Content digest</summary>
              <code class="inspector-ref">{{
                protocolVersion.content_hash
              }}</code>
            </details>
            <template v-else>Not sealed yet</template>
          </dd>
          <template v-if="designSummary">
            <dt>Design forms</dt>
            <dd id="inspector-version-forms">
              {{ designSummary.form_count }}
              {{ designSummary.form_count === 1 ? "form" : "forms" }} ({{
                designSummary.item_count
              }}
              {{ designSummary.item_count === 1 ? "item" : "items" }})
            </dd>
            <template
              v-if="
                designSummary.import_notes &&
                designSummary.import_notes.length > 0
              "
            >
              <dt>Import notes</dt>
              <dd id="inspector-version-notes">
                {{ designSummary.import_notes.length }} recorded
              </dd>
            </template>
          </template>
        </dl>

        <template v-else-if="participant">
          <dl class="details">
            <dt>State</dt>
            <dd id="inspector-state">
              {{ participantStateLabel(participant.status) }}
            </dd>
            <dt>Study</dt>
            <dd>{{ participant.study_id }}</dd>
            <dt>Site</dt>
            <dd id="inspector-site">
              {{ participant.site_id || "Not assigned" }}
            </dd>
            <dt>Protocol version</dt>
            <dd>{{ participant.active_protocol_version || "Not recorded" }}</dd>
            <dt>Enrollment number</dt>
            <dd class="numeric">
              {{ participant.enrollment_index }}
            </dd>
          </dl>

          <ParticipantStateChange
            v-if="canManageParticipants"
            :participant="participant"
          />

          <router-link
            v-if="recordLocation"
            id="inspector-open-record"
            class="inspector-link"
            :to="recordLocation"
          >
            Open participant's data
          </router-link>
          <router-link
            v-if="dataEntryLocation"
            id="inspector-open-data-entry"
            class="inspector-link"
            :to="dataEntryLocation"
          >
            Open in data entry
          </router-link>
          <router-link
            v-if="handoffLocation"
            id="inspector-handoff-standalone"
            data-testid="inspector-ecrf-handoff"
            class="inspector-link"
            :to="handoffLocation"
          >
            Open standalone form view
          </router-link>
        </template>

        <template v-else-if="genericDisplay">
          <p
            v-if="genericNodeLoading"
            id="inspector-generic-loading"
            class="inspector-empty"
            role="status"
          >
            Loading record details…
          </p>
          <p
            v-else-if="genericDisplay.masked"
            id="inspector-generic-masked"
            class="inspector-empty"
          >
            Details for this {{ genericDisplay.kindLabel.toLowerCase() }} are
            hidden ({{ genericDisplay.maskReason }}).
          </p>
          <dl v-else id="inspector-generic" class="details">
            <dt>Record type</dt>
            <dd id="inspector-generic-kind">{{ genericDisplay.kindLabel }}</dd>
            <template v-if="genericDisplay.code">
              <dt>Code</dt>
              <dd id="inspector-generic-code">{{ genericDisplay.code }}</dd>
            </template>
            <template
              v-if="
                genericDisplay.title &&
                genericDisplay.title !== genericDisplay.code
              "
            >
              <dt>Title</dt>
              <dd id="inspector-generic-title">{{ genericDisplay.title }}</dd>
            </template>
            <template v-if="genericDisplay.status">
              <dt>Status</dt>
              <dd id="inspector-generic-status">{{ genericDisplay.status }}</dd>
            </template>
            <dt>Study</dt>
            <dd>{{ studyCode }}</dd>
          </dl>
        </template>
      </template>

      <template v-else-if="protocolVersionError">
        <h2 id="canvas-inspector-title" class="inspector-title">
          Protocol version
        </h2>
        <div id="inspector-version-error" class="inspector-empty" role="alert">
          <p class="sdv-status">
            This protocol version couldn't be loaded. Check that it exists and
            is visible to your account.
          </p>
          <SupportDetails :detail="protocolVersionError" />
        </div>
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
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type { SubjectSummary } from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";
import ParticipantStateChange from "./ParticipantStateChange.vue";
import KernelActPanel from "./KernelActPanel.vue";
import KernelExplanation from "./KernelExplanation.vue";
import KernelLineagePanel from "./KernelLineagePanel.vue";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";
import {
  kernelApi,
  type KernelCommand,
  type KernelNode,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
  type KernelSimulationResult,
} from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  formatCommandTitle,
  formatFieldPath,
  getNodeDisplay,
} from "../../canvas/nodeResolver";
import { useAiAssistance } from "../../ai/aiAssistance";
import AssistPanel from "../ai/AssistPanel.vue";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): displays the selected object's
 * human-readable kind and code/title first, with the canonical NodeRef tucked inside a copyable secondary disclosure,
 * and exposes Trace, Impact, Explain, Simulate, Act, and Assist verbs for the active selection.
 */
const props = defineProps<{
  studyCode: string;
  /** The selected participant, when the canvas has loaded it. */
  participant: SubjectSummary | null;
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
  /** A question from the palette's Ask entry: opens Assist with it (ADR-2635). */
  assistPrompt?: string;
}>();

const emit = defineEmits<{
  (e: "update:collapsed", collapsed: boolean): void;
}>();

const { enabled: aiEnabled } = useAiAssistance();

const genericDisplay = computed(() => {
  if (!props.genericNodeRef || props.missing) return null;
  return getNodeDisplay(props.genericNodeRef, props.genericNode?.title ?? null);
});

const hasSelection = computed(() =>
  Boolean(props.participant || props.protocolVersion || genericDisplay.value)
);

const headerKind = computed(() => {
  if (props.participant) return "Participant";
  if (props.protocolVersion) return "Protocol version";
  return genericDisplay.value?.kindLabel ?? "Record";
});

const headerTitle = computed(() => {
  if (props.participant) return props.participant.subject_id;
  if (props.protocolVersion) return `Version ${props.protocolVersion.number}`;
  return genericDisplay.value?.label ?? "Record";
});

const activeNodeRef = computed(() => {
  if (props.participant) return props.nodeRef;
  if (props.protocolVersion?.ref) return props.protocolVersion.ref;
  if (props.genericNode?.ref) return props.genericNode.ref;
  return genericDisplay.value?.ref ?? "";
});

const copiedRef = ref(false);
async function copyInspectorRef() {
  if (!activeNodeRef.value) return;
  try {
    await globalThis.navigator?.clipboard?.writeText?.(activeNodeRef.value);
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
    hasSelection.value ||
    props.protocolVersionError ||
    props.missing ||
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

defineExpose({ expand });

const NO_RECORD_REF =
  "this selection doesn't have a linked study reference yet.";

/** The selection's canonical reference when Trace, Impact, Explain, Act, and Assist can query it. */
const kernelRef = computed(() => {
  if (props.participant) {
    return parseNodeRef(props.nodeRef) ? props.nodeRef : "";
  }
  const version = props.protocolVersion?.ref;
  if (version && parseNodeRef(version)) return version;
  if (
    genericDisplay.value &&
    !genericDisplay.value.masked &&
    parseNodeRef(genericDisplay.value.ref)
  ) {
    return genericDisplay.value.ref;
  }
  return "";
});

/** Impact on an amendment is the amendment's impact (K-PROT-2); on anything else, what depends on it. */
const lineageMode = computed(() => {
  if (currentVerb.value === "trace") return "trace";
  return props.protocolVersion?.parent_version && !props.participant
    ? "amendment"
    : "impact";
});

/** Simulate runs in-memory evaluation and schedule matrix gap checks on a stored protocol version. */
const canSimulate = computed(() =>
  Boolean(
    !props.participant &&
    props.protocolVersion?.ref &&
    parseNodeRef(props.protocolVersion.ref)?.kind === "protocol_version"
  )
);

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

type VerbId =
  "details" | "trace" | "impact" | "act" | "explain" | "simulate" | "assist";
const currentVerb = ref<VerbId>("details");
const explained = ref<KernelCommand | null>(null);
const shownVerb = ref<string | null>(null);

const simulationResult = ref<KernelSimulationResult | null>(null);
const simulationLoading = ref(false);
const simulationError = ref("");
let simulationRequest = 0;
const designSummary = ref<KernelProtocolDraftSummary | null>(null);
let designSummaryReq = 0;

watch(
  () => props.protocolVersion?.ref,
  async (versionRef) => {
    const req = ++designSummaryReq;
    designSummary.value = null;
    if (
      !versionRef ||
      typeof kernelApi.getProtocolDesignSummary !== "function"
    ) {
      return;
    }
    try {
      const summary = await kernelApi.getProtocolDesignSummary(versionRef);
      if (req === designSummaryReq) designSummary.value = summary;
    } catch {
      // Optional summary when protocol content exists.
    }
  },
  { immediate: true }
);

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
}

function explainCommand(command: KernelCommand) {
  explained.value = command;
  choose("explain");
}

watch(
  () => [props.nodeRef, props.protocolVersion?.ref, props.genericNodeRef],
  () => {
    simulationRequest++;
    shownVerb.value = null;
    currentVerb.value = "details";
    explained.value = null;
    copiedRef.value = false;
    simulationResult.value = null;
    simulationLoading.value = false;
    simulationError.value = "";
  }
);

// A question from the palette opens Assist on the selection; this runs after the reset above.
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

// Turning AI assistance off closes Assist.
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
  font-family: monospace;
}

.reasoning-modal-s-2 {
  border-left: 3px solid var(--warning);
  padding: 6px 8px;
  margin-bottom: 8px;
  background: var(--semantic-color-warning-bg-soft);
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
}

.inspector-kind {
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.inspector-title {
  margin: 0;
  font-size: 1.15rem;
}

.inspector-ref {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}

.inspector-ref-details {
  margin-top: 2px;
  font-size: 0.78rem;
  color: var(--neutral-dark);
}

.inspector-ref-summary {
  cursor: pointer;
  user-select: none;
}

.inspector-ref-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  margin-top: 4px;
}

.btn-copy-ref {
  padding: 2px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: transparent;
  color: inherit;
  font-size: 0.72rem;
  cursor: pointer;
}

.verbs {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.verb {
  padding: 4px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: inherit;
  font-size: 0.85rem;
  cursor: pointer;
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

.details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 6px 12px;
  margin: 0;
}

.details dt {
  color: var(--neutral-dark);
}

.details dd {
  margin: 0;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.inspector-empty {
  margin: 0;
  color: var(--neutral-dark);
}

.inspector-link {
  align-self: flex-start;
  color: var(--accent);
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

.sdv-status {
  margin: 0;
}

.sdv textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.sdv button {
  align-self: flex-start;
}
</style>
