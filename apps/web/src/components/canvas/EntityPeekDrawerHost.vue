<template>
  <ClinicalDrawer
    :open="state.open"
    :model-value="state.open"
    :docked="effectiveDocked"
    mobile-bottom-sheet
    overlay-class="entity-peek-drawer-overlay"
    :title="drawerTitle"
    @close="closePeek"
    @update:model-value="(val) => !val && closePeek()"
  >
    <div
      v-if="resolved"
      class="entity-peek-host"
      data-testid="entity-peek-drawer"
    >
      <div class="entity-peek-host__toolbar">
        <button
          v-if="state.history.length > 0"
          type="button"
          class="entity-peek-host__btn"
          @click="stepBack"
        >
          <ArrowLeft
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
            focusable="false"
          />
          <span>Previous ({{ state.history.length }})</span>
        </button>
        <button
          v-if="!state.openedOverModal"
          type="button"
          class="entity-peek-host__btn"
          :aria-pressed="state.docked"
          @click="toggleDocked"
        >
          <PanelRight
            :size="16"
            :stroke-width="1.75"
            aria-hidden="true"
            focusable="false"
          />
          <span>{{ state.docked ? "Undock panel" : "Dock on desktop" }}</span>
        </button>
      </div>
      <dl class="entity-peek-host__meta">
        <div>
          <dt>Kernel NodeRef</dt>
          <dd class="entity-peek-host__mono">{{ resolved.nodeRef }}</dd>
        </div>
        <div v-if="preservedScopeLabel">
          <dt>Preserved scope</dt>
          <dd class="entity-peek-host__mono">{{ preservedScopeLabel }}</dd>
        </div>
      </dl>
      <p class="entity-peek-host__summary">{{ resolved.summary }}</p>
      <button
        v-if="resolved.fullViewLocation"
        type="button"
        class="entity-peek-host__btn"
        @click="openFullView"
      >
        <ExternalLink
          :size="16"
          :stroke-width="1.75"
          aria-hidden="true"
          focusable="false"
        />
        <span>Open full view</span>
      </button>
    </div>
  </ClinicalDrawer>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import { ArrowLeft, ExternalLink, PanelRight } from "lucide-vue-next";
import { ClinicalDrawer } from "ui";
import { resolveEntityPeek } from "../../canvas/entityPeekResolver";
import { resolveWorkspaceNavigationScope } from "../../canvas/palette";
import { useActiveStudyScope } from "../../composables/useActiveStudyScope";
import { useEntityPeekDrawer } from "../../composables/useEntityPeekDrawer";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

const route = useRoute();
const router = useRouter();
const { activeStudyCode } = useActiveStudyScope();
const activeCanvasStore = computed(() => {
  const pinia = getActivePinia();
  return pinia ? useStudyCanvasStore(pinia) : null;
});
const { state, closePeek, stepBack, toggleDocked } = useEntityPeekDrawer();
const effectiveDocked = computed(
  () => Boolean(state.docked) && !state.openedOverModal
);

const workspaceScope = computed(() =>
  resolveWorkspaceNavigationScope(
    route,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  )
);

const resolved = computed(() =>
  state.current?.nodeRef
    ? resolveEntityPeek(
        state.current.nodeRef,
        (route?.query || {}) as Record<string, unknown>,
        {
          studyCode: workspaceScope.value.studyCode,
          workspaceScope: workspaceScope.value,
        }
      )
    : null
);
const drawerTitle = computed(
  () => state.current?.label || resolved.value?.title || "Record Inspector"
);
const preservedScopeLabel = computed(() => {
  const q = (route?.query || {}) as Record<string, unknown>;
  const scope = workspaceScope.value;
  return [
    scope.studyCode || q.studyId || q.study_id || q.study,
    scope.siteId || q.siteId || q.site_id || q.site,
    scope.subjectCode ||
      scope.participantRef ||
      q.subjectId ||
      q.subject_id ||
      q.subject ||
      q.participant ||
      q.participantId,
    q.visitId || q.visit_id,
  ]
    .filter(Boolean)
    .join(" | ");
});
function openFullView() {
  if (resolved.value?.fullViewLocation) {
    if (typeof window !== "undefined") {
      window.dispatchEvent(new CustomEvent("cadence:close-governance-drawers"));
    }
    router.push(resolved.value.fullViewLocation);
    closePeek();
  }
}
</script>

<style scoped>
:deep(.entity-peek-drawer-overlay) {
  z-index: 10005; /* deid-ignore: CSS stacking z-index, not a postal code */
}

.entity-peek-host {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  font-family: var(--font-sans);
  color: var(--color-text);
}
.entity-peek-host__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.entity-peek-host__btn {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 2.75rem;
  padding: 0.375rem 0.75rem;
  border-radius: 0.375rem;
  border: 1px solid var(--border-control, var(--color-border));
  background: var(--color-surface);
  color: var(--color-text);
  cursor: pointer;
}
.entity-peek-host__meta {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.entity-peek-host__meta dt {
  font-size: 0.75rem;
  color: var(--text-muted);
}
.entity-peek-host__meta dd {
  margin: 0;
}
.entity-peek-host__mono {
  font-family: var(--font-mono);
  font-size: 0.8125rem;
}
.entity-peek-host__summary {
  margin: 0;
  font-size: 0.875rem;
  line-height: 1.5;
}
</style>
