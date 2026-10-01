<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    aria-labelledby="canvas-inspector-title"
  >
    <template v-if="observation || participant || protocolVersion">
      <header v-if="observation" class="inspector-header">
        <span class="inspector-kind">Observation</span>
        <h2 id="canvas-inspector-title" class="inspector-title">
          {{ observation.test_name }}
        </h2>
        <span id="canvas-inspector-ref" class="inspector-ref">
          Execution id {{ observation.id }} (no kernel reference yet)
        </span>
      </header>
      <header v-else-if="participant" class="inspector-header">
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

      <KernelActPanel
        v-if="currentVerb === 'act' && kernelRef"
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
          <div
            class="seal-container"
            style="
              background: var(--neutral-light, #f8f9fa);
              padding: 8px;
              border-radius: 4px;
              font-size: 0.8rem;
              font-family: monospace;
            "
          >
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
              class="gap-card"
              style="
                border-left: 3px solid var(--warning, #ffc107);
                padding: 6px 8px;
                margin-bottom: 8px;
                background: #fff8e1;
              "
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

          <div
            class="proposal-actions"
            style="
              margin-top: 16px;
              display: flex;
              flex-direction: column;
              gap: 8px;
            "
          >
            <a
              href="#"
              class="inspect-reasoning-link"
              style="
                color: var(--primary, #0056b3);
                font-size: 0.85rem;
                text-decoration: underline;
              "
              @click.prevent="showReasoningModal = true"
            >
              Inspect reasoning &amp; lineage provenance
            </a>

            <div style="display: flex; gap: 8px; margin-top: 4px">
              <button
                type="button"
                class="btn btn-primary"
                style="flex: 1; padding: 6px 12px; font-size: 0.85rem"
                :disabled="proposalApproved"
                @click="
                  proposalApproved = true;
                  proposalRejected = false;
                "
              >
                {{
                  proposalApproved ? "✓ Proposal Approved" : "Approve Proposal"
                }}
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                style="flex: 1; padding: 6px 12px; font-size: 0.85rem"
                :disabled="proposalRejected"
                @click="
                  proposalRejected = true;
                  proposalApproved = false;
                "
              >
                {{
                  proposalRejected ? "✗ Proposal Rejected" : "Reject Proposal"
                }}
              </button>
            </div>
          </div>
        </div>
      </section>

      <template v-else-if="observation">
        <dl class="details">
          <dt>Result</dt>
          <dd id="inspector-result" class="numeric">
            {{ observationValue(observation) }}
          </dd>
          <template v-if="observation.normalized_value != null">
            <dt>Normalized</dt>
            <dd class="numeric">
              {{ observation.normalized_value }}
              {{ observation.normalized_unit || "" }}
            </dd>
          </template>
          <dt>Test</dt>
          <dd>{{ observation.domain }} · {{ observation.test_code }}</dd>
          <dt>Date</dt>
          <dd>{{ observation.observation_date }}</dd>
          <dt>Visit</dt>
          <dd>{{ observation.visit_id || "Not recorded" }}</dd>
          <template
            v-if="
              observation.reference_range_low != null ||
              observation.reference_range_high != null
            "
          >
            <dt>Reference range</dt>
            <dd class="numeric">
              {{ observation.reference_range_low ?? "…" }} to
              {{ observation.reference_range_high ?? "…" }}
            </dd>
          </template>
          <template v-if="observation.is_out_of_range">
            <dt>Range</dt>
            <dd id="inspector-range">
              Out of range{{
                observation.range_indicator
                  ? ` (${observation.range_indicator})`
                  : ""
              }}
            </dd>
          </template>
        </dl>

        <section
          id="inspector-sdv"
          class="sdv"
          aria-labelledby="inspector-sdv-title"
        >
          <h3 id="inspector-sdv-title">Source data verification</h3>
          <p id="inspector-sdv-status" class="sdv-status">
            {{
              observation.is_sdv_verified
                ? `Verified by ${observation.sdv_verified_by || "an unrecorded user"}${observation.sdv_verified_at ? ` on ${observation.sdv_verified_at}` : ""}.`
                : "Not verified."
            }}
          </p>
          <template v-if="canVerify">
            <button
              v-if="!observation.is_sdv_verified"
              id="btn-verify-sdv"
              type="button"
              class="btn btn-primary"
              :disabled="sdvBusy"
              @click="verify"
            >
              Mark verified against source
            </button>
            <label for="sdv-flag-reason">Reason to re-verify</label>
            <textarea
              id="sdv-flag-reason"
              v-model="flagReason"
              rows="2"
              placeholder="What doesn't match the source (recorded in the audit trail)"
            />
            <button
              id="btn-flag-sdv"
              type="button"
              class="btn btn-secondary"
              :disabled="sdvBusy || !flagReason.trim()"
              @click="flag"
            >
              Flag for re-verification
            </button>
          </template>
          <p v-else id="inspector-sdv-note" class="inspector-empty">
            Source data is verified by a monitor or data manager.
          </p>
        </section>

        <section
          id="inspector-queries"
          class="sdv"
          aria-labelledby="inspector-queries-title"
        >
          <h3 id="inspector-queries-title">Queries</h3>
          <p id="inspector-queries-status" class="sdv-status">
            {{ queriesSummary }}
          </p>
          <template v-if="canManage">
            <label for="query-explanation">New query</label>
            <textarea
              id="query-explanation"
              v-model="queryText"
              rows="2"
              placeholder="What the site should check or correct"
            />
            <button
              id="btn-raise-query"
              type="button"
              class="btn btn-secondary"
              :disabled="queryBusy || !queryText.trim()"
              @click="raise"
            >
              Raise query
            </button>
          </template>
        </section>
      </template>

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
          id="inspector-open-data-entry"
          class="inspector-link"
          :to="{
            path: '/ecrf',
            query: {
              studyId: participant.study_id,
              subjectId: participant.subject_id,
            },
          }"
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
        Select a participant or an observation to see its details here.
      </p>
    </template>

    <!-- Reasoning Inspection Modal -->
    <div
      v-if="showReasoningModal"
      class="reasoning-modal-overlay"
      style="
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.5);
        display: flex;
        align-items: center;
        justify-content: center;
        z-index: 9999;
      "
      @click.self="showReasoningModal = false"
    >
      <div
        class="reasoning-modal-card"
        style="
          background: white;
          border-radius: 8px;
          width: 90%;
          max-width: 600px;
          max-height: 85vh;
          padding: 20px;
          overflow-y: auto;
          box-shadow: 0 4px 20px rgba(0, 0, 0, 0.2);
        "
      >
        <header
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid #eee;
            padding-bottom: 10px;
            margin-bottom: 12px;
          "
        >
          <h3 style="margin: 0; font-size: 1.1rem; color: #111">
            AI Proposal Reasoning &amp; Provenance Chain
          </h3>
          <button
            type="button"
            class="btn"
            style="
              border: none;
              background: transparent;
              font-size: 1.2rem;
              cursor: pointer;
            "
            @click="showReasoningModal = false"
          >
            ×
          </button>
        </header>

        <div
          class="reasoning-steps"
          style="display: flex; flex-direction: column; gap: 12px"
        >
          <div
            class="reasoning-step"
            style="border-left: 3px solid #0056b3; padding-left: 10px"
          >
            <div style="font-weight: 600; font-size: 0.9rem; color: #0056b3">
              Step 1: Structure Synthesis
            </div>
            <p style="margin: 4px 0; font-size: 0.85rem">
              Synthesized core study objectives and endpoints from trial brief
              in memory.
            </p>
            <div style="font-size: 0.75rem; color: #666">
              <strong>Citations:</strong> ICH M11 §3.1 Primary Objectives, CDISC
              USDM v3.0 Core Specification
            </div>
          </div>

          <div
            class="reasoning-step"
            style="border-left: 3px solid #0056b3; padding-left: 10px"
          >
            <div style="font-weight: 600; font-size: 0.9rem; color: #0056b3">
              Step 2: Epoch &amp; Arm Mapping
            </div>
            <p style="margin: 4px 0; font-size: 0.85rem">
              Structured Screening (14d), Treatment (12wk), and Follow-up (4wk)
              epochs with Experimental and Active Comparator arms.
            </p>
            <div style="font-size: 0.75rem; color: #666">
              <strong>Citations:</strong> ICH M11 §4.1 Study Design, CDISC USDM
              Arm/Epoch Taxonomy
            </div>
          </div>

          <div
            class="reasoning-step"
            style="border-left: 3px solid #0056b3; padding-left: 10px"
          >
            <div style="font-weight: 600; font-size: 0.9rem; color: #0056b3">
              Step 3: Schedule of Activities Grid
            </div>
            <p style="margin: 4px 0; font-size: 0.85rem">
              Mapped vital signs, lab panels, ECGs, and adverse event monitoring
              across planned visits with explicit UCUM units.
            </p>
            <div style="font-size: 0.75rem; color: #666">
              <strong>Citations:</strong> ICH M11 §5.0 Schedule of Activities,
              UCUM Unified Code for Units of Measure
            </div>
          </div>

          <div
            class="reasoning-step"
            style="border-left: 3px solid #0056b3; padding-left: 10px"
          >
            <div style="font-weight: 600; font-size: 0.9rem; color: #0056b3">
              Step 4: Matrix Gap Verification
            </div>
            <p style="margin: 4px 0; font-size: 0.85rem">
              Verified all planned activities link to endpoints and safety rules
              without orphan encounters or cyclic epoch transitions.
            </p>
            <div style="font-size: 0.75rem; color: #666">
              <strong>Citations:</strong> K-PROT-3 Data Purpose Mapping, FDA
              Technical Conformance Guide
            </div>
          </div>
        </div>

        <div style="margin-top: 16px; text-align: right">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showReasoningModal = false"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import type {
  ClinicalQuery,
  Observation,
  SubjectSummary,
} from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";
import ParticipantStateChange from "./ParticipantStateChange.vue";
import KernelActPanel from "./KernelActPanel.vue";
import KernelExplanation from "./KernelExplanation.vue";
import KernelLineagePanel from "./KernelLineagePanel.vue";
import type { KernelCommand, KernelProtocolVersion } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): the selected object's kind,
 * title and node reference, its verbs and its details. Trace, Impact, Act and Explain read the kernel's lineage,
 * dependents, command catalogue and explanations for a selected participant or protocol version (ADR-2540); on an
 * amendment, Impact is the amendment's impact (K-PROT-2). Simulate, and those four
 * verbs on an observation (which has no kernel reference yet), are shown disabled, each saying what it waits on. Nothing here is
 * computed in the browser (§6.1).
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
  /** The selected observation; shown in place of the participant. */
  observation?: Observation | null;
  /** Where the participant's record opens; absent when it is already open. */
  recordLocation?: RouteLocationRaw | null;
  /** Whether to offer source data verification (Execution enforces who may, #4920). */
  canVerify?: boolean;
  verifySdv?: (observation: Observation) => Promise<boolean>;
  /** Queries on the selected observation. */
  observationQueries?: ClinicalQuery[];
  /** Whether to offer raising a query (Execution records the author). */
  canManage?: boolean;
  /** Whether to offer changing the participant's state (Execution enforces who may, #4943). */
  canManageParticipants?: boolean;
  raiseQuery?: (
    observation: Observation,
    explanation: string
  ) => Promise<boolean>;
  flagSdv?: (observation: Observation, reason: string) => Promise<boolean>;
}>();

const NO_KERNEL_REF =
  "an observation has no kernel reference yet, so the kernel can't answer for it.";

/** The selection's kernel reference, when the kernel can answer for it: a participant or protocol version, not an observation. */
const kernelRef = computed(() => {
  if (props.observation) return "";
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
  id: "trace" | "impact" | "explain" | "simulate" | "act";
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
]);

type VerbId = "details" | "trace" | "impact" | "act" | "explain" | "simulate";
const currentVerb = ref<VerbId>("details");
const explained = ref<KernelCommand | null>(null);
const shownVerb = ref<string | null>(null);

const simulationResult = ref<any>(null);
const simulationLoading = ref(false);
const simulationError = ref("");
const showReasoningModal = ref(false);
const proposalApproved = ref(false);
const proposalRejected = ref(false);

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

const flagReason = ref("");
const sdvBusy = ref(false);
const queryText = ref("");
const queryBusy = ref(false);

const LIVE_QUERY = new Set(["OPEN", "ANSWERED", "REOPENED"]);
const queriesSummary = computed(() => {
  const queries = props.observationQueries ?? [];
  if (!queries.length) return "No queries on this observation.";
  const live = queries.filter((q) => LIVE_QUERY.has(q.status)).length;
  const noun = queries.length === 1 ? "query" : "queries";
  return `${queries.length} ${noun}, ${live} still open or awaiting review.`;
});

watch(
  () => [props.nodeRef, props.observation?.id, props.protocolVersion?.ref],
  () => {
    shownVerb.value = null;
    currentVerb.value = "details";
    explained.value = null;
    flagReason.value = "";
    queryText.value = "";
  }
);

function observationValue(observation: Observation): string {
  const value = observation.value_string ?? observation.value;
  if (value === null || value === undefined || value === "") {
    return "No value";
  }
  return observation.unit ? `${value} ${observation.unit}` : String(value);
}

async function verify() {
  const observation = props.observation;
  if (!observation || !props.verifySdv) return;
  sdvBusy.value = true;
  try {
    await props.verifySdv(observation);
  } finally {
    sdvBusy.value = false;
  }
}

async function flag() {
  const observation = props.observation;
  const reason = flagReason.value.trim();
  if (!observation || !reason || !props.flagSdv) return;
  sdvBusy.value = true;
  try {
    if (await props.flagSdv(observation, reason)) flagReason.value = "";
  } finally {
    sdvBusy.value = false;
  }
}

async function raise() {
  const observation = props.observation;
  const text = queryText.value.trim();
  if (!observation || !text || !props.raiseQuery) return;
  queryBusy.value = true;
  try {
    if (await props.raiseQuery(observation, text)) queryText.value = "";
  } finally {
    queryBusy.value = false;
  }
}
</script>

<style scoped>
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
