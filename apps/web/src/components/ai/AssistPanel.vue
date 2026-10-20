<template>
  <section
    id="ai-assist"
    class="assist"
    aria-labelledby="ai-assist-title"
    :aria-busy="loading"
  >
    <h3 id="ai-assist-title">Assist</h3>
    <p class="assist-scope">
      About
      <code v-if="nodeRef">{{ nodeRef }}</code
      ><template v-else>{{ studyCode }}</template
      >. An agent reads and proposes; a person decides, and nothing changes
      until they do.
    </p>

    <form id="ai-assist-form" class="assist-form" @submit.prevent>
      <label for="ai-assist-question">Ask about this</label>
      <textarea
        id="ai-assist-question"
        v-model="question"
        rows="3"
        maxlength="2000"
        placeholder="e.g. Which visits for this participant are out of window?"
      />
      <button
        id="btn-ai-assist-ask"
        type="submit"
        class="btn btn-primary"
        aria-disabled="true"
        aria-describedby="ai-assist-unavailable"
      >
        Ask
      </button>
      <p id="ai-assist-unavailable" class="assist-note">
        No agent runs in this study yet: agents arrive with the Intelligence
        module (#5374) and its first agents (#5547). Until then, Act shows each
        command you may run on a selected object with the kernel's dry-run
        decision.
      </p>
      <button
        v-if="nodeRef"
        id="btn-ai-assist-act"
        type="button"
        class="btn btn-secondary"
        @click="emit('act')"
      >
        Open Act
      </button>
    </form>

    <h4 id="ai-assist-proposals-title">
      {{ nodeRef ? "Proposals about this" : "Proposals in this study" }}
    </h4>
    <p
      v-if="statusLine"
      id="ai-assist-status"
      class="assist-note"
      role="status"
    >
      {{ statusLine }}
    </p>
    <ul
      v-if="related.length"
      id="ai-assist-proposals"
      class="assist-proposals"
      aria-labelledby="ai-assist-proposals-title"
    >
      <li v-for="(proposal, i) in related" :key="proposal.ref ?? i">
        <AiLabel
          v-if="isAgentProposal(proposal)"
          label="Proposed by an AI agent"
          :model="proposal.model_id"
        />
        <router-link
          v-if="proposal.ref"
          :id="`ai-assist-proposal-${i}`"
          :to="reviewLocation(proposal.ref)"
        >
          {{ proposal.intent }}
        </router-link>
        <span class="assist-state">{{ proposal.status }}</span>
      </li>
    </ul>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { kernelApi, type KernelProposal } from "../../api/kernelApi";
import { isAgentProposal, proposalSources } from "../../ai/proposals";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import AiLabel from "./AiLabel.vue";

/**
 * The inspector's Assist pane (ADR-2635, UX-8): scoped to the selected node. It lists the study's proposals that name
 * the node, from the kernel, and holds the question box an agent will answer once one runs in the study (#5374,
 * #5547); until then the box says so and points at Act, whose dry runs answer "may I" without an agent. An answer
 * never writes: an agent's way to change anything is a Proposal a person reviews (ADR-2630). The Canvas shows this
 * pane only while the viewer has AI assistance on.
 */
const props = defineProps<{
  studyCode: string;
  /** The selected node, as a kernel NodeRef; empty scopes the pane to the whole study. */
  nodeRef?: string;
  /** A question carried in from the palette's Ask entry. */
  prefill?: string;
}>();
const emit = defineEmits<{ act: [] }>();

const PROPOSAL_LIMIT = 100;

const question = ref(props.prefill ?? "");
const proposals = ref<KernelProposal[]>([]);
const loading = ref(false);
const error = ref("");

const target = computed(() =>
  props.nodeRef ? parseNodeRef(props.nodeRef) : null
);

/** Proposals whose plan or affected objects include the selected node, at any version; every one without a node. */
const related = computed(() =>
  proposals.value.filter(
    (proposal) =>
      !target.value ||
      proposalSources(proposal).some((source) => {
        const node = parseNodeRef(source.ref);
        return (
          node !== null &&
          node.kind === target.value?.kind &&
          node.id === target.value?.id
        );
      })
  )
);

const statusLine = computed(() => {
  if (loading.value) return "Reading proposals from the kernel...";
  if (error.value) return `The kernel couldn't answer: ${error.value}`;
  if (!related.value.length) {
    return props.nodeRef
      ? "No proposal names this yet."
      : "No proposal in this study yet.";
  }
  return "";
});

function reviewLocation(ref: string): RouteLocationRaw {
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: { proposal: ref },
  };
}

let latest = 0;

async function load() {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  proposals.value = [];
  try {
    const studyRef = await findKernelStudyRef(props.studyCode);
    if (request !== latest || !studyRef) return;
    const found = await kernelApi.listProposals(studyRef, {
      limit: PROPOSAL_LIMIT,
    });
    if (request === latest) proposals.value = found;
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

watch(
  () => props.prefill,
  (text) => {
    if (text) question.value = text;
  }
);
watch(() => props.studyCode, load, { immediate: true });
</script>

<style scoped>
.assist {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
  border: 1px solid var(--semantic-color-ai-border, #c4b5fd);
  border-radius: 6px;
  background: var(--semantic-color-ai-bg, #f5f3ff);
}

.assist h3,
.assist h4 {
  margin: 0;
  font-size: 0.95rem;
}

.assist-scope,
.assist-note {
  margin: 0;
  font-size: 0.85rem;
}

.assist-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.assist-form textarea {
  width: 100%;
  font: inherit;
}

.assist-form .btn[aria-disabled="true"] {
  opacity: 0.6;
  cursor: not-allowed;
}

.assist-proposals {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.assist-proposals li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.assist-state {
  font-size: 0.8rem;
}
</style>
