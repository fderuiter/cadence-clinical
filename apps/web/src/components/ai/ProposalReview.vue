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

    <div v-if="error" id="proposal-review-status" role="alert" tabindex="-1">
      <p>This proposal couldn't be loaded right now.</p>
      <SupportDetails :detail="error" />
    </div>
    <p
      v-else-if="statusLine"
      id="proposal-review-status"
      role="status"
      tabindex="-1"
    >
      {{ statusLine }}
    </p>

    <template v-if="proposal && proposal.ref">
      <header class="review-header">
        <span class="review-kind">Proposal</span>
        <h2 id="proposal-review-title" tabindex="-1">{{ proposal.intent }}</h2>
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
        approving it does not run its plan automatically: whoever carries it out
        still performs each action under their own account.
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
            Not recorded for this proposal.
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
        <dt>Verification</dt>
        <dd>
          <details class="proposal-hash-details">
            <summary class="proposal-hash-summary">Content digest</summary>
            <code id="proposal-review-hash" class="mono">{{
              proposal.content_hash
            }}</code>
          </details>
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
            {{ capabilityWords(token) }}
          </li>
        </ul>
        <p class="note">
          Actions required when a reviewer carries out this proposal after
          approval.
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
            v-for="(source, sourceIdx) in sources"
            :key="source.ref ?? `masked-${sourceIdx}`"
            class="source"
            :data-opened="source.ref ? opened.has(source.ref) : false"
          >
            <span class="source-role">{{
              source.role === "plan"
                ? "Plan"
                : source.ref
                  ? formatKindTitle(source.ref)
                  : "Affected"
            }}</span>
            <template v-if="source.mask">
              <span class="masked">
                Hidden from you ({{ maskWords(source.mask) }})
              </span>
            </template>
            <template v-else>
              <span class="source-ref">
                <NodeRefChip
                  :node-ref="source.ref"
                  :fallback-title="
                    source.role === 'plan' ? proposal.plan?.title : null
                  "
                  :resolve-remotely="source.role !== 'plan'"
                />
              </span>
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
                  <details class="proposal-hash-details">
                    <summary class="proposal-hash-summary">
                      Plan verification digest
                    </summary>
                    <code class="mono">{{ proposal.plan.content_hash }}</code>
                  </details>
                  <p class="note">
                    Verified against the stored plan record so the plan you
                    review matches the one proposed.
                  </p>
                </template>
                <div v-else-if="nodeError[source.ref]" role="alert">
                  <p>{{ nodeError[source.ref] }}</p>
                  <SupportDetails
                    v-if="nodeErrorDetail[source.ref]"
                    :detail="nodeErrorDetail[source.ref]"
                  />
                </div>
                <template v-else-if="nodes[source.ref]">
                  <p>
                    <strong>{{
                      nodes[source.ref]!.title ||
                      formatKindTitle(nodes[source.ref]!.kind)
                    }}</strong>
                    <span
                      v-if="
                        nodes[source.ref]!.code &&
                        nodes[source.ref]!.code !== nodes[source.ref]!.title
                      "
                    >
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
                    Open in study workspace
                  </router-link>
                </template>
                <p v-else>Loading source details…</p>
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
          <div v-if="decisionError" id="proposal-reject-error" role="alert">
            <p>Proposal couldn't be rejected right now.</p>
            <SupportDetails :detail="decisionError" />
          </div>
        </form>
      </section>

      <section v-else class="block" aria-labelledby="proposal-review-decided">
        <h3 id="proposal-review-decided" tabindex="-1">Decision</h3>
        <p id="proposal-review-decision-text">
          <template
            v-if="
              proposal.status === 'Approved' && proposal.approval_attestation
            "
          >
            Approved by {{ proposal.decided_by }} on
            {{ when(proposal.decided_at) }}, signed with
            <NodeRefChip :node-ref="proposal.approval_attestation" />.
          </template>
          <template v-else>{{ decisionWords }}</template>
        </p>
      </section>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { handoffFocus } from "ui";
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
import {
  formatKindInline,
  formatKindTitle,
  getCachedNodeEntry,
  maskReasonWords,
  resolveNode,
  seedNodeSummary,
} from "../../canvas/nodeResolver";
import NodeRefChip from "../canvas/NodeRefChip.vue";
import SupportDetails from "../canvas/SupportDetails.vue";
import AiLabel from "./AiLabel.vue";

/**
 * The proposal review screen (ADR-2635, docs/ux/CLINICAL_CANVAS.md PX-17): displays a proposal's intent,
 * proposer, required actions, and referenced study objects with plain clinical explanations.
 */
const props = defineProps<{
  studyCode: string;
  proposalRef: string;
  /** The signed-in person's actor id. */
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
const nodeErrorDetail = reactive<Record<string, string>>({});
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
  if (loading.value) return "Loading proposal…";
  if (proposal.value && !proposal.value.ref) {
    return `You may not see this proposal (${maskWords(proposal.value.mask)}).`;
  }
  return "";
});

const proposerWords = computed(() => {
  const p = proposal.value;
  if (!p) return "";
  if (p.proposed_by_kind === "AutomationAgent") {
    const agentLabel = `AI agent ${(p.proposed_by ?? "").replace(/^agent:/, "")}`;
    return p.requested_by ? `${agentLabel}, for ${p.requested_by}` : agentLabel;
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
      return `Approved by ${p.decided_by} on ${when(p.decided_at)}.`;
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
  return maskReasonWords(mask);
}

function index(source: ProposalSource): number {
  return sources.value.indexOf(source);
}

/** Where the Canvas shows the object, for the kinds it can show. */
function canvasLocation(refText: string): RouteLocationRaw | null {
  const node = parseNodeRef(refText);
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
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: { node: formatNodeRef(node) },
  };
}

async function openSource(source: ProposalSource) {
  shown.value = shown.value === source.ref ? null : source.ref;
  if (shown.value === null) return;
  if (source.role === "plan") {
    opened.add(source.ref);
    return;
  }
  if (nodes[source.ref] || nodeError[source.ref]) return;

  const resolved = await resolveNode(source.ref);
  if (resolved) {
    nodes[source.ref] = resolved;
    opened.add(source.ref);
    return;
  }
  const entry = getCachedNodeEntry(source.ref);
  if (entry?.status === "not_found") {
    nodeError[source.ref] =
      `Detailed preview isn't available for this ${formatKindInline(source.ref)} yet; you can still review its record link above.`;
    opened.add(source.ref);
  } else {
    nodeError[source.ref] =
      "Source details couldn't be loaded right now. Try again before approving.";
    nodeErrorDetail[source.ref] = entry?.error ?? "";
  }
}

async function load(refText: string) {
  loading.value = true;
  error.value = "";
  proposal.value = null;
  opened.clear();
  shown.value = null;
  reason.value = "";
  decisionError.value = "";
  rejectionKey = "";
  for (const key of Object.keys(nodes)) delete nodes[key];
  for (const key of Object.keys(nodeError)) delete nodeError[key];
  for (const key of Object.keys(nodeErrorDetail)) delete nodeErrorDetail[key];
  try {
    const loaded = await kernelApi.getProposal(refText);
    proposal.value = loaded;
    if (loaded.ref) {
      seedNodeSummary(loaded.ref, {
        kind: "proposal",
        title: loaded.intent,
        status: loaded.status,
      });
    }
    if (loaded.plan?.ref && loaded.plan.title) {
      seedNodeSummary(loaded.plan.ref, {
        kind: "artifact_version",
        title: loaded.plan.title,
      });
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    loading.value = false;
  }
  if (typeof document !== "undefined") {
    const active = document.activeElement;
    const lostFocus =
      !active ||
      active === document.body ||
      active.id === "proposal-review-status" ||
      active.id === "canvas-workspace";
    if (lostFocus) {
      void handoffFocus(["#proposal-review-title", "#proposal-review-status"]);
    }
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
    if (proposal.value?.status !== "Open") {
      void handoffFocus(["#proposal-review-decided", "#proposal-review-title"]);
    }
  } catch (e) {
    decisionError.value = e instanceof Error ? e.message : String(e);
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
