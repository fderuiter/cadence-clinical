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
      v-if="proposals.length || findings.length"
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
      <li
        v-for="(finding, j) in findings"
        :key="finding.ref ?? `finding-${j}`"
        :data-testid="`needs-attention-finding-${finding.rule_id}`"
        class="attention-item attention-finding"
      >
        <span class="attention-kind">
          Validation finding ({{ finding.severity }} · {{ finding.status }})
        </span>
        <router-link
          :id="`needs-attention-finding-${j}`"
          class="attention-intent"
          :to="findingLocation(finding)"
        >
          [{{ finding.rule_id }}] {{ finding.message }}
        </router-link>
      </li>
    </ul>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  kernelApi,
  type KernelProposal,
  type KernelValidationFinding,
} from "../../api/kernelApi";
import { isAgentProposal } from "../../ai/proposals";
import { seedKernelStudyRef } from "../../canvas/kernelStudy";
import {
  collectionInstanceRef,
  observationNodeRef,
} from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import SupportDetails from "../canvas/SupportDetails.vue";
import AiLabel from "./AiLabel.vue";

/**
 * Needs Attention on study home (docs/ux/CLINICAL_CANVAS.md PX-10): the study's open proposals and active validation
 * findings from the kernel, queried directly by `studyCode` in Wave 1 (`GET /api/v1/kernel/studies/{studyRef}/attention`).
 * An agent's proposal carries the AI label (UX-8). It shows whether AI assistance is on or off: a proposal already
 * made is a kernel record a person must still decide (ADR-2635).
 */
const props = defineProps<{ studyCode: string }>();

const LIMIT = 20;

const proposals = ref<KernelProposal[]>([]);
const findings = ref<KernelValidationFinding[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const noStudy = ref(false);

let requestSeq = 0;

function seedProposalSummaries(items: ReadonlyArray<KernelProposal>): void {
  for (const item of items) {
    if (item.ref && !item.mask) {
      seedNodeSummary(item.ref, {
        kind: "proposal",
        title: item.intent ?? null,
        status: item.status ?? null,
      });
    }
  }
}

function dedupeCappedFindings(
  items: ReadonlyArray<KernelValidationFinding>
): KernelValidationFinding[] {
  const seen = new Set<string>();
  const merged: KernelValidationFinding[] = [];
  for (const item of items) {
    if (seen.has(item.ref)) continue;
    seen.add(item.ref);
    merged.push(item);
    if (merged.length >= LIMIT) break;
  }
  return merged;
}

async function loadAttention(rawCode: string): Promise<void> {
  const seq = ++requestSeq;
  const code = rawCode.trim();
  proposals.value = [];
  findings.value = [];
  error.value = null;
  noStudy.value = false;

  if (!code) {
    noStudy.value = true;
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    if (typeof kernelApi.getStudyAttention === "function") {
      const attention = await kernelApi.getStudyAttention(code, {
        limit: LIMIT,
      });
      if (seq !== requestSeq) return;
      seedKernelStudyRef(code, attention.study_ref);
      const openProposals = Array.isArray(attention.proposals)
        ? attention.proposals.slice(0, LIMIT)
        : [];
      proposals.value = openProposals;
      seedProposalSummaries(openProposals);
      findings.value = dedupeCappedFindings(attention.findings ?? []);
      return;
    }

    const canReadFindings =
      typeof kernelApi.listStudyValidationFindings === "function";
    const [openProposals, openFindingsPage, investigatingFindingsPage] =
      await Promise.all([
        typeof kernelApi.listProposals === "function"
          ? kernelApi.listProposals(code, { status: "Open", limit: LIMIT })
          : Promise.resolve([] as KernelProposal[]),
        canReadFindings
          ? kernelApi
              .listStudyValidationFindings(code, {
                status: "Open",
                limit: LIMIT,
              })
              .catch(() => ({ findings: [] as KernelValidationFinding[] }))
          : Promise.resolve({ findings: [] as KernelValidationFinding[] }),
        canReadFindings
          ? kernelApi
              .listStudyValidationFindings(code, {
                status: "Investigating",
                limit: LIMIT,
              })
              .catch(() => ({ findings: [] as KernelValidationFinding[] }))
          : Promise.resolve({ findings: [] as KernelValidationFinding[] }),
      ]);
    if (seq !== requestSeq) return;
    proposals.value = openProposals;
    const hintedStudyRef =
      openProposals.find((p) => p.study)?.study ??
      openFindingsPage?.findings?.[0]?.study_ref ??
      investigatingFindingsPage?.findings?.[0]?.study_ref;
    if (hintedStudyRef) {
      seedKernelStudyRef(code, hintedStudyRef);
    }
    seedProposalSummaries(openProposals);
    findings.value = dedupeCappedFindings([
      ...(openFindingsPage?.findings ?? []),
      ...(investigatingFindingsPage?.findings ?? []),
    ]);
  } catch (err) {
    if (seq !== requestSeq) return;
    if ((err as { status?: number | null } | null)?.status === 404) {
      noStudy.value = true;
      return;
    }
    error.value = err instanceof Error ? err.message : String(err);
  } finally {
    if (seq === requestSeq) {
      loading.value = false;
    }
  }
}

watch(
  () => props.studyCode,
  (code) => {
    void loadAttention(code ?? "");
  },
  { immediate: true }
);

const statusLine = computed(() => {
  if (loading.value) return "Reading what needs attention...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (!proposals.value.length && !findings.value.length) {
    return "Nothing is waiting on a decision.";
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

function findingLocation(finding: KernelValidationFinding): RouteLocationRaw {
  const formRef =
    finding.target_kind === "DatasetRecord" || finding.target_kind === "Record"
      ? collectionInstanceRef(finding.record_key)
      : null;
  const obsRef =
    finding.target_kind === "Observation"
      ? observationNodeRef(finding.record_key)
      : null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "validation",
      ...(formRef ? { form: formRef } : {}),
      ...(formRef && finding.variable ? { item: finding.variable } : {}),
      ...(obsRef ? { observation: obsRef } : {}),
    },
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

.attention-finding {
  border-left: 4px solid var(--semantic-color-warning-dark);
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
