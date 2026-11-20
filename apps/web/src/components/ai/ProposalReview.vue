<template>
  <section
    id="proposal-review"
    class="review"
    :class="{ 'review-ai': agent }"
    aria-labelledby="proposal-review-title"
    :aria-busy="loading"
  >
    <router-link id="proposal-review-back" class="back" :to="backLocation">
      Back to {{ studyCode }} home
    </router-link>

    <p v-if="statusLine" id="proposal-review-status" role="status">
      {{ statusLine }}
    </p>

    <template v-if="proposal && proposal.ref">
      <header class="review-header">
        <span class="review-kind">Proposal</span>
        <h2 id="proposal-review-title">{{ proposal.intent }}</h2>
        <div class="review-tags">
          <AiLabel
            v-if="agent"
            id="proposal-review-ai"
            label="Proposed by an AI agent"
            :model="proposal.model_id"
          />
          <span id="proposal-review-state" class="state" :data-state="status">
            {{ status }}
          </span>
        </div>
      </header>

      <p id="proposal-review-safety" class="safety">
        Nothing in the study changes until a person approves this proposal, and
        approving it does not run its plan: whoever carries it out still acts
        through the kernel as themselves (K-SIM-5).
      </p>

      <dl class="details">
        <dt>Proposed by</dt>
        <dd id="proposal-review-proposer">
          {{ proposerWords }}
        </dd>
        <template v-if="agent">
          <dt>Model</dt>
          <dd id="proposal-review-model" class="mono">
            {{ proposal.model_id || "Not recorded" }}
          </dd>
          <dt>Task configuration</dt>
          <dd id="proposal-review-configuration">
            Not recorded: proposals name their qualified configuration once task
            configurations exist (#5385).
          </dd>
        </template>
        <template v-if="proposal.confidence_score != null">
          <dt>Confidence the proposer gave</dt>
          <dd class="numeric">{{ confidence }}</dd>
        </template>
        <dt>Proposed</dt>
        <dd>{{ when(proposal.proposed_at) }}</dd>
        <dt>{{ status === "Open" ? "Expires" : "Expired or expires" }}</dt>
        <dd>{{ when(proposal.expires_at) }}</dd>
        <dt>Content hash</dt>
        <dd id="proposal-review-hash" class="mono">
          {{ proposal.content_hash }}
        </dd>
        <template v-if="proposal.simulated_impact">
          <dt>Simulation result</dt>
          <dd class="mono">{{ proposal.simulated_impact }}</dd>
        </template>
        <template v-if="proposal.is_escalated">
          <dt>Escalated to</dt>
          <dd>{{ proposal.escalated_to }}: {{ proposal.escalation_reason }}</dd>
        </template>
      </dl>

      <section class="block" aria-labelledby="proposal-review-capabilities">
        <h3 id="proposal-review-capabilities">What carrying it out needs</h3>
        <ul id="proposal-review-capability-list" class="plain">
          <li
            v-for="token in proposal.requested_capabilities ?? []"
            :key="token"
          >
            {{ capabilityWords(token) }} <code>{{ token }}</code>
          </li>
        </ul>
        <p class="note">
          The proposal names the kernel capabilities a person would use. The
          exact commands and their diff are recorded once approval can execute
          them (K-AI-2, #4783).
        </p>
      </section>

      <section class="block" aria-labelledby="proposal-review-sources">
        <h3 id="proposal-review-sources">What it reasoned over</h3>
        <p v-if="risk === 'high'" id="proposal-review-open-rule" class="note">
          An AI agent's proposal is reviewed as high risk: open each source
          before approving. {{ openedCount }} of {{ visibleSources }} opened.
        </p>
        <ol id="proposal-review-source-list" class="sources">
          <li
            v-for="source in sources"
            :key="source.ref"
            class="source"
            :data-opened="opened.has(source.ref)"
          >
            <span class="source-role">{{
              source.role === "plan" ? "Plan" : kindWords(source.ref)
            }}</span>
            <template v-if="source.mask">
              <span class="masked">
                Hidden from you ({{ maskWords(source.mask) }})
              </span>
            </template>
            <template v-else>
              <code class="mono source-ref">{{ source.ref }}</code>
              <button
                :id="`proposal-source-open-${index(source)}`"
                type="button"
                class="btn btn-secondary btn-small"
                :aria-expanded="shown === source.ref"
                @click="openSource(source)"
              >
                {{ opened.has(source.ref) ? "Opened" : "Open" }}
              </button>
              <div
                v-if="shown === source.ref"
                class="source-detail"
                role="region"
                :aria-label="`Source ${index(source) + 1}`"
              >
                <template v-if="source.role === 'plan' && proposal.plan">
                  <p>
                    <strong>{{ proposal.plan.title }}</strong>, version
                    {{ proposal.plan.number }} ({{ proposal.plan.media_type }})
                  </p>
                  <p class="mono">{{ proposal.plan.content_hash }}</p>
                  <p class="note">
                    The plan's bytes are kept in artifact storage; the kernel
                    holds their hash, so the plan you read is the one proposed.
                  </p>
                </template>
                <p v-else-if="nodeError[source.ref]" role="alert">
                  {{ nodeError[source.ref] }}
                </p>
                <template v-else-if="nodes[source.ref]">
                  <p>
                    <strong>{{
                      nodes[source.ref]!.title || nodes[source.ref]!.kind
                    }}</strong>
                    <span v-if="nodes[source.ref]!.code">
                      · {{ nodes[source.ref]!.code }}</span
                    >
                    <span v-if="nodes[source.ref]!.status">
                      · {{ nodes[source.ref]!.status }}</span
                    >
                  </p>
                  <router-link
                    v-if="canvasLocation(source.ref)"
                    :to="canvasLocation(source.ref)!"
                  >
                    Open it in the Canvas
                  </router-link>
                </template>
                <p v-else>Reading from the kernel...</p>
              </div>
            </template>
          </li>
        </ol>
        <p v-if="!sources.length" class="note">
          This proposal names no sources.
        </p>
      </section>

      <section
        v-if="status === 'Open'"
        class="block decision"
        aria-labelledby="proposal-review-decision"
      >
        <h3 id="proposal-review-decision">Decide</h3>

        <div class="decision-approve">
          <button
            id="btn-approve-proposal"
            type="button"
            class="btn btn-primary"
            aria-disabled="true"
            aria-describedby="proposal-approve-blockers"
            @click.prevent
          >
            Approve and sign
          </button>
          <ul id="proposal-approve-blockers" class="blockers">
            <li v-for="blocker in blockers" :key="blocker.id">
              {{ blocker.text }}
            </li>
          </ul>
        </div>

        <form
          id="proposal-reject-form"
          class="decision-reject"
          @submit.prevent="reject"
        >
          <label for="proposal-reject-reason">Reason for rejecting</label>
          <textarea
            id="proposal-reject-reason"
            v-model="reason"
            rows="3"
            maxlength="2000"
            required
            placeholder="What is wrong with it (recorded in the audit trail)"
          />
          <button
            id="btn-reject-proposal"
            type="submit"
            class="btn btn-secondary"
            :disabled="busy || !reason.trim()"
          >
            Reject
          </button>
          <p v-if="decisionError" id="proposal-reject-error" role="alert">
            {{ decisionError }}
          </p>
        </form>
      </section>

      <section v-else class="block" aria-labelledby="proposal-review-decided">
        <h3 id="proposal-review-decided">Decision</h3>
        <p id="proposal-review-decision-text">{{ decisionWords }}</p>
      </section>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { ApiError } from "../../api/client";
import {
  kernelApi,
  type KernelNode,
  type KernelProposal,
} from "../../api/kernelApi";
import {
  approvalBlockers,
  capabilityWords,
  isAgentProposal,
  proposalSources,
  reviewRisk,
  type ProposalSource,
} from "../../ai/proposals";
import { parseNodeRef, formatNodeRef } from "../../canvas/nodeRef";
import AiLabel from "./AiLabel.vue";

/**
 * The proposal review screen (ADR-2635, docs/ux/CLINICAL_CANVAS.md PX-17): one proposal from the kernel, its intent,
 * proposer and model, the capabilities carrying it out needs, every source it reasoned over (each opened here), and
 * the decision. An AI agent's proposal carries the AI label and is reviewed as high risk until task configurations
 * record a risk class, so each source must be opened before approval is offered. Approval is an electronic signature
 * the kernel cannot take over the web yet (#5552), so it is shown disabled with its reasons (UX-9); rejection sends the
 * kernel's proposal.reject with the reviewer's reason.
 */
const props = defineProps<{
  studyCode: string;
  proposalRef: string;
  /** The signed-in person's kernel actor id (the sign-in token's subject). */
  reviewer: string;
}>();
const emit = defineEmits<{ decided: [status: string] }>();

const proposal = ref<KernelProposal | null>(null);
const loading = ref(false);
const error = ref("");
const opened = reactive(new Set<string>());
const shown = ref<string | null>(null);
const nodes = reactive<Record<string, KernelNode>>({});
const nodeError = reactive<Record<string, string>>({});
const reason = ref("");
const busy = ref(false);
const decisionError = ref("");
let rejectionKey = "";

const agent = computed(() =>
  proposal.value ? isAgentProposal(proposal.value) : false
);
const status = computed(() => proposal.value?.status ?? "");
const risk = computed(() =>
  proposal.value ? reviewRisk(proposal.value) : "standard"
);
const sources = computed(() =>
  proposal.value ? proposalSources(proposal.value) : []
);
const visibleSources = computed(
  () => sources.value.filter((s) => s.mask === null).length
);
const openedCount = computed(
  () => sources.value.filter((s) => opened.has(s.ref)).length
);
const blockers = computed(() =>
  proposal.value
    ? approvalBlockers(proposal.value, {
        reviewer: props.reviewer,
        opened,
        signingAvailable: false,
      })
    : []
);

const statusLine = computed(() => {
  if (loading.value) return "Reading the proposal from the kernel...";
  if (error.value) return `The kernel couldn't answer: ${error.value}`;
  if (proposal.value && !proposal.value.ref) {
    return `You may not see this proposal (${maskWords(proposal.value.mask)}).`;
  }
  return "";
});

const proposerWords = computed(() => {
  const p = proposal.value;
  if (!p) return "";
  if (p.proposed_by_kind === "AutomationAgent") {
    const agent = `AI agent ${(p.proposed_by ?? "").replace(/^agent:/, "")}`;
    return p.requested_by ? `${agent}, for ${p.requested_by}` : agent;
  }
  if (p.proposed_by_kind === "Service") return `Service ${p.proposed_by}`;
  return p.proposed_by ?? "";
});

const confidence = computed(() => {
  const score = Number(proposal.value?.confidence_score);
  return Number.isFinite(score) ? `${Math.round(score * 100)}%` : "";
});

const decisionWords = computed(() => {
  const p = proposal.value;
  if (!p) return "";
  switch (p.status) {
    case "Approved":
      return `Approved by ${p.decided_by} on ${when(p.decided_at)}, signed with ${p.approval_attestation}.`;
    case "Rejected":
      return `Rejected by ${p.decided_by} on ${when(p.decided_at)}: ${p.decision_reason}`;
    case "Withdrawn":
      return `Withdrawn by its proposer on ${when(p.decided_at)}: ${p.decision_reason}`;
    case "Expired":
      return `Expired on ${when(p.expires_at)} before anyone decided it.`;
    default:
      return "";
  }
});

const backLocation = computed<RouteLocationRaw>(() => ({
  name: "study",
  params: { studyCode: props.studyCode },
}));

function when(value: string | null | undefined): string {
  if (!value) return "Not recorded";
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "short",
      });
}

function maskWords(mask: string | null | undefined): string {
  return (mask ?? "").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
}

function kindWords(ref: string): string {
  const kind = parseNodeRef(ref)?.kind ?? "object";
  const words = kind.replace(/_/g, " ");
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function index(source: ProposalSource): number {
  return sources.value.indexOf(source);
}

/** Where the Canvas shows the object, for the kinds it can show. */
function canvasLocation(ref: string): RouteLocationRaw | null {
  const node = parseNodeRef(ref);
  if (!node) return null;
  const text = formatNodeRef({ kind: node.kind, id: node.id });
  if (node.kind === "protocol_version") {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: { stage: "design", section: "protocol-versions", node: text },
    };
  }
  if (node.kind === "study") {
    return { name: "study", params: { studyCode: props.studyCode } };
  }
  return null;
}

async function openSource(source: ProposalSource) {
  shown.value = shown.value === source.ref ? null : source.ref;
  if (shown.value === null) return;
  if (source.role === "plan") {
    opened.add(source.ref);
    return;
  }
  if (nodes[source.ref] || nodeError[source.ref]) return;
  try {
    const node = await kernelApi.getNode(source.ref);
    nodes[source.ref] = node;
    opened.add(source.ref);
  } catch (e) {
    // The kernel answered, but has no view of this kind yet: the reviewer has seen all there is to see.
    if (e instanceof ApiError && e.status === 404) {
      nodeError[source.ref] =
        `The kernel has no view of a ${kindWords(source.ref).toLowerCase()} yet; its reference above is all it holds here.`;
      opened.add(source.ref);
    } else {
      nodeError[source.ref] =
        `The kernel couldn't answer: ${e instanceof Error ? e.message : String(e)}`;
    }
  }
}

async function load(ref: string) {
  loading.value = true;
  error.value = "";
  proposal.value = null;
  opened.clear();
  shown.value = null;
  reason.value = "";
  rejectionKey = "";
  try {
    proposal.value = await kernelApi.getProposal(ref);
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    loading.value = false;
  }
}

async function reject() {
  const text = reason.value.trim();
  if (!proposal.value?.ref || !text || busy.value) return;
  busy.value = true;
  decisionError.value = "";
  rejectionKey ||= globalThis.crypto.randomUUID();
  try {
    const state = await kernelApi.rejectProposal({
      proposalRef: proposal.value.ref,
      reason: text,
      idempotencyKey: rejectionKey,
    });
    proposal.value = await kernelApi.getProposal(proposal.value.ref);
    emit("decided", state.status);
  } catch (e) {
    decisionError.value = `The kernel refused the rejection: ${e instanceof Error ? e.message : String(e)}`;
  } finally {
    busy.value = false;
  }
}

watch(() => props.proposalRef, load, { immediate: true });
</script>

<style scoped>
.review {
  display: flex;
  flex-direction: column;
  gap: 14px;
  max-width: 860px;
}

.review-ai {
  border-left: 3px solid var(--semantic-color-ai-border);
  padding-left: 14px;
}

.back {
  font-size: 0.85rem;
}

.review-kind {
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
}

.review-header h2 {
  margin: 2px 0 6px;
  font-size: 1.25rem;
}

.review-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.state {
  padding: 1px 8px;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: 0.78rem;
}

.safety {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
  font-size: 0.9rem;
}

.details {
  display: grid;
  grid-template-columns: minmax(9rem, max-content) 1fr;
  gap: 4px 16px;
}

.details dt {
  color: var(--neutral-dark);
}

.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.block h3 {
  margin-bottom: 6px;
  font-size: 1rem;
}

.plain,
.sources {
  margin: 0;
  padding-left: 1.2rem;
}

.source {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  align-items: baseline;
  padding: 6px 0;
  border-bottom: 1px solid var(--border);
}

.source-role {
  min-width: 7rem;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.source-ref {
  flex: 1;
}

.source-detail {
  flex-basis: 100%;
  padding: 8px 10px;
  border-radius: 6px;
  background: var(--neutral-light);
}

.masked {
  color: var(--neutral-dark);
  font-style: italic;
}

.note {
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.btn-small {
  padding: 2px 10px;
  font-size: 0.8rem;
}

.decision {
  display: grid;
  gap: 16px;
}

.decision-approve [aria-disabled="true"] {
  opacity: 0.6;
  cursor: not-allowed;
}

.blockers {
  margin: 6px 0 0;
  padding-left: 1.2rem;
  font-size: 0.85rem;
}

.decision-reject {
  display: grid;
  gap: 6px;
  max-width: 560px;
}
</style>
