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
      v-if="proposals.length || findings.length || sdvAlerts.length"
      id="needs-attention-list"
      class="attention-list"
    >
      <li
        v-for="(alert, i) in sdvAlerts"
        :key="alert.alert_id"
        class="attention-item attention-sdv-alert"
        data-testid="needs-attention-sdv-alert"
      >
        <span class="attention-kind">SDV re-verification required</span>
        <router-link
          :id="`needs-attention-sdv-alert-${i}`"
          class="attention-intent"
          :to="sdvAlertLocation(alert)"
        >
          {{ alert.message }}
        </router-link>
        <span class="attention-when">
          Rev {{ alert.superseded_revision }} verified by
          {{ alert.verified_by }} · edited to rev
          {{ alert.current_revision }} by {{ alert.modified_by }} ({{
            when(alert.modified_at)
          }})
        </span>
      </li>
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
import { computed, ref } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  ecrfQueriesApi,
  type FormSdvAlertResponse,
} from "../../api/ecrfQueriesApi";
import {
  kernelApi,
  type KernelProposal,
  type KernelValidationFinding,
} from "../../api/kernelApi";
import { isAgentProposal } from "../../ai/proposals";
import {
  collectionInstanceRef,
  observationNodeRef,
  parseNodeRef,
} from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import SupportDetails from "../canvas/SupportDetails.vue";
import AiLabel from "./AiLabel.vue";

/**
 * Needs Attention on study home (docs/ux/CLINICAL_CANVAS.md PX-10): the study's open proposals from the kernel and
 * active CRA SDV modification alerts (`PRD-QRY-006`) from Execution, newest first. An agent's proposal carries the AI
 * label (UX-8). It shows whether AI assistance is on or off: a proposal already made is a kernel record a person must
 * still decide (ADR-2635).
 */
const props = defineProps<{ studyCode: string }>();

const LIMIT = 20;

const proposals = ref<KernelProposal[]>([]);
const findings = ref<KernelValidationFinding[]>([]);
const sdvAlerts = ref<FormSdvAlertResponse[]>([]);

async function hydrateStudySdvAlerts(
  studyRef: string,
  isCurrent: () => boolean
): Promise<void> {
  const parsed = parseNodeRef(studyRef);
  if (
    parsed?.kind !== "study" ||
    typeof ecrfQueriesApi?.listSdvAlerts !== "function"
  ) {
    return;
  }
  try {
    const alerts = await ecrfQueriesApi.listSdvAlerts({ studyId: parsed.id });
    if (isCurrent() && Array.isArray(alerts)) {
      sdvAlerts.value = alerts;
    }
  } catch {
    // Keep kernel proposals visible if execution SDV alerts are unavailable.
  }
}

const {
  studyLoading: loading,
  studyError: error,
  noStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    proposals.value = [];
    findings.value = [];
    sdvAlerts.value = [];
  },
  onResolved: async (studyRef, { isCurrent }) => {
    void hydrateStudySdvAlerts(studyRef, isCurrent);
    const canReadFindings =
      typeof kernelApi.listStudyValidationFindings === "function";
    const [open, openFindingsPage, investigatingFindingsPage] =
      await Promise.all([
        kernelApi.listProposals(studyRef, {
          status: "Open",
          limit: LIMIT,
        }),
        canReadFindings
          ? kernelApi
              .listStudyValidationFindings(studyRef, { status: "Open" })
              .catch(() => ({ findings: [] as KernelValidationFinding[] }))
          : Promise.resolve({ findings: [] as KernelValidationFinding[] }),
        canReadFindings
          ? kernelApi
              .listStudyValidationFindings(studyRef, {
                status: "Investigating",
              })
              .catch(() => ({ findings: [] as KernelValidationFinding[] }))
          : Promise.resolve({ findings: [] as KernelValidationFinding[] }),
      ]);
    if (!isCurrent()) return;
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
    const seen = new Set<string>();
    const merged: KernelValidationFinding[] = [];
    for (const item of [
      ...(openFindingsPage?.findings ?? []),
      ...(investigatingFindingsPage?.findings ?? []),
    ]) {
      if (seen.has(item.ref)) continue;
      seen.add(item.ref);
      merged.push(item);
      if (merged.length >= LIMIT) break;
    }
    findings.value = merged;
  },
});

const statusLine = computed(() => {
  if (loading.value) return "Reading what needs attention...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (
    !proposals.value.length &&
    !findings.value.length &&
    !sdvAlerts.value.length
  ) {
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

function sdvAlertLocation(alert: FormSdvAlertResponse): RouteLocationRaw {
  const obsRef = observationNodeRef(alert.observation_id);
  const formRef = alert.form_id ? collectionInstanceRef(alert.form_id) : null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "conduct",
      ...(alert.site_code ? { site: alert.site_code } : {}),
      ...(formRef ? { form: formRef } : {}),
      ...(alert.item_id ? { item: alert.item_id } : {}),
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

.attention-sdv-alert {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
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
