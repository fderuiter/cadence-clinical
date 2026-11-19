<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    aria-labelledby="canvas-inspector-title"
  >
    <template v-if="participant || protocolVersion">
      <header v-if="participant" class="inspector-header">
        <span class="inspector-kind">Participant</span>
        <h2 id="canvas-inspector-title" class="inspector-title">
          {{ participant.subject_id }}
        </h2>
        <code id="canvas-inspector-ref" class="inspector-ref">{{
          nodeRef
        }}</code>
      </header>
      <header v-else-if="protocolVersion" class="inspector-header">
        <span class="inspector-kind">Protocol version</span>
        <h2 id="canvas-inspector-title" class="inspector-title">
          Version {{ protocolVersion.number }}
        </h2>
        <code id="canvas-inspector-ref" class="inspector-ref">{{
          protocolVersion.ref
        }}</code>
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
            :aria-describedby="shownVerb === verb.id ? 'verb-note' : undefined"
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
          {{ explained ? `Why: ${explained.command}` : "Explain" }}
        </h3>
        <KernelExplanation
          v-if="explained"
          :explanation="explained.decision.explanation"
        />
        <p v-else id="kernel-explain-empty" class="inspector-empty">
          Choose Explain beside a command under Act to see the kernel's reasons
          for its decision.
        </p>
      </section>

      <section
        v-else-if="currentVerb === 'simulate' && kernelRef"
        id="kernel-simulate"
        class="sdv"
        aria-labelledby="kernel-simulate-title"
      >
        <h3 id="kernel-simulate-title">Simulation & Matrix Gap Analysis</h3>
        <p v-if="simulationLoading" class="inspector-empty">
          Running pure in-memory evaluation and matrix gap analysis...
        </p>
        <p
          v-else-if="simulationError"
          class="inspector-empty"
          style="color: var(--danger, #dc3545)"
        >
          {{ simulationError }}
        </p>
        <div v-else-if="simulationResult" class="simulation-details">
          <div class="seal-container reasoning-modal-s-1">
            <div>
              <strong>Input Hash Seal:</strong>
              {{ simulationResult.input_hash }}
            </div>
            <div>
              <strong>Result Hash Seal:</strong>
              {{ simulationResult.result_hash }}
            </div>
          </div>

          <h4 style="margin-top: 12px; margin-bottom: 6px">
            Schedule Matrix Gap Findings
          </h4>
          <div
            v-if="
              simulationResult.matrix_gaps &&
              simulationResult.matrix_gaps.length
            "
            class="gaps-list"
          >
            <div
              v-for="(gap, idx) in simulationResult.matrix_gaps"
              :key="idx"
              class="gap-card reasoning-modal-s-2"
            >
              <div style="font-weight: 600; font-size: 0.85rem">
                <span class="gap-badge">{{ gap.severity }}</span>
                {{ gap.kind }} (<code>{{ gap.json_pointer_path }}</code
                >)
              </div>
              <div style="font-size: 0.8rem; margin-top: 2px">
                {{ gap.description }}
              </div>
              <div
                v-if="gap.recommendation"
                style="font-size: 0.75rem; color: #555; margin-top: 2px"
              >
                <em>Rec: {{ gap.recommendation }}</em>
              </div>
            </div>
          </div>
          <p v-else style="font-size: 0.85rem; color: #28a745">
            ✓ No schedule matrix gaps detected.
          </p>
        </div>
      </section>

      <dl v-else-if="protocolVersion" id="inspector-version" class="details">
        <dt>Status</dt>
        <dd id="inspector-version-status">
          {{ protocolVersion.status }}
        </dd>
        <dt>Amends</dt>
        <dd>
          {{
            protocolVersion.parent_version
              ? protocolVersion.parent_version
              : "Nothing: this is the study's first version"
          }}
        </dd>
        <dt>Amendment intent</dt>
        <dd>{{ protocolVersion.amendment_intent || "None recorded" }}</dd>
        <dt>Content hash</dt>
        <dd class="inspector-ref">
          {{ protocolVersion.content_hash || "Not sealed yet" }}
        </dd>
        <template v-if="protocolVersion.approval_attestation">
          <dt>Approved by</dt>
          <dd class="inspector-ref">
            {{ protocolVersion.approval_attestation }}
          </dd>
        </template>
        <template v-if="protocolVersion.withdrawal_reason">
          <dt>Withdrawn because</dt>
          <dd>{{ protocolVersion.withdrawal_reason }}</dd>
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
      </template>
    </template>

    <template v-else-if="protocolVersionError">
      <h2 id="canvas-inspector-title" class="inspector-title">
        Protocol version
      </h2>
      <p id="inspector-version-error" class="inspector-empty" role="alert">
        The kernel couldn't answer: {{ protocolVersionError }}
      </p>
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
        Select a participant to see its details here.
      </p>
      <AssistPanel
        v-if="assistPrompt && aiEnabled"
        :study-code="studyCode"
        :prefill="assistPrompt"
      />
    </template>
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
import type { KernelCommand, KernelProtocolVersion } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import { useAiAssistance } from "../../ai/aiAssistance";
import AssistPanel from "../ai/AssistPanel.vue";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): the selected object's kind,
 * title and node reference, its verbs and its details. Trace, Impact, Act and Explain read the kernel's lineage,
 * dependents, command catalogue and explanations for a selected participant or protocol version (ADR-2540); on an
 * amendment, Impact is the amendment's impact (K-PROT-2). Simulate is shown disabled, saying what it waits on. Nothing
 * here is computed in the browser (§6.1).
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
  /** Where the participant's record opens; absent when it is already open. */
  recordLocation?: RouteLocationRaw | null;
  /** Where the participant's forms workspace opens inside Clinical Canvas; absent when already open. */
  dataEntryLocation?: RouteLocationRaw | null;
  /** Whether to offer changing the participant's state (Execution enforces who may, #4943). */
  canManageParticipants?: boolean;
  /** A question from the palette's Ask entry: opens Assist with it (ADR-2635). */
  assistPrompt?: string;
}>();

const { enabled: aiEnabled } = useAiAssistance();

const NO_KERNEL_REF =
  "this selection has no kernel reference, so the kernel can't answer for it.";

/** The selection's kernel reference, when the kernel can answer for it: a participant or protocol version. */
const kernelRef = computed(() => {
  if (props.participant)
    return parseNodeRef(props.nodeRef) ? props.nodeRef : "";
  const version = props.protocolVersion?.ref;
  return version && parseNodeRef(version) ? version : "";
});

/** Impact on an amendment is the amendment's impact (K-PROT-2); on anything else, what depends on it. */
const lineageMode = computed(() => {
  if (currentVerb.value === "trace") return "trace";
  return props.protocolVersion?.parent_version && !props.participant
    ? "amendment"
    : "impact";
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
    note: `Trace walks the kernel's lineage (ADR-2471), but ${NO_KERNEL_REF}`,
  },
  {
    id: "impact",
    label: "Impact",
    live: Boolean(kernelRef.value),
    note: `Impact counts what depends on this in the kernel's lineage (Canvas D2), but ${NO_KERNEL_REF}`,
  },
  {
    id: "explain",
    label: "Explain",
    live: Boolean(kernelRef.value),
    note: `Explain shows the kernel's reasons (ADR-2482), but ${NO_KERNEL_REF}`,
  },
  {
    id: "simulate",
    label: "Simulate",
    live: false,
    note: "Simulate waits on engine simulation endpoints (#4888).",
  },
  {
    id: "act",
    label: "Act",
    live: Boolean(kernelRef.value),
    note: `Act offers the kernel's commands with your dry-run decisions (ADR-2483), but ${NO_KERNEL_REF}`,
  },
  // Assist exists only while the viewer has AI assistance on; off, the inspector is exactly as before (ADR-2635).
  ...(aiEnabled.value
    ? [
        {
          id: "assist" as const,
          label: "Assist",
          live: Boolean(kernelRef.value),
          note: `Assist reads about this object in the kernel (ADR-2635), but ${NO_KERNEL_REF}`,
        },
      ]
    : []),
]);

type VerbId =
  "details" | "trace" | "impact" | "act" | "explain" | "simulate" | "assist";
const currentVerb = ref<VerbId>("details");
const explained = ref<KernelCommand | null>(null);
const shownVerb = ref<string | null>(null);

const simulationResult = ref<any>(null);
const simulationLoading = ref(false);
const simulationError = ref("");

async function runSimulation() {
  if (!kernelRef.value) return;
  simulationLoading.value = true;
  simulationError.value = "";
  try {
    simulationResult.value = await kernelApi.simulate({
      storedVersionRef: kernelRef.value,
    });
  } catch (e: any) {
    simulationError.value = e.message || String(e);
  } finally {
    simulationLoading.value = false;
  }
}

watch(currentVerb, (verb) => {
  if (
    verb === "simulate" &&
    !simulationResult.value &&
    !simulationLoading.value
  ) {
    runSimulation();
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
  () => [props.nodeRef, props.protocolVersion?.ref],
  () => {
    shownVerb.value = null;
    currentVerb.value = "details";
    explained.value = null;
  }
);

// A question from the palette opens Assist on the selection; this runs after the reset above.
watch(
  () => [props.assistPrompt, props.nodeRef, props.protocolVersion?.ref],
  () => {
    if (props.assistPrompt && aiEnabled.value && kernelRef.value) {
      choose("assist");
    }
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
  background: var(--neutral-light, #f8f9fa);
  padding: 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-family: monospace;
}

.reasoning-modal-s-2 {
  border-left: 3px solid var(--warning, #ffc107);
  padding: 6px 8px;
  margin-bottom: 8px;
  background: #fff8e1;
}

.inspector {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 16px;
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
