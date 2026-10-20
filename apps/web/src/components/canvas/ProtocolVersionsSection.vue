<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <h2 id="protocol-versions-title">Protocol versions</h2>
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
