<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <div
      style="
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 8px;
      "
    >
      <h2 id="protocol-versions-title" style="margin: 0">Protocol versions</h2>
      <button
        id="btn-generate-protocol-proposal"
        type="button"
        class="btn btn-primary"
        style="padding: 4px 10px; font-size: 0.85rem"
        @click="showProposalModal = true"
      >
        ⚡ Generate Protocol Proposal
      </button>
    </div>
    <p id="protocol-versions-status" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <table v-if="versions.length" class="versions-table">
      <thead>
        <tr>
          <th scope="col" class="numeric">Version</th>
          <th scope="col">Status</th>
          <th scope="col">Amends</th>
          <th scope="col">Amendment intent</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(version, index) in versions"
          :key="version.ref ?? `masked-${index}`"
          :class="{
            selected: version.ref !== null && version.ref === selectedRef,
          }"
        >
          <template v-if="version.ref !== null">
            <th scope="row" class="numeric">
              <button
                :id="`protocol-version-${version.number}`"
                type="button"
                class="row-select"
                :aria-pressed="version.ref === selectedRef"
                @click="emit('select', version)"
              >
                {{ version.number }}
              </button>
            </th>
            <td>{{ version.status }}</td>
            <td>{{ parentNumber(version) }}</td>
            <td>{{ version.amendment_intent || "None recorded" }}</td>
          </template>
          <td v-else colspan="4" class="masked-row">
            A version you may not see ({{ maskWords(version.mask) }})
          </td>
        </tr>
      </tbody>
    </table>

    <!-- Generative Protocol Proposal Modal -->
    <div
      v-if="showProposalModal"
      class="proposal-modal-overlay"
      role="button"
      tabindex="0"
      style="
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.5);
        display: flex;
        align-items: center;
        justify-content: center;
        z-index: 9999;
      "
      @click.self="showProposalModal = false"
      @keydown.escape="showProposalModal = false"
    >
      <div
        class="proposal-modal-card"
        style="
          background: white;
          border-radius: 8px;
          width: 90%;
          max-width: 650px;
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
            Generate In-Memory Protocol Proposal
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
            @click="showProposalModal = false"
          >
            ×
          </button>
        </header>

        <div style="display: flex; flex-direction: column; gap: 10px">
          <label
            for="trial-brief-input"
            style="font-weight: 600; font-size: 0.85rem"
            >Trial Brief Prompt:</label
          >
          <textarea
            id="trial-brief-input"
            v-model="trialBrief"
            rows="3"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid #ccc;
              border-radius: 4px;
              font-family: inherit;
            "
            placeholder="Enter clinical trial brief details..."
          />
          <button
            type="button"
            class="btn btn-primary"
            style="align-self: flex-end; padding: 6px 16px"
            :disabled="generating || !trialBrief.trim()"
            @click="handleGenerateProposal"
          >
            {{
              generating
                ? "Synthesizing Protocol Graph..."
                : "Draft USDM Proposal"
            }}
          </button>
        </div>

        <div
          v-if="proposalError"
          style="
            margin-top: 12px;
            color: var(--danger, #dc3545);
            font-size: 0.85rem;
          "
        >
          {{ proposalError }}
        </div>

        <div
          v-if="proposalResult"
          style="
            margin-top: 16px;
            border-top: 1px solid #eee;
            padding-top: 12px;
          "
        >
          <h4 style="margin: 0 0 6px 0; font-size: 0.95rem">
            Candidate USDM Protocol Proposal
          </h4>
          <p style="margin: 0 0 10px 0; font-size: 0.85rem; color: #555">
            Title: <strong>{{ proposalResult.design?.title }}</strong>
          </p>

          <div
            style="
              background: #f8f9fa;
              border: 1px solid #e9ecef;
              border-radius: 4px;
              padding: 10px;
              font-size: 0.8rem;
              margin-bottom: 12px;
            "
          >
            <div>
              <strong>Objectives:</strong>
              {{ proposalResult.design?.objectives?.length ?? 0 }}
            </div>
            <div>
              <strong>Endpoints:</strong>
              {{ proposalResult.design?.endpoints?.length ?? 0 }}
            </div>
            <div>
              <strong>Arms:</strong>
              {{ proposalResult.design?.arms?.length ?? 0 }}
            </div>
            <div>
              <strong>Epochs:</strong>
              {{ proposalResult.design?.epochs?.length ?? 0 }}
            </div>
            <div>
              <strong>Planned Activities:</strong>
              {{ proposalResult.design?.planned_activities?.length ?? 0 }}
            </div>
          </div>

          <h5 style="margin: 8px 0 4px 0; font-size: 0.85rem">
            Initial Matrix Gap Analysis
          </h5>
          <div
            v-if="
              proposalResult.initial_gap_check &&
              proposalResult.initial_gap_check.length
            "
          >
            <div
              v-for="(gap, idx) in proposalResult.initial_gap_check"
              :key="idx"
              style="
                font-size: 0.8rem;
                padding: 4px 6px;
                background: #fff3cd;
                border-left: 3px solid #ffc107;
                margin-bottom: 4px;
              "
            >
              [{{ gap.severity }}] {{ gap.kind }} -
              {{ gap.description }} (<code>{{ gap.json_pointer_path }}</code
              >)
            </div>
          </div>
          <p
            v-else
            style="font-size: 0.8rem; color: #28a745; margin-bottom: 12px"
          >
            ✓ Zero matrix gaps detected in candidate graph.
          </p>

          <div style="margin-bottom: 12px">
            <a
              href="#"
              style="
                font-size: 0.85rem;
                color: var(--primary, #0056b3);
                text-decoration: underline;
              "
              @click.prevent="showReasoning = !showReasoning"
            >
              {{
                showReasoning
                  ? "Hide reasoning"
                  : "Inspect reasoning &amp; lineage provenance"
              }}
            </a>
            <div
              v-if="showReasoning"
              style="
                margin-top: 8px;
                font-size: 0.8rem;
                background: #f1f3f5;
                padding: 8px;
                border-radius: 4px;
              "
            >
              <div
                v-for="step in proposalResult.reasoning_chain"
                :key="step.step_number"
                style="margin-bottom: 6px"
              >
                <strong
                  >Step {{ step.step_number }}: {{ step.step_type }}</strong
                >
                - {{ step.summary }}
                <div style="color: #666; font-size: 0.75rem">
                  Rationale: {{ step.rationale }}
                </div>
              </div>
            </div>
          </div>

          <div style="display: flex; gap: 10px; margin-top: 16px">
            <button
              type="button"
              class="btn btn-primary"
              style="flex: 1; padding: 8px"
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
              style="flex: 1; padding: 8px"
              :disabled="proposalRejected"
              @click="
                proposalRejected = true;
                proposalApproved = false;
              "
            >
              {{ proposalRejected ? "✗ Proposal Rejected" : "Reject Proposal" }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  isMasked,
  kernelApi,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";

/**
 * The study's protocol versions, newest first, from the kernel (ADR-2540). The study is found by its code with
 * kernel search, then its versions are listed without content. A version the reader may not see keeps its place,
 * with only why it is hidden. Selecting a version fills the inspector, where Impact shows what an amendment changes
 * and what depends on the version it amends (K-PROT-2).
 */
const props = defineProps<{ studyCode: string; selectedRef: string }>();
const emit = defineEmits<{ select: [version: KernelProtocolVersion] }>();

const versions = ref<KernelProtocolVersion[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);

/** Visible versions by id: a parent reference may name the version with or without its revision. */
const byId = computed(
  () =>
    new Map(
      versions.value.flatMap((v) => {
        const node = parseNodeRef(v.ref);
        return node ? [[node.id, v] as const] : [];
      })
    )
);

function parentNumber(version: KernelProtocolVersion): string {
  if (!version.parent_version) return "Nothing (first version)";
  const parentId = parseNodeRef(version.parent_version)?.id;
  const parent = parentId ? byId.value.get(parentId) : undefined;
  return parent ? `Version ${parent.number}` : "A version you may not see";
}

function maskWords(mask: string | null): string {
  return (mask ?? "").replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
}

const statusLine = computed(() => {
  if (loading.value) return "Loading protocol versions...";
  if (error.value) return `The kernel couldn't answer: ${error.value}`;
  if (noStudy.value) {
    return `The kernel has no study ${props.studyCode} that you may see.`;
  }
  const count = versions.value.length;
  if (count === 0) return `${props.studyCode} has no protocol versions yet.`;
  const hidden = versions.value.filter((v) => isMasked(v)).length;
  const noun = count === 1 ? "version" : "versions";
  return hidden
    ? `${count} ${noun}, ${hidden} of them hidden from you.`
    : `${count} ${noun}.`;
});

let latest = 0;

async function load(code: string) {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  versions.value = [];
  try {
    const study = await findKernelStudyRef(code);
    if (request !== latest) return;
    if (!study) {
      noStudy.value = true;
      return;
    }
    const listed = await kernelApi.listProtocolVersions(study);
    if (request === latest) versions.value = listed;
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

const showProposalModal = ref(false);
const trialBrief = ref(
  "Phase II Double-Blind Randomized Cardiovascular Study evaluating 100mg investigational drug vs placebo over 12 weeks with 4-week follow-up."
);
const generating = ref(false);
const proposalResult = ref<any>(null);
const proposalError = ref("");
const proposalApproved = ref(false);
const proposalRejected = ref(false);
const showReasoning = ref(false);

async function handleGenerateProposal() {
  if (!trialBrief.value.trim()) return;
  generating.value = true;
  proposalError.value = "";
  proposalResult.value = null;
  proposalApproved.value = false;
  proposalRejected.value = false;
  try {
    const res = await kernelApi.generateProposal(trialBrief.value);
    proposalResult.value = res;
  } catch (e: any) {
    proposalError.value = e.message || String(e);
  } finally {
    generating.value = false;
  }
}

watch(() => props.studyCode, load, { immediate: true });
</script>

<style scoped>
.versions {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.versions h2 {
  margin: 0;
  font-size: 1rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
}

.versions-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.versions-table th,
.versions-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.versions-table .numeric {
  text-align: right;
}

.versions-table tr.selected {
  background: var(--neutral-light);
}

.row-select {
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  font-weight: 600;
  color: var(--primary);
  cursor: pointer;
}

.masked-row {
  color: var(--neutral-dark);
  font-style: italic;
}
</style>
