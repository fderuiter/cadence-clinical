<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <h2 id="protocol-versions-title" tabindex="-1">Protocol versions</h2>
    <div
      v-if="error"
      id="protocol-versions-status"
      class="status-line"
      role="alert"
    >
      <p class="status-line">Protocol versions couldn't be loaded right now.</p>
      <SupportDetails :detail="error" />
    </div>
    <p v-else id="protocol-versions-status" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <div v-if="versions.length" class="scrollable-table-wrapper">
      <table class="versions-table">
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
    </div>

    <ProtocolImportPanel
      v-if="canShowImportPanel"
      :study-ref="studyNodeRef"
      :study-code="studyCode"
      :selected-version-ref="activeSummaryVersionRef"
      @imported="onDraftImported"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import {
  isMasked,
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import ProtocolImportPanel from "./ProtocolImportPanel.vue";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's protocol versions, newest first (ADR-2540). Restricted versions keep their place as masked placeholders.
 */
const props = defineProps<{ studyCode: string; selectedRef: string }>();
const emit = defineEmits<{ select: [version: KernelProtocolVersion] }>();

const versions = ref<KernelProtocolVersion[]>([]);

function seedVersions(listed: KernelProtocolVersion[]) {
  for (const v of listed) {
    if (v.ref) {
      seedNodeSummary(v.ref, {
        kind: "protocol_version",
        title: `Version ${v.number}`,
        status: v.status,
      });
    }
  }
}

const {
  studyRef,
  studyLoading: loading,
  studyError: error,
  noStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    versions.value = [];
  },
  onResolved: async (study, { isCurrent }) => {
    const listed = await kernelApi.listProtocolVersions(study);
    if (isCurrent()) {
      versions.value = listed;
      seedVersions(listed);
    }
  },
});

const studyNodeRef = computed(() => studyRef.value ?? "");

const canShowImportPanel = computed(
  () =>
    !loading.value &&
    !error.value &&
    !noStudy.value &&
    Boolean(studyNodeRef.value) &&
    typeof kernelApi.importProtocolDraft === "function"
);

const activeSummaryVersionRef = computed(() => {
  if (props.selectedRef) return props.selectedRef;
  const firstVisible = versions.value.find((v) => v.ref && !isMasked(v));
  return firstVisible?.ref ?? "";
});

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
  return maskReasonWords(mask);
}

const statusLine = computed(() => {
  if (loading.value) return "Loading protocol versions...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  const count = versions.value.length;
  if (count === 0) return `${props.studyCode} has no protocol versions yet.`;
  const hidden = versions.value.filter((v) => isMasked(v)).length;
  const noun = count === 1 ? "version" : "versions";
  return hidden
    ? `${count} ${noun}, ${hidden} of them hidden from you.`
    : `${count} ${noun}.`;
});

async function onDraftImported(summary: KernelProtocolDraftSummary) {
  if (studyNodeRef.value) {
    try {
      const listed = await kernelApi.listProtocolVersions(studyNodeRef.value);
      versions.value = listed;
      seedVersions(listed);
    } catch {
      // Keep the imported version visible even if refreshing the list fails.
    }
  }
  if (summary.version?.ref) {
    seedNodeSummary(summary.version.ref, {
      kind: "protocol_version",
      title: `Version ${summary.version.number}`,
      status: summary.version.status,
    });
    emit("select", summary.version);
  }
}
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

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
