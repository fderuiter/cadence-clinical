<template>
  <section
    :id="`kernel-${verb}`"
    class="lineage"
    :aria-labelledby="`kernel-${verb}-title`"
  >
    <h3 :id="`kernel-${verb}-title`">
      {{ title }}
    </h3>
    <p v-if="loading" class="lineage-note" role="status">
      Loading related records…
    </p>
    <CanvasErrorAlert
      v-else-if="error"
      message="Related records couldn't be loaded right now."
      :detail="error"
      @retry="load()"
      @explain="emit('explain')"
    />

    <template v-else-if="mode === 'trace' && lineage">
      <p v-if="!lineage.edges.length" class="lineage-note" role="status">
        No upstream source records are recorded for this item.
      </p>
      <ol v-else :id="`kernel-${verb}-list`" class="lineage-list">
        <li
          v-for="(edge, index) in lineage.edges"
          :key="`edge-${index}`"
          class="lineage-edge"
        >
          <span class="lineage-object">
            <span
              v-if="edgeNode(edge.from) && isMasked(edgeNode(edge.from)!)"
              class="noderef-masked"
              >{{
                formatMaskedNode(
                  edgeNode(edge.from)!.kind,
                  edgeNode(edge.from)!.mask
                )
              }}</span
            >
            <NodeRefChip
              v-else-if="edgeNode(edge.from)?.ref"
              :node-ref="edgeNode(edge.from)!.ref!"
            />
            <template v-else>{{ edge.from }}</template>
          </span>
          <span class="lineage-kind">{{ ` ${words(edge.kind)} ` }}</span>
          <span class="lineage-object">
            <span
              v-if="edgeNode(edge.to) && isMasked(edgeNode(edge.to)!)"
              class="noderef-masked"
              >{{
                formatMaskedNode(
                  edgeNode(edge.to)!.kind,
                  edgeNode(edge.to)!.mask
                )
              }}</span
            >
            <NodeRefChip
              v-else-if="edgeNode(edge.to)?.ref"
              :node-ref="edgeNode(edge.to)!.ref!"
            />
            <template v-else>{{ edge.to }}</template>
          </span>
        </li>
      </ol>
      <p v-if="lineage.truncated" class="lineage-note">
        The lineage goes further than the {{ lineage.depth }} steps shown.
      </p>
    </template>

    <template v-else-if="verb === 'impact' && dependents">
      <template v-if="amendment">
        <p
          v-if="!amendment.changed_elements.length"
          id="kernel-amendment-unchanged"
          class="lineage-note"
          role="status"
        >
          No protocol elements were changed in this amendment.
        </p>
        <ul v-else id="kernel-amendment-changes" class="lineage-list">
          <li
            v-for="change in amendment.changed_elements"
            :key="change.element_id"
            class="amendment-change"
            :data-change="change.change"
          >
            <span class="amendment-change-kind">{{ change.change }}</span>
            {{ change.instance_type ? words(change.instance_type) : "element" }}
            <code>{{ change.element_id }}</code>
            <span
              v-if="change.properties.length"
              class="lineage-note amendment-properties"
            >
              ({{ change.properties.map(formatFieldPath).join(", ") }})
            </span>
          </li>
        </ul>
        <div
          v-if="amendment.schedule"
          id="kernel-amendment-schedule"
          class="amendment-schedule"
        >
          <h4 class="lineage-subheading">Schedule and eligibility preview</h4>
          <p id="kernel-amendment-schedule-flags" class="lineage-note">
            {{
              amendment.schedule.changes_schedule
                ? "Changes on-study visit schedule"
                : "On-study visit schedule unchanged"
            }}
            ·
            {{
              amendment.schedule.changes_eligibility
                ? "Changes entry eligibility"
                : "Entry eligibility unchanged"
            }}
          </p>
          <ul
            v-if="amendment.schedule.directives.length"
            id="kernel-amendment-directives"
            class="lineage-list"
          >
            <li
              v-for="(directive, dIdx) in amendment.schedule.directives"
              :key="`${directive.kind}-${directive.element_id}-${dIdx}`"
              class="amendment-change"
              :data-directive-kind="directive.kind"
            >
              <span class="amendment-change-kind">{{
                formatCommandTitle(directive.kind)
              }}</span>
              <code>{{ directive.element_id }}</code>
              <span
                v-if="formatDirectiveDetail(directive)"
                class="lineage-note amendment-properties numeric"
              >
                ({{ formatDirectiveDetail(directive) }})
              </span>
              <span
                v-if="
                  directive.impacted_concepts &&
                  directive.impacted_concepts.length
                "
                class="amendment-concepts"
              >
                <NodeRefChip
                  v-for="conceptRef in directive.impacted_concepts"
                  :key="conceptRef"
                  :node-ref="conceptRef"
                />
              </span>
            </li>
          </ul>
          <p v-else id="kernel-amendment-directives-empty" class="lineage-note">
            No schedule or eligibility directives recorded.
          </p>
          <p
            v-if="
              amendment.schedule.undeclared &&
              amendment.schedule.undeclared.length
            "
            id="kernel-amendment-undeclared"
            class="amendment-undeclared"
            role="status"
          >
            Undeclared impacted elements:
            <template
              v-for="(elementId, uIdx) in amendment.schedule.undeclared"
              :key="elementId"
            >
              <template v-if="uIdx > 0">, </template>
              <code>{{ elementId }}</code>
            </template>
          </p>
        </div>
        <h4 class="lineage-subheading">
          What depends on the version it amends
        </h4>
      </template>
      <p v-if="!dependents.groups.length" class="lineage-note" role="status">
        No downstream records depend on this item.
      </p>
      <template v-else>
        <p :id="`kernel-${verb}-total`" class="lineage-total">
          {{ dependents.truncated ? "At least " : ""
          }}{{ dependents.total }} dependent
          {{ Number(dependents.total) === 1 ? "object" : "objects" }}
        </p>
        <ul :id="`kernel-${verb}-list`" class="lineage-list">
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
                  <span v-if="isMasked(object)" class="noderef-masked">
                    {{ formatMaskedNode(object.kind, object.mask) }}
                  </span>
                  <NodeRefChip v-else-if="object.ref" :node-ref="object.ref" />
                  <template v-else>{{ object.handle }}</template>
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
  type KernelAmendmentImpact,
  type KernelDependents,
  type KernelLineage,
  type KernelLineageNode,
  type KernelSoaDirective,
  type KernelSoaVisit,
} from "../../api/kernelApi";
import {
  ensureNodesResolved,
  formatCommandTitle,
  formatFieldPath,
  formatMaskedNode,
} from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";

/**
 * The Trace and Impact verbs (docs/ux/CLINICAL_CANVAS.md §4, ADR-2540): upstream lineage or downstream dependents
 * grouped by kind with exact counts. Restricted records keep their place as masked placeholders and are counted.
 */
const props = defineProps<{
  nodeRef: string;
  mode: "trace" | "impact" | "amendment";
}>();
const emit = defineEmits<{ explain: [] }>();

/** The verb the panel answers; an amendment's impact is still Impact. */
const verb = computed(() => (props.mode === "trace" ? "trace" : "impact"));
const title = computed(() => {
  if (props.mode === "trace") return "Where this came from";
  return props.mode === "amendment"
    ? "What this amendment changes"
    : "What depends on this";
});
const amendment = ref<KernelAmendmentImpact | null>(null);

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

function edgeNode(handle: string): KernelLineageNode | null {
  return nodes.value.get(handle) ?? null;
}

function formatSoaVisit(visit: KernelSoaVisit): string {
  const dayLabel =
    visit.study_day !== null && visit.study_day !== undefined
      ? `Day ${visit.study_day}`
      : "Event-driven";
  const windowLabel =
    visit.window_before || visit.window_after
      ? ` (-${visit.window_before}/+${visit.window_after}d)`
      : "";
  const locationLabel = visit.locations?.length
    ? `, ${visit.locations.join("/")}`
    : "";
  return `${dayLabel}${windowLabel} in ${visit.epoch_id}${locationLabel}`;
}

function formatDirectiveDetail(directive: KernelSoaDirective): string {
  if (directive.before && directive.after) {
    return `${formatSoaVisit(directive.before)} -> ${formatSoaVisit(directive.after)}`;
  }
  if (directive.after) {
    return formatSoaVisit(directive.after);
  }
  if (directive.before) {
    return `was ${formatSoaVisit(directive.before)}`;
  }
  if (
    directive.frequency_before !== null &&
    directive.frequency_before !== undefined &&
    directive.frequency_after !== null &&
    directive.frequency_after !== undefined
  ) {
    const parts = [
      `${directive.frequency_before} -> ${directive.frequency_after} visits`,
    ];
    if (directive.collected_at_added?.length) {
      parts.push(`+${directive.collected_at_added.join(", ")}`);
    }
    if (directive.collected_at_removed?.length) {
      parts.push(`-${directive.collected_at_removed.join(", ")}`);
    }
    return parts.join("; ");
  }
  return "";
}

let latest = 0;

async function load() {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  lineage.value = null;
  dependents.value = null;
  amendment.value = null;
  try {
    if (props.mode === "trace") {
      const result = await kernelApi.trace(props.nodeRef);
      if (request === latest) {
        lineage.value = result;
        ensureNodesResolved(
          result.nodes.filter((n) => !isMasked(n)).map((n) => n.ref)
        );
      }
    } else if (props.mode === "amendment") {
      const result = await kernelApi.amendmentImpact(props.nodeRef);
      if (request === latest) {
        amendment.value = result;
        dependents.value = result.dependents;
        ensureNodesResolved([
          ...result.dependents.groups.flatMap((g) =>
            g.objects.filter((o) => !isMasked(o)).map((o) => o.ref)
          ),
          ...(result.schedule?.impacted_concepts ?? []),
          ...(result.schedule?.directives.flatMap(
            (d) => d.impacted_concepts ?? []
          ) ?? []),
        ]);
      }
    } else {
      const result = await kernelApi.dependents(props.nodeRef);
      if (request === latest) {
        dependents.value = result;
        ensureNodesResolved(
          result.groups.flatMap((g) =>
            g.objects.filter((o) => !isMasked(o)).map((o) => o.ref)
          )
        );
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

.lineage-subheading {
  margin: 4px 0 0;
  font-size: 0.85rem;
}

.amendment-change {
  overflow-wrap: anywhere;
}

.amendment-change-kind {
  font-weight: 600;
  margin-right: 4px;
}

.amendment-schedule {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.amendment-concepts {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-left: 4px;
  vertical-align: middle;
}

.amendment-undeclared {
  margin: 0;
  padding: 6px 8px;
  border-left: 3px solid var(--warning);
  background: var(--semantic-color-warning-bg-soft);
  font-size: 0.82rem;
}

.lineage-total,
.numeric {
  font-variant-numeric: tabular-nums;
}

.lineage-total {
  margin: 0;
  font-weight: 600;
}

.amendment-change code,
.amendment-undeclared code {
  font-family: var(--semantic-font-mono);
}

.lineage-group summary {
  min-height: var(--semantic-control-target-min, 24px);
  padding-block: 4px;
  cursor: pointer;
  box-sizing: border-box;
}

.lineage-group .lineage-objects {
  margin-top: 4px;
}

.lineage-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}
</style>
