<template>
  <section
    :id="`kernel-${mode}`"
    class="lineage"
    :aria-labelledby="`kernel-${mode}-title`"
  >
    <h3 :id="`kernel-${mode}-title`">
      {{ mode === "trace" ? "Where this came from" : "What depends on this" }}
    </h3>
    <p v-if="loading" class="lineage-note" role="status">
      Reading the kernel's lineage…
    </p>
    <p v-else-if="error" class="lineage-note" role="alert">
      The kernel couldn't answer: {{ error }}
    </p>

    <template v-else-if="mode === 'trace' && lineage">
      <p v-if="!lineage.edges.length" class="lineage-note" role="status">
        The kernel records nothing this came from.
      </p>
      <ol v-else :id="`kernel-${mode}-list`" class="lineage-list">
        <li
          v-for="(edge, index) in lineage.edges"
          :key="`edge-${index}`"
          class="lineage-edge"
        >
          <span class="lineage-object">{{ describe(edge.from) }}</span>
          <span class="lineage-kind">{{ ` ${words(edge.kind)} ` }}</span>
          <span class="lineage-object">{{ describe(edge.to) }}</span>
        </li>
      </ol>
      <p v-if="lineage.truncated" class="lineage-note">
        The lineage goes further than the {{ lineage.depth }} steps shown.
      </p>
    </template>

    <template v-else-if="mode === 'impact' && dependents">
      <p v-if="!dependents.groups.length" class="lineage-note" role="status">
        Nothing the kernel records depends on this.
      </p>
      <template v-else>
        <p :id="`kernel-${mode}-total`" class="lineage-total">
          {{ dependents.truncated ? "At least " : ""
          }}{{ dependents.total }} dependent
          {{ Number(dependents.total) === 1 ? "object" : "objects" }}
        </p>
        <ul :id="`kernel-${mode}-list`" class="lineage-list">
          <li
            v-for="group in dependents.groups"
            :key="group.kind"
            class="lineage-group"
            :data-kind="group.kind"
          >
            <details>
              <summary>
                {{ words(group.kind) }}:
                <span class="numeric">{{ group.count }}</span>
              </summary>
              <ul class="lineage-objects">
                <li v-for="object in group.objects" :key="object.handle">
                  {{ label(object) }}
                </li>
              </ul>
            </details>
          </li>
        </ul>
      </template>
    </template>

    <p class="lineage-note">
      Objects you may not see are shown as hidden and still counted, so these
      numbers are the same for everyone.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  isMasked,
  kernelApi,
  type KernelDependents,
  type KernelLineage,
  type KernelLineageNode,
} from "../../api/kernelApi";

/**
 * The Trace and Impact verbs (docs/ux/CLINICAL_CANVAS.md §4, ADR-2540): the kernel's lineage walk backward from the
 * selected object, or its dependents grouped by kind with counts. Both come from the kernel as they are (Canvas D2):
 * an object the reader may not see keeps its place as its kind and mask reason, and is counted, never dropped. A
 * refusal or an unreachable kernel is reported as it is; nothing is inferred in the browser (K-TRUTH-1).
 */
const props = defineProps<{ nodeRef: string; mode: "trace" | "impact" }>();

const lineage = ref<KernelLineage | null>(null);
const dependents = ref<KernelDependents | null>(null);
const loading = ref(false);
const error = ref("");

const nodes = computed(
  () => new Map((lineage.value?.nodes ?? []).map((n) => [n.handle, n]))
);

/** `DerivedFrom` or `study_site` in plain words: "derived from", "study site". */
function words(name: string): string {
  return name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
}

function label(node: KernelLineageNode): string {
  return isMasked(node)
    ? `Hidden ${words(node.kind)} (${words(node.mask ?? "")})`
    : (node.ref ?? node.handle);
}

function describe(handle: string): string {
  const node = nodes.value.get(handle);
  return node ? label(node) : handle;
}

let latest = 0;

async function load() {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  lineage.value = null;
  dependents.value = null;
  try {
    if (props.mode === "trace") {
      const result = await kernelApi.trace(props.nodeRef);
      if (request === latest) lineage.value = result;
    } else {
      const result = await kernelApi.dependents(props.nodeRef);
      if (request === latest) dependents.value = result;
    }
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

watch(() => [props.nodeRef, props.mode], load, { immediate: true });
</script>

<style scoped>
.lineage {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.lineage h3 {
  margin: 0;
  font-size: 0.95rem;
}

.lineage-list,
.lineage-objects {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding-left: 18px;
  font-size: 0.85rem;
}

.lineage-edge {
  overflow-wrap: anywhere;
}

.lineage-kind {
  font-style: italic;
  color: var(--neutral-dark);
}

.lineage-total {
  margin: 0;
  font-weight: 600;
}

.lineage-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}
</style>
