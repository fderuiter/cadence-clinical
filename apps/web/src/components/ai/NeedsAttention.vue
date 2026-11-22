<template>
  <section
    id="needs-attention"
    class="attention"
    aria-labelledby="needs-attention-title"
    :aria-busy="loading"
  >
    <h2 id="needs-attention-title">Needs attention</h2>

    <div
      v-if="error"
      id="needs-attention-status"
      class="attention-status"
      role="alert"
    >
      <p class="attention-status">
        Items needing attention couldn't be loaded right now.
      </p>
      <SupportDetails :detail="error" />
    </div>
    <p
      v-else-if="statusLine"
      id="needs-attention-status"
      class="attention-status"
      role="status"
    >
      {{ statusLine }}
    </p>

    <ul
      v-if="proposals.length"
      id="needs-attention-list"
      class="attention-list"
    >
      <li
        v-for="(proposal, i) in proposals"
        :key="proposal.ref ?? i"
        class="attention-item"
        :class="{ 'attention-ai': isAgentProposal(proposal) }"
      >
        <span class="attention-kind">Proposal awaiting a decision</span>
        <AiLabel
          v-if="isAgentProposal(proposal)"
          label="Proposed by an AI agent"
          :model="proposal.model_id"
        />
        <router-link
          v-if="proposal.ref"
          :id="`needs-attention-proposal-${i}`"
          class="attention-intent"
          :to="reviewLocation(proposal.ref)"
        >
          {{ proposal.intent }}
        </router-link>
        <span v-else class="attention-intent masked">
          Hidden proposal ({{ maskWords(proposal.mask) }})
        </span>
        <span v-if="proposal.expires_at" class="attention-when">
          Expires {{ when(proposal.expires_at) }}
        </span>
      </li>
    </ul>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { kernelApi, type KernelProposal } from "../../api/kernelApi";
import { isAgentProposal } from "../../ai/proposals";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import SupportDetails from "../canvas/SupportDetails.vue";
import AiLabel from "./AiLabel.vue";

/**
 * Needs Attention on study home (docs/ux/CLINICAL_CANVAS.md PX-10): the study's open proposals from the kernel, newest
 * first, each opening the review screen. An agent's proposal carries the AI label (UX-8). It shows whether AI
 * assistance is on or off: a proposal already made is a kernel record a person must still decide (ADR-2635).
 * Findings, signals and the other attention items join it as their kernel reads arrive.
 */
const props = defineProps<{ studyCode: string }>();

const LIMIT = 20;

const proposals = ref<KernelProposal[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);

const statusLine = computed(() => {
  if (loading.value) return "Reading what needs attention...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (!proposals.value.length) return "Nothing is waiting on a decision.";
  return "";
});

function reviewLocation(ref: string): RouteLocationRaw {
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: { proposal: ref },
  };
}

function when(value: string): string {
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

let latest = 0;

async function load() {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  proposals.value = [];
  try {
    const studyRef = await findKernelStudyRef(props.studyCode);
    if (request !== latest) return;
    if (!studyRef) {
      noStudy.value = true;
      return;
    }
    const open = await kernelApi.listProposals(studyRef, {
      status: "Open",
      limit: LIMIT,
    });
    if (request === latest) {
      proposals.value = open;
      for (const item of open) {
        if (item.ref && !item.mask) {
          seedNodeSummary(item.ref, {
            kind: "proposal",
            title: item.intent ?? null,
            status: item.status ?? null,
          });
        }
      }
    }
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

watch(() => props.studyCode, load, { immediate: true });
</script>

<style scoped>
.attention {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 24px;
}

.attention h2 {
  margin: 0;
  font-size: 1rem;
}

.attention-status {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.attention-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.attention-item {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px 10px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
}

.attention-ai {
  border-left: 4px solid var(--semantic-color-ai);
}

.attention-kind,
.attention-when {
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.attention-intent {
  flex-basis: 100%;
  font-weight: 600;
  color: inherit;
}

.masked {
  font-style: italic;
  font-weight: 400;
}
</style>
