<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    aria-labelledby="canvas-inspector-title"
  >
    <template v-if="observation || participant">
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
        :mode="currentVerb"
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
          <dd class="numeric">{{ participant.enrollment_index }}</dd>
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
import type { KernelCommand } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): the selected object's kind,
 * title and node reference, its verbs and its details. Trace, Impact, Act and Explain read the kernel's lineage,
 * dependents, command catalogue and explanations for a selected participant (ADR-2540); Simulate, and those four
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

/** The selection's kernel reference, when the kernel can answer for it: a participant, not an observation. */
const kernelRef = computed(() =>
  !props.observation && props.participant && parseNodeRef(props.nodeRef)
    ? props.nodeRef
    : ""
);

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
    note: "Simulate needs the evaluate-only simulation engine over the API (ADR-2480, #4866), which the kernel host doesn't serve yet.",
  },
  {
    id: "act",
    label: "Act",
    live: Boolean(kernelRef.value),
    note: `Act offers the kernel's commands with your dry-run decisions (ADR-2483), but ${NO_KERNEL_REF}`,
  },
]);

type VerbId = "details" | "trace" | "impact" | "act" | "explain";
const currentVerb = ref<VerbId>("details");
const explained = ref<KernelCommand | null>(null);
const shownVerb = ref<string | null>(null);
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
  () => [props.nodeRef, props.observation?.id],
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
