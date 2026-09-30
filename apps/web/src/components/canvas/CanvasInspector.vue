<template>
  <aside
    id="canvas-inspector"
    class="inspector"
    aria-labelledby="canvas-inspector-title"
  >
    <template v-if="participant">
      <header class="inspector-header">
        <span class="inspector-kind">Participant</span>
        <h2 id="canvas-inspector-title" class="inspector-title">
          {{ participant.subject_id }}
        </h2>
        <code id="canvas-inspector-ref" class="inspector-ref">{{
          nodeRef
        }}</code>
      </header>

      <div class="verbs" role="toolbar" aria-label="Verbs">
        <button
          id="verb-details"
          type="button"
          class="verb verb-current"
          aria-pressed="true"
        >
          Details
        </button>
        <button
          v-for="verb in UNAVAILABLE_VERBS"
          :id="`verb-${verb.id}`"
          :key="verb.id"
          type="button"
          class="verb"
          aria-disabled="true"
          :aria-describedby="shownVerb === verb.id ? 'verb-note' : undefined"
          @click="shownVerb = verb.id"
        >
          {{ verb.label }}
        </button>
      </div>
      <p v-if="shownVerbNote" id="verb-note" class="verb-note" role="status">
        {{ shownVerbNote }}
      </p>

      <dl class="details">
        <dt>State</dt>
        <dd id="inspector-state">
          {{ participantStateLabel(participant.status) }}
        </dd>
        <dt>Study</dt>
        <dd>{{ participant.study_id }}</dd>
        <dt>Site</dt>
        <dd id="inspector-site">{{ participant.site_id || "Not assigned" }}</dd>
        <dt>Protocol version</dt>
        <dd>{{ participant.active_protocol_version || "Not recorded" }}</dd>
        <dt>Enrollment number</dt>
        <dd class="numeric">{{ participant.enrollment_index }}</dd>
      </dl>

      <router-link
        id="inspector-open-data-entry"
        class="inspector-link"
        :to="{
          path: '/ecrf',
          query: {
            studyId: participant.study_id,
            subjectId: participant.subject_id,
          },
        }"
      >
        Open in data entry
      </router-link>
    </template>

    <template v-else-if="missing">
      <h2 id="canvas-inspector-title" class="inspector-title">
        Participant not loaded
      </h2>
      <p class="inspector-empty">
        This participant isn't among those loaded for {{ studyCode }}. Find it
        in Participants to see its details.
      </p>
    </template>

    <template v-else>
      <h2 id="canvas-inspector-title" class="inspector-title">Inspector</h2>
      <p class="inspector-empty">
        Select a participant to see its details here.
      </p>
    </template>
  </aside>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { SubjectSummary } from "../../api/executionApi";
import { participantStateLabel } from "../../canvas/lifecycle";

/**
 * The Clinical Canvas inspector (docs/ux/CLINICAL_CANVAS.md §2.4, UX-3, UX-5): the selected object's kind,
 * title and node reference, its verbs and its details. Trace, Impact, Explain, Simulate and Act are shown
 * disabled, each saying what it waits on, until the kernel serves them; nothing here is computed in the
 * browser (§6.1).
 */
const props = defineProps<{
  studyCode: string;
  /** The selected participant, when the canvas has loaded it. */
  participant: SubjectSummary | null;
  /** The canonical node reference of the selection. */
  nodeRef: string;
  /** True when a participant is selected but the canvas has not loaded it. */
  missing: boolean;
}>();

const UNAVAILABLE_VERBS = [
  {
    id: "trace",
    label: "Trace",
    note: "Trace needs the kernel's lineage read API (#4840), which no service serves yet.",
  },
  {
    id: "impact",
    label: "Impact",
    note: "Impact needs the kernel's dependents query over the lineage graph (#4841), which no service serves yet.",
  },
  {
    id: "explain",
    label: "Explain",
    note: "Explain renders the kernel's explanations (ADR-2482, #4842), which no service serves over the API yet.",
  },
  {
    id: "simulate",
    label: "Simulate",
    note: "Simulate needs the evaluate-only simulation engine (ADR-2480, #4866), which isn't built yet.",
  },
  {
    id: "act",
    label: "Act",
    note: "Act offers the commands you may run from the kernel's command catalogue (ADR-2483, #4847), which no service serves over the API yet.",
  },
] as const;

const shownVerb = ref<string | null>(null);
const shownVerbNote = computed(
  () => UNAVAILABLE_VERBS.find((v) => v.id === shownVerb.value)?.note ?? ""
);

watch(
  () => props.nodeRef,
  () => {
    shownVerb.value = null;
  }
);
</script>

<style scoped>
.inspector {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 16px;
  min-width: 0;
}

.inspector-header {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.inspector-kind {
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.inspector-title {
  margin: 0;
  font-size: 1.15rem;
}

.inspector-ref {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.75rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}

.verbs {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.verb {
  padding: 4px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: inherit;
  font-size: 0.85rem;
  cursor: pointer;
}

.verb[aria-disabled="true"] {
  color: var(--neutral-dark);
  border-style: dashed;
}

.verb-current {
  background: var(--accent-bg);
  border-color: var(--accent);
}

.verb-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 6px 12px;
  margin: 0;
}

.details dt {
  color: var(--neutral-dark);
}

.details dd {
  margin: 0;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.inspector-empty {
  margin: 0;
  color: var(--neutral-dark);
}

.inspector-link {
  align-self: flex-start;
  color: var(--accent);
}
</style>
